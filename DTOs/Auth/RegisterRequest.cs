using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Auth
{
    public class RegisterRequest
    {
        /// <summary>
        /// Adresse email <b>ou</b> numéro de téléphone — le même identifiant
        /// qu'à l'envoi du code. Validé par <c>AuthIdentifier.Parse</c>, jamais
        /// par attribut (voir <see cref="SendOtpRequest.Identifier"/>).
        /// </summary>
        public string? Identifier { get; set; }

        /// <summary>
        /// Ancien nom du champ, toléré pour les applications déjà installées
        /// (§220). Voir <see cref="SendOtpRequest.Email"/>.
        /// </summary>
        public string? Email { get; set; }

        [Required(ErrorMessage = "Le code est requis.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Le code doit contenir 6 chiffres.")]
        public string OtpCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est requis.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Le mot de passe doit contenir au moins 8 caractères.")]
        public string Password { get; set; } = string.Empty;

        /// <summary>Langue préférée ("fr" ou "ar").</summary>
        public string? PreferredLanguage { get; set; }

        /// <summary>Ce que l'appelant a fourni, quel que soit le nom du champ.</summary>
        public string RawIdentifier =>
            !string.IsNullOrWhiteSpace(Identifier) ? Identifier! : (Email ?? string.Empty);
    }
}
