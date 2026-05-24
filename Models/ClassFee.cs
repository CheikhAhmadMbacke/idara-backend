namespace Idara.API.Models
{
    /// <summary>
    /// Montant standard mensuel pour une classe. Versionné par EffectiveFrom :
    /// le tarif courant pour une classe est celui avec EffectiveFrom le plus
    /// récent ≤ aujourd'hui. On ne met JAMAIS à jour une row existante — on
    /// insère une nouvelle row (audit trail naturel des changements de tarif).
    /// </summary>
    public class ClassFee
    {
        public int Id { get; set; }

        public int ClassId { get; set; }
        public Class Class { get; set; } = null!;

        /// <summary>Dénormalisé depuis Class.SchoolId pour multi-tenant + index.</summary>
        public int SchoolId { get; set; }

        public long AmountFcfa { get; set; }

        /// <summary>Date à partir de laquelle ce tarif s'applique (UTC, minuit).</summary>
        public DateTime EffectiveFrom { get; set; }

        public int CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
