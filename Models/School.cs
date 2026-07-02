using Idara.API.Enums;

namespace Idara.API.Models
{
    public class School
    {
        public int Id { get; set; }
        public KycStatus KycStatus { get; set; } = KycStatus.PendingSubmission;
        public string? Name { get; set; }
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

        public ICollection<User> Users { get; set; } = new List<User>();
    }
}
