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
    /// terminal sur une horloge, un 404, un timeout ou une absence de token.
    /// Transition UNIQUEMENT sur un statut 200 EXPLICITE de SenePay :
    /// <list type="bullet">
    /// <item><c>Completed</c> → crédit (via le service de règlement partagé).</item>
    /// <item><c>Failed</c>/<c>Cancelled</c> → échec (aucun fonds n'a bougé).</item>
    /// <item>404 / timeout / 5xx / Pending → on NE TOUCHE À RIEN, on réessaiera.</item>
    /// </list>
    /// Un paiement réellement débité ne peut donc que se compléter, jamais se
    /// perdre — même en cas de webhook retardé.</para>
    ///
    /// <para><b>Source de vérité</b> : le WEBHOOK reste le résolveur primaire et
    /// le plus fiable. Ce poll est une 2ᵉ source de réconciliation qui lit le
    /// statut AUTORITATIF de SenePay (<c>GET /api/v1/payments/{token}/status</c>) :
    /// il rattrape un webhook MANQUÉ et clôt les abandons (SenePay finit par
    /// passer un long-Pending en <c>Failed</c> → le poll le lit). Le webhook et le
    /// poll se sérialisent sur le verrou wallet (garde Status==Pending) : un seul
    /// transite, l'autre est no-op.</para>
    ///
    /// <para>⚠️ Le chemin DOIT contenir le segment <c>payments/</c> — sans lui,
    /// 404 systématique même pour un paiement réussi (piège vécu le 2026-06-24,
    /// §108). Corrigé dans <c>SenePayClient.GetPayinStatusAsync</c>.</para>
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
        // Wave) faire son travail d'abord. Évite de marteler l'API SenePay.
        private static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);
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
            // ⚠️ RÈGLE DE SÛRETÉ ABSOLUE : on ne marque JAMAIS un Payment terminal
            // sur une horloge, un 404, un timeout ou une absence de token. UNIQUEMENT
            // sur un statut 200 EXPLICITE renvoyé par SenePay (Completed/Failed/
            // Cancelled). Raison : l'endpoint statut SenePay s'est avéré renvoyer 404
            // même pour des paiements RÉUSSIS (testé en prod 2026-06-24, cf. §108) →
            // un 404 ne signifie PAS « n'existe pas ». Conclure « échec » d'un 404
            // marquerait Failed un paiement réel dont le webhook tarde (Orange jusqu'à
            // 15 min) → le webhook Completed serait ensuite ignoré → PERTE. Donc tout
            // ce qui n'est pas un statut terminal explicite = on laisse Pending.
            // Le résolveur fiable des paiements abandonnés reste le WEBHOOK SenePay
            // (payin.failed), confirmé en prod. Ce poll est un filet de sécurité qui
            // ne s'activera que lorsque l'endpoint statut renverra un état exploitable.

            // Pas de token = l'initiate a levé avant de stocker le token. On NE peut
            // PAS conclure : SenePay a pu créer le paiement malgré le timeout (le
            // webhook le complèterait via OrderId=Payment.Id). On laisse Pending.
            if (string.IsNullOrWhiteSpace(token))
                return;

            SenePayPayinStatusResponse? status;
            try
            {
                status = await senepay.GetPayinStatusAsync(token, ct);
            }
            catch (SenePayApiException ex)
            {
                // Timeout / 5xx / réseau : indéterminé → on NE TOUCHE À RIEN.
                _logger.LogWarning(ex,
                    "[payin-verify] GET status indéterminé pour Payment {Id} — on réessaiera", paymentId);
                return;
            }

            // 404 : NON concluant (l'endpoint 404 même pour des paiements réussis).
            // On laisse Pending — surtout PAS de transition terminale.
            if (status == null)
            {
                _logger.LogDebug(
                    "[payin-verify] Payment {Id} : statut SenePay 404 (non concluant) — laissé Pending", paymentId);
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
                    // Pending (ou statut inconnu) : on NE conclut PAS. Laissé Pending,
                    // revérifié au prochain tick. Pas d'expiration sur l'horloge.
                    break;
            }
        }
    }
}
