using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Alerts;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Auth
{
    /// <summary>
    /// Les cinq barrières anti « SMS pumping », en un seul endroit.
    ///
    /// <para><b>L'asymétrie qui rend tout cela possible</b> : ces deux portes ne
    /// servent qu'à ouvrir un compte d'école ou à récupérer un mot de passe
    /// oublié. Quelques dizaines d'appels par mois, pas davantage. Des seuils
    /// trente fois au-dessus de cet usage ne gêneront jamais un vrai directeur,
    /// et rendent pourtant l'attaque sans intérêt.</para>
    ///
    /// <para>Ordre d'évaluation : <b>du moins cher au plus cher</b>. Les deux
    /// premiers contrôles portent sur un index direct ; le taux de vérification
    /// et la bourse demandent une agrégation, on ne les fait donc que si les
    /// précédents passent.</para>
    /// </summary>
    public class AuthCodeThrottle : IAuthCodeThrottle
    {
        private readonly AppDbContext _context;
        private readonly IOpsAlertService _alerts;
        private readonly ILogger<AuthCodeThrottle> _logger;
        private readonly IConfiguration _config;

        public AuthCodeThrottle(
            AppDbContext context,
            IOpsAlertService alerts,
            ILogger<AuthCodeThrottle> logger,
            IConfiguration config)
        {
            _context = context;
            _alerts = alerts;
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// 🔒 Le message rendu à l'appelant quand le canal se ferme.
        /// Il ne dit NI que le budget est épuisé, NI qu'un seuil a sauté :
        /// ce serait confirmer à un attaquant que son attaque fonctionne. Il
        /// nomme en revanche la sortie de secours, qui elle reste ouverte.
        /// </summary>
        private const string ChannelClosedMessage =
            "Nous ne pouvons pas envoyer de SMS pour le moment. "
            + "Créez votre compte avec une adresse email, ou réessayez plus tard.";

        public async Task<ThrottleVerdict> EvaluateAsync(
            string ip, string recipient, OtpPurpose purpose, bool isSms, CancellationToken ct = default)
        {
            var p = await _context.GetPlatformSettingsAsync(ct);
            var now = DateTime.UtcNow;
            var ipHash = HttpContextExtensions.HashIp(ip, _config["Security:IpHashSalt"]);

            // Les demandes REFUSÉES ne comptent pas dans les plafonds : rien
            // n'est parti, et les faire peser ferait s'auto-alimenter le blocage
            // (même raisonnement que le garde-fou SMS, §191).
            var sent = _context.AuthCodeRequests.AsNoTracking()
                .Where(a => a.BlockedReason == null);

            // ===== Barrière 3 — par DESTINATAIRE =====
            // Placée en premier parce qu'elle protège une personne : recevoir
            // trois codes qu'on n'a pas demandés est du harcèlement, même quand
            // la dépense reste dérisoire.
            var toRecipient = sent.Where(a => a.Recipient == recipient);

            var last = await toRecipient
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => (DateTime?)a.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (last != null
                && (now - last.Value).TotalSeconds < p.AuthCodeMinSecondsBetween)
            {
                var attendre = p.AuthCodeMinSecondsBetween / 60;
                return new ThrottleVerdict(
                    ThrottleOutcome.TooManyForRecipient,
                    $"Un code vient de vous être envoyé. Attendez {Math.Max(1, attendre)} minutes avant d'en redemander un.",
                    "recipient_cooldown");
            }

            var dayStart = now.AddDays(-1);
            if (await toRecipient.CountAsync(a => a.CreatedAt >= dayStart, ct)
                >= p.AuthCodeMaxPerRecipientPerDay)
            {
                return new ThrottleVerdict(
                    ThrottleOutcome.TooManyForRecipient,
                    "Trop de codes demandés pour ce numéro aujourd'hui. Réessayez demain.",
                    "recipient_daily");
            }

            var monthStart = now.AddDays(-30);
            if (await toRecipient.CountAsync(a => a.CreatedAt >= monthStart, ct)
                >= p.AuthCodeMaxPerRecipientPerMonth)
            {
                return new ThrottleVerdict(
                    ThrottleOutcome.TooManyForRecipient,
                    "Trop de codes demandés pour ce numéro. Contactez-nous sur WhatsApp.",
                    "recipient_monthly");
            }

            // ===== Barrière 2 — par ADRESSE =====
            // 🔴 N'a de sens QUE si UseForwardedHeaders est posé (Program.cs).
            // Sinon toutes les requêtes partagent l'adresse de nginx, et ce
            // compteur bloquerait tout le monde ensemble.
            // On compte les numéros DISTINCTS, pas les appels : quelqu'un qui
            // redemande un code pour SON numéro n'est pas en train de balayer.
            var hourStart = now.AddHours(-1);
            var fromIp = sent.Where(a => a.IpHash == ipHash);

            var distinctHour = await fromIp
                .Where(a => a.CreatedAt >= hourStart)
                .Select(a => a.Recipient).Distinct().CountAsync(ct);
            if (distinctHour >= p.AuthCodeMaxPerIpPerHour)
            {
                return new ThrottleVerdict(
                    ThrottleOutcome.TooManyForIp,
                    "Trop de demandes depuis cet appareil. Réessayez dans une heure, "
                    + "ou créez votre compte avec une adresse email.",
                    "ip_hourly");
            }

            var distinctDay = await fromIp
                .Where(a => a.CreatedAt >= dayStart)
                .Select(a => a.Recipient).Distinct().CountAsync(ct);
            if (distinctDay >= p.AuthCodeMaxPerIpPerDay)
            {
                return new ThrottleVerdict(
                    ThrottleOutcome.TooManyForIp,
                    "Trop de demandes depuis cet appareil aujourd'hui. Réessayez demain, "
                    + "ou créez votre compte avec une adresse email.",
                    "ip_daily");
            }

            // Les deux dernières barrières ne concernent que le SMS : l'email ne
            // coûte rien et n'a pas de bourse à épuiser.
            if (!isSms) return new ThrottleVerdict(ThrottleOutcome.Allowed, string.Empty, null);

            // ===== Barrière 4 — le TAUX DE VÉRIFICATION =====
            // Le seul signal qui mesure l'INTENTION plutôt que le volume. Un
            // robot qui tire des numéros au hasard ne saisit jamais le code :
            // il n'a pas les téléphones.
            var windowSms = sent.Where(a => a.IsSms && a.CreatedAt >= hourStart);
            var envoyes = await windowSms.CountAsync(ct);
            if (envoyes >= p.AuthCodeVerifyRateMinSamples)
            {
                var verifies = await windowSms.CountAsync(a => a.VerifiedAt != null, ct);
                var taux = verifies * 100 / envoyes;
                if (taux < p.AuthCodeMinVerifyRatePercent)
                {
                    await RaiseChannelClosedAsync(
                        "taux de verification effondre",
                        $"{taux} % sur la derniere heure ({verifies}/{envoyes}), seuil {p.AuthCodeMinVerifyRatePercent} %",
                        ipHash, ct);
                    return new ThrottleVerdict(
                        ThrottleOutcome.SmsChannelClosed, ChannelClosedMessage, "verify_rate");
                }
            }

            // ===== Barrière 1 — la BOURSE =====
            // Bornée et SÉPARÉE du budget SMS commun : une attaque ne doit pas
            // pouvoir éteindre les reçus de paiement et les rappels de toute la
            // plateforme. C'est la barrière qui borne le dégât.
            var todayStart = now.Date;
            var smsToday = await sent.CountAsync(a => a.IsSms && a.CreatedAt >= todayStart, ct);
            var coutUnitaire = p.SmsOnNetPriceCentimes; // majorant : le tarif le plus élevé
            var depenseFcfa = smsToday * coutUnitaire / 100;
            if (depenseFcfa >= p.SmsAuthDailyCapFcfa)
            {
                await RaiseChannelClosedAsync(
                    "bourse quotidienne epuisee",
                    $"{smsToday} code(s) envoye(s) aujourd'hui, ~{depenseFcfa} FCFA, plafond {p.SmsAuthDailyCapFcfa} FCFA",
                    ipHash, ct);
                return new ThrottleVerdict(
                    ThrottleOutcome.SmsChannelClosed, ChannelClosedMessage, "auth_budget");
            }

            return new ThrottleVerdict(ThrottleOutcome.Allowed, string.Empty, null);
        }

        public async Task RecordAsync(
            string ip, string recipient, OtpPurpose purpose, bool isSms,
            string? blockedReason, CancellationToken ct = default)
        {
            _context.AuthCodeRequests.Add(new AuthCodeRequest
            {
                IpHash = HttpContextExtensions.HashIp(ip, _config["Security:IpHashSalt"]),
                Recipient = recipient,
                IsSms = isSms,
                Purpose = purpose,
                CreatedAt = DateTime.UtcNow,
                BlockedReason = blockedReason,
            });
            await _context.SaveChangesAsync(ct);
        }

        public async Task MarkVerifiedAsync(
            string recipient, OtpPurpose purpose, CancellationToken ct = default)
        {
            // La demande la plus récente non encore vérifiée pour ce
            // destinataire. Les précédentes restent non vérifiées : c'est
            // volontaire — trois codes demandés dont un seul saisi, c'est bien
            // un taux d'un sur trois.
            var ligne = await _context.AuthCodeRequests
                .Where(a => a.Recipient == recipient
                            && a.Purpose == purpose
                            && a.BlockedReason == null
                            && a.VerifiedAt == null)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (ligne == null) return;   // rien à marquer : sans conséquence

            ligne.VerifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        private async Task RaiseChannelClosedAsync(
            string motif, string detail, string ipHash, CancellationToken ct)
        {
            _logger.LogWarning(
                "[auth-throttle] Canal SMS des codes FERME — {Motif} ({Detail})", motif, detail);

            var hourStart = DateTime.UtcNow.AddHours(-1);
            var recents = _context.AuthCodeRequests.AsNoTracking()
                .Where(a => a.IsSms && a.CreatedAt >= hourStart && a.BlockedReason == null);
            var numeros = await recents.Select(a => a.Recipient).Distinct().CountAsync(ct);
            var adresses = await recents.Select(a => a.IpHash).Distinct().CountAsync(ct);

            await _alerts.SendAsync(new OpsAlertRequest(
                Kind: OpsAlertKind.AuthCodeChannelClosed,
                // Une seule alerte par heure : le dispositif se déclenche à
                // CHAQUE tentative suivante, et on ne veut pas d'une boîte pleine.
                GroupingKey: $"auth-code-closed-{DateTime.UtcNow:yyyyMMddHH}",
                Subject: "Envoi des codes par SMS suspendu — " + motif,
                Facts: new List<AlertFact>
                {
                    new("Motif", motif),
                    new("Detail", detail),
                    new("Numeros distincts sur 1 h", numeros.ToString()),
                    new("Appelants distincts sur 1 h", adresses.ToString()),
                    new("Dernier appelant (empreinte)", ipHash[..8]),
                },
                Advice: "L'inscription et la reinitialisation par SMS sont fermees ; "
                        + "l'email reste ouvert. Regarde la table AuthCodeRequests sur la derniere "
                        + "heure : beaucoup de numeros distincts jamais verifies = balayage. "
                        + "Le canal rouvre seul quand la fenetre d'une heure se vide, ou au "
                        + "changement de jour pour la bourse."), ct);
        }
    }
}
