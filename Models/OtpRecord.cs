using Idara.API.Enums;

namespace Idara.API.Models
{
    public class OtpRecord
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string OtpCode { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; }

        /// <summary>
        /// Action pour laquelle l'OTP a été émis (Register / ResetPassword).
        /// Empêche la réutilisation d'un code émis pour une autre action.
        /// </summary>
        public OtpPurpose Purpose { get; set; } = OtpPurpose.Register;
    }
}
