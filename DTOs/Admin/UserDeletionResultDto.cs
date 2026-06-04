namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Résultat d'une suppression d'utilisateur (DELETE /api/users/{id}).
    /// <see cref="Action"/> indique laquelle des deux branches hybrides a
    /// été appliquée, pour que l'UI affiche le bon message.
    /// </summary>
    public class UserDeletionResultDto
    {
        public int UserId { get; set; }

        /// <summary>"HardDeleted" (ligne physiquement supprimée, utilisateur
        /// vierge) ou "Anonymized" (PII effacées, historique préservé).</summary>
        public string Action { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }
}
