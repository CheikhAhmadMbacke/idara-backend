using Idara.API.Enums;
using Idara.API.Models;

namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// Aide au calcul des DEUX poches d'un <see cref="SchoolWallet"/> :
    /// Solde paiement (= <c>Available − DonationBalance</c>) et Solde don
    /// (<see cref="SchoolWallet.DonationBalanceFcfa"/>). Utilisé partout où un
    /// débit du wallet doit répartir sur les poches selon la source choisie
    /// (retrait : le daara choisit ; abonnement auto : paiements d'abord).
    ///
    /// À appeler SOUS le verrou pessimiste (LockWalletAsync) — ces méthodes lisent
    /// l'état du wallet à un instant qui doit être figé.
    /// </summary>
    public static class WalletPocketExtensions
    {
        /// <summary>Solde « paiement » dérivé (mensualités parents + topups).</summary>
        public static long FeeBalance(this SchoolWallet w) =>
            w.AvailableBalance - w.DonationBalanceFcfa;

        /// <summary>
        /// Vrai si la poche choisie couvre <paramref name="amount"/>. Fee = solde
        /// paiement ; Donation = solde don ; Total = solde disponible complet.
        /// </summary>
        public static bool HasEnoughForSource(this SchoolWallet w, long amount, WithdrawalSource source) =>
            source switch
            {
                WithdrawalSource.Fee => w.FeeBalance() >= amount,
                WithdrawalSource.Donation => w.DonationBalanceFcfa >= amount,
                _ => w.AvailableBalance >= amount
            };

        /// <summary>
        /// Part de <paramref name="amount"/> prélevée sur la poche « Don » pour un
        /// débit de la source donnée. Fee → 0 (tout sur les paiements) ; Donation →
        /// tout ; Total → ce qui dépasse le solde paiement (paiements d'abord, puis
        /// dons). Suppose que la poche a été validée par <see cref="HasEnoughForSource"/>.
        /// </summary>
        public static long DonationDrawFor(this SchoolWallet w, long amount, WithdrawalSource source) =>
            source switch
            {
                WithdrawalSource.Fee => 0,
                WithdrawalSource.Donation => amount,
                _ => System.Math.Max(0, amount - w.FeeBalance())
            };
    }
}
