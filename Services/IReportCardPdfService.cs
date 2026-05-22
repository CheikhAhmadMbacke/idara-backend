using Idara.API.Models;

namespace Idara.API.Services
{
    public interface IReportCardPdfService
    {
        /// <summary>
        /// Génère le PDF du bulletin et le sauvegarde dans wwwroot/uploads/bulletins.
        /// Retourne le chemin relatif (commençant par /uploads/bulletins/...) à
        /// stocker dans <see cref="ReportCard.FilePath"/>.
        /// L'appelant doit fournir un bulletin avec ses Lines et Student/AcademicPeriod
        /// déjà chargés.
        /// </summary>
        Task<string> GenerateAsync(ReportCard card, School school);
    }
}
