using Idara.API.Enums;

namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Utilisateur rattaché à une école pour la gestion côté SchoolAdmin
    /// (enseignants, personnel, et parents liés via leurs enfants). Pour les
    /// parents, <see cref="Children"/> liste les enfants liés (pour la
    /// dissociation ciblée).
    /// </summary>
    public class SchoolUserDto
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string Role { get; set; } = string.Empty;
        public AccountStatus AccountStatus { get; set; }
        public string? PhoneNumber { get; set; }

        /// <summary>false = personnel « sans appli » (pointé seulement, pas de login).</summary>
        public bool CanLogin { get; set; } = true;

        /// <summary>Fonction libre (« Cuisinière », « Comptable »…). Null si absente.</summary>
        public string? JobTitle { get; set; }

        /// <summary>Date de création du compte (= date d'invitation pour les
        /// comptes ajoutés par l'école). Sert à contextualiser une relance
        /// (« invité il y a 5 j, jamais connecté »).</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Dernière connexion. <c>null</c> = ne s'est JAMAIS connecté
        /// (les comptes invités sont créés sans LastLoginAt) → cible de relance.</summary>
        public DateTime? LastLoginAt { get; set; }

        public List<SchoolUserChildDto> Children { get; set; } = new();
    }

    public class SchoolUserChildDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string? Relationship { get; set; }
    }
}
