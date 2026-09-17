namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Une sortie d'argent du compte marchand, telle que la réconciliation la
    /// voit — indépendamment de la forme que lui donne le prestataire.
    /// </summary>
    /// <param name="Reference">Identifiant de la transaction chez le prestataire.</param>
    /// <param name="ClientReference">Notre référence (<c>Withdrawal.Id</c>), absente si la sortie n'est pas passée par Idara.</param>
    /// <param name="AmountFcfa">Montant reçu par le bénéficiaire.</param>
    /// <param name="FeeFcfa">Frais prélevés en sus.</param>
    /// <param name="ReserveDebitFcfa">Impact réel sur le solde = montant + frais. C'est ce qu'on impute.</param>
    /// <param name="IsManual">
    /// Sortie faite hors d'Idara (application Business). C'est elle qu'on
    /// cherche : elle creuse la réserve sans qu'aucune écriture ne l'explique,
    /// et casse <c>R = D + P</c> en silence (§112).
    /// </param>
    public record ProviderPayoutRow(
        string? Reference,
        string? ClientReference,
        long AmountFcfa,
        long FeeFcfa,
        long ReserveDebitFcfa,
        string? RecipientPhone,
        string? RecipientName,
        DateTime? CompletedAt,
        bool IsManual);
}
