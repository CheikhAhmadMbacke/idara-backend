using Idara.API.Enums;

namespace Idara.API.Services.Auth
{
    /// <summary>Ce que le garde-fou répond, et ce que l'appelant doit en faire.</summary>
    public enum ThrottleOutcome
    {
        /// <summary>Rien à signaler, l'envoi peut partir.</summary>
        Allowed = 0,
        /// <summary>Trop de demandes pour ce destinataire → 429.</summary>
        TooManyForRecipient,
        /// <summary>Trop de demandes depuis cette adresse → 429.</summary>
        TooManyForIp,
        /// <summary>Canal SMS fermé : bourse épuisée, ou taux de vérification effondré → 503.</summary>
        SmsChannelClosed,
    }

    /// <param name="Outcome">La décision.</param>
    /// <param name="UserMessage">
    /// Ce qu'on affiche. 🔒 Ne dit JAMAIS à l'appelant qu'un budget est épuisé
    /// ni qu'un seuil a sauté : ce serait lui confirmer que son attaque marche.
    /// </param>
    /// <param name="LogReason">Le motif réel, pour le registre et l'alerte.</param>
    public readonly record struct ThrottleVerdict(
        ThrottleOutcome Outcome,
        string UserMessage,
        string? LogReason)
    {
        public bool Allowed => Outcome == ThrottleOutcome.Allowed;
    }

    /// <summary>
    /// Garde-fou des codes d'authentification — anti « SMS pumping ».
    ///
    /// <para>🔑 <b>Point de passage UNIQUE des deux portes publiques</b> : la
    /// création de compte par numéro et la réinitialisation du mot de passe. Deux
    /// copies d'une règle de sécurité finissent par diverger, et la divergence ne
    /// se voit que le jour où l'une des deux laisse passer (§199).</para>
    /// </summary>
    public interface IAuthCodeThrottle
    {
        /// <summary>
        /// Décide si un code peut partir. N'écrit rien : l'appelant enregistre
        /// ensuite par <see cref="RecordAsync"/>, refus compris.
        /// </summary>
        Task<ThrottleVerdict> EvaluateAsync(
            string ip, string recipient, OtpPurpose purpose, bool isSms, CancellationToken ct = default);

        /// <summary>
        /// Écrit la demande — <b>y compris quand elle a été refusée</b> : une
        /// demande bloquée est exactement ce qu'on cherchait à voir, l'effacer
        /// masquerait l'attaque que le dispositif vient de détecter.
        /// </summary>
        Task RecordAsync(
            string ip, string recipient, OtpPurpose purpose, bool isSms,
            string? blockedReason, CancellationToken ct = default);

        /// <summary>
        /// Marque le code comme saisi et accepté.
        ///
        /// <para>🔴 <b>Sans cet appel, le taux de vérification lit 0 % et ferme
        /// l'inscription à tout le monde.</b> Les deux points de vérification
        /// doivent l'appeler : <c>Register</c> et <c>phone/set-password</c>.</para>
        /// </summary>
        Task MarkVerifiedAsync(string recipient, OtpPurpose purpose, CancellationToken ct = default);
    }
}
