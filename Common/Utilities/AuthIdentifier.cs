namespace Idara.API.Common.Utilities
{
    /// <summary>Nature d'un identifiant de connexion.</summary>
    public enum IdentifierKind
    {
        /// <summary>Ni une adresse email plausible, ni un mobile sénégalais.</summary>
        Invalid = 0,
        Email = 1,
        Phone = 2,
    }

    /// <summary>Motif du refus, pour choisir le message affiché.</summary>
    public enum IdentifierError
    {
        None = 0,
        /// <summary>Rien n'a été saisi.</summary>
        Empty,
        /// <summary>Une adresse sans « @ », ou mal formée.</summary>
        BadEmail,
        /// <summary>Moins ou plus de 9 chiffres.</summary>
        BadPhoneLength,
        /// <summary>Neuf chiffres, mais un préfixe qu'aucun opérateur n'exploite.</summary>
        UnknownPrefix,
    }

    /// <summary>
    /// Un identifiant lu et normalisé : une adresse en minuscules, ou un numéro
    /// en E.164.
    /// </summary>
    public readonly record struct AuthId(IdentifierKind Kind, string Value, IdentifierError Error)
    {
        public bool IsValid => Kind != IdentifierKind.Invalid;
        public bool IsEmail => Kind == IdentifierKind.Email;
        public bool IsPhone => Kind == IdentifierKind.Phone;
    }

    /// <summary>
    /// Lecture d'un identifiant de compte — une adresse email <b>ou</b> un
    /// numéro de téléphone.
    ///
    /// <para><b>La règle n'est pas nouvelle</b> : <c>Login</c> l'applique depuis
    /// toujours — la présence d'un <c>@</c> tranche. Elle est simplement
    /// rassemblée ici pour que l'inscription, la connexion et la
    /// réinitialisation lisent un identifiant de la <b>même</b> façon. Deux
    /// copies d'une règle métier finissent toujours par diverger (§199).</para>
    ///
    /// <para>🔑 Pourquoi le <c>@</c> et pas une expression régulière savante :
    /// aucun numéro n'en contient, et aucune adresse n'en manque. Toute
    /// subtilité supplémentaire ne servirait qu'à refuser des saisies valides.</para>
    /// </summary>
    public static class AuthIdentifier
    {
        /// <summary>
        /// Préfixes mobiles réellement attribués au Sénégal, tels qu'ils sont
        /// écrits dans les réglages (« 70,75,76,77,78 »).
        ///
        /// <para>⚠️ Ce contrôle ne vaut que pour les <b>parcours publics</b>
        /// (inscription, réinitialisation). <see cref="SenegalPhone.Normalize"/>
        /// reste inchangé partout ailleurs : il sert aussi à enregistrer les
        /// responsables d'élèves, et le durcir d'un coup refuserait des numéros
        /// <b>déjà en base</b>.</para>
        /// </summary>
        public static AuthId Parse(string? raw, string? allowedPrefixes = null)
        {
            var value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
                return new AuthId(IdentifierKind.Invalid, string.Empty, IdentifierError.Empty);

            if (value.Contains('@'))
            {
                // Une adresse doit avoir quelque chose des deux côtés du @, et un
                // point après. On ne va pas plus loin : c'est l'envoi du code qui
                // dira si l'adresse existe vraiment.
                var parts = value.Split('@');
                var ok = parts.Length == 2
                         && parts[0].Length > 0
                         && parts[1].Contains('.')
                         && parts[1].Length >= 3
                         && !parts[1].StartsWith('.')
                         && !parts[1].EndsWith('.');
                return ok
                    ? new AuthId(IdentifierKind.Email, value.ToLowerInvariant(), IdentifierError.None)
                    : new AuthId(IdentifierKind.Invalid, string.Empty, IdentifierError.BadEmail);
            }

            var phone = SenegalPhone.Normalize(value);
            if (phone == null)
                return new AuthId(IdentifierKind.Invalid, string.Empty, IdentifierError.BadPhoneLength);

            if (!HasAllowedPrefix(phone, allowedPrefixes))
                return new AuthId(IdentifierKind.Invalid, string.Empty, IdentifierError.UnknownPrefix);

            return new AuthId(IdentifierKind.Phone, phone, IdentifierError.None);
        }

        /// <summary>
        /// Vrai si le numéro E.164 commence par l'un des préfixes autorisés.
        /// Une liste vide ou absente laisse passer : un réglage non renseigné ne
        /// doit pas fermer l'inscription (contrairement aux taux de frais, §256,
        /// où l'absence est un refus — ici, refuser tout le monde serait pire
        /// que d'accepter un préfixe de trop).
        /// </summary>
        private static bool HasAllowedPrefix(string e164, string? allowedPrefixes)
        {
            if (string.IsNullOrWhiteSpace(allowedPrefixes)) return true;
            var national = e164.StartsWith("+221") ? e164[4..] : e164;
            foreach (var p in allowedPrefixes.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                        | StringSplitOptions.TrimEntries))
            {
                if (national.StartsWith(p, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Message destiné à l'utilisateur. Il dit quoi FAIRE, jamais ce qui a
        /// raté (§125) — et il nomme la sortie de secours quand il y en a une.
        /// </summary>
        public static string MessageFor(IdentifierError error) => error switch
        {
            IdentifierError.Empty =>
                "Entrez votre numéro de téléphone ou votre adresse email.",
            IdentifierError.BadEmail =>
                "Cette adresse ne ressemble pas à une adresse email. Exemple : nom@gmail.com",
            IdentifierError.BadPhoneLength =>
                "Un numéro sénégalais a 9 chiffres. Exemple : 77 123 45 67.",
            IdentifierError.UnknownPrefix =>
                "Ce numéro ne correspond à aucun opérateur. Un numéro sénégalais commence par 70, 75, 76, 77 ou 78.",
            _ => "Identifiant invalide.",
        };
    }
}
