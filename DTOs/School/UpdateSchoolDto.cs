using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Validation;
using Idara.API.Enums;

namespace Idara.API.DTOs.School
{
    /// <summary>
    /// Édition des informations d'une école par le SuperAdmin (les écoles qui
    /// veulent modifier leurs infos passent par le support avec justificatif).
    /// Ne touche PAS au statut KYC (géré par validate/reject) ni aux documents.
    /// </summary>
    public class UpdateSchoolDto : IValidatableObject
    {
        /// <summary>
        /// Nom en français. Plus <c>[Required]</c> depuis l'ajout du nom arabe :
        /// la règle est « au moins l'un des deux », vérifiée dans
        /// <see cref="Validate"/>. Une école dont le nom n'existe qu'en arabe doit
        /// pouvoir laisser ce champ vide.
        /// </summary>
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Le nom en français doit faire au moins 2 caractères.")]
        public string? Name { get; set; }

        /// <summary>Nom en arabe. Même règle que <see cref="Name"/>.</summary>
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Le nom en arabe doit faire au moins 2 caractères.")]
        public string? NameAr { get; set; }

        [StringLength(300)]
        public string? Address { get; set; }

        [StringLength(30)]
        public string? PhoneNumber { get; set; }

        [StringLength(100)]
        public string? RepresentativeFirstName { get; set; }

        [StringLength(100)]
        public string? RepresentativeLastName { get; set; }

        [StringLength(30)]
        public string? RepresentativePhone { get; set; }

        /// <summary>Lecture (riwâya) du Coran. Optionnel : si null, inchangé.</summary>
        public QuranRiwaya? QuranRiwaya { get; set; }

        /// <summary>
        /// Le daara doit porter un nom dans au moins une écriture. C'est la PAIRE
        /// qui est invalide quand les deux sont vides, pas l'un des deux champs :
        /// l'erreur est donc rattachée aux deux, pour que l'app puisse les
        /// surligner ensemble.
        /// </summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
            SchoolNameRule.Validate(Name, NameAr);
    }
}
