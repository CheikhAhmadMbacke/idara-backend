using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Réponse de <c>GET /api/v1/merchant/wallet/balance</c>. Utilisée par le job
    /// de réconciliation quotidien pour vérifier l'invariant
    /// <c>réserve ≥ Σ(SchoolWallet.Available + Pending)</c>.
    ///
    /// ⚠️ Le solde est **imbriqué sous `data`** (doc SenePay §wallet) :
    /// <code>{ "message": "...", "data": { "balance": 125430, "currency": "XOF", "updatedAt": "..." } }</code>
    /// Il n'y a PAS de champ `success` ni de solde top-level.
    /// </summary>
    public class SenePayMerchantBalanceResponse
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public SenePayMerchantBalanceData? Data { get; set; }

        /// <summary>Solde réserve en FCFA (XOF = pas de décimales, arrondi défensif).</summary>
        [JsonIgnore]
        public long ReserveBalanceFcfa =>
            (long)System.Math.Round(Data?.Balance ?? 0m, System.MidpointRounding.AwayFromZero);
    }

    public class SenePayMerchantBalanceData
    {
        [JsonPropertyName("balance")]
        public decimal Balance { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }
}
