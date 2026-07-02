namespace Idara.API.Enums
{
    /// <summary>
    /// Nature métier d'un <see cref="Models.Payment"/>. Remplace la détection
    /// fragile par champs null (StudentId/GuardianId) qui ne distinguait pas un
    /// topup d'un don (les deux ont Student/Guardian null). Le webhook payin
    /// branche désormais dessus (crédit invoice, notif, reçu, poches wallet).
    /// - SchoolFee : paiement parent → école (mensualité ou montant libre).
    /// - WalletTopup : recharge du wallet par le SchoolAdmin (Phase 4).
    /// - Donation : don d'un DONATEUR → école (StudentId/GuardianId null,
    ///   DonorId renseigné). Crédite la poche « Don » du wallet école.
    /// </summary>
    public enum PaymentPurpose
    {
        SchoolFee = 0,
        WalletTopup = 1,
        Donation = 2
    }
}
