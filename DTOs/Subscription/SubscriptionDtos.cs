using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Subscription
{
    /// <summary>Plan tarifaire (réponse SuperAdmin + école).</summary>
    public class SubscriptionPlanDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int? StudentMin { get; set; }
        public int? StudentMax { get; set; }
        public long MonthlyPriceFcfa { get; set; }
        public long AnnualPriceFcfa { get; set; }
        public int NotificationQuota { get; set; }
        public bool IsActive { get; set; }
        public bool IsCustom { get; set; }
        public int? SchoolId { get; set; }
        public string? SchoolName { get; set; }
    }

    /// <summary>Édition d'un plan (public ou custom) par le SuperAdmin.</summary>
    public class UpdateSubscriptionPlanDto
    {
        [Required, StringLength(60, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 100_000_000)]
        public long MonthlyPriceFcfa { get; set; }

        [Range(0, 1_000_000_000)]
        public long AnnualPriceFcfa { get; set; }

        [Range(0, 1_000_000)]
        public int NotificationQuota { get; set; }

        public bool IsActive { get; set; } = true;

        [Range(0, 100000)]
        public int? StudentMin { get; set; }

        [Range(0, 100000)]
        public int? StudentMax { get; set; }
    }

    /// <summary>Création d'un plan PUBLIC (visible/choisissable par toutes les écoles).</summary>
    public class CreateSubscriptionPlanDto
    {
        [Required, StringLength(60, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Code unique (slug). Optionnel : auto-généré depuis le nom si vide.</summary>
        [StringLength(40)]
        public string? Code { get; set; }

        [Range(0, 100_000_000)]
        public long MonthlyPriceFcfa { get; set; }

        [Range(0, 1_000_000_000)]
        public long AnnualPriceFcfa { get; set; }

        [Range(0, 1_000_000)]
        public int NotificationQuota { get; set; }

        [Range(0, 100000)]
        public int? StudentMin { get; set; }

        [Range(0, 100000)]
        public int? StudentMax { get; set; }
    }

    /// <summary>Création d'un deal custom pour une école précise.</summary>
    public class CreateCustomPlanDto
    {
        [Required]
        public int SchoolId { get; set; }

        [Required, StringLength(60, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 100_000_000)]
        public long MonthlyPriceFcfa { get; set; }

        [Range(0, 1_000_000_000)]
        public long AnnualPriceFcfa { get; set; }

        [Range(0, 1_000_000)]
        public int NotificationQuota { get; set; }
    }

    /// <summary>Abonnement d'une école (réponse SuperAdmin + école).</summary>
    public class SubscriptionDto
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string? SchoolName { get; set; }
        public int? PlanId { get; set; }
        public string? PlanName { get; set; }
        public BillingCycle BillingCycle { get; set; }
        public SubscriptionStatus Status { get; set; }
        public long AmountFcfa { get; set; }
        public int NotificationQuota { get; set; }
        public int NotificationUsedThisCycle { get; set; }
        public DateTime TrialEndsAt { get; set; }
        public DateTime NextBillingAt { get; set; }
        public DateTime? GracePeriodEndsAt { get; set; }
        public DateTime? ReadOnlyEndsAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public DateTime? SuspendedAt { get; set; }
    }

    /// <summary>Assigner / changer le plan d'une école (SuperAdmin, ou école pour son propre downgrade/upgrade).</summary>
    public class AssignPlanDto
    {
        [Required]
        public int PlanId { get; set; }

        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    }

    /// <summary>Recharge wallet école par le SchoolAdmin (topup via SenePay payin).</summary>
    public class TopupRequestDto
    {
        [Range(200, 100_000_000, ErrorMessage = "Le montant doit être d'au moins 200 FCFA.")]
        public long Amount { get; set; }

        /// <summary>« wave » ou « orange ».</summary>
        [Required]
        public string Operator { get; set; } = string.Empty;

        /// <summary>Numéro national 9 chiffres (sans +221).</summary>
        [Required, RegularExpression(@"^\d{9}$", ErrorMessage = "Numéro à 9 chiffres attendu.")]
        public string Phone { get; set; } = string.Empty;

        /// <summary>OTP Orange (généré via #144#391# sur le téléphone). Ignoré pour Wave.</summary>
        public string? OtpCode { get; set; }
    }

    /// <summary>Statut d'une recharge wallet (poll).</summary>
    public class TopupStatusDto
    {
        public int PaymentId { get; set; }
        public PaymentStatus Status { get; set; }
        public long AmountFcfa { get; set; }       // débité du payeur (target × 1,08)
        public long NetCreditedFcfa { get; set; }  // net SenePay (interne)
        public long CreditedFcfa { get; set; }     // réellement ajouté au wallet (= target)
        public string? FailureReason { get; set; }
    }

    /// <summary>
    /// Adéquation effectif ↔ plan d'abonnement (pour prévenir l'école sans la
    /// bloquer quand elle dépasse le plafond de son plan).
    /// </summary>
    public class SubscriptionCapacityDto
    {
        public int StudentCount { get; set; }
        public string? PlanName { get; set; }
        /// <summary>Plafond d'élèves du plan courant (null = illimité).</summary>
        public int? StudentMax { get; set; }
        /// <summary>True si l'effectif dépasse le plafond d'un plan public borné.</summary>
        public bool ExceedsCap { get; set; }
        /// <summary>Plan vers lequel l'école sera auto-remontée au prochain prélèvement.</summary>
        public string? SuggestedPlanName { get; set; }
        public long? SuggestedPlanMonthlyFcfa { get; set; }
    }
}
