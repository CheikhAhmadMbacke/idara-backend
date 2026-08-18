using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    /// <summary>
    /// Corps de POST /api/auth/me/language — l'app pousse la langue choisie par
    /// l'utilisateur dès qu'il la change, pour que « sa langue actuelle » (règle
    /// d'or des SMS/notifs) n'attende pas sa prochaine connexion.
    /// </summary>
    public class UpdateLanguageRequest
    {
        [Required]
        public string Language { get; set; } = string.Empty;
    }
}
