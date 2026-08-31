using Idara.API.Enums;
namespace Idara.API.Models
{
    /// <summary>Bulletin d'un élève pour une période académique.</summary>
    public class ReportCard
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public int AcademicPeriodId { get; set; }
        public AcademicPeriod AcademicPeriod { get; set; } = null!;

        public int? ClassId { get; set; }
        public Class? Class { get; set; }

        public DateTime GeneratedAt { get; set; }
        /// <summary>
        /// Moyenne du domaine « arabe et religieux » (Coran + matières
        /// religieuses). NULL = l'école n'enseigne pas ce domaine, ou bulletin
        /// généré avant ce champ. Jamais 0 : cf. ReportCardDomains.
        /// </summary>
        public double? ArabicAverage { get; set; }

        /// <summary>Moyenne du domaine « français et général ». Même règle.</summary>
        public double? GeneralSubjectsAverage { get; set; }

        public double GeneralAverage { get; set; }
        public int? Rank { get; set; }
        public int? TotalStudents { get; set; }
        public string? Mention { get; set; }
        public string? Appreciation { get; set; }
        public string? FilePath { get; set; }

        public ICollection<ReportCardLine> Lines { get; set; } = new List<ReportCardLine>();
    }

    public class ReportCardLine
    {
        public int Id { get; set; }
        public int ReportCardId { get; set; }
        public ReportCard ReportCard { get; set; } = null!;

        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public string SubjectName { get; set; } = string.Empty;

        /// <summary>
        /// Domaine de la matière, SNAPSHOTÉ à la génération — comme
        /// <see cref="SubjectName"/>. Une école qui reclasse une matière plus
        /// tard (cela arrive, cf. §179) ne doit pas voir les bulletins déjà
        /// délivrés changer de moyennes rétroactivement. NULL sur les bulletins
        /// générés avant ce champ.
        /// </summary>
        public SubjectKind? SubjectKind { get; set; }

        public double Average { get; set; }
        public double Coefficient { get; set; }
        public double MaxValue { get; set; } = 20.0;
        public int? RankInClass { get; set; }
        public string? Appreciation { get; set; }
    }
}
