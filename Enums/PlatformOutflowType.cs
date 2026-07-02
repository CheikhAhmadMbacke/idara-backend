namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'une sortie du solde de gains plateforme (réserve marchand SenePay
    /// appartenant à la plateforme, hors dette écoles).
    /// </summary>
    public enum PlatformOutflowType
    {
        /// <summary>Retrait des gains via l'API SenePay Payout (Phase B).</summary>
        PlatformWithdrawal = 0,

        /// <summary>
        /// Retrait effectué manuellement depuis le dashboard marchand SenePay
        /// (hors Idara), enregistré a posteriori pour garder la réconciliation
        /// exacte. Sans mouvement SenePay déclenché par Idara.
        /// </summary>
        ManualAdjustment = 1
    }
}
