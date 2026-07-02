using Idara.API.DTOs.Admin;

namespace Idara.API.Services
{
    /// <summary>
    /// Réconciliation financière plateforme (SuperAdmin) : rapproche la réserve
    /// marchand SenePay (live) avec la dette écoles et les gains plateforme, tous
    /// deux RECALCULÉS depuis les tables sources (aucune dérive possible).
    /// </summary>
    public interface IPlatformFinanceService
    {
        Task<ReconciliationDto> ComputeReconciliationAsync(CancellationToken ct = default);
        Task<List<SchoolBalanceDto>> GetSchoolBalancesAsync(CancellationToken ct = default);

        /// <summary>
        /// Calcule (dette écoles D, gains plateforme P) depuis la DB uniquement
        /// (sans appel SenePay). Utilisé sous le verrou plateforme pour le garde-fou
        /// du retrait des gains. Le détail P est fourni pour l'affichage éventuel.
        /// </summary>
        Task<(long owedToSchools, PlatformBalanceDto platform)> ComputeDebtAndPlatformAsync(
            CancellationToken ct = default);

        /// <summary>Marge de sécurité au-dessus de la dette (%). Seuil vert = D×(1+marge).</summary>
        double SafetyMarginPercent { get; }
    }
}
