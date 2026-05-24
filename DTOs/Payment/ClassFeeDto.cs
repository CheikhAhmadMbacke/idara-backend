using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Payment
{
    public class ClassFeeDto
    {
        public int Id { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public int SchoolId { get; set; }
        public long AmountFcfa { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Vue agrégée par classe : le tarif courant + indicateur s'il y a un
    /// historique. Pratique pour afficher la table "tarifs par classe" côté UI.
    /// </summary>
    public class ClassCurrentFeeDto
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public int? StudentCount { get; set; }

        /// <summary>null si la classe n'a jamais eu de tarif défini.</summary>
        public long? CurrentAmountFcfa { get; set; }
        public DateTime? CurrentEffectiveFrom { get; set; }
        public int HistoryCount { get; set; }
    }

    public class CreateClassFeeDto
    {
        /// <summary>Montant en FCFA. >= 200 (minimum SenePay) en pratique, mais on tolère
        /// plus bas pour ne pas bloquer les écoles qui veulent juste tester un tarif.</summary>
        [Range(0, 100_000_000, ErrorMessage = "AmountFcfa doit être positif et raisonnable.")]
        public long AmountFcfa { get; set; }

        /// <summary>Date à partir de laquelle le tarif s'applique. Si null = aujourd'hui (UTC, minuit).
        /// Une date passée est acceptée (audit/rattrapage), une date future est acceptée (annonce).</summary>
        public DateTime? EffectiveFrom { get; set; }
    }
}
