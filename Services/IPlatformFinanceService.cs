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
    }
}
