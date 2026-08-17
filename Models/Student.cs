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

        /// <summary>
        /// Régime d'hébergement (interne / demi-interne / externe). <c>null</c> =
        /// non renseigné : l'élève garde alors le tarif de sa classe ou le tarif
        /// général. Volontairement laissé vide pour les élèves créés avant
        /// l'ajout du champ — les classer d'office ferait changer leur montant
        /// dû le jour où l'école saisit un tarif par statut, sans que personne
        /// n'ait touché à leur fiche.
        /// </summary>
        public BoardingStatus? BoardingStatus { get; set; }

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

        // ----- Sortie de l'effectif (2026-08-17) -----
        // « Sorti » ≠ « supprimé » : IsDeleted veut dire « cet élève n'aurait
        // jamais dû exister » (doublon, erreur de saisie) ; ExitDate veut dire
        // « il a été élève et il est parti » — historique conservé, fiche
        // consultable, dette payable.

        /// <summary>
        /// Date de sortie de l'effectif, éventuellement dans le FUTUR (sortie
        /// programmée : « il part fin juin » saisi en mai). Trois états, tous
        /// DÉRIVÉS de cette seule date — jamais de booléen parallèle, qui
        /// finirait par la contredire sans que rien ne le signale :
        ///   null            → inscrit
        ///   &gt; aujourd'hui   → inscrit, sortie prévue
        ///   &lt;= aujourd'hui  → sorti
        /// La bascule se fait par comparaison de dates à chaque requête
        /// (StudentScopeExtensions), PAS par une tâche planifiée : un cron en
        /// panne laisserait des élèves partis facturés et comptés dans le
        /// palier d'abonnement (même principe que §144 — ce qui se calcule ne
        /// se stocke pas).
        /// </summary>
        public DateTime? ExitDate { get; set; }

        public StudentExitReason? ExitReason { get; set; }

        /// <summary>Précision libre. Obligatoire quand ExitReason == Other.</summary>
        public string? ExitReasonDetail { get; set; }

        public DateTime? ExitRecordedAt { get; set; }
        public int? ExitRecordedById { get; set; }

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
