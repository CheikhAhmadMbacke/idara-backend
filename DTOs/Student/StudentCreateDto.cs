using System.ComponentModel.DataAnnotations;
using Idara.API.Common.Validation;
using Idara.API.Enums;

namespace Idara.API.DTOs.Student
{
    /// <summary>
    /// Création d'un élève. Seuls prénom et nom sont obligatoires —
    /// le reste peut être complété plus tard via UpdateStudent.
    /// </summary>
    public class StudentCreateDto
    {
        // ----- Identité -----
        [Required(ErrorMessage = "Le prénom est requis.")]
        [StringLength(100, MinimumLength = 1)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom est requis.")]
        [StringLength(100, MinimumLength = 1)]
        public string LastName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? MiddleName { get; set; }

        public Gender? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }

        [StringLength(150)]
        public string? PlaceOfBirth { get; set; }

        [StringLength(80)]
        public string? Nationality { get; set; }

        public string? PhotoBase64 { get; set; }

        // ----- Adresse -----
        [StringLength(250)] public string? Address { get; set; }
        [StringLength(100)] public string? City { get; set; }
        [StringLength(100)] public string? Region { get; set; }
        [StringLength(80)]  public string? Country { get; set; }

        // ----- Scolarité -----
        public DateTime? EnrollmentDate { get; set; }

        public int? ClassId { get; set; }

        [StringLength(50)] public string? StudentNumber { get; set; }
        [StringLength(150)] public string? PreviousSchool { get; set; }
        [StringLength(100)] public string? PreviousClass { get; set; }
        [StringLength(500)] public string? TransferReason { get; set; }

        // ----- Santé -----
        [StringLength(10)]  public string? BloodType { get; set; }
        [StringLength(500)] public string? Allergies { get; set; }
        [StringLength(500)] public string? ChronicConditions { get; set; }
        [StringLength(500)] public string? CurrentMedications { get; set; }
        [StringLength(150)] public string? DoctorName { get; set; }
        [StringLength(30)]  public string? DoctorPhone { get; set; }
        [StringLength(150)] public string? EmergencyContactName { get; set; }
        [StringLength(30)]  public string? EmergencyContactPhone { get; set; }
        [StringLength(60)]  public string? EmergencyContactRelation { get; set; }

        // ----- Parents -----
        [StringLength(150)] public string? FatherFullName { get; set; }
        [StringLength(30)]  public string? FatherPhone { get; set; }
        [OptionalEmailAddress] [StringLength(150)] public string? FatherEmail { get; set; }
        [StringLength(120)] public string? FatherProfession { get; set; }
        [StringLength(150)] public string? MotherFullName { get; set; }
        [StringLength(30)]  public string? MotherPhone { get; set; }
        [OptionalEmailAddress] [StringLength(150)] public string? MotherEmail { get; set; }
        [StringLength(120)] public string? MotherProfession { get; set; }

        // ----- Notes libres -----
        [StringLength(2000)] public string? Notes { get; set; }

        public List<GuardianInputDto> Guardians { get; set; } = new();
        public List<StudentDocumentInputDto> Documents { get; set; } = new();
    }
}
