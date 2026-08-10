using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Class
{
    public class CreateClassDto
    {
        [Required(ErrorMessage = "Le nom de la classe est requis.")]
        [StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? Level { get; set; }

        [Range(1, 1000)]
        public int? Capacity { get; set; }

        /// <summary>
        /// Mensualité de la classe (optionnelle). Enregistrée dans la table
        /// <c>ClassFee</c> — la MÊME que l'écran « Tarif par classe », qui reste
        /// la vue d'historique. <c>null</c> = pas de tarif de classe (les élèves
        /// retombent sur leur tarif de statut ou le tarif général).
        /// Borne basse à 1 volontairement : un 0 signifierait « aucun tarif du
        /// tout » et priverait l'élève même du tarif général.
        /// </summary>
        [Range(1, 100_000_000)]
        public long? MonthlyFeeFcfa { get; set; }
    }
}
