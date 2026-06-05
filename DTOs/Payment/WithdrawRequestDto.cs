using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Corps de `POST /api/school/wallet/withdraw`. Saisie manuelle complète à
    /// chaque retrait (pas de comptes pré-enregistrés — spec §4.2). Le montant,
    /// le solde et l'égalité des deux numéros sont aussi revalidés serveur.
    /// </summary>
    public class WithdrawRequestDto
    {
        /// <summary>Montant à retirer (FCFA). Min 25 000, max = AvailableBalance (vérifié serveur).</summary>
        [Range(25000, long.MaxValue, ErrorMessage = "Le montant minimum de retrait est de 25 000 FCFA.")]
        public long Amount { get; set; }

        [Required(ErrorMessage = "Le nom du bénéficiaire est obligatoire.")]
        [StringLength(120, MinimumLength = 3, ErrorMessage = "Le nom doit faire au moins 3 caractères.")]
        public string RecipientName { get; set; } = string.Empty;

        /// <summary>Numéro national sénégalais : 9 chiffres commençant par 7.</summary>
        [Required(ErrorMessage = "Le numéro du bénéficiaire est obligatoire.")]
        [RegularExpression(@"^7\d{8}$", ErrorMessage = "Numéro invalide (format attendu : 7XXXXXXXX).")]
        public string RecipientPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez confirmer le numéro.")]
        public string RecipientPhoneConfirm { get; set; } = string.Empty;

        /// <summary>"wave" ou "orange".</summary>
        [Required(ErrorMessage = "L'opérateur est obligatoire.")]
        public string Operator { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le code OTP est obligatoire.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Le code OTP doit contenir 6 chiffres.")]
        public string OtpCode { get; set; } = string.Empty;
    }
}
