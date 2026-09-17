using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Wave
{
    // =====================================================================
    // Checkout API — https://docs.wave.com/checkout
    // ⚠️ Chez Wave, TOUS les montants sont des CHAÎNES, et XOF n'accepte
    //    aucune décimale. On convertit aux frontières (long ⇄ string) pour
    //    que le reste du code continue de manipuler des `long` (§55).
    // =====================================================================

    /// <summary>Corps de <c>POST /v1/checkout/sessions</c>.</summary>
    public class WaveCreateCheckoutRequest
    {
        [JsonPropertyName("amount")]
        public string Amount { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "XOF";

        /// <summary>URL HTTPS de retour après paiement réussi.</summary>
        [JsonPropertyName("success_url")]
        public string SuccessUrl { get; set; } = string.Empty;

        /// <summary>URL HTTPS de retour après échec ou abandon.</summary>
        [JsonPropertyName("error_url")]
        public string ErrorUrl { get; set; } = string.Empty;

        /// <summary>Notre référence (le <c>Payment.Id</c>) — ≤ 255 caractères.</summary>
        [JsonPropertyName("client_reference")]
        public string? ClientReference { get; set; }

        /// <summary>
        /// 🔴 VOLONTAIREMENT JAMAIS RENSEIGNÉ (décision du 2026-09-17).
        /// Le renseigner VERROUILLE la session sur ce numéro : tout autre
        /// payeur est refusé en <c>payer-mobile-mismatch</c>. Or au daara,
        /// l'oncle, le grand frère ou un voisin règlent couramment depuis leur
        /// propre compte — c'était déjà le comportement avec le prestataire
        /// précédent, où le numéro n'était qu'informatif. On garde donc la
        /// saisie du numéro (elle sert au reçu SMS, §229) sans l'imposer.
        /// </summary>
        [JsonPropertyName("restrict_payer_mobile")]
        public string? RestrictPayerMobile { get; set; }
    }

    /// <summary>Objet Session renvoyé par le Checkout (création et lecture).</summary>
    public class WaveCheckoutSession
    {
        /// <summary>Identifiant <c>cos-…</c>.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        /// <summary><c>open</c> · <c>complete</c> · <c>expired</c>.</summary>
        [JsonPropertyName("checkout_status")]
        public string? CheckoutStatus { get; set; }

        /// <summary><c>processing</c> · <c>cancelled</c> · <c>succeeded</c>.</summary>
        [JsonPropertyName("payment_status")]
        public string? PaymentStatus { get; set; }

        [JsonPropertyName("client_reference")]
        public string? ClientReference { get; set; }

        /// <summary>Identifiant visible par le payeur dans son application Wave.</summary>
        [JsonPropertyName("transaction_id")]
        public string? TransactionId { get; set; }

        /// <summary>🔑 C'est là qu'on envoie le payeur (équivalent de l'ancien redirectUrl).</summary>
        [JsonPropertyName("wave_launch_url")]
        public string? WaveLaunchUrl { get; set; }

        [JsonPropertyName("last_payment_error")]
        public WaveErrorDetail? LastPaymentError { get; set; }

        [JsonPropertyName("when_created")]
        public DateTime? WhenCreated { get; set; }

        [JsonPropertyName("when_completed")]
        public DateTime? WhenCompleted { get; set; }

        [JsonPropertyName("when_expires")]
        public DateTime? WhenExpires { get; set; }

        [JsonPropertyName("business_name")]
        public string? BusinessName { get; set; }
    }

    public class WaveErrorDetail
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    /// <summary>Enveloppe de <c>GET /v1/checkout/sessions/search</c>.</summary>
    public class WaveCheckoutSearchResponse
    {
        [JsonPropertyName("result")]
        public List<WaveCheckoutSession> Result { get; set; } = new();
    }

    // =====================================================================
    // Payout API — https://docs.wave.com/payout
    // =====================================================================

    /// <summary>Corps de <c>POST /v1/payout</c>.</summary>
    public class WaveCreatePayoutRequest
    {
        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "XOF";

        /// <summary>Bénéficiaire au format E.164 (<c>+221…</c>).</summary>
        [JsonPropertyName("mobile")]
        public string Mobile { get; set; } = string.Empty;

        /// <summary>
        /// 🔑 Ce que le bénéficiaire reçoit, NET. Les frais Wave s'ajoutent au
        /// débit de notre wallet — c'est la sémantique « frais en sus » qu'on
        /// forçait déjà chez le prestataire précédent (« ni plus ni moins »
        /// côté daara).
        /// </summary>
        [JsonPropertyName("receive_amount")]
        public string ReceiveAmount { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("national_id")]
        public string? NationalId { get; set; }

        /// <summary>Notre référence (le <c>Withdrawal.Id</c>).</summary>
        [JsonPropertyName("client_reference")]
        public string? ClientReference { get; set; }

        /// <summary>≤ 40 caractères — IMPRIMÉ sur le reçu du bénéficiaire.</summary>
        [JsonPropertyName("payment_reason")]
        public string? PaymentReason { get; set; }
    }

    /// <summary>Objet Payout (création, lecture, réversion).</summary>
    public class WavePayout
    {
        /// <summary>Identifiant <c>pt-…</c>.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("receive_amount")]
        public string? ReceiveAmount { get; set; }

        /// <summary>Frais Wave, débités de NOTRE wallet en plus du montant reçu.</summary>
        [JsonPropertyName("fee")]
        public string? Fee { get; set; }

        [JsonPropertyName("mobile")]
        public string? Mobile { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("client_reference")]
        public string? ClientReference { get; set; }

        /// <summary><c>processing</c> · <c>succeeded</c> · <c>failed</c> · <c>reversed</c>.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonPropertyName("payout_error")]
        public WaveErrorDetail? PayoutError { get; set; }
    }

    /// <summary>Enveloppe de <c>GET /v1/payouts/search</c>.</summary>
    public class WavePayoutSearchResponse
    {
        [JsonPropertyName("result")]
        public List<WavePayout> Result { get; set; } = new();
    }

    // =====================================================================
    // Balance & Reconciliation API — https://docs.wave.com/balance-api
    // =====================================================================

    public class WaveBalance
    {
        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
    }

    /// <summary>
    /// Une ligne du registre du compte marchand. C'est la source de vérité de
    /// la réconciliation <c>R = D + P</c> (§112) : elle porte les frais ligne à
    /// ligne ET les mouvements faits hors Idara (depuis l'application Business).
    /// </summary>
    public class WaveTransaction
    {
        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonPropertyName("transaction_id")]
        public string? TransactionId { get; set; }

        [JsonPropertyName("transaction_type")]
        public string? TransactionType { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("fee")]
        public string? Fee { get; set; }

        [JsonPropertyName("balance")]
        public string? Balance { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("is_reversal")]
        public bool? IsReversal { get; set; }

        [JsonPropertyName("counterparty_name")]
        public string? CounterpartyName { get; set; }

        [JsonPropertyName("counterparty_mobile")]
        public string? CounterpartyMobile { get; set; }

        [JsonPropertyName("client_reference")]
        public string? ClientReference { get; set; }

        [JsonPropertyName("payment_reason")]
        public string? PaymentReason { get; set; }

        [JsonPropertyName("checkout_api_session_id")]
        public string? CheckoutSessionId { get; set; }

        [JsonPropertyName("government_tax_amount")]
        public string? GovernmentTaxAmount { get; set; }

        [JsonPropertyName("government_tax_paid_by_wave")]
        public bool? GovernmentTaxPaidByWave { get; set; }
    }

    public class WaveTransactionPage
    {
        [JsonPropertyName("items")]
        public List<WaveTransaction> Items { get; set; } = new();

        [JsonPropertyName("page_info")]
        public WavePageInfo? PageInfo { get; set; }
    }

    public class WavePageInfo
    {
        [JsonPropertyName("has_next_page")]
        public bool HasNextPage { get; set; }

        [JsonPropertyName("end_cursor")]
        public string? EndCursor { get; set; }
    }

    // =====================================================================
    // Webhooks — https://docs.wave.com/webhook
    // =====================================================================

    /// <summary>
    /// Enveloppe commune à tous les événements : <c>{ id, type, data }</c>.
    /// <c>id</c> est la clé d'idempotence (§50) — meilleure que l'ancienne, qui
    /// reposait sur l'identifiant de transaction.
    /// </summary>
    public class WaveWebhookEnvelope
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("data")]
        public WaveWebhookData? Data { get; set; }
    }

    /// <summary>
    /// Union des champs portés par <c>data</c> selon le type d'événement.
    /// Wave n'envoie que les champs pertinents ; les autres restent nuls.
    /// </summary>
    public class WaveWebhookData
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("fee")]
        public string? Fee { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("payment_status")]
        public string? PaymentStatus { get; set; }

        [JsonPropertyName("checkout_status")]
        public string? CheckoutStatus { get; set; }

        [JsonPropertyName("client_reference")]
        public string? ClientReference { get; set; }

        [JsonPropertyName("transaction_id")]
        public string? TransactionId { get; set; }

        [JsonPropertyName("sender_mobile")]
        public string? SenderMobile { get; set; }

        [JsonPropertyName("when_completed")]
        public DateTime? WhenCompleted { get; set; }

        [JsonPropertyName("when_created")]
        public DateTime? WhenCreated { get; set; }

        [JsonPropertyName("last_payment_error")]
        public WaveErrorDetail? LastPaymentError { get; set; }
    }
}
