using Idara.API.Data;
using Idara.API.DTOs.Admin;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Calcule la réconciliation R = D + P sans hook ni ledger de revenu : les
    /// gains plateforme P sont RECALCULÉS à la volée depuis Payments (excédent
    /// +8%), SubscriptionInvoices (revenus abo) et Withdrawals (frais payout),
    /// moins les sorties plateforme enregistrées (PlatformOutflows). Aucune
    /// modification des flux monétaires → aucun risque de casse ni de dérive.
    /// </summary>
    public class PlatformFinanceService : IPlatformFinanceService
    {
        private readonly AppDbContext _db;
        private readonly ISenePayClient _senepay;
        private readonly ILogger<PlatformFinanceService> _logger;

        /// <summary>Marge de sécurité au-dessus de la dette (décision produit : 5%).</summary>
        /// <inheritdoc/>
        public double SafetyMarginPercent => 5.0;

        /// <summary>Tolérance sur l'écart de réconciliation (arrondis SenePay).</summary>
        private const long EpsilonFcfa = 50;

        public PlatformFinanceService(
            AppDbContext db, ISenePayClient senepay, ILogger<PlatformFinanceService> logger)
        {
            _db = db;
            _senepay = senepay;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<(long owedToSchools, PlatformBalanceDto platform)> ComputeDebtAndPlatformAsync(
            CancellationToken ct = default)
        {
            // --- D : dette totale envers les écoles (Available + Pending) ---
            var owedToSchools = await _db.SchoolWallets
                .SumAsync(w => w.AvailableBalance + w.PendingBalance, ct);

            // --- P : gains plateforme, recalculés depuis les sources ---
            // Excédent +8% : en mode Parent, l'école est créditée du montant CIBLE
            // et la réserve reçoit le net SenePay (> cible) ; l'écart est le gain
            // plateforme. En mode School, l'école est créditée du net → 0 (le
            // filtre FeesPayer=Parent l'exclut donc du calcul).
            var surplus8 = await _db.Payments
                .Where(p => p.Status == PaymentStatus.Completed
                            && p.FeesPayer == FeesPayer.Parent
                            && p.TargetAmountFcfa > 0)
                .SumAsync(p => p.NetCreditedFcfa - p.TargetAmountFcfa, ct);

            // Revenus d'abonnement encaissés (débités du wallet école → gain plateforme).
            var subscriptionRevenue = await _db.SubscriptionInvoices
                .Where(i => i.Status == SubscriptionInvoiceStatus.Paid)
                .SumAsync(i => i.AmountFcfa, ct);

            // Frais de payout des retraits ÉCOLE complétés (absorbés par la plateforme).
            // On exclut les retraits plateforme (comptés séparément ci-dessous).
            var schoolPayoutFees = await _db.Withdrawals
                .Where(w => w.Status == WithdrawalStatus.Completed && !w.IsPlatform)
                .SumAsync(w => w.FeesFcfa, ct);

            // Sorties plateforme = ajustements manuels (dashboard) + retraits gains
            // via API (Withdrawal.IsPlatform). Un retrait plateforme NON-Failed
            // (Initiated/UnderVerification/Completed) sort de la réserve son montant
            // + les frais → réduit P dès sa création (réservation implicite, P dérivé).
            var manualOutflows = await _db.PlatformOutflows.SumAsync(o => o.AmountFcfa, ct);
            var platformWithdrawalCost = await _db.Withdrawals
                .Where(w => w.IsPlatform && w.Status != WithdrawalStatus.Failed)
                .SumAsync(w => w.AmountFcfa + w.FeesFcfa, ct);
            var platformOutflows = manualOutflows + platformWithdrawalCost;

            var platform = new PlatformBalanceDto
            {
                SubscriptionRevenueFcfa = subscriptionRevenue,
                Surplus8PercentFcfa = surplus8,
                SchoolPayoutFeesFcfa = schoolPayoutFees,
                PlatformOutflowsFcfa = platformOutflows,
                TotalFcfa = surplus8 + subscriptionRevenue - schoolPayoutFees - platformOutflows
            };

            return (owedToSchools, platform);
        }

        public async Task<ReconciliationDto> ComputeReconciliationAsync(CancellationToken ct = default)
        {
            var (owedToSchools, platform) = await ComputeDebtAndPlatformAsync(ct);
            var platformTotal = platform.TotalFcfa;

            // --- R : réserve marchand SenePay (live) ---
            long reserve = 0;
            var reserveLive = false;
            try
            {
                var balance = await _senepay.GetMerchantBalanceAsync(ct);
                reserve = balance.ReserveBalanceFcfa;
                reserveLive = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[finance] Solde SenePay injoignable pour la réconciliation");
            }

            var dto = new ReconciliationDto
            {
                ReserveSenePayFcfa = reserve,
                ReserveLive = reserveLive,
                OwedToSchoolsFcfa = owedToSchools,
                Platform = platform,
                SafetyMarginPercent = SafetyMarginPercent,
                ComputedAt = DateTime.UtcNow
            };

            // Seuil sereign = D × (1 + marge). Arrondi supérieur (on ne descend pas
            // sous le seuil à cause d'un arrondi).
            var safeThreshold = (long)Math.Ceiling(owedToSchools * (1 + SafetyMarginPercent / 100.0));

            if (!reserveLive)
            {
                dto.HealthColor = "red";
                dto.Analysis = "Solde SenePay injoignable — impossible de vérifier la solvabilité pour l'instant. "
                    + $"Dette écoles : {Fmt(owedToSchools)} FCFA. Réessaie dans un instant.";
                return dto;
            }

            dto.DiscrepancyFcfa = reserve - (owedToSchools + platformTotal);
            dto.AmountToCoverDebtFcfa = Math.Max(0, owedToSchools - reserve);
            dto.AmountToReachSafeFcfa = Math.Max(0, safeThreshold - reserve);
            dto.WithdrawablePlatformFcfa = Math.Max(0, Math.Min(platformTotal, reserve - safeThreshold));

            if (reserve < owedToSchools)
                dto.HealthColor = "red";
            else if (reserve < safeThreshold)
                dto.HealthColor = "yellow";
            else
                dto.HealthColor = "green";

            dto.Analysis = BuildAnalysis(dto);

            _logger.LogInformation(
                "[finance] Réconciliation : R={Reserve} D={Owed} P={Platform} (abo={Sub}, surplus8={Surplus}, "
                + "fraisPayout=-{Fees}, sorties=-{Outflows}) écart={Discrepancy} couleur={Color}",
                reserve, owedToSchools, platformTotal, platform.SubscriptionRevenueFcfa,
                platform.Surplus8PercentFcfa, platform.SchoolPayoutFeesFcfa,
                platform.PlatformOutflowsFcfa, dto.DiscrepancyFcfa, dto.HealthColor);

            return dto;
        }

        public async Task<List<SchoolBalanceDto>> GetSchoolBalancesAsync(CancellationToken ct = default)
        {
            return await _db.SchoolWallets
                .Select(w => new SchoolBalanceDto
                {
                    SchoolId = w.SchoolId,
                    SchoolName = _db.Schools
                        .Where(s => s.Id == w.SchoolId)
                        .Select(s => s.Name)
                        .FirstOrDefault() ?? $"École #{w.SchoolId}",
                    AvailableFcfa = w.AvailableBalance,
                    PendingFcfa = w.PendingBalance,
                    TotalFcfa = w.AvailableBalance + w.PendingBalance
                })
                .OrderByDescending(w => w.TotalFcfa)
                .ToListAsync(ct);
        }

        // --- Helpers ---

        /// <summary>Commentaire d'analyse en clair (SANS emoji — la couleur/icône
        /// est portée par HealthColor côté UI).</summary>
        private static string BuildAnalysis(ReconciliationDto d)
        {
            string msg;
            if (d.HealthColor == "red")
            {
                msg = $"CRITIQUE : la réserve SenePay ({Fmt(d.ReserveSenePayFcfa)} FCFA) est INFÉRIEURE à ce qu'on "
                    + $"doit aux écoles ({Fmt(d.OwedToSchoolsFcfa)} FCFA). Ajoute au moins "
                    + $"{Fmt(d.AmountToCoverDebtFcfa)} FCFA sur ton compte marchand SenePay pour couvrir la dette "
                    + $"(idéalement {Fmt(d.AmountToReachSafeFcfa)} FCFA pour la marge de sécurité).";
            }
            else if (d.HealthColor == "yellow")
            {
                msg = $"ATTENTION : la réserve ({Fmt(d.ReserveSenePayFcfa)} FCFA) couvre la dette écoles "
                    + $"({Fmt(d.OwedToSchoolsFcfa)} FCFA) mais la marge est faible. Ajoute "
                    + $"{Fmt(d.AmountToReachSafeFcfa)} FCFA pour atteindre la marge sereine de "
                    + $"{d.SafetyMarginPercent:0}% (couvre les frais des futurs retraits).";
            }
            else
            {
                msg = $"SAIN : la réserve ({Fmt(d.ReserveSenePayFcfa)} FCFA) couvre la dette écoles "
                    + $"({Fmt(d.OwedToSchoolsFcfa)} FCFA) + la marge de sécurité. Gains plateforme retirables "
                    + $"en sécurité : {Fmt(d.WithdrawablePlatformFcfa)} FCFA.";
            }

            if (d.DiscrepancyFcfa < -EpsilonFcfa)
            {
                msg += $" Écart de {Fmt(-d.DiscrepancyFcfa)} FCFA entre la réserve et (dette + gains) : "
                    + "probable retrait manuel non enregistré depuis le dashboard SenePay — consigne-le pour "
                    + "garder la réconciliation exacte.";
            }
            else if (d.DiscrepancyFcfa > EpsilonFcfa)
            {
                msg += $" Surplus inexpliqué de {Fmt(d.DiscrepancyFcfa)} FCFA (réserve > dette + gains) : "
                    + "un crédit récent pas encore reflété, ou un ajustement manuel de trop.";
            }

            return msg;
        }

        private static string Fmt(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            .Replace(",", " ");
    }
}
