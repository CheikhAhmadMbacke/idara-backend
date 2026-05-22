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
    }
}
