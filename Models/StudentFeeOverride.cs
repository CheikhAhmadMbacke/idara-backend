namespace Idara.API.Models
{
    /// <summary>
    /// Override de tarif pour un élève spécifique (bourse, demi-tarif, fratrie...).
    /// 1-1 avec Student (PK = StudentId). Si présent, prime sur le ClassFee
    /// courant de la classe de l'élève.
    /// </summary>
    public class StudentFeeOverride
    {
        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        /// <summary>Dénormalisé depuis Student.SchoolId pour multi-tenant + index.</summary>
        public int SchoolId { get; set; }

        public long AmountFcfa { get; set; }
        public string? Reason { get; set; }

        public int CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
