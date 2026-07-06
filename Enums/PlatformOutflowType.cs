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
        ManualAdjustment = 1,

        /// <summary>
        /// Injection de capital : argent que la plateforme ajoute elle-même à la
        /// réserve marchand SenePay (recharge de son propre compte), hors gains
        /// opérationnels. AUGMENTE le solde plateforme P (mouvement ENTRANT). Par
        /// convention, <see cref="PlatformOutflow.AmountFcfa"/> reste positif ; le
        /// calcul de P l'ADDITIONNE au lieu de le soustraire.
        /// </summary>
        CapitalInjection = 2,

        /// <summary>
        /// Contrepartie plateforme du DÉBIT manuel d'un wallet école (SuperAdmin).
        /// Quand on débite une école de X sans que la réserve R ne bouge (ex :
        /// retrait payé hors SenePay depuis l'argent perso de la plateforme, ou
        /// correction), le montant X « revient » aux gains plateforme et devient
        /// retirable (pour se rembourser). AUGMENTE P (mouvement ENTRANT, comme
        /// <see cref="CapitalInjection"/>) : <see cref="PlatformOutflow.AmountFcfa"/>
        /// reste positif, le calcul de P l'ADDITIONNE. Garde R = D + P exact quand
        /// D baisse. La contrepartie SYMÉTRIQUE d'un CRÉDIT école (P baisse) réutilise
        /// <see cref="ManualAdjustment"/> (montant soustrait de P).
        /// </summary>
        SchoolDebitReturn = 3
    }
}
