namespace Idara.API.Enums
{
    /// <summary>
    /// Périodicité de facturation parent.
    /// - Monthly : MVP. 1 Invoice par mois calendrier, jour de génération = MonthlyDueDay.
    /// - SchoolYear : V2 (Phase 8.5). Skip automatiquement juillet-août.
    /// </summary>
    public enum BillingPeriod
    {
        Monthly = 0,
        SchoolYear = 1
    }
}
