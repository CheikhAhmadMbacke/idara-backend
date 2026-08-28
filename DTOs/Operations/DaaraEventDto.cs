using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Operations
{
    public class DaaraEventPhotoDto
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class DaaraEventDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DaaraEventCategory Category { get; set; }

        /// <summary>Précision libre de la catégorie « Autre », s'il y en a une.</summary>
        public string? CategoryLabel { get; set; }

        public EventVisibility Visibility { get; set; }

        /// <summary>Objectif que cet événement fait avancer, s'il y en a un.</summary>
        public int? DaaraObjectiveId { get; set; }

        /// <summary>Titre de l'objectif lié, pour l'afficher sans second appel.</summary>
        public string? DaaraObjectiveTitle { get; set; }

        public List<DaaraEventPhotoDto> Photos { get; set; } = new();

        /// <summary>
        /// Nom de l'auteur, pour savoir qui a consigné quoi côté école.
        /// Toujours nul côté parent : cela ne le regarde pas, et cela pourrait
        /// mettre un membre de l'équipe en cause sur un événement mal reçu.
        /// </summary>
        public string? CreatedByName { get; set; }

        /// <summary>
        /// Nom du daara. Renseigné UNIQUEMENT côté parent : un parent dont les
        /// enfants sont dans deux daara doit savoir lequel a organisé quoi.
        /// Côté école, l'écran est déjà celui d'un daara précis.
        /// </summary>
        public string? SchoolName { get; set; }
        public string? SchoolNameAr { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class DaaraEventPhotoInputDto
    {
        [Required] public string ContentBase64 { get; set; } = string.Empty;
        [StringLength(255)] public string? OriginalFileName { get; set; }
        [StringLength(100)] public string? ContentType { get; set; }
    }

    public class CreateDaaraEventDto
    {
        /// <summary>Jour de l'événement. Null = aujourd'hui.</summary>
        public DateTime? Date { get; set; }

        [Required(ErrorMessage = "Le titre est requis.")]
        [StringLength(200, MinimumLength = 1)]
        public string Title { get; set; } = string.Empty;

        [StringLength(4000)]
        public string? Description { get; set; }

        public DaaraEventCategory Category { get; set; } = DaaraEventCategory.Other;

        /// <summary>Précision facultative de « Autre ». Ignorée pour toute autre catégorie.</summary>
        [StringLength(60)]
        public string? CategoryLabel { get; set; }

        public EventVisibility Visibility { get; set; } = EventVisibility.School;

        /// <summary>Objectif à rattacher. Null = aucun.</summary>
        public int? DaaraObjectiveId { get; set; }

        public List<DaaraEventPhotoInputDto> Photos { get; set; } = new();
    }

    /// <summary>
    /// Mise à jour d'un événement. Contrairement à la fiche élève, ce n'est PAS
    /// un PATCH partiel : le formulaire du journal est court et toujours affiché
    /// en entier, donc il renvoie tous les champs. Une description vidée est
    /// bien une description effacée.
    /// </summary>
    public class UpdateDaaraEventDto
    {
        [Required] public int Id { get; set; }

        public DateTime? Date { get; set; }

        [Required(ErrorMessage = "Le titre est requis.")]
        [StringLength(200, MinimumLength = 1)]
        public string Title { get; set; } = string.Empty;

        [StringLength(4000)]
        public string? Description { get; set; }

        public DaaraEventCategory Category { get; set; }

        /// <summary>
        /// Précision facultative de « Autre ». Convention PATCH : <c>null</c> =
        /// ne pas toucher (une application antérieure n'envoie pas le champ —
        /// sans cette règle, chaque édition depuis un vieux téléphone
        /// effacerait la précision, piège §140), <c>""</c> = vider.
        /// </summary>
        [StringLength(60)]
        public string? CategoryLabel { get; set; }

        public EventVisibility Visibility { get; set; }

        /// <summary>Objectif rattaché. Null = détacher.</summary>
        public int? DaaraObjectiveId { get; set; }
    }

    public class DaaraEventListDto
    {
        public List<DaaraEventDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
