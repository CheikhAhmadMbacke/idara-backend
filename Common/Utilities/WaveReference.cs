using System.Globalization;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Le <c>client_reference</c> qu'Idara pose sur chaque opération Wave, et la
    /// seule façon de le relire.
    ///
    /// <para>🔑 <b>Le compte Wave est PARTAGÉ avec Àppal</b> (autre produit
    /// Pyranil, autre clé d'API, même portefeuille). Dans l'application Wave, une
    /// référence nue « 397 » ne dit pas de quel produit vient l'opération. Toute
    /// référence d'Idara commence donc par <c>idara-</c> — comme celles d'Àppal
    /// commencent par <c>appal:</c> — et nomme sa nature : <c>idara-paiement-397</c>,
    /// <c>idara-retrait-12</c>.</para>
    ///
    /// <para>⚠️ <b>Les références nues restent reconnues</b> : tout ce qui a été
    /// ouvert avant le 2026-10-12 en porte une, et une session ou un retrait
    /// encore en vérification doit pouvoir être retrouvé (§78). Une référence
    /// d'un AUTRE produit (<c>appal:…</c>, ou tout ce qui n'est ni <c>idara-</c>
    /// ni un nombre) n'est jamais à nous : elle s'ignore, elle n'est pas une
    /// erreur.</para>
    /// </summary>
    public static class WaveReference
    {
        public const string Prefix = "idara-";
        private const string PaymentPrefix = Prefix + "paiement-";
        private const string WithdrawalPrefix = Prefix + "retrait-";

        /// <summary>Session d'encaissement : <c>idara-paiement-{Payment.Id}</c>.</summary>
        public static string ForPayment(int paymentId) =>
            PaymentPrefix + paymentId.ToString(CultureInfo.InvariantCulture);

        /// <summary>Décaissement : <c>idara-retrait-{Withdrawal.Id}</c>.</summary>
        public static string ForWithdrawal(int withdrawalId) =>
            WithdrawalPrefix + withdrawalId.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// La forme d'avant le 2026-10-12 (l'identifiant nu). Ne sert qu'à
        /// retrouver une opération ouverte avant la bascule.
        /// </summary>
        public static string Legacy(int id) => id.ToString(CultureInfo.InvariantCulture);

        /// <summary>Relit un <c>Payment.Id</c> — forme actuelle ou ancienne.</summary>
        public static bool TryParsePayment(string? reference, out int paymentId) =>
            TryParse(reference, PaymentPrefix, out paymentId);

        /// <summary>Relit un <c>Withdrawal.Id</c> — forme actuelle ou ancienne.</summary>
        public static bool TryParseWithdrawal(string? reference, out int withdrawalId) =>
            TryParse(reference, WithdrawalPrefix, out withdrawalId);

        private static bool TryParse(string? reference, string prefix, out int id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(reference)) return false;

            var digits = reference.Trim();
            if (digits.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                digits = digits[prefix.Length..];

            // NumberStyles.None : ni signe, ni espace, ni séparateur — « appal:12 »
            // ou « idara-retrait-12 » lu comme un paiement ne passent pas.
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out id)
                   && id > 0;
        }
    }
}
