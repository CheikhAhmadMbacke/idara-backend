namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Recherche texte des historiques de transactions — SOURCE UNIQUE de la
    /// normalisation, pour que les 7 écrans (wallet, paiements reçus, retraits,
    /// caisse, paiements parent, dons, virements reçus) se comportent pareil.
    ///
    /// <para>La recherche est faite CÔTÉ SERVEUR, sur tout l'historique : une
    /// école doit pouvoir retrouver un virement d'il y a un an, pas seulement
    /// dans les lignes déjà affichées à l'écran.</para>
    ///
    /// <para>Comparaison via <c>ILIKE</c> PostgreSQL : insensible à la casse sans
    /// dépendre d'un <c>ToLower()</c> côté serveur (qui empêche l'usage d'un
    /// index). Les caractères joker de LIKE présents dans la saisie sont échappés
    /// — sans quoi taper « % » ramènerait TOUT l'historique, et « _ » ferait
    /// correspondre n'importe quel caractère.</para>
    /// </summary>
    public static class TransactionSearch
    {
        /// <summary>
        /// Longueur maximale retenue : au-delà, c'est un copier-coller accidentel
        /// (ou une tentative d'alourdir la requête), pas une recherche.
        /// </summary>
        private const int MaxLength = 80;

        /// <summary>
        /// Transforme la saisie en motif <c>%terme%</c> prêt pour ILIKE, ou
        /// <c>null</c> si la recherche est vide (= pas de filtre).
        /// </summary>
        public static string? Pattern(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var term = raw.Trim();
            if (term.Length > MaxLength) term = term[..MaxLength];

            // \ d'abord : sinon on ré-échapperait les antislashs qu'on vient d'ajouter.
            term = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            return $"%{term}%";
        }

        /// <summary>
        /// Variante « chiffres seuls » d'une saisie, pour chercher un numéro de
        /// téléphone quel que soit son formatage : « 77 123 45 67 », « +221771234567 »
        /// et « 771234567 » doivent tous retrouver la même transaction.
        /// Renvoie <c>null</c> si la saisie ne contient pas assez de chiffres.
        /// </summary>
        public static string? PhonePattern(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length < 4) return null; // trop court = trop de faux positifs
            // Un numéro stocké en +221771234567 doit sortir sur une saisie « 771234567 ».
            return $"%{digits}%";
        }
    }
}
