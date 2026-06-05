using System.Text.Json.Serialization;

namespace Idara.API.DTOs.Senepay
{
    /// <summary>
    /// Réponse de <c>GET /api/v1/merchant/wallet/balance</c>. Utilisée par le job
    /// de réconciliation quotidien pour vérifier l'invariant
    /// <c>solde réserve ≈ Σ(SchoolWallet.Available + Pending)</c>. Le champ exact
    /// du solde disponible n'est pas figé côté SenePay — on lit `available_balance`
    /// avec repli sur `balance` si absent.
    /// </summary>
    public class SenePayMerchantBalanceResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("available_balance")]
        public decimal? AvailableBalance { get; set; }

        [JsonPropertyName("balance")]
        public decimal? Balance { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        /// <summary>Solde réserve effectif en FCFA (available_balance prioritaire, repli balance).</summary>
        [JsonIgnore]
        public long ReserveBalanceFcfa =>
            (long)System.Math.Round(AvailableBalance ?? Balance ?? 0m, System.MidpointRounding.AwayFromZero);
    }
}
