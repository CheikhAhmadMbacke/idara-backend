namespace Idara.API.Options
{
    /// <summary>
    /// Configuration des notifications push Firebase Cloud Messaging (FCM v1).
    /// L'authentification se fait via un compte de service Google (JSON).
    ///
    /// Deux façons de fournir le compte de service (l'une OU l'autre) :
    ///   - <see cref="ServiceAccountPath"/> : chemin vers le fichier JSON sur le
    ///     serveur (recommandé en prod, ex. <c>/etc/idara/fcm-service-account.json</c>
    ///     en chmod 600). Clé d'env : <c>Fcm__ServiceAccountPath</c>.
    ///   - <see cref="ServiceAccountJson"/> : le JSON inline (pratique en dev /
    ///     user-secrets). Clé d'env : <c>Fcm__ServiceAccountJson</c>.
    ///
    /// Si AUCUN des deux n'est renseigné, le service push est en NO-OP (aucun
    /// envoi, warning de log) — un déploiement avant configuration ne casse rien,
    /// exactement comme le SMS (cf. <see cref="OrangeSmsSettings"/>).
    /// </summary>
    public class FcmSettings
    {
        public const string SectionName = "Fcm";

        /// <summary>Chemin du fichier JSON du compte de service Google. Secret.</summary>
        public string ServiceAccountPath { get; set; } = string.Empty;

        /// <summary>Contenu JSON inline du compte de service (alternative au chemin). Secret.</summary>
        public string ServiceAccountJson { get; set; } = string.Empty;
    }
}
