using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    public class SchoolPaymentSettingsDto
    {
        public int SchoolId { get; set; }
        public BillingMode BillingMode { get; set; }
        public FeesPayer FeesPayer { get; set; }
        public int MonthlyDueDay { get; set; }
        public BillingPeriod BillingPeriod { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdateSchoolPaymentSettingsDto
    {
        [Required]
        public BillingMode BillingMode { get; set; }

        [Required]
        public FeesPayer FeesPayer { get; set; }

        /// <summary>Jour du mois (1..28) où le cron génère les Invoices. Borné à 28 pour
        /// éviter les mois courts (février) sans tarif.</summary>
        [Range(1, 28, ErrorMessage = "MonthlyDueDay doit être entre 1 et 28.")]
        public int MonthlyDueDay { get; set; } = 5;

        [Required]
        public BillingPeriod BillingPeriod { get; set; }
    }
}
