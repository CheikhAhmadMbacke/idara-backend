namespace Idara.API.Models
{
    /// <summary>
    /// Une ligne du registre du portefeuille Wave, recopiée telle que Wave la
    /// donne (<c>GET /v1/transactions</c>).
    ///
    /// <para>🔑 <b>Le portefeuille est PARTAGÉ avec Àppal</b> (autre produit
    /// Pyranil, autres clés : seul le portefeuille est commun). Son solde n'est
    /// donc pas la réserve d'Idara. La part d'Idara se reconstitue ligne à ligne,
    /// en rattachant chaque ligne à une opération qu'Idara connaît — d'où cette
    /// copie : relire 60 jours de registre à chaque affichage coûterait 60 appels,
    /// et une ligne attribuée doit pouvoir se montrer, pas seulement s'additionner.
    /// </para>
    ///
    /// <para>Append-only : une ligne de registre ne change jamais. Montants
    /// signés comme chez Wave — <see cref="AmountFcfa"/> EST la variation du solde,
    /// frais compris (§274).</para>
    /// </summary>
    public class WaveLedgerEntry
    {
        public int Id { get; set; }

        /// <summary><c>transaction_id</c> — <c>T_…</c> pour un encaissement, <c>pt-…</c> pour un décaissement.</summary>
        public string TransactionId { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }

        /// <summary><c>api_checkout</c>, <c>api_payout</c>, … — gardé brut.</summary>
        public string? TransactionType { get; set; }

        /// <summary>Variation du solde, signée, frais compris.</summary>
        public long AmountFcfa { get; set; }

        /// <summary>Frais de la ligne (positif) — décompose <see cref="AmountFcfa"/>, ne s'y ajoute pas.</summary>
        public long FeeFcfa { get; set; }

        /// <summary>Solde du portefeuille ENTIER après la ligne (Idara + Àppal).</summary>
        public long BalanceFcfa { get; set; }

        public bool IsReversal { get; set; }

        public string? ClientReference { get; set; }
        public string? PaymentReason { get; set; }

        /// <summary><c>cos-…</c> — rapproche un encaissement de <c>Payment.ProviderTransactionId</c>.</summary>
        public string? CheckoutSessionId { get; set; }

        public string? CounterpartyName { get; set; }
        public string? CounterpartyMobile { get; set; }

        /// <summary>Quand la ligne a été recopiée chez nous.</summary>
        public DateTime SyncedAt { get; set; }
    }
}
