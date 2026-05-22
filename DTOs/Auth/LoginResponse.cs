namespace Idara.API.DTOs.Auth
{
    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        /// <summary>Refresh token (longue durée, à stocker en secure storage côté client).</summary>
        public string RefreshToken { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int? SchoolId { get; set; }
        public string AccountStatus { get; set; } = string.Empty;
        public string? KycStatus { get; set; }
    }
}
