using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Payment
{
    public class SchoolPaymentSettingsDto
    {
        public int SchoolId { get; set; }
        public BillingMode BillingMode { get; set; }
        public FeesPayer FeesPayer { get; set; }

        /// <summary>Qui paie les frais sur les DONS (Parent=donateur, School=daara).</summary>
        public FeesPayer DonationFeesPayer { get; set; }
        /// <summary>Jour d'OUVERTURE du paiement (émission + information des familles).</summary>
        public int MonthlyDueDay { get; set; }

        /// <summary>Jour LIMITE : au-delà, la mensualité non réglée est en retard.</summary>
        public int PaymentDeadlineDay { get; set; }

        public BillingPeriod BillingPeriod { get; set; }

        /// <summary>Tarif général appliqué aux élèves sans tarif plus spécifique. Null = non défini.</summary>
        public long? GeneralMonthlyFeeFcfa { get; set; }

        /// <summary>Tarif mensuel des internes. Null = non défini.</summary>
        public long? BoardingMonthlyFeeFcfa { get; set; }

        /// <summary>Tarif mensuel des demi-internes. Null = non défini.</summary>
        public long? HalfBoardingMonthlyFeeFcfa { get; set; }

        /// <summary>Tarif mensuel des externes. Null = non défini.</summary>
        public long? DayMonthlyFeeFcfa { get; set; }

        /// <summary>Frais d'inscription (une fois par élève). Null = non défini.</summary>
        public long? RegistrationFeeFcfa { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdateSchoolPaymentSettingsDto
    {
        [Required]
        public BillingMode BillingMode { get; set; }

        [Required]
        public FeesPayer FeesPayer { get; set; }

        /// <summary>Qui paie les frais sur les DONS (Parent=donateur, School=daara).</summary>
        [Required]
        public FeesPayer DonationFeesPayer { get; set; }

        /// <summary>Jour d'OUVERTURE (1..28) : le cron génère les mensualités et
        /// prévient les familles. Borné à 28 pour éviter les mois courts.</summary>
        [Range(1, 28, ErrorMessage = "Le jour d'ouverture doit être entre 1 et 28.")]
        public int MonthlyDueDay { get; set; } = 5;

        /// <summary>
        /// Jour LIMITE (1..28) : au-delà, la mensualité non réglée est en retard.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>NULLABLE, et ce n'est pas un détail.</b> Ce DTO est envoyé par
        /// TOUTES les versions de l'application. Une version antérieure au
        /// 2026-08-23 ne connaît pas ce champ : reçu à 0 sur un <c>int</c>, un
        /// simple enregistrement des réglages depuis un vieux téléphone
        /// ramènerait la limite au 1er du mois et déclencherait une vague de
        /// rappels de retard. <c>null</c> = « ne pas toucher » (5ᵉ occurrence du
        /// piège §137/§140/§152/§158).
        /// </remarks>
        [Range(1, 28, ErrorMessage = "Le jour limite doit être entre 1 et 28.")]
        public int? PaymentDeadlineDay { get; set; }

        [Required]
        public BillingPeriod BillingPeriod { get; set; }

        /// <summary>Tarif général école (FCFA/mois). Null ou 0 = pas de tarif général.</summary>
        [Range(0, 100_000_000, ErrorMessage = "Le tarif général doit être entre 0 et 100 000 000 FCFA.")]
        public long? GeneralMonthlyFeeFcfa { get; set; }

        // Tarifs par régime d'hébergement. Null ou 0 = pas de tarif pour ce
        // régime (les élèves concernés retombent sur le tarif de leur classe,
        // puis sur le tarif général).
        // ⚠️ Ces trois champs sont ABSENTS des requêtes envoyées par les
        // anciennes versions de l'application. Ils arriveraient donc à null et
        // EFFACERAIENT les tarifs saisis si on les recopiait tels quels —
        // FeesController ne les applique que lorsqu'ils sont fournis.

        [Range(0, 100_000_000, ErrorMessage = "Le tarif internat doit être entre 0 et 100 000 000 FCFA.")]
        public long? BoardingMonthlyFeeFcfa { get; set; }

        [Range(0, 100_000_000, ErrorMessage = "Le tarif demi-internat doit être entre 0 et 100 000 000 FCFA.")]
        public long? HalfBoardingMonthlyFeeFcfa { get; set; }

        [Range(0, 100_000_000, ErrorMessage = "Le tarif externat doit être entre 0 et 100 000 000 FCFA.")]
        public long? DayMonthlyFeeFcfa { get; set; }

        /// <summary>
        /// Drapeau posé par les versions de l'application qui gèrent les tarifs
        /// par statut. Sans lui, une ancienne version — qui n'envoie pas ces
        /// trois champs — remettrait les trois tarifs à zéro à chaque
        /// enregistrement des réglages, silencieusement.
        /// </summary>
        public bool IncludesBoardingFees { get; set; }

        /// <summary>Frais d'inscription (FCFA, une fois par élève). Null ou 0 = pas de frais.</summary>
        [Range(0, 100_000_000, ErrorMessage = "Les frais d'inscription doivent être entre 0 et 100 000 000 FCFA.")]
        public long? RegistrationFeeFcfa { get; set; }

        /// <summary>
        /// Même garde-fou de capacité que IncludesBoardingFees, pour le champ
        /// RegistrationFeeFcfa : absent = le serveur n'y touche pas.
        /// </summary>
        public bool IncludesRegistrationFee { get; set; }
    }
}
