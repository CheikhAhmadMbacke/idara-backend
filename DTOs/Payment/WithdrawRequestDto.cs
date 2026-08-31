using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Validation;
using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Corps de `POST /api/school/wallet/withdraw` (transfert sortant unifié :
    /// retrait simple OU paiement d'un bénéficiaire du carnet).
    ///
    /// Deux modes :
    ///  - <b>Carnet</b> : <see cref="BeneficiaryId"/> fourni → nom/téléphone/
    ///    opérateur viennent du bénéficiaire enregistré (les champs manuels sont
    ///    ignorés).
    ///  - <b>Ponctuel</b> : <see cref="BeneficiaryId"/> null → saisie manuelle
    ///    complète (nom + téléphone × 2 confirmation + opérateur), comme avant.
    ///
    /// Le montant minimum n'est plus codé en dur ici : il est vérifié serveur
    /// contre PlatformSettings.MinWithdrawalFcfa (éditable SuperAdmin), pour
    /// permettre des transferts plus petits (ex. petits salaires). On garde juste
    /// un plancher positif.
    /// </summary>
    public class WithdrawRequestDto : IValidatableObject
    {
        [Range(1, long.MaxValue, ErrorMessage = "Le montant doit être positif.")]
        public long Amount { get; set; }

        /// <summary>Nature du transfert (retrait, salaire, loyer…).</summary>
        public TransferCategory Category { get; set; } = TransferCategory.Withdrawal;

        /// <summary>
        /// Poche du wallet dans laquelle puiser (le daara choisit) : Total (défaut),
        /// Fee (solde paiement uniquement) ou Donation (solde don uniquement).
        /// </summary>
        public WithdrawalSource Source { get; set; } = WithdrawalSource.Total;

        /// <summary>Nom de la nature, obligatoire quand Category == Other (sinon ignoré).</summary>
        [StringLength(120, ErrorMessage = "La catégorie ne doit pas dépasser 120 caractères.")]
        public string? CategoryLabel { get; set; }

        /// <summary>
        /// Motif / détails de l'opération (optionnel) — distinct de la catégorie.
        /// </summary>
        [StringLength(300, ErrorMessage = "Le motif ne doit pas dépasser 300 caractères.")]
        public string? Motif { get; set; }

        /// <summary>Bénéficiaire du carnet. Null = saisie manuelle ponctuelle.</summary>
        public int? BeneficiaryId { get; set; }

        [OptionalStringLength(120, MinimumLength = 3, ErrorMessage = "Le nom doit faire au moins 3 caractères.")]
        public string? RecipientName { get; set; }

        /// <summary>Numéro national sénégalais : 9 chiffres commençant par 7.</summary>
        public string? RecipientPhone { get; set; }

        public string? RecipientPhoneConfirm { get; set; }

        /// <summary>"wave" ou "orange".</summary>
        public string? Operator { get; set; }

        /// <summary>
        /// Mot de passe du SchoolAdmin — step-up de sécurité au retrait (remplace
        /// l'OTP). Vérifié côté serveur (BCrypt). Saisi une fois au verrou de
        /// l'écran paiement puis réutilisé.
        /// </summary>
        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        public string Password { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Catégorie « Autre » → un libellé libre est obligatoire.
            if (Category == TransferCategory.Other &&
                string.IsNullOrWhiteSpace(CategoryLabel))
                yield return new ValidationResult(
                    "Précisez la catégorie.", new[] { nameof(CategoryLabel) });

            // En mode saisie ponctuelle (pas de bénéficiaire du carnet), les
            // coordonnées manuelles deviennent obligatoires et validées.
            if (BeneficiaryId == null)
            {
                if (string.IsNullOrWhiteSpace(RecipientName) || RecipientName.Trim().Length < 3)
                    yield return new ValidationResult(
                        "Le nom du bénéficiaire est obligatoire.", new[] { nameof(RecipientName) });

                if (string.IsNullOrWhiteSpace(RecipientPhone) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(RecipientPhone, @"^7\d{8}$"))
                    yield return new ValidationResult(
                        "Numéro invalide (format attendu : 7XXXXXXXX).", new[] { nameof(RecipientPhone) });

                if (RecipientPhone != RecipientPhoneConfirm)
                    yield return new ValidationResult(
                        "Les deux numéros ne correspondent pas.", new[] { nameof(RecipientPhoneConfirm) });

                var op = Operator?.ToLowerInvariant();
                if (op != "wave" && op != "orange")
                    yield return new ValidationResult(
                        "L'opérateur est obligatoire (wave ou orange).", new[] { nameof(Operator) });
            }
        }
    }
}
