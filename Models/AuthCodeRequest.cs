using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Une demande de code d'authentification — création de compte ou
    /// réinitialisation du mot de passe.
    ///
    /// <para><b>Pourquoi une table, alors que le registre des SMS existe déjà</b> :
    /// <c>NotificationLogs</c> ne porte ni l'adresse de l'appelant ni le fait que
    /// le code ait été <i>saisi</i>. Or ce sont exactement les deux signaux dont
    /// les garde-fous ont besoin — le premier pour compter par appelant, le
    /// second pour distinguer une attaque d'une bonne journée.</para>
    ///
    /// <para>🔴 <b>Et pourquoi en base plutôt qu'en mémoire</b> : un compteur
    /// <c>IMemoryCache</c> repart à zéro à chaque redéploiement (§92). Un
    /// attaquant n'aurait qu'à attendre la prochaine mise en production — ou à
    /// la provoquer — pour retrouver son quota intact.</para>
    ///
    /// <para>Append-only : une ligne n'est jamais modifiée, sauf
    /// <see cref="VerifiedAt"/> au moment où le code est accepté.</para>
    /// </summary>
    public class AuthCodeRequest
    {
        public int Id { get; set; }

        /// <summary>
        /// 🔒 <b>Empreinte</b> de l'adresse de l'appelant, jamais l'adresse.
        /// Une IP est une donnée personnelle et Pyranil en est sous-traitant
        /// (§215) : on a besoin de <i>compter</i> les appels d'un même appelant,
        /// jamais de savoir qui il est. Voir <c>HttpContextExtensions.HashIp</c>.
        /// </summary>
        public string IpHash { get; set; } = string.Empty;

        /// <summary>
        /// Destinataire normalisé : numéro en E.164, ou adresse en minuscules.
        /// C'est la clé des plafonds « par destinataire ».
        /// </summary>
        public string Recipient { get; set; } = string.Empty;

        /// <summary>Vrai si le code est parti par SMS (donc s'il a coûté).</summary>
        public bool IsSms { get; set; }

        /// <summary>Création de compte, ou réinitialisation.</summary>
        public OtpPurpose Purpose { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Rempli quand le code est effectivement saisi et accepté.
        ///
        /// <para>🔴 <b>Sans ce marquage, le taux de vérification lit 0 % et ferme
        /// l'inscription à tout le monde.</b> Les deux points de vérification
        /// (<c>Register</c> et <c>phone/set-password</c>) doivent le poser.</para>
        /// </summary>
        public DateTime? VerifiedAt { get; set; }

        /// <summary>
        /// Vrai si l'appel portait une attestation d'application valide.
        /// Réservé à la barrière 6 (Firebase App Check), pas encore en service :
        /// la colonne existe pour que la mesure démarre avec le reste.
        /// </summary>
        public bool AppCheckOk { get; set; }

        /// <summary>
        /// Motif de refus, s'il y en a eu un. Une demande bloquée est écrite
        /// <b>quand même</b> : c'est précisément ce qu'on cherchait à voir
        /// (même principe que le registre des SMS, §191).
        /// </summary>
        public string? BlockedReason { get; set; }
    }
}
