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
    }
}
