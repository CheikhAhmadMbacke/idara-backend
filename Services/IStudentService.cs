using Idara.API.DTOs.Student;

namespace Idara.API.Services
{
    public interface IStudentService
    {
        /// <param name="restrictToClassIds">
        /// Périmètre de l'appelant (cf. <see cref="Common.Extensions.AcademicScopeExtensions"/>) :
        /// <c>null</c> = toute l'école, une liste = uniquement ces classes, une
        /// liste VIDE = aucun élève. Appliqué AVANT la recherche, les compteurs,
        /// le total et la pagination.
        /// </param>
        Task<StudentListResponseDto> GetStudentsAsync(
            int schoolId, StudentPaginationDto pagination,
            IReadOnlyList<int>? restrictToClassIds = null);
        Task<StudentResponseDto?> GetStudentByIdAsync(int id, int schoolId);
        Task<StudentResponseDto> CreateStudentAsync(int schoolId, int currentUserId, StudentCreateDto dto);
        Task<StudentResponseDto?> UpdateStudentAsync(int schoolId, int currentUserId, StudentUpdateDto dto);
        Task<bool> DeleteStudentAsync(int id, int schoolId);

        Task<StudentDocumentResponseDto?> AddDocumentAsync(int studentId, int schoolId, StudentDocumentInputDto dto);
        Task<bool> DeleteDocumentAsync(int studentId, int documentId, int schoolId);

        // ----- Sortie de l'effectif (2026-08-17) -----

        /// <summary>
        /// Ce que la sortie impliquerait pour les dettes de l'élève, à la date
        /// donnée — pour que la case « annuler les mensualités impayées » ne
        /// soit jamais un choix aveugle. Null si l'élève est introuvable.
        /// </summary>
        Task<StudentExitPreviewDto?> GetExitPreviewAsync(
            int studentId, int schoolId, DateTime exitDate, CancellationToken ct);

        /// <summary>
        /// Marque l'élève sortant (date passée, du jour, ou FUTURE = programmée).
        /// <paramref name="canCancelInvoices"/> = l'appelant a le droit
        /// d'annuler des factures (SchoolAdmin) — le service ne fait AUCUN
        /// contrôle d'autorisation lui-même (§77).
        /// </summary>
        Task<StudentExitResult> ExitStudentAsync(
            int studentId, int schoolId, int currentUserId, bool canCancelInvoices,
            StudentExitRequestDto dto, CancellationToken ct);

        /// <summary>Annule la sortie (prévue ou effective) : efface les 5 champs.</summary>
        Task<StudentExitResult> ReinstateStudentAsync(
            int studentId, int schoolId, CancellationToken ct);
    }

    /// <summary>Issue d'une opération de sortie : Ok, ou un message d'erreur utilisateur.</summary>
    public sealed record StudentExitResult(bool Ok, string? Error, int CancelledInvoices = 0)
    {
        public static StudentExitResult Success(int cancelled = 0) => new(true, null, cancelled);
        public static StudentExitResult Fail(string error) => new(false, error);
    }
}
