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

        /// <summary>true si CE daara fait porter les frais au donateur (majoration).
        /// false (défaut) = le daara paie les frais, le donateur donne le montant
        /// exact. Sert au client à n'afficher la note de frais que si pertinent.</summary>
        public bool DonorPaysFees { get; set; }
    }
}
