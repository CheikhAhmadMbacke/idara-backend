namespace Idara.API.DTOs.Class
{
    public class ClassDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Level { get; set; }
        public int? Capacity { get; set; }
        public int StudentCount { get; set; }

        /// <summary>
        /// Mensualité COURANTE de la classe (tarif <c>ClassFee</c> le plus
        /// récent déjà en vigueur), ou null si la classe n'en a jamais eu.
        /// Sert à pré-remplir le formulaire : sans elle, rouvrir une classe
        /// afficherait un champ vide et l'enregistrement écraserait le tarif.
        /// </summary>
        public long? MonthlyFeeFcfa { get; set; }

        public int SchoolId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
