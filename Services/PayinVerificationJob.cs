using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Wave;
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
        /// <summary>
        /// Vérification LIVE d'un seul paiement Pending, à la demande (page du
        /// lien de paiement : un parent qui a annulé sur Wave ne doit pas
        /// attendre 45 min que SenePay passe l'abandon en Failed pour réessayer).
        /// Mêmes règles de sûreté que le tick : transition UNIQUEMENT sur un
        /// statut 200 explicite — 404/timeout/Pending laissent tout en l'état.
        /// Ne lève jamais ; renvoie le statut du Payment après vérification.
        /// </summary>
        public async Task<PaymentStatus?> VerifyPaymentNowAsync(int paymentId, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var wave = scope.ServiceProvider.GetRequiredService<IWaveClient>();
            var settlement = scope.ServiceProvider.GetRequiredService<IPayinSettlementService>();

            var p = await db.Payments
                .Where(x => x.Id == paymentId)
                .Select(x => new { x.Status, x.ProviderTransactionId, x.InitiatedAt })
                .FirstOrDefaultAsync(ct);
            if (p == null) return null;
            if (p.Status != PaymentStatus.Pending) return p.Status;

            try
            {
                await VerifyOneAsync(wave, settlement, paymentId, p.ProviderTransactionId, p.InitiatedAt, DateTime.UtcNow, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[payin-verify] Échec vérification à la demande Payment {Id}", paymentId);
            }

            return await db.Payments.Where(x => x.Id == paymentId).Select(x => (PaymentStatus?)x.Status).FirstOrDefaultAsync(ct);
        }

        public async Task<int> RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var wave = scope.ServiceProvider.GetRequiredService<IWaveClient>();
            var settlement = scope.ServiceProvider.GetRequiredService<IPayinSettlementService>();

            var now = DateTime.UtcNow;
            var cutoff = now - MinAge;

            var due = await db.Payments
                // 🔑 Uniquement les paiements ouverts CHEZ WAVE. Les paiements
                // hérités du prestataire précédent ne sont pas interrogeables
                // ici : lui demander des nouvelles d'une opération qu'il n'a
                // jamais vue ne renverrait qu'un « inconnu » trompeur.
                .Where(p => p.Status == PaymentStatus.Pending && p.InitiatedAt <= cutoff
                            && p.Provider == PaymentProviders.Wave)
                .OrderBy(p => p.InitiatedAt)
                .Take(BatchSize)
                .Select(p => new { p.Id, p.ProviderTransactionId, p.InitiatedAt })
                .ToListAsync(ct);

            if (due.Count == 0) return 0;

            _logger.LogInformation("[payin-verify] {Count} paiement(s) Pending à vérifier", due.Count);

            var processed = 0;
            foreach (var item in due)
            {
                try
                {
                    await VerifyOneAsync(wave, settlement, item.Id, item.ProviderTransactionId, item.InitiatedAt, now, ct);
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
            IWaveClient wave, IPayinSettlementService settlement,
            int paymentId, string? sessionId, DateTime initiatedAt, DateTime now, CancellationToken ct)
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

            // Pas d'identifiant de session : l'ouverture a échoué avant de
            // l'enregistrer. On NE peut PAS conclure — Wave a pu créer la
            // session malgré le délai. On la retrouve par NOTRE référence,
            // c'est précisément à cela que sert `client_reference`.
            WaveCheckoutSession? session;
            try
            {
                session = string.IsNullOrWhiteSpace(sessionId)
                    ? await wave.FindCheckoutByClientReferenceAsync(paymentId.ToString(), ct)
                    : await wave.GetCheckoutSessionAsync(sessionId, ct);
            }
            catch (WaveApiException ex)
            {
                // Délai / 5xx / réseau : indéterminé → on NE TOUCHE À RIEN.
                _logger.LogWarning(ex,
                    "[payin-verify] Lecture de session indéterminée pour Payment {Id} — on réessaiera", paymentId);
                return;
            }

            // Introuvable : non concluant. Laissé en attente, surtout pas de
            // transition terminale (§78).
            if (session is null)
            {
                _logger.LogDebug(
                    "[payin-verify] Payment {Id} : session inconnue de Wave — laissé en attente", paymentId);
                return;
            }

            // Si la session n'avait pas été enregistrée (ouverture en échec puis
            // retrouvée par référence), on la rattache maintenant.
            if (string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(session.Id))
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Payments.Where(x => x.Id == paymentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.ProviderTransactionId, session.Id), ct);
                _logger.LogInformation(
                    "[payin-verify] Payment {Id} rattaché à la session {SessionId} retrouvée par référence",
                    paymentId, session.Id);
            }

            var reference = session.Id;
            switch (session.PaymentStatus?.ToLowerInvariant())
            {
                case "succeeded":
                {
                    var charged = WaveClient.ParseAmount(session.Amount);

                    // Wave ne dit pas les frais dans l'objet session : on les
                    // dérive de la grille saisie, exactement comme le webhook,
                    // et la réconciliation du registre les confronte au réel.
                    long fees = 0;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var grid = (await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct))?.Fees;
                        if (grid is { IsConfigured: true } && charged > 0)
                            fees = grid.Value.PayinFeesFor(charged);
                    }

                    var net = Math.Max(0, charged - fees);
                    var result = await settlement.SettleAsync(
                        paymentId, PaymentStatus.Completed, fees, net, reference,
                        session.WhenCompleted ?? now, null, "poll", ct);

                    // Rattrapage d'un webhook manqué : reçu et notifications.
                    if (result.Outcome == PayinSettlementOutcome.Transitioned)
                        await settlement.RunPostCompletionEffectsAsync(paymentId, "poll", ct);
                    break;
                }

                case "cancelled":
                    await settlement.SettleAsync(
                        paymentId, PaymentStatus.Cancelled, 0, 0, reference, now,
                        session.LastPaymentError?.Code ?? session.LastPaymentError?.Message ?? "cancelled",
                        "poll", ct);
                    break;

                default:
                    // `processing`, ou une session simplement expirée sans
                    // paiement : on ne conclut PAS sur l'horloge. Une session
                    // expirée reste en attente jusqu'à ce que Wave le dise —
                    // c'est la règle qui a évité les faux échecs (§78).
                    if (string.Equals(session.CheckoutStatus, "expired", StringComparison.OrdinalIgnoreCase))
                    {
                        await settlement.SettleAsync(
                            paymentId, PaymentStatus.Expired, 0, 0, reference,
                            session.WhenCompleted ?? now, "expired", "poll", ct);
                    }
                    break;
            }
        }
    }
}
