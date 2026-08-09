namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Référence Idara d'une opération, telle qu'imprimée sur les reçus et
    /// affichée dans les détails de transaction.
    ///
    /// Elle sert la réconciliation : quand un daara appelle au sujet d'un
    /// virement, cette référence désigne SANS ambiguïté une ligne de la base,
    /// là où « Réservation retrait #87 » — le libellé interne affiché jusqu'ici
    /// sous l'étiquette « Référence » — ne désignait rien de trouvable.
    ///
    /// Un préfixe par TABLE, pas par nature d'opération : un don, une recharge
    /// et une mensualité sont tous des lignes de <c>Payments</c> et partagent le
    /// même espace d'identifiants. Leur donner trois préfixes différents
    /// fabriquerait trois références pour ce qui se retrouve d'une seule requête.
    /// </summary>
    public static class IdaraReference
    {
        /// <summary>Encaissement (mensualité, don, recharge) — table <c>Payments</c>.</summary>
        public static string Payment(int paymentId) => $"PAY-{paymentId:D6}";

        /// <summary>Retrait ou virement sortant — table <c>Withdrawals</c>.</summary>
        public static string Withdrawal(int withdrawalId) => $"RET-{withdrawalId:D6}";
    }
}
