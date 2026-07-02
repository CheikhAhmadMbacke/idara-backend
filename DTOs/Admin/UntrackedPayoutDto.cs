namespace Idara.API.DTOs.Admin
{
    /// <summary>Un payout SenePay `completed` qui n'a PAS de retrait Idara correspondant
    /// (= effectué depuis le dashboard marchand, hors Idara).</summary>
    public class UntrackedPayoutDto
    {
        public string? DisbursementId { get; set; }
        public string? ExternalId { get; set; }
        public long AmountFcfa { get; set; }
        public long NetAmountFcfa { get; set; }
        public string? RecipientPhone { get; set; }
        public string? Operator { get; set; }
        public DateTime? CompletedAt { get; set; }

        /// <summary>true = déjà consigné dans les PlatformOutflows (ne sera pas réenregistré).</summary>
        public bool AlreadyRecorded { get; set; }
    }

    /// <summary>Anomalie : payout SenePay `completed` dont l'external_id correspond à un
    /// retrait Idara qui n'est PAS Completed (argent sorti mais Idara croit à un échec).</summary>
    public class PayoutAnomalyDto
    {
        public string? DisbursementId { get; set; }
        public int WithdrawalId { get; set; }
        public string IdaraStatus { get; set; } = string.Empty;
        public long AmountFcfa { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>Résultat du scan de rapprochement payouts SenePay ↔ retraits Idara.</summary>
    public class UntrackedPayoutsResultDto
    {
        public List<UntrackedPayoutDto> Untracked { get; set; } = new();
        public List<PayoutAnomalyDto> Anomalies { get; set; } = new();

        /// <summary>Total des orphelins PAS encore consignés (à enregistrer).</summary>
        public long TotalToRecordFcfa { get; set; }

        /// <summary>Total des orphelins déjà consignés.</summary>
        public long TotalAlreadyRecordedFcfa { get; set; }

        /// <summary>Nombre total de payouts SenePay `completed` examinés.</summary>
        public int ScannedCount { get; set; }

        /// <summary>false si SenePay injoignable (résultat non fiable).</summary>
        public bool SenePayReachable { get; set; }
    }
}
