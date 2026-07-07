using Idara.API.Data;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public interface IInvoiceRepricingService
    {
        /// <summary>
        /// Ré-applique le tarif COURANT (hiérarchie override &gt; classe &gt;
        /// général, via <see cref="FeeResolver"/>) à TOUTES les factures
        /// impayées des élèves concernés, après un changement de tarif.
        ///
        /// Ne touche JAMAIS : les factures payées, partiellement payées,
        /// annulées, ni celles avec un paiement SenePay EN COURS (montant figé).
        /// Best-effort : n'échoue jamais (le changement de tarif est déjà commit).
        /// Renvoie le nombre de factures effectivement mises à jour.
        /// </summary>
        /// <param name="studentIds">Portée : <c>null</c> = tous les élèves de
        /// l'école (changement de tarif général) ; sinon les élèves visés
        /// (classe re-tarifée, override modifié/supprimé).</param>
        Task<int> RepriceUnpaidInvoicesAsync(
            int schoolId, IReadOnlyCollection<int>? studentIds, CancellationToken ct);
    }

    /// <summary>
    /// Rend le montant des factures IMPAYÉES dynamique vis-à-vis du tarif : quand
    /// l'école change un tarif (général, classe, override), les factures déjà
    /// émises mais non réglées passent au nouveau montant (décision produit
    /// 2026-07-07 : « toutes les factures impayées »). Les factures payées
    /// restent des instantanés immuables (règlement acquis).
    /// </summary>
    public class InvoiceRepricingService : IInvoiceRepricingService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<InvoiceRepricingService> _logger;

        public InvoiceRepricingService(AppDbContext db, ILogger<InvoiceRepricingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<int> RepriceUnpaidInvoicesAsync(
            int schoolId, IReadOnlyCollection<int>? studentIds, CancellationToken ct)
        {
            try
            {
                var settings = await _db.SchoolPaymentSettings
                    .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
                // Seul le mode FixedAmount pré-génère des factures : rien à re-tarifer sinon.
                if (settings == null || settings.BillingMode != BillingMode.FixedAmount)
                    return 0;

                var today = DateTime.UtcNow.Date;

                // Élèves concernés (avec leur classe, pour résoudre le tarif).
                var studentsQuery = _db.Students
                    .Where(s => s.SchoolId == schoolId && !s.IsDeleted);
                if (studentIds != null)
                    studentsQuery = studentsQuery.Where(s => studentIds.Contains(s.Id));
                var students = await studentsQuery
                    .Select(s => new { s.Id, s.ClassId })
                    .ToListAsync(ct);
                if (students.Count == 0) return 0;

                var fees = await FeeResolver.ResolveAsync(
                    _db, schoolId, settings.GeneralMonthlyFeeFcfa,
                    students.Select(s => (s.Id, s.ClassId)).ToList(), today, ct);

                var sids = students.Select(s => s.Id).ToList();

                // Factures IMPAYÉES (Pending/Overdue), rien encore réglé dessus,
                // hors annulées/payées.
                var invoices = await _db.Invoices
                    .Where(i => i.SchoolId == schoolId
                                && sids.Contains(i.StudentId)
                                && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue)
                                && i.AmountPaidFcfa == 0)
                    .ToListAsync(ct);
                if (invoices.Count == 0) return 0;

                // EXCLUSION course « paiement en cours » : une facture visée par un
                // Payment Pending (direct OU allocation consolidée) a son montant
                // FIGÉ chez SenePay / dans l'allocation. La re-tarifer créerait, au
                // crédit du webhook, un résidu (hausse) ou un trop-perçu (baisse),
                // car PayinSettlementService crédite l'allocation figée puis compare
                // à AmountDueFcfa pour marquer Paid. On laisse donc ces factures se
                // solder au montant montré au parent.
                var invoiceIds = invoices.Select(i => i.Id).ToList();
                var inFlightDirect = await _db.Payments
                    .Where(p => p.Status == PaymentStatus.Pending
                                && p.InvoiceId != null
                                && invoiceIds.Contains(p.InvoiceId.Value))
                    .Select(p => p.InvoiceId!.Value)
                    .ToListAsync(ct);
                var inFlightAlloc = await _db.PaymentInvoiceAllocations
                    .Where(a => a.Payment.Status == PaymentStatus.Pending
                                && invoiceIds.Contains(a.InvoiceId))
                    .Select(a => a.InvoiceId)
                    .ToListAsync(ct);
                var inFlight = inFlightDirect.Concat(inFlightAlloc).ToHashSet();

                var updated = 0;
                foreach (var inv in invoices)
                {
                    if (inFlight.Contains(inv.Id)) continue;
                    if (!fees.TryGetValue(inv.StudentId, out var newAmount)
                        || newAmount is null or <= 0) continue;
                    if (inv.AmountDueFcfa == newAmount.Value) continue;

                    inv.AmountDueFcfa = newAmount.Value;
                    inv.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }

                if (updated > 0)
                {
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "[fees-reprice] SchoolId={SchoolId} {Count} facture(s) impayée(s) re-tarifée(s) au tarif courant",
                        schoolId, updated);
                }
                return updated;
            }
            catch (Exception ex)
            {
                // Best-effort : un échec de re-tarification ne doit pas faire
                // échouer le changement de tarif (déjà commit) côté école.
                _logger.LogError(ex,
                    "[fees-reprice] SchoolId={SchoolId} échec de la re-tarification des factures impayées",
                    schoolId);
                return 0;
            }
        }
    }
}
