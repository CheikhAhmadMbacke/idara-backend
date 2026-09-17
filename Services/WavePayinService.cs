using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Wave;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// Issue de l'ouverture d'une session de paiement. <c>RedirectUrl</c> est
    /// l'adresse vers laquelle envoyer le payeur ; elle n'est posée qu'en cas
    /// de succès.
    /// </summary>
    public record PayinStartOutcome(
        bool Ok, string Status, string? RedirectUrl, string? ErrorMessage, int HttpStatus, string? ErrorCode);

    /// <summary>
    /// Ouvre une session de paiement Wave pour un <see cref="Payment"/> déjà
    /// persisté, et écrit sur lui les identifiants du prestataire.
    ///
    /// <para>🔑 <b>Point unique</b> : les cinq chemins qui encaissent — paiement
    /// d'un parent, lien de paiement, recharge de solde, don depuis un compte,
    /// don public, achat de pages — passent tous par ici. Chacun avait sa copie
    /// de la même requête ; c'est ainsi qu'une règle finit par diverger d'un
    /// écran à l'autre (§199).</para>
    /// </summary>
    public interface IWavePayinService
    {
        /// <summary>
        /// Ne lève jamais : l'issue est dans le résultat. Le paiement est clos
        /// en échec quand — et seulement quand — Wave a refusé AVANT d'exécuter
        /// quoi que ce soit.
        /// </summary>
        Task<PayinStartOutcome> StartAsync(Payment payment, string? customerName, CancellationToken ct = default);

        /// <summary>Adresse publique de la page de résultat d'un paiement.</summary>
        string ResultPageUrl(Payment payment);
    }

    public class WavePayinService : IWavePayinService
    {
        private readonly IWaveClient _wave;
        private readonly IPaymentAvailabilityService _availability;
        private readonly WaveSettings _settings;
        private readonly AppDbContext _context;
        private readonly ILogger<WavePayinService> _logger;

        public WavePayinService(
            IWaveClient wave, IPaymentAvailabilityService availability,
            IOptions<WaveSettings> settings,
            AppDbContext context, ILogger<WavePayinService> logger)
        {
            _wave = wave;
            _availability = availability;
            _settings = settings.Value;
            _context = context;
            _logger = logger;
        }

        public string ResultPageUrl(Payment payment) =>
            $"{_settings.PublicBaseUrl.TrimEnd('/')}/pay/{payment.Id}/{payment.PublicResultToken}";

        public async Task<PayinStartOutcome> StartAsync(
            Payment payment, string? customerName, CancellationToken ct = default)
        {
            // 🔴 GUICHET FERMÉ ? La question se pose ICI, avant tout appel au
            // prestataire, parce que c'est le point de passage OBLIGÉ des cinq
            // chemins qui encaissent. Le paiement est clos proprement : laissé
            // en attente, il encombrerait les écrans et le travail de
            // vérification pour une opération qui n'a jamais commencé.
            var blocked = await _availability.PayinBlockedReasonAsync(payment.SchoolId, ct);
            if (blocked is not null)
            {
                payment.Status = PaymentStatus.Cancelled;
                payment.FailedAt = DateTime.UtcNow;
                payment.FailureReason = "Encaissements fermés (back-office)";
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[payin] Payment {PaymentId} refusé : guichet d'encaissement fermé", payment.Id);
                return new PayinStartOutcome(false, "Cancelled", null, blocked, 503, "payin-disabled");
            }

            var resultBase = ResultPageUrl(payment);

            var request = new WaveCreateCheckoutRequest
            {
                Amount = WaveClient.FormatAmount(payment.AmountFcfa),
                Currency = "XOF",
                SuccessUrl = $"{resultBase}?status=success",
                ErrorUrl = $"{resultBase}?status=cancel",
                ClientReference = payment.Id.ToString(),
                // RestrictPayerMobile volontairement absent : voir le commentaire
                // porté par le champ. Un tiers doit pouvoir régler.
            };

            WaveCheckoutSession session;
            try
            {
                session = await _wave.CreateCheckoutSessionAsync(request, ct);
            }
            catch (WaveApiException ex)
            {
                _logger.LogError(ex,
                    "[payin] Wave a refusé l'ouverture de session pour Payment {PaymentId} (HTTP {Status}, code {Code})",
                    payment.Id, ex.StatusCode, ex.Code);

                // Refus AVANT exécution : rien n'existe chez Wave, on peut clore.
                // Timeout ou 5xx : état indéterminé, on ne touche à RIEN — le
                // travail de vérification retrouvera la session par notre
                // référence si elle a malgré tout été créée (§78).
                if (ex.IsDefinitiveRejection)
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.FailedAt = DateTime.UtcNow;
                    payment.FailureReason = $"Wave HTTP {ex.StatusCode} {ex.Code}".Trim();
                    await _context.SaveChangesAsync(ct);
                }

                return new PayinStartOutcome(false, "Failed", null,
                    "Le paiement est temporairement indisponible. Réessayez dans quelques secondes.",
                    502, ex.Code);
            }

            payment.Provider = PaymentProviders.Wave;
            payment.ProviderTransactionId = session.Id;             // cos-…
            payment.ProviderInternalId = session.TransactionId;     // visible par le payeur
            await _context.SaveChangesAsync(ct);

            // Une session fraîche est `open`. Si Wave répond déjà terminé —
            // cas théorique — on n'invente rien : le webhook et le travail de
            // vérification trancheront sur la même source.
            var status = session.PaymentStatus ?? "processing";

            // 🔴 L'adresse est suivie par le navigateur du payeur : on n'accepte
            // qu'un https absolu. Un schéma javascript: ou data: issu d'une
            // réponse altérée s'exécuterait chez lui.
            var redirect = session.WaveLaunchUrl;
            if (!string.IsNullOrWhiteSpace(redirect)
                && !(Uri.TryCreate(redirect, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            {
                _logger.LogWarning(
                    "[payin] wave_launch_url rejeté (schéma non https) pour Payment {Id}", payment.Id);
                redirect = null;
            }

            if (string.IsNullOrWhiteSpace(redirect))
            {
                return new PayinStartOutcome(false, status, null,
                    "Wave n'a pas renvoyé de page de paiement. Réessayez.", 502, null);
            }

            _logger.LogInformation(
                "[payin] Payment {PaymentId} → session {SessionId} ({Amount} FCFA)",
                payment.Id, session.Id, payment.AmountFcfa);

            return new PayinStartOutcome(true, status, redirect, null, 200, null);
        }
    }
}
