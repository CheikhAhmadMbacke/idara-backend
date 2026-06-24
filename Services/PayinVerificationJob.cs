using Idara.API.Data;
using Idara.API.DTOs.Senepay;
using Idara.API.Enums;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Poll des payins restés <see cref="PaymentStatus.Pending"/> pour les
    /// résoudre ACTIVEMENT via <c>GET /api/v1/{token}/status</c> (autoritatif),
    /// au lieu de dépendre uniquement du webhook. Deux objectifs :
    ///
    /// 1. **Faire disparaître les paiements abandonnés** (parent redirigé vers
    ///    Wave/Orange puis revenu sans confirmer — solde insuffisant, etc.) :
    ///    SenePay finit par les marquer Failed/Cancelled → on les passe terminal
    ///    → ils sortent de l'historique (les échecs sont masqués par défaut).
    ///
    /// 2. **Récupérer un webhook MANQUÉ** : si SenePay dit `Completed` mais que
    ///    le webhook n'est jamais arrivé, on crédite quand même (sinon le Payment
    ///    resterait « en cours » à vie ET l'école ne serait jamais créditée).
    ///
    /// <para><b>Sûreté (exigence absolue)</b> : on ne marque JAMAIS un Payment
    /// terminal sur une simple horloge ou une erreur réseau. Transition terminale
    /// UNIQUEMENT sur le statut EXPLICITE de SenePay :
    /// <list type="bullet">
    /// <item><c>Completed</c> → crédit (via le service de règlement partagé).</item>
    /// <item><c>Failed</c>/<c>Cancelled</c> → échec (aucun fonds n'a bougé).</item>
    /// <item>404 (jamais créé chez SenePay) → échec, mais seulement passé un délai.</item>
    /// <item><c>Pending</c> très ancien (&gt; 6h) → Expired (SenePay confirme qu'il
    ///       n'est PAS Completed → aucun débit parent → sûr).</item>
    /// <item>timeout / 5xx / réseau → on NE TOUCHE À RIEN, on réessaiera.</item>
    /// </list>
    /// Un paiement réellement débité ne peut donc que se compléter, jamais se
    /// perdre — même en cas de webhook retardé.</para>
    ///
    /// Pas de colonnes de scheduling sur Payment (pas de migration) : on re-scanne
    /// les Pending échus à chaque tick. Volume attendu faible (quelques paiements).
    /// </summary>
    public class PayinVerificationJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PayinVerificationJob> _logger;

        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(3);
        // On ne vérifie pas un Pending trop jeune : laisse le webhook (rapide pour
        // Wave) faire son travail d'abord. Inoffensif si on vérifie quand même
        // (SenePay répond Pending/Completed), mais évite de marteler l'API.
        private static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);
        // Pending sans token SenePay = l'initiate n'a jamais abouti côté SenePay
        // (appel en échec) → aucun paiement possible → échec après ce délai.
        private static readonly TimeSpan NoTokenFailAfter = TimeSpan.FromMinutes(15);
        // 404 (PAYMENT_NOT_FOUND) sur un token : SenePay ne connaît pas ce
        // paiement. On attend ce délai avant de conclure (évite toute course
        // juste après l'initiate).
        private static readonly TimeSpan NotFoundFailAfter = TimeSpan.FromMinutes(10);
        // Pending TOUJOURS Pending chez SenePay après ce délai = abandonné pour
        // de bon (Wave/Orange tranchent en quelques minutes) → Expired. Sûr :
        // SenePay confirme « pas Completed » donc le parent n'a pas été débité.
        private static readonly TimeSpan HardExpiry = TimeSpan.FromHours(6);
        private const int BatchSize = 100;

        public PayinVerificationJob(
            IServiceScopeFactory scopeFactory,
            ILogger<PayinVerificationJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[payin-verify] Démarré (intervalle {Interval}min)", PollInterval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // Catch global obligatoire : un throw tuerait le service.
                    _logger.LogError(ex, "[payin-verify] Échec du tick");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Traite tous les payins Pending échus. Public pour rejeu manuel
        /// (endpoint admin / réconciliation). Retourne le nombre traité.
        /// </summary>
        public async Task<int> RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var senepay = scope.ServiceProvider.GetRequiredService<ISenePayClient>();
            var settlement = scope.ServiceProvider.GetRequiredService<IPayinSettlementService>();

            var now = DateTime.UtcNow;
            var cutoff = now - MinAge;

            var due = await db.Payments
                .Where(p => p.Status == PaymentStatus.Pending && p.InitiatedAt <= cutoff)
                .OrderBy(p => p.InitiatedAt)
                .Take(BatchSize)
                .Select(p => new { p.Id, p.SenePayTransactionId, p.InitiatedAt })
                .ToListAsync(ct);

            if (due.Count == 0) return 0;

            _logger.LogInformation("[payin-verify] {Count} paiement(s) Pending à vérifier", due.Count);

            var processed = 0;
            foreach (var item in due)
            {
                try
                {
                    await VerifyOneAsync(senepay, settlement, item.Id, item.SenePayTransactionId, item.InitiatedAt, now, ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[payin-verify] Échec vérification Payment {Id}", item.Id);
                }
            }

            return processed;
        }

        private async Task VerifyOneAsync(
            ISenePayClient senepay, IPayinSettlementService settlement,
            int paymentId, string? token, DateTime initiatedAt, DateTime now, CancellationToken ct)
        {
            var age = now - initiatedAt;

            // Pas de token = l'initiate n'a jamais abouti chez SenePay (l'appel a
            // levé). Aucun paiement réel possible → échec passé le délai de grâce.
            if (string.IsNullOrWhiteSpace(token))
            {
                if (age >= NoTokenFailAfter)
                {
                    await settlement.SettleAsync(
                        paymentId, PaymentStatus.Failed, 0, 0, null, now,
                        "Initiation SenePay jamais aboutie (aucun token)", "poll", ct);
                }
                return;
            }

            SenePayPayinStatusResponse? status;
            try
            {
                status = await senepay.GetPayinStatusAsync(token, ct);
            }
            catch (SenePayApiException ex)
            {
                // Timeout / 5xx / réseau : indéterminé. On NE TOUCHE À RIEN — on
                // réessaiera au prochain tick. Jamais de transition sur ambigu.
                _logger.LogWarning(ex,
                    "[payin-verify] GET status indéterminé pour Payment {Id} — on réessaiera", paymentId);
                return;
            }

            // 404 : SenePay n'a aucun paiement pour ce token. Aucun fonds n'a
            // bougé → échec sûr, mais seulement passé un délai (anti-course).
            if (status == null)
            {
                if (age >= NotFoundFailAfter)
                {
                    await settlement.SettleAsync(
                        paymentId, PaymentStatus.Failed, 0, 0, null, now,
                        "Paiement introuvable chez SenePay", "poll", ct);
                }
                return;
            }

            var s = status.Status?.ToLowerInvariant();
            switch (s)
            {
                case "completed":
                    var net = (long)Math.Round(status.CreditedAmount, MidpointRounding.AwayFromZero);
                    var fees = (long)Math.Round(status.TotalFee, MidpointRounding.AwayFromZero);
                    var result = await settlement.SettleAsync(
                        paymentId, PaymentStatus.Completed, fees, net, token, now, null, "poll", ct);
                    // Récupération d'un webhook manqué → déclenche reçu/notifs.
                    if (result.Outcome == PayinSettlementOutcome.Transitioned)
                    {
                        await settlement.RunPostCompletionEffectsAsync(paymentId, "poll", ct);
                    }
                    break;

                case "failed":
                    await settlement.SettleAsync(
                        paymentId, PaymentStatus.Failed, 0, 0, token, now,
                        status.FailedReason ?? status.ErrorCode ?? "failed", "poll", ct);
                    break;

                case "cancelled":
                    await settlement.SettleAsync(
                        paymentId, PaymentStatus.Cancelled, 0, 0, token, now,
                        status.FailedReason ?? status.ErrorCode ?? "cancelled", "poll", ct);
                    break;

                default:
                    // Toujours Pending chez SenePay. Au-delà de 6h = abandonné pour
                    // de bon → Expired (SenePay confirme « pas Completed » : sûr).
                    // Sinon on laisse, on revérifiera au prochain tick.
                    if (age >= HardExpiry)
                    {
                        _logger.LogInformation(
                            "[payin-verify] Payment {Id} Pending chez SenePay depuis {Hours:0.0}h → Expired",
                            paymentId, age.TotalHours);
                        await settlement.SettleAsync(
                            paymentId, PaymentStatus.Expired, 0, 0, token, now,
                            "Paiement non confirmé (expiré)", "poll", ct);
                    }
                    break;
            }
        }
    }
}
