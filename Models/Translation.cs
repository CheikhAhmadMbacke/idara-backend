namespace Idara.API.Models
{
    /// <summary>
    /// Une clé de traduction de l'application, avec ses valeurs de référence.
    ///
    /// ⚠️ La SOURCE reste les fichiers `assets/translations/*.json` du dépôt
    /// frontend : cette table en est une COPIE de travail, alimentée par un
    /// import explicite du SuperAdmin. Le backend n'a aucun moyen de lire ces
    /// fichiers (ils sont compilés en code depuis §155 et ne sont plus servis),
    /// et les recopier dans le dépôt backend garantirait la divergence.
    ///
    /// Le cycle est donc : import des JSON → relecture par des humains →
    /// export du JSON corrigé → réinjection dans le frontend.
    /// </summary>
    public class TranslationKey
    {
        public int Id { get; set; }

        /// <summary>Clé i18n, ex. « school_info.title ». Unique.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Texte français de référence — ce que le relecteur traduit.</summary>
        public string Fr { get; set; } = string.Empty;

        /// <summary>Texte arabe actuellement dans l'application.</summary>
        public string? Ar { get; set; }

        /// <summary>
        /// Texte wolof actuel. Vide aujourd'hui : la langue se collecte avant
        /// d'être activée dans l'application (une app à moitié traduite ferait
        /// plus de mal que de bien).
        /// </summary>
        public string? Wo { get; set; }

        /// <summary>
        /// Section déduite du préfixe de la clé (« school_info », « payment »…).
        /// Stockée pour pouvoir grouper et paginer sans recalculer : 1660 clés
        /// d'un bloc ne se relisent pas, par section elles se relisent.
        /// </summary>
        public string Section { get; set; } = string.Empty;

        /// <summary>
        /// La clé n'existe plus dans le dernier import. On ne la SUPPRIME pas :
        /// une clé peut disparaître le temps d'une refonte et revenir, et
        /// supprimer emporterait les propositions déjà écrites. Elle est
        /// simplement masquée aux relecteurs.
        /// </summary>
        public bool IsObsolete { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public ICollection<TranslationProposal> Proposals { get; set; } = new List<TranslationProposal>();
    }

    /// <summary>
    /// Correction ou traduction proposée par un relecteur pour une clé donnée,
    /// dans une langue donnée. Append-only du point de vue du relecteur : il
    /// modifie SA proposition, il n'écrase jamais celle d'un autre.
    /// </summary>
    public class TranslationProposal
    {
        public int Id { get; set; }

        public int TranslationKeyId { get; set; }
        public TranslationKey TranslationKey { get; set; } = null!;

        public int ReviewerId { get; set; }
        public TranslationReviewer Reviewer { get; set; } = null!;

        /// <summary>Code langue : « ar » ou « wo ».</summary>
        public string Lang { get; set; } = string.Empty;

        /// <summary>
        /// Texte proposé. Une chaîne vide signifie « je retire ma proposition »
        /// et non « le texte doit être vide » : appliquer un vide effacerait un
        /// libellé de l'application.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Commentaire libre du relecteur (contexte, doute, question).</summary>
        public string? Note { get; set; }

        /// <summary>
        /// Valeur affichée au relecteur au moment où il a proposé. Permet de
        /// repérer une proposition devenue caduque parce que le texte de
        /// référence a changé entre-temps.
        /// </summary>
        public string? BasedOn { get; set; }

        /// <summary>Le SuperAdmin a repris la proposition dans l'application.</summary>
        public bool IsApplied { get; set; }
        public DateTime? AppliedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Personne invitée à relire une langue. Elle n'a pas de compte Idara :
    /// un relecteur arabophone n'est pas forcément utilisateur de la
    /// plateforme, et lui imposer une inscription ferait perdre la moitié des
    /// bonnes volontés. Il ouvre un lien, il travaille.
    /// </summary>
    public class TranslationReviewer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Langue confiée à ce relecteur : « ar » ou « wo ».</summary>
        public string Lang { get; set; } = string.Empty;

        /// <summary>
        /// Jeton opaque de 128 bits qui EST l'authentification : le lien vaut
        /// accès. Même principe que le lien de paiement (§161) et la page de
        /// résultat (§60). Révocable en désactivant le relecteur.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime? LastSeenAt { get; set; }

        public ICollection<TranslationProposal> Proposals { get; set; } = new List<TranslationProposal>();
    }
}
