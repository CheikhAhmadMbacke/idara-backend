using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Sms
{
    /// <summary>Une ligne du journal des envois.</summary>
    public class SmsLogDto
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Numéro MASQUÉ (77 123 ** **) : le back-office sert à
        /// comprendre une dépense, pas à consulter les carnets d'adresses des
        /// écoles. Le numéro complet reste en base pour la déduplication.</summary>
        public string Recipient { get; set; } = string.Empty;

        public int? SchoolId { get; set; }
        public string? SchoolName { get; set; }

        /// <summary>Événement déclencheur, en clair (« Mensualité due »).</summary>
        public string Event { get; set; } = string.Empty;
        public string TemplateCode { get; set; } = string.Empty;

        /// <summary>D'où vient l'envoi (cron, endpoint, webhook).</summary>
        public string? TriggerSource { get; set; }
        public int? TriggerUserId { get; set; }

        public string Channel { get; set; } = string.Empty;
        public string Encoding { get; set; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public int CharCount { get; set; }
        public int Segments { get; set; }
        public int SegmentsFixed160 { get; set; }

        /// <summary>Coût en FCFA (décimal : le segment on-net vaut 3,50 F).</summary>
        public double CostFcfa { get; set; }

        public bool Success { get; set; }
        public string? Error { get; set; }

        /// <summary>Motif de blocage EN CLAIR, null si l'envoi a bien eu lieu.</summary>
        public string? Blocked { get; set; }
        public string Priority { get; set; } = string.Empty;
    }

    /// <summary>Un poste de la répartition (par école, par événement, par jour…).</summary>
    public class SmsBreakdownRowDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Messages { get; set; }
        public int Segments { get; set; }
        public double CostFcfa { get; set; }
        public int Failed { get; set; }
        public int Blocked { get; set; }
    }

    /// <summary>
    /// Le total attendu d'un mois, décomposé exactement comme une facture
    /// Sonatel : consommation + redevance + TVA. Sans la redevance (10 000 F HT
    /// même à zéro SMS) et sans la TVA, la comparaison paraîtrait toujours
    /// fausse alors qu'elle ne le serait pas.
    /// </summary>
    public class SmsMonthTotalsDto
    {
        public int Year { get; set; }
        public int Month { get; set; }

        public int MessagesSent { get; set; }
        public int MessagesFailed { get; set; }
        public int MessagesBlocked { get; set; }

        /// <summary>Segments selon la norme SMS — l'hypothèse de référence.</summary>
        public int Segments { get; set; }

        /// <summary>Segments selon l'autre lecture du contrat (« lot de 160
        /// caractères »). Affiché À CÔTÉ, et non à la place : c'est la facture
        /// reçue qui dira laquelle des deux Orange applique réellement.</summary>
        public int SegmentsFixed160 { get; set; }

        public double ConsumptionHtFcfa { get; set; }
        public double ConsumptionFixed160HtFcfa { get; set; }

        public long MonthlyFeeHtFcfa { get; set; }
        public double VatPercent { get; set; }

        /// <summary>Consommation + redevance, hors taxes.</summary>
        public double ExpectedHtFcfa { get; set; }

        /// <summary>Le montant à confronter au TTC de la facture.</summary>
        public double ExpectedTtcFcfa { get; set; }

        /// <summary>Même total, calculé sur l'autre hypothèse de facturation.</summary>
        public double ExpectedTtcFixed160Fcfa { get; set; }

        // ---- Facture réellement reçue, si elle a été saisie ----
        public bool InvoiceRecorded { get; set; }
        public long InvoiceHtFcfa { get; set; }
        public long InvoiceTtcFcfa { get; set; }
        public int? InvoiceQuantity { get; set; }
        public string? InvoiceNote { get; set; }

        /// <summary>Facture reçue − total attendu (TTC). Positif = on paie plus
        /// que ce qu'Idara a envoyé : à comprendre avant de régler.</summary>
        public double GapTtcFcfa { get; set; }
    }

    /// <summary>Où en est la dépense face aux plafonds, à l'instant présent.</summary>
    public class SmsBudgetStateDto
    {
        public bool KillSwitch { get; set; }

        public double SpentTodayFcfa { get; set; }
        public double SpentMonthFcfa { get; set; }

        public long SoftDailyCapFcfa { get; set; }
        public long SoftMonthlyCapFcfa { get; set; }
        public long HardDailyCapFcfa { get; set; }
        public long HardMonthlyCapFcfa { get; set; }

        /// <summary>« Normal », « Palier d'alerte atteint », « Tout suspendu ».</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Envois refusés par le garde-fou sur 24 h, par motif — c'est
        /// le tableau de bord d'une attaque en cours.</summary>
        public List<SmsBreakdownRowDto> BlockedLast24h { get; set; } = new();
    }

    /// <summary>Consommation d'une école, face à son plafond.</summary>
    public class SmsSchoolUsageDto
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public int Students { get; set; }

        public int SegmentsMonth { get; set; }
        public int MonthlyCapSegments { get; set; }
        public bool CapOverridden { get; set; }
        public double CostMonthFcfa { get; set; }

        public bool Suspended { get; set; }
        public string? SuspendedReason { get; set; }

        /// <summary>Part du plafond consommée (0-100+). Au-delà de 100, l'école
        /// est bloquée pour ses envois non critiques.</summary>
        public double UsagePercent { get; set; }
    }

    /// <summary>Tout ce qu'affiche l'écran « SMS » du back-office, en un appel.</summary>
    public class SmsOverviewDto
    {
        public SmsMonthTotalsDto Totals { get; set; } = new();
        public SmsBudgetStateDto Budget { get; set; } = new();
        public List<SmsBreakdownRowDto> BySchool { get; set; } = new();
        public List<SmsBreakdownRowDto> ByEvent { get; set; } = new();
        public List<SmsBreakdownRowDto> ByDay { get; set; } = new();
        public List<SmsBreakdownRowDto> ByNetwork { get; set; } = new();
        public List<SmsSchoolUsageDto> SchoolsAtRisk { get; set; } = new();

        /// <summary>
        /// Date à partir de laquelle le coût est réellement mesuré. Les envois
        /// antérieurs existent au journal mais sans chiffrage : on ne peut pas
        /// recalculer après coup le coût d'un message dont le texte n'a pas été
        /// conservé. Affiché pour qu'un total partiel ne soit pas lu comme un
        /// total complet.
        /// </summary>
        public DateTime? MeasuredSince { get; set; }
    }

    /// <summary>Réglages SMS, éditables au back-office.</summary>
    public class SmsSettingsDto
    {
        public bool Bilingual { get; set; }
        public bool KillSwitch { get; set; }

        public long OnNetPriceCentimes { get; set; }
        public long OffNetPriceCentimes { get; set; }
        public long InternationalPriceCentimes { get; set; }
        public long MonthlyFeeHtFcfa { get; set; }
        public double VatPercent { get; set; }

        public long SoftDailyCapFcfa { get; set; }
        public long SoftMonthlyCapFcfa { get; set; }
        public long HardDailyCapFcfa { get; set; }
        public long HardMonthlyCapFcfa { get; set; }

        public int SchoolMonthlySegmentsPerStudent { get; set; }
        public int SchoolMonthlyFloorSegments { get; set; }
        public int SchoolDailySegmentsPerStudent { get; set; }
        public int SchoolDailyFloorSegments { get; set; }
        public int SchoolHourlySegmentsPerStudent { get; set; }
        public int SchoolHourlyFloorSegments { get; set; }

        public int MaxMessagesPerDistinctRecipient { get; set; }
        public int RatioMinMessages { get; set; }
        public int MaxPerRecipientPerDay { get; set; }
        public int MaxPerRecipientPerMonth { get; set; }
    }

    /// <summary>
    /// Mise à jour des réglages SMS. Bornes volontairement larges en haut : ce
    /// sont des garde-fous, et un plafond qu'on ne peut pas relever assez vite
    /// un jour de rentrée est un plafond qu'on finit par désarmer.
    /// </summary>
    public class UpdateSmsSettingsDto
    {
        public bool Bilingual { get; set; }
        public bool KillSwitch { get; set; }

        [Range(0, 100_000)] public long OnNetPriceCentimes { get; set; }
        [Range(0, 100_000)] public long OffNetPriceCentimes { get; set; }
        [Range(0, 1_000_000)] public long InternationalPriceCentimes { get; set; }
        [Range(0, 10_000_000)] public long MonthlyFeeHtFcfa { get; set; }
        [Range(0, 100)] public double VatPercent { get; set; }

        [Range(0, 100_000_000)] public long SoftDailyCapFcfa { get; set; }
        [Range(0, 100_000_000)] public long SoftMonthlyCapFcfa { get; set; }
        [Range(0, 100_000_000)] public long HardDailyCapFcfa { get; set; }
        [Range(0, 100_000_000)] public long HardMonthlyCapFcfa { get; set; }

        [Range(1, 10_000)] public int SchoolMonthlySegmentsPerStudent { get; set; }
        [Range(1, 1_000_000)] public int SchoolMonthlyFloorSegments { get; set; }
        [Range(1, 10_000)] public int SchoolDailySegmentsPerStudent { get; set; }
        [Range(1, 1_000_000)] public int SchoolDailyFloorSegments { get; set; }
        [Range(1, 10_000)] public int SchoolHourlySegmentsPerStudent { get; set; }
        [Range(1, 1_000_000)] public int SchoolHourlyFloorSegments { get; set; }

        [Range(1, 1_000)] public int MaxMessagesPerDistinctRecipient { get; set; }
        [Range(1, 100_000)] public int RatioMinMessages { get; set; }
        [Range(1, 1_000)] public int MaxPerRecipientPerDay { get; set; }
        [Range(1, 10_000)] public int MaxPerRecipientPerMonth { get; set; }
    }

    /// <summary>Saisie de la facture Orange d'un mois.</summary>
    public class RecordSmsInvoiceDto
    {
        [Range(2020, 2100)] public int Year { get; set; }
        [Range(1, 12)] public int Month { get; set; }
        [Range(0, 1_000_000_000)] public long AmountHtFcfa { get; set; }
        [Range(0, 1_000_000_000)] public long AmountTtcFcfa { get; set; }
        [Range(0, 100_000_000)] public int? ProviderQuantity { get; set; }
        [StringLength(500)] public string? Note { get; set; }
    }

    /// <summary>Relève ponctuellement le plafond mensuel d'une école (rentrée…).</summary>
    public class SetSchoolSmsCapDto
    {
        /// <summary>Segments par mois. <c>null</c> = revenir au calcul sur
        /// l'effectif. La convention « null = par défaut » est celle des autres
        /// PATCH partiels du projet (§12).</summary>
        [Range(1, 10_000_000)] public int? MonthlyCapSegments { get; set; }
    }

    /// <summary>Suspend ou rétablit les SMS non critiques d'une école.</summary>
    public class SetSchoolSmsSuspensionDto
    {
        public bool Suspended { get; set; }

        [StringLength(300)] public string? Reason { get; set; }
    }
}
