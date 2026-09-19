using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    public static class QuranSubjectExtensions
    {
        /// <summary>Nom de la matière pré-créée, dans les deux écritures.</summary>
        public const string NomFr = "Coran";
        /// <summary>Nom arabe de la matière pré-créée.</summary>
        public const string NomAr = "القرآن";

        /// <summary>
        /// 📖 Garantit qu'un <b>daara</b> possède sa matière « Coran ».
        ///
        /// <para><b>Décision produit du 2026-09-19.</b> Dans un daara on apprend
        /// forcément le Coran : la matière est donc là dès le premier jour, sans
        /// que l'école ait à la créer. Une école <b>franco-arabe</b> n'y est pas
        /// tenue — elle crée la sienne si elle le veut —, et c'est pourquoi cette
        /// méthode ne fait rien hors du type <see cref="SchoolType.Daara"/>.</para>
        ///
        /// <para>🔴 <b>Posée une fois, jamais reposée.</b> On regarde AUSSI les
        /// matières supprimées : un daara qui retire sciemment sa matière Coran
        /// ne doit pas la voir revenir au prochain enregistrement de sa fiche.
        /// Sans cette garde, l'école perdrait toujours, sans jamais comprendre
        /// pourquoi — c'est la discipline du §74, déjà payée une fois sur le type
        /// des matières.</para>
        ///
        /// <para>La reconnaissance passe par <see cref="QuranSubjectNaming"/> :
        /// un daara qui a déjà créé « Al quran », « Xuraan bi » ou « Tahfiz » a
        /// sa matière, et n'a pas besoin d'un doublon nommé « Coran ».</para>
        ///
        /// <para>Appelée (a) à la création de l'école au KYC, (b) chaque fois que
        /// son type est (re)posé, et (c) une fois pour les daara déjà en base
        /// (<c>DbInitializer.BackfillSchoolTypesAsync</c>).</para>
        /// </summary>
        /// <returns><c>true</c> si la matière vient d'être créée.</returns>
        public static async Task<bool> EnsureQuranSubjectAsync(
            this AppDbContext db, int schoolId, CancellationToken ct = default)
        {
            // Pas d'AsNoTracking : si l'appelant vient de changer le type de
            // l'école dans ce même contexte, c'est bien la valeur à jour qu'on
            // doit lire, pas celle de la base.
            var school = await db.Schools.FirstOrDefaultAsync(s => s.Id == schoolId, ct);
            if (school == null || school.Type != SchoolType.Daara) return false;

            // ⚠️ Pas de filtre !IsDeleted : une matière retirée compte comme un
            // choix de l'école (voir la remarque ci-dessus).
            var existantes = await db.Subjects
                .Where(s => s.SchoolId == schoolId)
                .Select(s => new { s.Name, s.NameAr, s.Kind })
                .ToListAsync(ct);

            if (existantes.Any(s => s.Kind == SubjectKind.Coran
                                    || QuranSubjectNaming.LooksLikeQuran(s.Name, s.NameAr)))
                return false;

            db.Subjects.Add(new Subject
            {
                SchoolId = schoolId,
                Name = NomFr,
                NameAr = NomAr,
                Kind = SubjectKind.Coran,
                // Coefficient 2 : dans un daara, le Coran pèse plus que le reste.
                // C'est un DÉFAUT, l'école le change depuis l'écran Matières.
                DefaultCoefficient = 2.0,
                DefaultMaxValue = 20.0,
                OrderIndex = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            return true;
        }
    }
}
