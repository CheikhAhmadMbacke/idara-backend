using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>Élève dont il faut résoudre le tarif mensuel.</summary>
    public readonly record struct FeeTarget(int StudentId, int? ClassId, BoardingStatus? Boarding);

    /// <summary>
    /// Résolution du tarif mensuel d'un élève selon la hiérarchie
    /// <c>tarif élève &gt; tarif statut &gt; tarif classe courant &gt; tarif général</c>.
    ///
    /// Source UNIQUE partagée par la génération de factures
    /// (<see cref="MonthlyInvoiceGenerationJob"/>) ET la re-tarification des
    /// factures impayées (<see cref="InvoiceRepricingService"/>) : les deux
    /// DOIVENT résoudre le même montant, sinon une facture re-tarifée
    /// diffèrerait d'une facture fraîchement générée pour le même élève.
    ///
    /// ⚠️ Tout nouveau niveau de tarif s'ajoute ICI et NULLE PART AILLEURS :
    /// c'est ce qui garantit que le montant affiché à l'école, celui de la
    /// facture générée et celui de la facture re-tarifée ne peuvent pas diverger.
    /// </summary>
    public static class FeeResolver
    {
        /// <summary>
        /// Tarif du régime d'hébergement demandé, ou <c>null</c> si non configuré
        /// (ou si l'élève n'a pas de statut renseigné).
        /// </summary>
        public static long? BoardingFeeFor(SchoolPaymentSettings settings, BoardingStatus? status) => status switch
        {
            BoardingStatus.Boarding => settings.BoardingMonthlyFeeFcfa,
            BoardingStatus.HalfBoarding => settings.HalfBoardingMonthlyFeeFcfa,
            BoardingStatus.Day => settings.DayMonthlyFeeFcfa,
            _ => null
        };

        /// <summary>
        /// Renvoie, pour chaque élève fourni, son tarif mensuel résolu
        /// (<c>null</c> si aucun tarif applicable — l'appelant décide : sans-tarif
        /// à la génération, ou « ne pas re-tarifer » à la re-tarification).
        /// </summary>
        public static async Task<Dictionary<int, long?>> ResolveAsync(
            AppDbContext db,
            int schoolId,
            SchoolPaymentSettings settings,
            IReadOnlyList<FeeTarget> students,
            DateTime today,
            CancellationToken ct)
        {
            var result = new Dictionary<int, long?>(students.Count);
            if (students.Count == 0) return result;

            var studentIds = students.Select(s => s.StudentId).ToList();

            // Tarif personnalisé de l'élève (1-1, prime sur tout).
            var overrides = await db.StudentFeeOverrides
                .Where(o => studentIds.Contains(o.StudentId))
                .ToDictionaryAsync(o => o.StudentId, o => o.AmountFcfa, ct);

            // Tarif classe COURANT (max EffectiveFrom <= today). Le tie-break
            // ThenByDescending(Id) départage plusieurs saisies du même jour de
            // façon déterministe (la dernière gagne) — cf. gotcha §107.
            var classIds = students
                .Where(s => s.ClassId.HasValue)
                .Select(s => s.ClassId!.Value)
                .Distinct()
                .ToList();

            Dictionary<int, long> classFeeByClassId = new();
            if (classIds.Count > 0)
            {
                var feesQuery = await db.ClassFees
                    .Where(f => f.SchoolId == schoolId
                                && classIds.Contains(f.ClassId)
                                && f.EffectiveFrom <= today)
                    .GroupBy(f => f.ClassId)
                    .Select(g => new
                    {
                        ClassId = g.Key,
                        Amount = g.OrderByDescending(f => f.EffectiveFrom)
                                  .ThenByDescending(f => f.Id)
                                  .First().AmountFcfa
                    })
                    .ToListAsync(ct);
                classFeeByClassId = feesQuery.ToDictionary(x => x.ClassId, x => x.Amount);
            }

            foreach (var s in students)
            {
                long? amount = null;
                var boardingFee = BoardingFeeFor(settings, s.Boarding);

                if (overrides.TryGetValue(s.StudentId, out var ov))
                    amount = ov;
                else if (boardingFee is > 0)
                    amount = boardingFee;
                else if (s.ClassId.HasValue && classFeeByClassId.TryGetValue(s.ClassId.Value, out var cf))
                    amount = cf;
                else if (settings.GeneralMonthlyFeeFcfa is > 0)
                    amount = settings.GeneralMonthlyFeeFcfa;

                result[s.StudentId] = amount is > 0 ? amount : null;
            }

            return result;
        }
    }
}
