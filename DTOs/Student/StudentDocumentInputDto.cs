using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Student
{
    /// <summary>
    /// Document à attacher à un élève. Le contenu est en base64 (image ou PDF),
    /// éventuellement préfixé d'un dataURI (data:application/pdf;base64,...).
    /// </summary>
    public class StudentDocumentInputDto
    {
        public StudentDocumentType Type { get; set; } = StudentDocumentType.Other;

        [Required(ErrorMessage = "Le contenu base64 du document est requis.")]
        public string ContentBase64 { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom de fichier original est requis.")]
        [StringLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [StringLength(120)]
        public string? ContentType { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }
}
