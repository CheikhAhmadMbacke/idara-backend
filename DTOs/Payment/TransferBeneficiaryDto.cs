using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    /// <summary>Bénéficiaire du carnet, vue lecture. Numéro masqué dans les listes.</summary>
    public class TransferBeneficiaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneMasked { get; set; } = string.Empty;
        public PaymentOperator Operator { get; set; }
        public TransferCategory DefaultCategory { get; set; }
        public string? Note { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateTransferBeneficiaryDto
    {
        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [StringLength(120, MinimumLength = 3, ErrorMessage = "Le nom doit faire au moins 3 caractères.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le numéro est obligatoire.")]
        [RegularExpression(@"^7\d{8}$", ErrorMessage = "Numéro invalide (format attendu : 7XXXXXXXX).")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'opérateur est obligatoire.")]
        public string Operator { get; set; } = string.Empty;

        public TransferCategory DefaultCategory { get; set; } = TransferCategory.Other;

        [StringLength(200)]
        public string? Note { get; set; }
    }

    public class UpdateTransferBeneficiaryDto : CreateTransferBeneficiaryDto
    {
        [Required] public int Id { get; set; }
    }
}
