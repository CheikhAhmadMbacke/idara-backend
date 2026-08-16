using Idara.API.Constants;
using Idara.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// SOURCE UNIQUE du périmètre pédagogique d'un utilisateur : quelles classes
    /// et quels élèves il a le droit de voir et de modifier.
    ///
    /// Règle : la direction (SchoolAdmin / SchoolStaff), l'Observateur et le
    /// Surveillant voient TOUTE l'école ; un <b>enseignant ne voit QUE les
    /// classes qui lui sont affectées</b> (<see cref="Models.ClassSubjectTeacher"/>),
    /// et les élèves de ces classes.
    ///
    /// ⚠️ À appeler dans TOUT endpoint qui liste ou accepte une classe / un élève.
    /// Filtrer une liste sans filtrer l'accès unitaire laisserait lire la fiche
    /// d'un élève d'une autre classe en devinant son identifiant (§142).
    ///
    /// ⚠️ Le Surveillant garde l'école entière : il pointe les présences de tout
    /// le monde, le restreindre le rendrait inutilisable.
    /// </summary>
    public static class AcademicScopeExtensions
    {
        /// <summary>
        /// Rôles qui voient toute l'école. L'Observateur est inclus : son contrat
        /// est « voit tout ce que voit le directeur » et <c>GetRole()</c> renvoie
        /// bien « SchoolViewer » pour lui (les rôles de lecture SchoolAdmin /
        /// SchoolStaff ne sont que des claims SECONDAIRES, cf. JwtService).
        /// L'omettre ici le priverait de tout accès élève.
        /// </summary>
        public static bool SeesWholeSchool(string? role) =>
            role == UserRoles.SchoolAdmin
            || role == UserRoles.SchoolStaff
            || role == UserRoles.SchoolViewer
            || role == UserRoles.Surveillant;

        /// <summary>
        /// Identifiants des classes visibles par l'appelant.
        /// <b><c>null</c> = aucune restriction</b> (toute l'école) — à distinguer
        /// d'une liste VIDE, qui signifie « cet enseignant n'a aucune classe
        /// affectée » et doit se traduire par un écran vide explicite, jamais par
        /// l'école entière.
        /// </summary>
        public static async Task<IReadOnlyList<int>?> VisibleClassIdsAsync(
            this AppDbContext db, string? role, int userId, int schoolId,
            CancellationToken ct = default)
        {
            if (SeesWholeSchool(role)) return null;
            if (role != UserRoles.Teacher) return Array.Empty<int>();

            return await db.ClassSubjectTeachers
                .Where(a => a.SchoolId == schoolId && a.TeacherId == userId)
                .Select(a => a.ClassId)
                .Distinct()
                .ToListAsync(ct);
        }

        /// <summary>Vrai si l'appelant peut agir sur cette classe.</summary>
        public static async Task<bool> CanAccessClassAsync(
            this AppDbContext db, string? role, int userId, int schoolId, int classId,
            CancellationToken ct = default)
        {
            var exists = await db.Classes
                .AnyAsync(c => c.Id == classId && c.SchoolId == schoolId && !c.IsDeleted, ct);
            if (!exists) return false;

            if (SeesWholeSchool(role)) return true;
            if (role != UserRoles.Teacher) return false;

            return await db.ClassSubjectTeachers.AnyAsync(
                a => a.SchoolId == schoolId && a.TeacherId == userId && a.ClassId == classId, ct);
        }

        /// <summary>
        /// Vrai si l'appelant peut agir sur cet élève. Un élève <b>sans classe</b>
        /// est hors du périmètre d'un enseignant : aucune affectation ne peut le
        /// rattacher à lui.
        /// </summary>
        public static async Task<bool> CanAccessStudentAsync(
            this AppDbContext db, string? role, int userId, int schoolId, int studentId,
            CancellationToken ct = default)
        {
            var classId = await db.Students
                .Where(s => s.Id == studentId && s.SchoolId == schoolId && !s.IsDeleted)
                .Select(s => s.ClassId)
                .FirstOrDefaultAsync(ct);

            // Aucune ligne OU élève sans classe : FirstOrDefault renvoie null dans
            // les deux cas. On refait donc un test d'existence explicite plutôt que
            // de conclure d'un null — sinon la direction perdrait l'accès aux
            // élèves non encore affectés à une classe.
            if (classId == null)
            {
                var exists = await db.Students.AnyAsync(
                    s => s.Id == studentId && s.SchoolId == schoolId && !s.IsDeleted, ct);
                if (!exists) return false;
                return SeesWholeSchool(role);
            }

            if (SeesWholeSchool(role)) return true;
            if (role != UserRoles.Teacher) return false;

            return await db.ClassSubjectTeachers.AnyAsync(
                a => a.SchoolId == schoolId && a.TeacherId == userId && a.ClassId == classId.Value, ct);
        }
    }
}
