using System.ComponentModel.DataAnnotations;
using Idara.API.Enums;

namespace Idara.API.DTOs.Academic
{
    public class ClassSubjectTeacherDto
    {
        public int Id { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public SubjectKind SubjectKind { get; set; }
        public int TeacherId { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
    }

    public class ClassSubjectTeacherCreateDto
    {
        [Required] public int ClassId { get; set; }
        [Required] public int SubjectId { get; set; }
        [Required] public int TeacherId { get; set; }
    }
}
