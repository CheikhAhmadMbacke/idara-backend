namespace Idara.API.Options
{
    /// <summary>
    /// Réglages de l'API Wave Business (prestataire de paiement depuis la
    /// migration du 2026-09-17). Posés en production dans
    /// <c>/etc/idara/idara.env</c> — jamais dans appsettings.json.
    /// </summary>
    public class WaveSettings
    {
        public const string SectionName = "Wave";

        public string BaseUrl { get; set; } = "https://api.wave.com";

        /// <summary>
        /// Clé d'API (<c>wave_sn_prod_…</c>), portée en <c>Authorization: Bearer</c>.
        /// Une clé = un seul wallet. Affichée UNE seule fois à la création.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Secret de signature des requêtes SORTANTES (<c>wave_sn_AKS_…</c>),
        /// remis à la création de la clé quand « request signing » est activé.
        /// Chaque appel porte alors <c>Wave-Signature: t={ts},v1={hmac}</c> sur
        /// <c>timestamp + corps brut</c> — corps vide pour un GET.
        ///
        /// <para>Laisser vide désactive la signature côté client : c'est le
        /// repli si la clé a été créée sans. Une clé créée AVEC signature
        /// refuse tout appel non signé (401 <c>missing-signature</c>).</para>
        /// </summary>
        public string SigningSecret { get; set; } = string.Empty;

        /// <summary>
        /// Secret du webhook, remis à l'enregistrement de l'endpoint dans le
        /// portail (stratégie « Signing Secret »). DISTINCT de
        /// <see cref="SigningSecret"/> : l'un signe ce qu'on envoie, l'autre
        /// vérifie ce qu'on reçoit.
        /// </summary>
        public string WebhookSecret { get; set; } = string.Empty;

        /// <summary>
        /// Base absolue des URLs publiques (page de résultat de paiement, liens
        /// de paiement, reçus). Sert à construire <c>success_url</c> /
        /// <c>error_url</c> de chaque session.
        /// </summary>
        public string PublicBaseUrl { get; set; } = "https://api.idara.sn";

        /// <summary>
        /// Fenêtre de tolérance, en secondes, sur l'horodatage d'un webhook
        /// reçu. Wave rejette au-delà de 5 minutes côté requêtes sortantes ; on
        /// applique la même règle en entrée (anti-rejeu, §49).
        /// </summary>
        public int WebhookToleranceSeconds { get; set; } = 300;
    }
}
