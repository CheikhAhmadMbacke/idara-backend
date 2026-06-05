namespace Idara.API.Enums
{
    /// <summary>
    /// Type de mouvement dans le journal append-only WalletTransaction.
    /// - Credit : entrée d'argent (paiement parent, topup).
    /// - Debit : sortie d'argent (retrait terminé, débit abo).
    /// - Reservation : Available → Pending (retrait initié, en attente webhook).
    /// - Release : Pending → Available (retrait échoué, restitution).
    /// - Adjustment : correction manuelle ou automatique (re-débit définitif
    ///   après un webhook `completed` tardif arrivé sur un retrait déjà restitué
    ///   — annule une restitution erronée, cf. scénario S3 du risque payout).
    /// </summary>
    public enum WalletTransactionType
    {
        Credit = 0,
        Debit = 1,
        Reservation = 2,
        Release = 3,
        Adjustment = 4
    }
}
