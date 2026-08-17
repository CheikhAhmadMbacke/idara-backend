using Idara.API.Data;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// SOURCE UNIQUE du périmètre « effectif » d'une école (2026-08-17), sur le
    /// modèle d'AcademicScopeExtensions (§150). Un élève a trois états, tous
    /// DÉRIVÉS de <c>ExitDate</c> — la bascule d'une sortie programmée se fait
    /// par comparaison de dates à chaque requête, jamais par une tâche
    /// planifiée (un cron en panne laisserait des élèves partis facturés).
    ///
    /// ⚠️ Décision d'architecture : PAS de HasQueryFilter global sur Student,
    /// malgré les ~60 filtres manuels. Un filtre global s'appliquerait aussi
    /// aux navigations (Invoice.Student, Payment.Student…) : une facture dont
    /// l'élève est filtré DISPARAÎTRAIT des résultats — inacceptable sur des
    /// écrans qui portent de l'argent. Et il faudrait truffer la cascade de
    /// suppression d'école d'IgnoreQueryFilters (§142).
    ///
    /// ⚠️ Où NE PAS appliquer ces filtres : les contrôles d'accès en LECTURE
    /// (AcademicScopeExtensions.CanAccessStudentAsync, GuardianController
    /// IsLinked, outstanding) — la fiche d'un sortant reste consultable, son
    /// historique lisible, sa dette payable (décisions D2/D4).
    /// </summary>
    public static class StudentScopeExtensions
    {
        /// <summary>
        /// L'effectif AUJOURD'HUI : ni supprimé, ni sorti. Une sortie programmée
        /// (date future) laisse l'élève dans l'effectif jusqu'à sa date — borne
        /// STRICTE : le jour J, il n'est plus de l'effectif.
        /// </summary>
        public static IQueryable<Student> Enrolled(this IQueryable<Student> query, DateTime? asOf = null)
        {
            var day = (asOf ?? DateTime.UtcNow).Date;
            return query.Where(s => !s.IsDeleted && (s.ExitDate == null || s.ExitDate > day));
        }

        /// <summary>
        /// A fait partie de l'effectif à un moment de la période commençant à
        /// <paramref name="periodStart"/> (roster mensuel, classement d'un
        /// bulletin). ⚠️ Distinct d'<see cref="Enrolled"/> : borne LARGE
        /// (&gt;=) — un élève parti LE 1er octobre appartient encore au suivi
        /// d'octobre — et la question porte sur une période, pas sur
        /// aujourd'hui.
        /// </summary>
        public static IQueryable<Student> EnrolledDuring(this IQueryable<Student> query, DateTime periodStart)
        {
            var day = periodStart.Date;
            return query.Where(s => !s.IsDeleted && (s.ExitDate == null || s.ExitDate >= day));
        }

        /// <summary>
        /// Déjà sortis (onglet « Sortis »). EXCLUT les sorties programmées : un
        /// élève encore présent en classe reste dans les onglets normaux, avec
        /// un badge « Sortie prévue ».
        /// </summary>
        public static IQueryable<Student> Exited(this IQueryable<Student> query, DateTime? asOf = null)
        {
            var day = (asOf ?? DateTime.UtcNow).Date;
            return query.Where(s => !s.IsDeleted && s.ExitDate != null && s.ExitDate <= day);
        }

        /// <summary>
        /// Garde-fou d'ÉCRITURE pédagogique (notes, présences, journal, Coran) :
        /// false si l'élève est déjà sorti — on ne saisit plus rien sur lui. La
        /// LECTURE de son historique et la génération de son bulletin restent
        /// libres (D4), elles ne passent pas par ici.
        /// </summary>
        public static Task<bool> IsEnrolledAsync(
            this AppDbContext db, int studentId, CancellationToken ct = default)
        {
            return db.Students.Enrolled().AnyAsync(s => s.Id == studentId, ct);
        }

        /// <summary>Le drapeau « sorti » CALCULÉ PAR LE SERVEUR pour les DTO —
        /// l'app ne le recalcule jamais (une horloge de téléphone décalée
        /// afficherait « sorti » un élève présent, §151).</summary>
        public static bool IsExited(DateTime? exitDate, DateTime? asOf = null)
        {
            if (exitDate == null) return false;
            var day = (asOf ?? DateTime.UtcNow).Date;
            return exitDate.Value.Date <= day;
        }
    }
}
