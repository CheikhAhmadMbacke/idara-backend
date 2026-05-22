using Idara.API.Enums;

namespace Idara.API.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
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
    }
}
