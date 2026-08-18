using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    /// <summary>
    /// Corps de POST /api/auth/users/{userId}/credentials-sms (bouton « SMS » du
    /// modal récap). Le code affiché dans le modal est renvoyé au serveur (qui ne
    /// le connaît qu'en BCrypt) ; strictement 6 chiffres, et vérifié contre le
    /// hash du compte cible avant tout envoi.
    /// </summary>
    public class SendCredentialsSmsRequest
    {
        [Required]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Code invalide.")]
        public string Code { get; set; } = string.Empty;
    }
}
