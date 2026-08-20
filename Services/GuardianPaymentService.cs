using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.DTOs.Senepay;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>Une facture impayable d'un enfant du responsable (calcul consolidé).</summary>
    public record OutstandingLine(
        int InvoiceId, int StudentId, string StudentName, InvoiceType Type,
        DateTime PeriodStart, long RemainingFcfa);

    /// <summary>Un enfant du responsable dans l'école (tous, même sans facture).</summary>
    public record OutstandingChild(int StudentId, string StudentName, string? ClassName, bool IsExited);

    /// <summary>
    /// Photo de la dette d'un responsable dans UNE école, calculée à l'instant T.
    /// Source unique pour « Tout payer » (app) ET la page du lien de paiement.
    /// </summary>
    public class GuardianOutstanding
    {
        public int SchoolId { get; init; }
        public int GuardianId { get; init; }
        public BillingMode BillingMode { get; init; }
        public FeesPayer FeesPayer { get; init; }
        public long MinPayinFcfa { get; init; }
        public double ParentFeeMultiplier { get; init; }
        /// <summary>Majoration affichée (ex : 8 = +8 %).</summary>
        public double ParentFeePercent { get; init; }
        public List<OutstandingLine> Lines { get; init; } = new();
        public List<OutstandingChild> Children { get; init; } = new();

        public long TotalDueFcfa => Lines.Sum(l => l.RemainingFcfa);

        /// <summary>Montant libre possible : école hors montant fixe ET aucune mensualité due.</summary>
        public bool IsFreeAmount => BillingMode != BillingMode.FixedAmount
                                    && !Lines.Any(l => l.Type == InvoiceType.MonthlyFee);

        public long ChargeFor(long target) => FeesPayer == FeesPayer.Parent
            ? (long)Math.Ceiling(target * ParentFeeMultiplier)
            : target;
    }

    /// <summary>Issue de l'appel SenePay à l'initiation (succès = RedirectUrl posé).</summary>
    public record SenePayInitiateOutcome(
        bool Ok, string Status, string? RedirectUrl, string? ErrorMessage, int HttpStatus, string? ErrorCode);

    public interface IGuardianPaymentService
    {
        /// <summary>
        /// Dette actuelle du responsable dans l'école : factures Pending/Overdue
        /// à reste dû &gt; 0 de ses enfants NON supprimés (un enfant SORTI reste
        /// redevable : la dette est payable, §159), toutes natures confondues
        /// (mensualité + inscription — le parent paie les deux dans le même
        /// « Tout payer »). Null si l'école n'a pas de réglages de paiement.
        /// </summary>
        Task<GuardianOutstanding?> GetOutstandingAsync(int guardianId, int schoolId, CancellationToken ct);

        /// <summary>
        /// Crée le Payment CONSOLIDÉ (montant fixe) avec ses allocations par
        /// facture, persisté AVANT l'appel SenePay (anti-race webhook). Lève
        /// <see cref="InvalidOperationException"/> avec un message utilisateur
        /// si la dette ne peut pas être réglée (rien à payer, minimum, mode).
        /// </summary>
        Task<Payment> CreateConsolidatedPaymentAsync(
            GuardianOutstanding outstanding, int? paymentLinkId, CancellationToken ct);

        /// <summary>
        /// Crée un Payment en MONTANT LIBRE ventilé par enfant (lien de paiement,
        /// école hors montant fixe). Les parts à 0 sont ignorées ; un enfant
        /// hors de la fratrie est refusé.
        /// </summary>
        Task<Payment> CreateFreeAmountPaymentAsync(
            GuardianOutstanding outstanding, IReadOnlyDictionary<int, long> amountByStudent,
            int? paymentLinkId, CancellationToken ct);

        /// <summary>
        /// Appelle SenePay pour un Payment déjà persisté, stocke les identifiants,
        /// applique un échec synchrone. Ne lève pas : l'issue est dans le résultat.
        /// </summary>
        Task<SenePayInitiateOutcome> InitiateWithSenePayAsync(
            Payment payment, string? customerName, CancellationToken ct);
    }

    public class GuardianPaymentService : IGuardianPaymentService
    {
        private readonly AppDbContext _context;
        private readonly ISenePayClient _senepay;
        private readonly SenePaySettings _settings;
        private readonly ILogger<GuardianPaymentService> _logger;

        public GuardianPaymentService(
            AppDbContext context,
            ISenePayClient senepay,
            IOptions<SenePaySettings> settings,
            ILogger<GuardianPaymentService> logger)
        {
            _context = context;
            _senepay = senepay;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<GuardianOutstanding?> GetOutstandingAsync(int guardianId, int schoolId, CancellationToken ct)
        {
            await _context.EnsurePaymentFoundationsAsync(schoolId, ct);
            var platform = await _context.GetPlatformSettingsAsync(ct);
            var settings = await _context.SchoolPaymentSettings
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (settings == null) return null;

            var lines = await _context.Invoices
                .Where(i => i.SchoolId == schoolId
                            && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue)
                            && i.AmountDueFcfa - i.AmountPaidFcfa > 0
                            && _context.StudentGuardians.Any(sg =>
                                sg.StudentId == i.StudentId
                                && sg.GuardianId == guardianId
                                && !sg.Student.IsDeleted))
                .OrderBy(i => i.Student.FirstName).ThenBy(i => i.PeriodStart)
                .Select(i => new OutstandingLine(
                    i.Id, i.StudentId,
                    (i.Student.FirstName + " " + i.Student.LastName).Trim(),
                    i.Type, i.PeriodStart,
                    i.AmountDueFcfa - i.AmountPaidFcfa))
                .ToListAsync(ct);

            var children = await _context.StudentGuardians
                .Where(sg => sg.GuardianId == guardianId
                             && sg.Student.SchoolId == schoolId
                             && !sg.Student.IsDeleted)
                .OrderBy(sg => sg.Student.FirstName)
                .Select(sg => new
                {
                    sg.StudentId,
                    sg.Student.FirstName,
                    sg.Student.LastName,
                    ClassName = sg.Student.Class != null ? sg.Student.Class.Name : null,
                    sg.Student.ExitDate
                })
                .ToListAsync(ct);

            var today = DateTime.UtcNow.Date;
            return new GuardianOutstanding
            {
                SchoolId = schoolId,
                GuardianId = guardianId,
                BillingMode = settings.BillingMode,
                FeesPayer = settings.FeesPayer,
                MinPayinFcfa = platform.MinPayinFcfa,
                ParentFeeMultiplier = platform.ParentFeeMultiplier,
                ParentFeePercent = platform.ParentFeePercent,
                Lines = lines,
                Children = children
                    .Select(c => new OutstandingChild(
                        c.StudentId,
                        $"{c.FirstName} {c.LastName}".Trim(),
                        c.ClassName,
                        c.ExitDate != null && c.ExitDate.Value.Date <= today))
                    .ToList()
            };
        }

        public async Task<Payment> CreateConsolidatedPaymentAsync(
            GuardianOutstanding o, int? paymentLinkId, CancellationToken ct)
        {
            // Garde « montant fixe » ASSOUPLIE (2026-08-17) : une facture
            // d'INSCRIPTION est un montant fixe par nature quel que soit le
            // BillingMode — sans cette exception, une école en montant libre avec
            // des frais d'inscription ne pourrait jamais les encaisser.
            if (o.BillingMode != BillingMode.FixedAmount
                && o.Lines.Any(l => l.Type == InvoiceType.MonthlyFee))
            {
                throw new InvalidOperationException(
                    "Le paiement global n'est disponible que pour les écoles en montant fixe.");
            }
            if (o.Lines.Count == 0)
            {
                throw new InvalidOperationException(
                    "Aucune facture à payer pour vos enfants dans cette école.");
            }
            var target = o.TotalDueFcfa;
            if (target < o.MinPayinFcfa)
            {
                throw new InvalidOperationException($"Le montant minimum est de {o.MinPayinFcfa} FCFA.");
            }

            var payment = new Payment
            {
                SchoolId = o.SchoolId,
                StudentId = null,            // consolidé → pas d'élève unique
                GuardianId = o.GuardianId,
                InvoiceId = null,            // consolidé → factures portées par les allocations
                Purpose = PaymentPurpose.SchoolFee,
                AmountFcfa = o.ChargeFor(target),
                TargetAmountFcfa = target,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = PaymentOperator.Wave, // Wave uniquement (2026-07-07)
                FeesPayer = o.FeesPayer,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = GeneratePublicToken(),
                PaymentLinkId = paymentLinkId,
                InvoiceAllocations = o.Lines
                    .Select(l => new PaymentInvoiceAllocation { InvoiceId = l.InvoiceId, AmountFcfa = l.RemainingFcfa })
                    .ToList()
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);
            return payment;
        }

        public async Task<Payment> CreateFreeAmountPaymentAsync(
            GuardianOutstanding o, IReadOnlyDictionary<int, long> amountByStudent,
            int? paymentLinkId, CancellationToken ct)
        {
            if (!o.IsFreeAmount)
            {
                throw new InvalidOperationException(
                    "Cette école fonctionne en montant fixe : le montant est calculé automatiquement.");
            }
            var allowed = o.Children.ToDictionary(c => c.StudentId);
            var parts = new List<PaymentStudentAllocation>();
            foreach (var (studentId, amount) in amountByStudent)
            {
                if (amount <= 0) continue;
                if (!allowed.ContainsKey(studentId))
                {
                    throw new InvalidOperationException("Un des enfants n'appartient pas à ce responsable.");
                }
                parts.Add(new PaymentStudentAllocation { StudentId = studentId, AmountFcfa = amount });
            }
            if (parts.Count == 0)
            {
                throw new InvalidOperationException("Saisissez un montant pour au moins un enfant.");
            }
            var target = parts.Sum(p => p.AmountFcfa);
            if (target < o.MinPayinFcfa)
            {
                throw new InvalidOperationException($"Le montant minimum est de {o.MinPayinFcfa} FCFA.");
            }

            var payment = new Payment
            {
                SchoolId = o.SchoolId,
                // Un seul enfant → on le porte sur StudentId (historiques école/
                // parent par élève) ; plusieurs → null + ventilation.
                StudentId = parts.Count == 1 ? parts[0].StudentId : null,
                GuardianId = o.GuardianId,
                InvoiceId = null,
                Purpose = PaymentPurpose.SchoolFee,
                AmountFcfa = o.ChargeFor(target),
                TargetAmountFcfa = target,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = PaymentOperator.Wave,
                FeesPayer = o.FeesPayer,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = GeneratePublicToken(),
                PaymentLinkId = paymentLinkId,
                StudentAllocations = parts.Count > 1 ? parts : new List<PaymentStudentAllocation>()
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);
            return payment;
        }

        public async Task<SenePayInitiateOutcome> InitiateWithSenePayAsync(
            Payment payment, string? customerName, CancellationToken ct)
        {
            // Numéro du responsable récupéré en base (identité par téléphone).
            // Informatif pour Wave (checkout par redirection) : un tiers peut
            // payer depuis un autre compte Wave (confirmé 2026-08-20).
            var payerPhone = payment.GuardianId is int gid
                ? await _context.Users.Where(u => u.Id == gid).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct)
                : null;
            if (string.IsNullOrWhiteSpace(payerPhone))
            {
                return new SenePayInitiateOutcome(false, "Failed", null,
                    "Aucun numéro de téléphone n'est associé à votre compte. Contactez votre école.", 400, null);
            }

            var publicBase = _settings.PublicBaseUrl.TrimEnd('/');
            var resultBase = $"{publicBase}/pay/{payment.Id}/{payment.PublicResultToken}";
            var request = new SenePayInitiatePaymentRequest
            {
                Amount = payment.AmountFcfa,
                Currency = "XOF",
                CountryCode = "SN",
                Operator = "wave",
                CustomerPhone = PaymentPhone.ForSenePay(payerPhone),
                OtpCode = null,
                OrderId = payment.Id.ToString(),
                CustomerName = customerName,
                WebhookUrl = _settings.WebhookPayinUrl,
                ReturnUrl = $"{resultBase}?status=success",
                CancelUrl = $"{resultBase}?status=cancel"
            };

            SenePayInitiatePaymentResponse response;
            try
            {
                response = await _senepay.InitiatePaymentAsync(request, ct);
            }
            catch (SenePayApiException ex)
            {
                _logger.LogError(ex, "[payment/initiate] SenePay error pour Payment {PaymentId}", payment.Id);
                return new SenePayInitiateOutcome(false, "Failed", null,
                    "SenePay temporairement indisponible. Réessayez dans quelques secondes.", 502, null);
            }

            payment.SenePayInternalId = response.InternalId;
            payment.SenePayTransactionId = response.Token;
            await _context.SaveChangesAsync(ct);

            var status = response.Status ?? "Pending";
            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                    ? PaymentStatus.Cancelled
                    : PaymentStatus.Failed;
                payment.FailedAt = DateTime.UtcNow;
                payment.FailureReason = response.FailedReason ?? response.ErrorCode;
                await _context.SaveChangesAsync(ct);
                return new SenePayInitiateOutcome(false, status, null,
                    response.FailedReason ?? "Le paiement a été refusé.", 200, response.ErrorCode);
            }

            return new SenePayInitiateOutcome(true, status, response.RedirectUrl, null, 200, response.ErrorCode);
        }

        private static string GeneratePublicToken() => Guid.NewGuid().ToString("N");
    }
}
