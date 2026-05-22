using Idara.API.Enums;

namespace Idara.API.Models
{
    public class Student
    {
        public int Id { get; set; }

        // ----- Identité -----
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public Gender? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? PlaceOfBirth { get; set; }
        public string? Nationality { get; set; }
        public string? PhotoUrl { get; set; }

        // ----- Adresse -----
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }

        // ----- Scolarité -----
        public DateTime EnrollmentDate { get; set; }
        public int? ClassId { get; set; }
        public Class? Class { get; set; }
        public string? StudentNumber { get; set; }
        public string? PreviousSchool { get; set; }
        public string? PreviousClass { get; set; }
        public string? TransferReason { get; set; }

        // ----- Santé -----
        public string? BloodType { get; set; }
        public string? Allergies { get; set; }
        public string? ChronicConditions { get; set; }
        public string? CurrentMedications { get; set; }
        public string? DoctorName { get; set; }
        public string? DoctorPhone { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? EmergencyContactRelation { get; set; }

        // ----- Parents (info dénormalisée pour identification rapide) -----
        public string? FatherFullName { get; set; }
        public string? FatherPhone { get; set; }
        public string? FatherEmail { get; set; }
        public string? FatherProfession { get; set; }
        public string? MotherFullName { get; set; }
        public string? MotherPhone { get; set; }
        public string? MotherEmail { get; set; }
        public string? MotherProfession { get; set; }

        // ----- Métadonnées -----
        public string? Notes { get; set; }
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<StudentGuardian> StudentGuardians { get; set; } = new List<StudentGuardian>();
        public ICollection<StudentDocument> Documents { get; set; } = new List<StudentDocument>();
    }
}
