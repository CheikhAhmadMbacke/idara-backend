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
        /// <summary>Nom du daara en arabe (affiché sous le nom français). Null si absent.</summary>
        public string? NameAr { get; set; }
        public string? Address { get; set; }
        /// <summary>Logo du daara (chemin relatif /uploads/...) pour l'afficher dans la liste + l'écran de don.</summary>
        public string? LogoUrl { get; set; }

        /// <summary>true si CE daara fait porter les frais au donateur (majoration).
        /// false (défaut) = le daara paie les frais, le donateur donne le montant
        /// exact. Sert au client à n'afficher la note de frais que si pertinent.</summary>
        public bool DonorPaysFees { get; set; }
    }
}
