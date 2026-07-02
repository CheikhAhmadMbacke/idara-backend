namespace Idara.API.DTOs.Donation
{
    /// <summary>
    /// Daara affiché dans la liste publique « choisir un daara à soutenir ».
    /// PII minimale : nom + localisation (adresse). Uniquement les écoles
    /// validées (KycStatus=Validated) sont exposées.
    /// </summary>
    public class DonationSchoolDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
    }
}
