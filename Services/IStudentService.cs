using Idara.API.DTOs.Student;

namespace Idara.API.Services
{
    public interface IStudentService
    {
        Task<StudentListResponseDto> GetStudentsAsync(int schoolId, StudentPaginationDto pagination);
        Task<StudentResponseDto?> GetStudentByIdAsync(int id, int schoolId);
        Task<StudentResponseDto> CreateStudentAsync(int schoolId, int currentUserId, StudentCreateDto dto);
        Task<StudentResponseDto?> UpdateStudentAsync(int schoolId, int currentUserId, StudentUpdateDto dto);
        Task<bool> DeleteStudentAsync(int id, int schoolId);

        Task<StudentDocumentResponseDto?> AddDocumentAsync(int studentId, int schoolId, StudentDocumentInputDto dto);
        Task<bool> DeleteDocumentAsync(int studentId, int documentId, int schoolId);
    }
}
