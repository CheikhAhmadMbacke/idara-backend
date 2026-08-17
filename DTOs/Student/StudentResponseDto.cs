using Idara.API.Enums;

namespace Idara.API.DTOs.Student
{
    public class StudentResponseDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public Gender? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? PlaceOfBirth { get; set; }
        public string? Nationality { get; set; }
        public string? PhotoUrl { get; set; }

        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }

        public DateTime EnrollmentDate { get; set; }
        public int? ClassId { get; set; }
        public string? ClassName { get; set; }
        public string? StudentNumber { get; set; }
        public string? PreviousSchool { get; set; }
        public string? PreviousClass { get; set; }
        public string? TransferReason { get; set; }

        /// <summary>Régime d'hébergement, ou null si non renseigné.</summary>
        public BoardingStatus? BoardingStatus { get; set; }

        /// <summary>
        /// Tarif mensuel personnalisé de l'élève, ou null s'il n'en a pas.
        /// ⚠️ Ce n'est PAS le montant finalement facturé : celui-ci suit la
        /// hiérarchie tarif élève &gt; statut &gt; classe &gt; général
        /// (<see cref="Services.FeeResolver"/>).
        /// </summary>
        public long? MonthlyFeeFcfa { get; set; }

        /// <summary>Motif du tarif personnalisé (usage interne à l'école).</summary>
        public string? MonthlyFeeReason { get; set; }

        // ----- Sortie de l'effectif -----

        /// <summary>Date de sortie (éventuellement future = programmée). Null = inscrit.</summary>
        public DateTime? ExitDate { get; set; }
        public StudentExitReason? ExitReason { get; set; }
        public string? ExitReasonDetail { get; set; }

        /// <summary>
        /// « Déjà sorti » — CALCULÉ PAR LE SERVEUR : l'app ne compare jamais
        /// ExitDate à l'horloge du téléphone (§151). false + ExitDate non nulle
        /// = sortie programmée, encore dans l'effectif.
        /// </summary>
        public bool IsExited { get; set; }

        public string? BloodType { get; set; }
        public string? Allergies { get; set; }
        public string? ChronicConditions { get; set; }
        public string? CurrentMedications { get; set; }
        public string? DoctorName { get; set; }
        public string? DoctorPhone { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? EmergencyContactRelation { get; set; }

        public string? FatherFullName { get; set; }
        public string? FatherPhone { get; set; }
        public string? FatherEmail { get; set; }
        public string? FatherProfession { get; set; }
        public string? MotherFullName { get; set; }
        public string? MotherPhone { get; set; }
        public string? MotherEmail { get; set; }
        public string? MotherProfession { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public List<GuardianResponseDto> Guardians { get; set; } = new();
        public List<StudentDocumentResponseDto> Documents { get; set; } = new();

        /// <summary>
        /// Identifiants des responsables NOUVELLEMENT créés lors de ce
        /// create/update (numéro + code + message à partager). Vide en lecture
        /// (GET/list). Sert au modal récap côté école.
        /// </summary>
        public List<Common.UserCredentialDto> NewGuardianCredentials { get; set; } = new();
    }
}
