using Idara.API.Enums;

namespace Idara.API.Models
{
    public class User
    {
        public int Id { get; set; }

        /// <summary>
        /// Email — identifiant de connexion pour SuperAdmin/SchoolAdmin. NULLABLE
        /// depuis l'incrément 2 Phase 2 : les comptes ajoutés par les écoles
        /// (Teacher/SchoolStaff/Guardian) s'identifient par TÉLÉPHONE et n'ont pas
        /// forcément d'email. Unicité email vérifiée en code (pas d'index DB).
        /// </summary>
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// Si false, le compte ne peut PAS se connecter (personnel « sans appli »
        /// — ex. cuisinière, gardien — créé uniquement pour être pointé par le
        /// surveillant). Aucun SMS/code n'est envoyé à sa création. Défaut true.
        /// </summary>
        public bool CanLogin { get; set; } = true;

        /// <summary>
        /// Fonction libre saisie par l'école (« Cuisinière », « Comptable »,
        /// « Gardien »…), indépendante du rôle technique. Sert d'étiquette
        /// d'affichage / sous-catégorie du personnel. Null = non renseignée.
        /// </summary>
        public string? JobTitle { get; set; }

        /// <summary>
        /// Nature du compte DONATEUR (particulier vs organisation/Dahira/fondation),
        /// choisie à l'auto-inscription. NULL pour tous les autres rôles — n'a de
        /// sens que quand <c>Role == UserRoles.Donor</c>. Affiché au daara sur
        /// chaque don (chip Particulier / Organisation).
        /// </summary>
        public DonorType? DonorType { get; set; }

        public bool IsEmailVerified { get; set; } = true;
        public AccountStatus AccountStatus { get; set; } = AccountStatus.Inactive;
        public int? SchoolId { get; set; }
        public School? School { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }

        /// <summary>
        /// Langue préférée pour les emails et notifications.
        /// Codes ISO 2 lettres : "fr" (défaut) ou "ar".
        /// </summary>
        public string PreferredLanguage { get; set; } = "fr";

        /// <summary>
        /// Suppression logique. Mis à <c>true</c> lors d'une suppression
        /// SuperAdmin quand l'utilisateur a un historique (paiements, journaux,
        /// affectations…) qu'on ne peut pas effacer sans casser l'intégrité
        /// financière append-only (cf. UsersController.DeleteUser). Un User
        /// vierge (aucune référence) est supprimé physiquement à la place.
        /// Quand <c>true</c> : PII scrubées, email anonymisé (libère l'original),
        /// PasswordHash vidé (login impossible). Exclu des listings et du login.
        /// </summary>
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
    }
}
