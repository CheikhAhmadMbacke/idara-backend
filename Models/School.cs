using Idara.API.Enums;

namespace Idara.API.Models
{
    public class School
    {
        public int Id { get; set; }
        public KycStatus KycStatus { get; set; } = KycStatus.PendingSubmission;

        /// <summary>
        /// Nom du daara en français. Nullable comme <see cref="NameAr"/> : la
        /// règle métier est « au moins l'un des deux », vérifiée dans les DTO de
        /// saisie (KYC + édition), pas au niveau du schéma — une contrainte DB
        /// portant sur deux colonnes bloquerait les écoles existantes créées
        /// avant l'ajout du nom arabe.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Nom du daara en arabe. Optionnel. Affiché SOUS le nom français (jamais
        /// concaténé sur la même ligne, cf. <see cref="Common.Utilities.SchoolDisplayName"/>) ;
        /// devient le nom principal si le nom français est absent.
        /// </summary>
        public string? NameAr { get; set; }

        public string? Address { get; set; }
        public string? PhoneNumber { get; set; }
        public string? LegalDocumentsUrl { get; set; }
        public string? RepresentativeFirstName { get; set; }
        public string? RepresentativeLastName { get; set; }
        public string? RepresentativePhone { get; set; }
        public string? RepresentativeIdDocumentUrl { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public int? ValidatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Lecture (riwâya) du Coran utilisée par l'école pour
        /// l'autocomplétion du texte. Warsh par défaut.</summary>
        /// <summary>
        /// Nature de l'établissement (daara, franco-arabe, classique…).
        /// NULL = non renseigné : les écoles créées avant ce champ n'en ont pas,
        /// et on ne leur en invente pas (cf. <see cref="Enums.SchoolType"/>).
        /// </summary>
        public SchoolType? Type { get; set; }

        public QuranRiwaya QuranRiwaya { get; set; } = QuranRiwaya.Warsh;

        // ----- Personnalisation de l'espace (branding) -----
        // Affiché sur la carte d'accueil de TOUS les utilisateurs de l'école
        // (admin/personnel/enseignant/surveillant/observateur). Le titre = Name.

        /// <summary>Logo du daara (remplace l'icône Idara sur la carte d'accueil). URL relative /uploads/school-branding/…</summary>
        public string? LogoUrl { get; set; }

        /// <summary>Sous-titre éditable de la carte d'accueil (défaut côté client si null).</summary>
        public string? WelcomeSubtitle { get; set; }

        /// <summary>Couleur de fond de la carte d'accueil (hex "#RRGGBB"). Null = dégradé vert par défaut.</summary>
        public string? CoverColor { get; set; }

        /// <summary>Image de couverture de la carte d'accueil. Prioritaire sur CoverColor si renseignée.</summary>
        public string? CoverImageUrl { get; set; }

        // ----- Garde-fou SMS (2026-09-01) -----

        /// <summary>
        /// Relève ponctuellement le plafond SMS mensuel de CETTE école, en
        /// segments. NULL (défaut) = plafond calculé sur l'effectif.
        ///
        /// <para>Indispensable pour que le garde-fou reste vivable : une rentrée
        /// où l'école crée trois cents comptes d'un coup est légitime et sortira
        /// du plafond ordinaire. Sans cette soupape, la seule issue serait de
        /// relever le plafond de TOUTES les écoles — c'est-à-dire de désarmer le
        /// dispositif pour tout le monde à cause d'une seule.</para>
        /// </summary>
        public int? SmsMonthlyCapOverrideSegments { get; set; }

        /// <summary>
        /// Suspend tous les SMS non critiques de CETTE école. Posé
        /// automatiquement quand un emballement est détecté, retiré à la main
        /// par le SuperAdmin une fois la cause comprise. Les codes de connexion
        /// et les identifiants continuent de partir : on isole l'école, on ne
        /// l'enferme pas dehors.
        /// </summary>
        public bool SmsSuspended { get; set; }

        /// <summary>Motif et date de la suspension, pour que l'écran dise POURQUOI
        /// une école ne notifie plus.</summary>
        public string? SmsSuspendedReason { get; set; }

        public DateTime? SmsSuspendedAt { get; set; }

        public ICollection<User> Users { get; set; } = new List<User>();
    }
}
