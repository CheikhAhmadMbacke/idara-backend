namespace Idara.API.Models
{
    /// <summary>
    /// Wallet logique par école (1-1, PK = SchoolId). Les fonds physiques sont
    /// consolidés sur le compte SenePay marchand Idara unique — cette table
    /// trace le solde dû à chaque école individuellement.
    ///
    /// AvailableBalance + PendingBalance = solde total réservé à l'école.
    /// Lors d'un retrait : Available -= X, Pending += X (réservation). Au
    /// webhook completed : Pending -= X (débit final). Au webhook failed :
    /// Pending -= X, Available += X (restitution).
    /// </summary>
    public class SchoolWallet
    {
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public long AvailableBalance { get; set; }
        public long PendingBalance { get; set; }

        public long TotalCreditedLifetime { get; set; }
        public long TotalWithdrawnLifetime { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
