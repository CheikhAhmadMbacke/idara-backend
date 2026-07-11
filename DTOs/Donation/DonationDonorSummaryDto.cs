namespace Idara.API.DTOs.Donation
{
    /// <summary>
    /// Vu côté ÉCOLE : un donateur ayant fait au moins un don (complété) au daara
    /// sur la période, avec son cumul. Sert à l'écran « Rapport donateur ».
    /// </summary>
    public class DonationDonorSummaryDto
    {
        public int DonorId { get; set; }
        public string DonorName { get; set; } = string.Empty;
        public int DonationCount { get; set; }
        public long TotalFcfa { get; set; }
    }
}
