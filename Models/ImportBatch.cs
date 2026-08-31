using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Un fichier déposé par une école, analysé puis (éventuellement) importé.
    ///
    /// L'analyse et l'écriture sont DEUX temps séparés : l'école dépose, elle
    /// voit ce qui sera créé et ce qui cloche, et rien n'est écrit tant qu'elle
    /// n'a pas confirmé. C'est le seul parcours où un directeur non-tech ne
    /// peut pas abîmer ses données d'un clic.
    ///
    /// Le résultat de l'analyse est conservé ici pour que la confirmation n'ait
    /// pas à renvoyer le fichier : sur un forfait sénégalais, faire monter deux
    /// fois un tableau de 300 élèves est une raison suffisante d'abandonner.
    /// </summary>
    public class ImportBatch
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Qui a déposé le fichier.</summary>
        public int CreatedById { get; set; }

        public ImportKind Kind { get; set; }

        public string FileName { get; set; } = string.Empty;

        public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Analyzed;

        /// <summary>
        /// Lignes analysées, en JSON. Stocké en `jsonb` : c'est consultable
        /// directement en base pour comprendre un import qui s'est mal passé,
        /// sans avoir à redemander le fichier à l'école.
        /// </summary>
        public string RowsJson { get; set; } = "[]";

        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int ErrorRows { get; set; }
        public int DuplicateRows { get; set; }

        /// <summary>Compte réellement créé, renseigné à la confirmation.</summary>
        public int CreatedStudents { get; set; }
        public int CreatedClasses { get; set; }
        public int CreatedGuardians { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CommittedAt { get; set; }

        /// <summary>Message d'échec éventuel, pour ne pas perdre la raison.</summary>
        public string? Error { get; set; }
    }
}
