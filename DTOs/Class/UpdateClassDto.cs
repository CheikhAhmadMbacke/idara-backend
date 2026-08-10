using System.ComponentModel.DataAnnotations;

namespace Idara.API.DTOs.Class
{
    public class UpdateClassDto
    {
        [Required] public int Id { get; set; }

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
        /// Nouvelle mensualité de la classe. <c>null</c> = NE PAS TOUCHER au
        /// tarif en place (cas d'un simple renommage). Une valeur différente du
        /// tarif courant insère une nouvelle version dans <c>ClassFee</c>
        /// (table append-only : on ne modifie jamais une ligne existante) et
        /// re-tarife les factures impayées des élèves de la classe.
        /// Une valeur IDENTIQUE au tarif courant est ignorée, pour ne pas
        /// empiler une version à chaque enregistrement du formulaire.
        /// </summary>
        [Range(1, 100_000_000)]
        public long? MonthlyFeeFcfa { get; set; }
    }
}
