using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Bénéficiaire du carnet, vue lecture. Le numéro est renvoyé EN CLAIR
    /// (<see cref="Phone"/>) : c'est l'école qui l'a saisi, elle doit pouvoir le
    /// vérifier, rappeler la personne, et le corriger sans le retaper en entier.
    /// <see cref="PhoneMasked"/> est conservé uniquement pour les anciennes
    /// versions de l'app installées (rétro-compatibilité) — ne plus l'utiliser.
    /// </summary>
    public class TransferBeneficiaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneMasked { get; set; } = string.Empty;

        /// <summary>Numéro complet (9 chiffres, format national).</summary>
        public string Phone { get; set; } = string.Empty;

        public PaymentOperator Operator { get; set; }
        public TransferCategory DefaultCategory { get; set; }

        /// <summary>Nom de la nature quand DefaultCategory == Other.</summary>
        public string? DefaultCategoryLabel { get; set; }

        public string? Note { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateTransferBeneficiaryDto : IValidatableObject
    {
        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [StringLength(120, MinimumLength = 3, ErrorMessage = "Le nom doit faire au moins 3 caractères.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le numéro est obligatoire.")]
        [RegularExpression(@"^7\d{8}$", ErrorMessage = "Numéro invalide (format attendu : 7XXXXXXXX).")]
        public string Phone { get; set; } = string.Empty;

        /// <summary>
        /// Ressaisie du numéro (confirmation), comme au retrait manuel : un
        /// numéro erroné enregistré au carnet enverrait de l'argent au mauvais
        /// destinataire à CHAQUE virement futur. Toléré null pour les anciennes
        /// versions de l'app ; vérifié dès qu'il est fourni.
        /// </summary>
        public string? PhoneConfirm { get; set; }

        [Required(ErrorMessage = "L'opérateur est obligatoire.")]
        public string Operator { get; set; } = string.Empty;

        public TransferCategory DefaultCategory { get; set; } = TransferCategory.Other;

        /// <summary>Nom de la nature, obligatoire quand DefaultCategory == Other.</summary>
        [StringLength(120, ErrorMessage = "La catégorie ne doit pas dépasser 120 caractères.")]
        public string? DefaultCategoryLabel { get; set; }

        [StringLength(200)]
        public string? Note { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (PhoneConfirm != null && PhoneConfirm.Trim() != Phone.Trim())
                yield return new ValidationResult(
                    "Les deux numéros ne correspondent pas.", new[] { nameof(PhoneConfirm) });

            if (DefaultCategory == TransferCategory.Other &&
                string.IsNullOrWhiteSpace(DefaultCategoryLabel))
                yield return new ValidationResult(
                    "Précisez la catégorie.", new[] { nameof(DefaultCategoryLabel) });
        }
    }

    public class UpdateTransferBeneficiaryDto : CreateTransferBeneficiaryDto
    {
        [Required] public int Id { get; set; }
    }
}
