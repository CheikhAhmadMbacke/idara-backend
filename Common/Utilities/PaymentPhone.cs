namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Normalise un numéro de téléphone STOCKÉ (identité du payeur, récupéré en
    /// base depuis la refonte UX 2026-07-07 : plus de saisie côté client) vers le
    /// format attendu par SenePay : <c>+221XXXXXXXXX</c>.
    /// </summary>
    public static class PaymentPhone
    {
        /// <summary>
        /// Accepte l'E.164 (<c>+221XXXXXXXXX</c>, format de stockage §91), le
        /// national (<c>XXXXXXXXX</c>) ou avec indicatif sans « + » (<c>221…</c>,
        /// <c>00221…</c>). Idempotent. On ne valide pas ici (le numéro vient de
        /// notre base, déjà normalisé) — on garantit juste le préfixe « +221 ».
        /// </summary>
        public static string ForSenePay(string? phone)
        {
            var p = (phone ?? string.Empty).Trim().Replace(" ", string.Empty);
            if (p.StartsWith("+")) return p;
            if (p.StartsWith("00221")) return "+" + p[2..];
            if (p.StartsWith("221")) return "+" + p;
            return "+221" + p;
        }
    }
}
