using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.DTOs.Payment;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public interface IPaymentRosterService
    {
        /// <summary>Le cahier d'appel des mensualités d'un mois, pour une école.</summary>
        Task<PaymentRosterResponseDto> BuildAsync(int schoolId, int year, int month, CancellationToken ct);
    }

    /// <summary>
    /// « Qui a payé ce mois-ci, qui est en retard ». Extrait de
    /// <c>FeesController</c> le 2026-10-07 : l'écran, l'export PDF et
    /// l'assistant IA doivent donner LA MÊME réponse — une seule règle (§199).
    /// </summary>
    public class PaymentRosterService : IPaymentRosterService
    {
        private readonly AppDbContext _context;

        public PaymentRosterService(AppDbContext context) => _context = context;

        /// <summary>Calcule le roster d'un mois (partagé par la vue JSON et l'export PDF).</summary>
        public async Task<PaymentRosterResponseDto> BuildAsync(
            int schoolId, int year, int month, CancellationToken ct)
        {
            var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var todayUtc = DateTime.UtcNow.Date;

            // EnrolledDuring(periodStart), PAS Enrolled() : le suivi d'octobre
            // doit encore montrer un élève parti en novembre (il devait sa
            // mensualité d'octobre), et ne plus montrer celui parti en septembre.
            var students = await _context.Students
                .Where(s => s.SchoolId == schoolId)
                .EnrolledDuring(periodStart)
                .Select(s => new
                {
                    s.Id,
                    s.FirstName,
                    s.LastName,
                    s.StudentNumber,
                    ClassName = s.Class != null ? s.Class.Name : null,
                    // Parent à contacter : le responsable LIÉ au compte (celui qui
                    // paie dans l'app) d'abord — principal s'il y en a un —, sinon
                    // le père puis la mère du dossier élève.
                    LinkedGuardian = s.StudentGuardians
                        .Where(g => !g.Guardian.IsDeleted)
                        .OrderByDescending(g => g.IsPrimaryGuardian)
                        .ThenBy(g => g.GuardianId)
                        .Select(g => new { g.GuardianId, g.Guardian.FullName, g.Guardian.PhoneNumber })
                        .FirstOrDefault(),
                    s.FatherFullName,
                    s.FatherPhone,
                    s.MotherFullName,
                    s.MotherPhone
                })
                .ToListAsync(ct);

            // Factures du mois (hors annulées) — matérialisées pour faire la
            // comparaison d'échéance en mémoire (évite tout souci de traduction).
            // MENSUALITÉS uniquement : le roster est le cahier d'appel des
            // mensualités — une facture d'inscription impayée ne doit pas faire
            // apparaître l'élève « en retard » sur son mois, ni payée le faire
            // apparaître « à jour » alors que sa mensualité n'existe pas.
            var invoices = await _context.Invoices
                .Where(i => i.SchoolId == schoolId
                    && i.PeriodStart == periodStart
                    && i.Type == InvoiceType.MonthlyFee
                    && i.Status != InvoiceStatus.Cancelled)
                .Select(i => new
                {
                    i.Id,
                    i.StudentId,
                    i.AmountDueFcfa,
                    i.AmountPaidFcfa,
                    i.Status,
                    i.DueDate
                })
                .ToListAsync(ct);

            var byStudent = invoices
                .GroupBy(i => i.StudentId)
                .ToDictionary(g => g.Key, g => g.First());

            // Date du dernier paiement COMPLÉTÉ imputé à chaque facture : soit un
            // paiement direct (InvoiceId), soit une part de paiement global
            // consolidé (allocation). Les deux chemins existent depuis le
            // « paiement global » parent — n'en oublier aucun sinon des élèves
            // payés apparaîtraient sans date.
            var invoiceIds = invoices.Select(i => i.Id).ToList();
            var paidDirect = await _context.Payments
                .Where(p => p.InvoiceId != null && invoiceIds.Contains(p.InvoiceId.Value)
                            && p.Status == PaymentStatus.Completed && p.PaidAt != null)
                .Select(p => new { InvoiceId = p.InvoiceId!.Value, p.PaidAt })
                .ToListAsync(ct);
            var paidAllocated = await _context.PaymentInvoiceAllocations
                .Where(a => invoiceIds.Contains(a.InvoiceId)
                            && a.Payment.Status == PaymentStatus.Completed && a.Payment.PaidAt != null)
                .Select(a => new { a.InvoiceId, a.Payment.PaidAt })
                .ToListAsync(ct);
            var paidAtByInvoice = paidDirect.Concat(paidAllocated)
                .GroupBy(x => x.InvoiceId)
                .ToDictionary(g => g.Key, g => g.Max(x => x.PaidAt));

            // Liens de paiement ACTIFS des responsables liés (lien = par
            // responsable, donc partagé entre frères et sœurs) : « lien envoyé
            // le… / ouvert » dans le roster, pour savoir qui relancer.
            var linkedGuardianIds = students
                .Where(s => s.LinkedGuardian != null)
                .Select(s => s.LinkedGuardian!.GuardianId)
                .Distinct()
                .ToList();
            var linksByGuardian = await _context.PaymentLinks
                .Where(l => l.SchoolId == schoolId && l.RevokedAt == null && linkedGuardianIds.Contains(l.GuardianId))
                .Select(l => new { l.GuardianId, l.CreatedAt, l.LastSharedAt, l.LastOpenedAt })
                .ToDictionaryAsync(l => l.GuardianId, ct);

            var entries = new List<PaymentRosterEntryDto>(students.Count);
            foreach (var s in students)
            {
                var link = s.LinkedGuardian != null && linksByGuardian.TryGetValue(s.LinkedGuardian.GuardianId, out var lk)
                    ? lk : null;
                byStudent.TryGetValue(s.Id, out var inv);
                RosterPaymentStatus status;
                if (inv == null)
                    status = RosterPaymentStatus.NoInvoice;
                else if (inv.Status == InvoiceStatus.Paid || inv.AmountPaidFcfa >= inv.AmountDueFcfa)
                    status = RosterPaymentStatus.Paid;
                else if (inv.Status == InvoiceStatus.Overdue || todayUtc > inv.DueDate.Date)
                    status = RosterPaymentStatus.Overdue;
                else
                    status = RosterPaymentStatus.Pending;

                // Cascade parent : responsable lié au compte > père > mère. Le nom
                // et le numéro viennent TOUJOURS de la même source (sinon on
                // afficherait le nom du père avec le numéro de la mère).
                var guardianName = s.LinkedGuardian?.FullName;
                var guardianPhone = s.LinkedGuardian?.PhoneNumber;
                if (string.IsNullOrWhiteSpace(guardianName))
                    (guardianName, guardianPhone) = (s.FatherFullName, s.FatherPhone);
                if (string.IsNullOrWhiteSpace(guardianName))
                    (guardianName, guardianPhone) = (s.MotherFullName, s.MotherPhone);

                entries.Add(new PaymentRosterEntryDto
                {
                    StudentId = s.Id,
                    StudentFirstName = s.FirstName,
                    StudentLastName = s.LastName,
                    StudentNumber = s.StudentNumber,
                    ClassName = s.ClassName,
                    Status = status,
                    AmountDueFcfa = inv?.AmountDueFcfa ?? 0,
                    AmountPaidFcfa = inv?.AmountPaidFcfa ?? 0,
                    InvoiceId = inv?.Id,
                    DueDate = inv?.DueDate,
                    GuardianFullName = string.IsNullOrWhiteSpace(guardianName) ? null : guardianName.Trim(),
                    GuardianPhone = string.IsNullOrWhiteSpace(guardianPhone) ? null : guardianPhone.Trim(),
                    PaidAt = inv != null && paidAtByInvoice.TryGetValue(inv.Id, out var pa) ? pa : null,
                    HasLinkedGuardian = s.LinkedGuardian != null,
                    PaymentLinkSentAt = link?.LastSharedAt ?? link?.CreatedAt,
                    PaymentLinkOpenedAt = link?.LastOpenedAt
                });
            }

            // Tri : en retard d'abord (à traiter en priorité), puis en attente,
            // puis à jour, puis sans facture ; ensuite par nom.
            static int Rank(RosterPaymentStatus s) => s switch
            {
                RosterPaymentStatus.Overdue => 0,
                RosterPaymentStatus.Pending => 1,
                RosterPaymentStatus.Paid => 2,
                _ => 3
            };
            entries = entries
                .OrderBy(e => Rank(e.Status))
                .ThenBy(e => e.StudentLastName)
                .ThenBy(e => e.StudentFirstName)
                .ToList();

            // Le calendrier du mois affiché : c'est lui qui dit au daara s'il
            // doit encore attendre ou s'il peut relancer et clore son mois.
            //
            // ⚠️ UNIQUEMENT en montant fixe. Une école en montant libre n'a pas
            // de mensualité générée, donc pas d'échéance : lui annoncer une date
            // limite serait une promesse que rien ne tient — aucune facture ne
            // basculera « en retard » ce jour-là, et personne ne sera relancé.
            var schedule = await _context.SchoolPaymentSettings
                .Where(s => s.SchoolId == schoolId)
                .Select(s => new { s.BillingMode, s.MonthlyDueDay, s.PaymentDeadlineDay })
                .FirstOrDefaultAsync(ct);
            var hasSchedule = schedule?.BillingMode == BillingMode.FixedAmount;
            var openingDay = schedule?.MonthlyDueDay ?? 5;
            var deadlineDay = schedule?.PaymentDeadlineDay ?? 15;

            return new PaymentRosterResponseDto
            {
                Year = year,
                Month = month,
                OpeningDay = hasSchedule ? openingDay : 0,
                DeadlineDate = hasSchedule
                    ? PaymentSchedule.DeadlineFor(periodStart, openingDay, deadlineDay)
                    : null,
                PaidCount = entries.Count(e => e.Status == RosterPaymentStatus.Paid),
                PendingCount = entries.Count(e => e.Status == RosterPaymentStatus.Pending),
                OverdueCount = entries.Count(e => e.Status == RosterPaymentStatus.Overdue),
                NoInvoiceCount = entries.Count(e => e.Status == RosterPaymentStatus.NoInvoice),
                Entries = entries
            };
        }
    }
}
