using Idara.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Résolution du tarif mensuel d'un élève selon la hiérarchie
    /// <c>override élève &gt; tarif classe courant &gt; tarif général école</c>.
    ///
    /// Source UNIQUE partagée par la génération de factures
    /// (<see cref="MonthlyInvoiceGenerationJob"/>) ET la re-tarification des
    /// factures impayées (<see cref="InvoiceRepricingService"/>) : les deux
    /// DOIVENT résoudre le même montant, sinon une facture re-tarifée
    /// diffèrerait d'une facture fraîchement générée pour le même élève.
    /// </summary>
    public static class FeeResolver
    {
        /// <summary>
        /// Renvoie, pour chaque élève fourni, son tarif mensuel résolu
        /// (<c>null</c> si aucun tarif applicable — l'appelant décide : sans-tarif
        /// à la génération, ou « ne pas re-tarifer » à la re-tarification).
        /// </summary>
        public static async Task<Dictionary<int, long?>> ResolveAsync(
            AppDbContext db,
            int schoolId,
            long? generalFee,
            IReadOnlyList<(int StudentId, int? ClassId)> students,
            DateTime today,
            CancellationToken ct)
        {
            var result = new Dictionary<int, long?>(students.Count);
            if (students.Count == 0) return result;

            var studentIds = students.Select(s => s.StudentId).ToList();

            // Overrides élève (1-1, prime sur tout).
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
                if (overrides.TryGetValue(s.StudentId, out var ov))
                    amount = ov;
                else if (s.ClassId.HasValue && classFeeByClassId.TryGetValue(s.ClassId.Value, out var cf))
                    amount = cf;
                else if (generalFee is > 0)
                    amount = generalFee;

                result[s.StudentId] = amount is > 0 ? amount : null;
            }

            return result;
        }
    }
}
