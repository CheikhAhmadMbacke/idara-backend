using System.Globalization;
using System.Text;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Le motif imprimé sur le reçu du bénéficiaire, dans son application Wave.
    ///
    /// <para>🔑 <b>C'est la seule chose qu'un directeur lit</b> quand l'argent
    /// arrive sur son téléphone, et souvent des heures après l'avoir demandé.
    /// « Idara retrait ecole 5 » ne lui apprend rien : le numéro interne d'une
    /// école ne veut rien dire pour elle, et il ressemble à un montant. Le
    /// motif doit nommer ce qui s'est passé et pour qui.</para>
    ///
    /// <para>⚠️ Wave limite ce champ à <b>40 caractères</b>. Au-delà, l'appel
    /// est rejeté — on coupe donc, mais du côté du NOM (§243 : un nom se coupe,
    /// un libellé à nous se réduit), en gardant toujours le mot qui dit la
    /// nature de l'opération.</para>
    /// </summary>
    public static class PayoutReason
    {
        /// <summary>Limite imposée par Wave sur <c>payment_reason</c>.</summary>
        public const int MaxLength = 40;

        /// <summary>Retrait d'un établissement vers son propre numéro.</summary>
        public static string ForSchoolWithdrawal(string? schoolName) =>
            Compose("Retrait", schoolName);

        /// <summary>Transfert d'un établissement vers un bénéficiaire (fournisseur, personnel…).</summary>
        public static string ForBeneficiaryTransfer(string? schoolName) =>
            Compose("Transfert", schoolName);

        /// <summary>Retrait des gains de la plateforme — aucun établissement concerné.</summary>
        public static string ForPlatformWithdrawal() => "Idara - gains plateforme";

        /// <summary>
        /// « Retrait Ecole de demonstration ». Sans accent : le reçu d'un
        /// opérateur de paiement mobile n'est pas garanti de les rendre, et un
        /// « Ã© » au milieu d'un nom d'école inquiète plus qu'il n'informe.
        /// </summary>
        private static string Compose(string nature, string? schoolName)
        {
            var nom = Sanitize(schoolName);
            if (string.IsNullOrWhiteSpace(nom)) return $"{nature} Idara";

            var motif = $"{nature} {nom}";
            if (motif.Length <= MaxLength) return motif;

            // On rogne le NOM, jamais la nature de l'opération : « Retrait »
            // doit rester lisible même pour une école au nom très long.
            var place = MaxLength - nature.Length - 2;   // espace + point de suspension
            if (place < 4) return nature;
            return $"{nature} {nom[..place].TrimEnd()}.";
        }

        /// <summary>
        /// Retire accents et caractères que l'opérateur pourrait refuser, en
        /// gardant lettres, chiffres, espaces et quelques séparateurs courants.
        /// </summary>
        private static string Sanitize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var decompose = raw.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decompose.Length);
            foreach (var c in decompose)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;   // l'accent lui-même
                if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '\'' or '.')
                    sb.Append(c);
                else if (!char.IsControl(c))
                    sb.Append(' ');
            }

            // Espaces multiples réduits : un nom d'école saisi à la main en
            // contient souvent, et ils mangent la place disponible.
            var texte = sb.ToString().Normalize(NormalizationForm.FormC);
            while (texte.Contains("  ")) texte = texte.Replace("  ", " ");
            return texte.Trim();
        }
    }
}
