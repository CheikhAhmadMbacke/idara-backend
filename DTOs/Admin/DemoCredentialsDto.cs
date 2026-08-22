namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Identifiants des comptes de démo, affichés dans le back-office SuperAdmin
    /// (école + directeur + parent). Valeurs issues des constantes seedées.
    /// </summary>
    public class DemoCredentialsDto
    {
        public string SchoolName { get; set; } = string.Empty;

        /// <summary>Id de l'école démo si le seed a tourné (null sinon).</summary>
        public int? SchoolId { get; set; }

        public string AdminEmail { get; set; } = string.Empty;
        public string AdminPassword { get; set; } = string.Empty;

        public string GuardianPhone { get; set; } = string.Empty;
        public string GuardianCode { get; set; } = string.Empty;

        public string TeacherPhone { get; set; } = string.Empty;
        public string TeacherCode { get; set; } = string.Empty;
    }
}
