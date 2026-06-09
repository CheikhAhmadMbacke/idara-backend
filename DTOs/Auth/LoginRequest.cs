using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    public class LoginRequest
    {
        /// <summary>
        /// Identifiant de connexion : email (SuperAdmin/SchoolAdmin) OU numéro de
        /// téléphone (parents, enseignants, personnel). Le champ JSON reste
        /// "email" pour la rétro-compatibilité avec le client existant ; il est
        /// résolu en email si la valeur contient "@", sinon normalisé en numéro.
        /// </summary>
        [Required(ErrorMessage = "L'email ou le numéro est requis.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est requis.")]
        public string Password { get; set; } = string.Empty;
    }
}
