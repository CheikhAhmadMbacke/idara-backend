using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Alerte d'exploitation, append-only. Sert à la fois de <b>journal</b>
    /// consultable au back-office et de <b>mémoire du regroupement</b> : c'est en
    /// relisant cette table qu'on sait qu'un même défaut a déjà été signalé il y
    /// a dix minutes et qu'un second e-mail n'apprendrait rien.
    ///
    /// <para><b>Pourquoi une table et pas seulement un e-mail.</b> Un e-mail se
    /// perd, se classe, ou n'est jamais envoyé (plafond journalier atteint,
    /// SMTP indisponible). L'événement, lui, doit rester consultable : une
    /// alerte non envoyée est justement celle qu'on veut pouvoir retrouver.
    /// C'est aussi ce qui manquait à <see cref="PayoutAlert"/>, écrit en base
    /// depuis des mois sans que rien ne le fasse remonter.</para>
    /// </summary>
    public class OpsAlert
    {
        public int Id { get; set; }

        public OpsAlertKind Kind { get; set; }

        /// <summary>
        /// Clé de regroupement : deux alertes qui la partagent racontent le même
        /// défaut. Ex. <c>sms-school-42-daily</c>, <c>withdrawal-outage</c>.
        ///
        /// <para>Sans elle, une réserve de décaissement à sec produirait un
        /// e-mail par école qui tente un retrait — vingt copies du même message,
        /// noyant celui qui, lui, serait nouveau.</para>
        /// </summary>
        public string GroupingKey { get; set; } = string.Empty;

        /// <summary>Titre lisible, tel qu'il part en objet d'e-mail.</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>Corps déjà rendu (texte), affiché tel quel au back-office.</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>Ce qu'il y a à faire, quand la réponse est connue d'avance
        /// (« recharger la réserve SenePay »). Null si le diagnostic reste ouvert.</summary>
        public string? Advice { get; set; }

        /// <summary>École concernée, si l'alerte en vise une (index pour la
        /// consultation par école).</summary>
        public int? SchoolId { get; set; }

        /// <summary>Entité métier visée (retrait, utilisateur…), selon le Kind.</summary>
        public int? RelatedId { get; set; }

        /// <summary>Horodatage de l'envoi de l'e-mail. NULL = alerte enregistrée
        /// mais NON envoyée (regroupée, plafond atteint, ou envoi en échec) —
        /// distinction importante : elle dit si tu as réellement été prévenu.</summary>
        public DateTime? EmailedAt { get; set; }

        /// <summary>Traitée / classée par le SuperAdmin.</summary>
        public bool Resolved { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
