using Idara.API.DTOs.Wave;

namespace Idara.API.Services
{
    /// <summary>
    /// Client HTTP typé pour l'API Wave Business. Un seul point d'accès au
    /// prestataire : c'est ici que sont appliqués l'authentification Bearer,
    /// la signature des requêtes (<c>Wave-Signature</c>) et la clé
    /// d'idempotence des décaissements. Ne jamais appeler Wave autrement.
    /// </summary>
    public interface IWaveClient
    {
        // ---------------------------------------------------------------
        // Encaissement (Checkout)
        // ---------------------------------------------------------------

        /// <summary>
        /// <c>POST /v1/checkout/sessions</c> — crée la session et renvoie le
        /// <c>wave_launch_url</c> vers lequel envoyer le payeur.
        /// </summary>
        /// <exception cref="WaveApiException">
        /// Sur 4xx/5xx/timeout. L'appelant branche sur
        /// <see cref="WaveApiException.StatusCode"/> : un 4xx est un rejet
        /// AVANT exécution (rien n'a été créé, sûr de clore en échec) ; un
        /// 5xx ou un timeout est indéterminé (ne rien conclure).
        /// </exception>
        Task<WaveCheckoutSession> CreateCheckoutSessionAsync(
            WaveCreateCheckoutRequest request, CancellationToken ct = default);

        /// <summary>
        /// <c>GET /v1/checkout/sessions/:id</c> — état AUTORITATIF d'un
        /// encaissement. <c>null</c> si la session est inconnue de Wave (404).
        /// </summary>
        Task<WaveCheckoutSession?> GetCheckoutSessionAsync(
            string sessionId, CancellationToken ct = default);

        /// <summary>
        /// <c>GET /v1/checkout/sessions/search?client_reference=…</c> — filet
        /// quand on a perdu l'identifiant de session (timeout à la création)
        /// mais qu'on connaît notre propre référence.
        /// </summary>
        Task<WaveCheckoutSession?> FindCheckoutByClientReferenceAsync(
            string clientReference, CancellationToken ct = default);

        /// <summary>
        /// <c>POST /v1/checkout/sessions/:id/refund</c> — remboursement
        /// (art. 5.6 du contrat : toute réclamation doit être traitée sous
        /// 24 h). Idempotent côté Wave. <c>false</c> si la session est inconnue.
        /// </summary>
        Task<bool> RefundCheckoutAsync(string sessionId, CancellationToken ct = default);

        // ---------------------------------------------------------------
        // Décaissement (Payout)
        // ---------------------------------------------------------------

        /// <summary>
        /// <c>POST /v1/payout</c> — décaissement vers un numéro Mobile Money.
        /// </summary>
        /// <param name="idempotencyKey">
        /// 🔴 OBLIGATOIRE et STABLE pour un même retrait : c'est la seule chose
        /// qui empêche un rejeu après timeout de partir une seconde fois. Deux
        /// clés différentes pour le même retrait = deux décaissements réels.
        /// </param>
        Task<WavePayout> CreatePayoutAsync(
            WaveCreatePayoutRequest request, string idempotencyKey, CancellationToken ct = default);

        /// <summary>
        /// <c>GET /v1/payout/:id</c> — état AUTORITATIF d'un décaissement.
        /// <c>null</c> sur 404 (jamais créé).
        /// <para>🔴 Wave n'émet AUCUN webhook de décaissement : c'est l'unique
        /// chemin pour trancher un retrait. Le poll n'est plus un filet.</para>
        /// </summary>
        Task<WavePayout?> GetPayoutAsync(string payoutId, CancellationToken ct = default);

        /// <summary>
        /// <c>GET /v1/payouts/search?client_reference=…</c> — retrouve un
        /// décaissement dont on n'a pas gardé l'identifiant Wave (timeout à la
        /// création). Indispensable au traitement des états indéterminés (§78).
        /// </summary>
        Task<WavePayout?> FindPayoutByClientReferenceAsync(
            string clientReference, CancellationToken ct = default);

        // ---------------------------------------------------------------
        // Solde et registre
        // ---------------------------------------------------------------

        /// <summary><c>GET /v1/balance</c> — solde du compte marchand, en FCFA.</summary>
        Task<long> GetBalanceFcfaAsync(CancellationToken ct = default);

        /// <summary>
        /// <c>GET /v1/transactions</c> — registre d'une journée, paginé vers
        /// l'avant. Source de la réconciliation et de la détection des
        /// mouvements faits hors Idara.
        /// </summary>
        Task<WaveTransactionPage> GetTransactionsAsync(
            DateOnly? date, string? after, int? first, CancellationToken ct = default);
    }
}
