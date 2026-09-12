using Idara.API.Constants;

namespace Idara.API.Common.Utilities
{
    /// <summary>Une ligne de personnel lue dans le tableau de l'école, déjà interprétée.</summary>
    public sealed class ParsedStaffRow
    {
        public int RowNumber { get; set; }

        /// <summary>Nom affiché. Le personnel n'a qu'un champ nom (cf. <c>User.FullName</c>).</summary>
        public string FullName { get; set; } = string.Empty;

        /// <summary>Numéro normalisé en E.164, ou null s'il était illisible.</summary>
        public string? Phone { get; set; }

        /// <summary>Constante de <see cref="UserRoles"/>, jamais un libellé libre.</summary>
        public string? Role { get; set; }

        /// <summary>Fonction affichée (« Cuisinière », « Comptable »…).</summary>
        public string? JobTitle { get; set; }

        /// <summary>false = compte de pointage, sans connexion ni code.</summary>
        public bool CanLogin { get; set; } = true;

        public string? Email { get; set; }

        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Traduit le tableau du personnel d'une école en comptes à créer.
    ///
    /// Même principe directeur que <see cref="StudentImportParser"/> :
    /// **indulgent sur la forme, strict sur le sens.** « Enseignant »,
    /// « professeur », « maître » ou « ustaz » désignent la même chose et
    /// doivent tous passer.
    ///
    /// <para><b>Une exception assumée à l'indulgence : la fonction.</b> Une
    /// fonction vide ou non reconnue est une ERREUR, jamais un défaut. Deviner
    /// « personnel » donnerait à quelqu'un l'accès aux élèves, aux classes et
    /// aux écrans financiers de l'école — un rôle ne se devine pas, il se
    /// déclare. C'est la même logique que le tarif d'un élève (§ StudentImportParser).</para>
    /// </summary>
    public static class StaffImportParser
    {
        public static readonly string[] HFullName =
            { "nom complet", "nom et prenom", "prenom et nom", "nom & prenom", "nom", "full name" };
        public static readonly string[] HFirstName = { "prenom", "prénom", "first name" };
        public static readonly string[] HLastName = { "nom de famille", "last name" };
        public static readonly string[] HPhone =
            { "telephone", "téléphone", "numero", "numéro", "tel", "portable", "phone" };
        public static readonly string[] HFunction =
            { "fonction", "role", "rôle", "qualite", "qualité", "categorie", "catégorie" };
        public static readonly string[] HJobTitle =
            { "intitule du poste", "intitulé du poste", "poste", "intitule", "intitulé", "specialite", "spécialité" };
        public static readonly string[] HCanLogin =
            { "acces a l'application", "accès à l'application", "acces", "accès", "connexion", "compte" };
        public static readonly string[] HEmail = { "email", "e-mail", "adresse email", "courriel" };

        public static List<ParsedStaffRow> Parse(SheetTable table)
        {
            var h = table.Headers;
            int iFull = SheetReader.IndexOf(h, HFullName);
            int iFirst = SheetReader.IndexOf(h, HFirstName);
            int iLast = SheetReader.IndexOf(h, HLastName);
            int iPhone = SheetReader.IndexOf(h, HPhone);
            int iFunc = SheetReader.IndexOf(h, HFunction);
            int iJob = SheetReader.IndexOf(h, HJobTitle);
            int iLogin = SheetReader.IndexOf(h, HCanLogin);
            int iMail = SheetReader.IndexOf(h, HEmail);

            // Une école peut avoir gardé un tableau à deux colonnes (Prénom /
            // Nom) : on recompose plutôt que de refuser le fichier. Si les deux
            // formes coexistent, « Nom complet » gagne — c'est la colonne du
            // modèle, donc celle que l'école a remplie en connaissance de cause.
            bool splitName = iFull < 0 && (iFirst >= 0 || iLast >= 0);

            var outp = new List<ParsedStaffRow>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                var p = new ParsedStaffRow
                {
                    RowNumber = i < table.RowNumbers.Count ? table.RowNumbers[i] : i + 2,
                    JobTitle = Nullify(SheetReader.Cell(row, iJob)),
                    Email = Nullify(SheetReader.Cell(row, iMail)),
                };

                p.FullName = splitName
                    ? $"{SheetReader.Cell(row, iFirst)} {SheetReader.Cell(row, iLast)}".Trim()
                    : SheetReader.Cell(row, iFull).Trim();

                if (string.IsNullOrWhiteSpace(p.FullName))
                    p.Errors.Add("Aucun nom : impossible d'identifier la personne.");
                else if (p.FullName.Length > 150)
                    p.FullName = p.FullName[..150];

                // --- Le numéro EST l'identifiant : sans lui, pas de compte.
                var phoneRaw = SheetReader.Cell(row, iPhone);
                if (string.IsNullOrWhiteSpace(phoneRaw))
                {
                    p.Errors.Add("Numéro de téléphone manquant : c'est lui qui identifie le compte.");
                }
                else
                {
                    var norm = SenegalPhone.Normalize(phoneRaw);
                    if (norm == null) p.Errors.Add($"Numéro de téléphone invalide (« {phoneRaw} »).");
                    else p.Phone = norm;
                }

                // --- La fonction : jamais devinée.
                var funcRaw = SheetReader.Cell(row, iFunc);
                if (string.IsNullOrWhiteSpace(funcRaw))
                {
                    p.Errors.Add("Fonction manquante (Enseignant, Personnel, Surveillant ou Observateur).");
                }
                else
                {
                    p.Role = ParseRole(funcRaw);
                    if (p.Role == null)
                        p.Errors.Add($"Fonction non reconnue (« {funcRaw} ») : "
                                     + "écrivez Enseignant, Personnel, Surveillant ou Observateur.");
                }

                // --- L'accès à l'application.
                var loginRaw = SheetReader.Cell(row, iLogin);
                var login = ParseYesNo(loginRaw);
                if (!string.IsNullOrWhiteSpace(loginRaw) && login == null)
                    p.Warnings.Add($"Accès à l'application illisible (« {loginRaw} ») : accès accordé par défaut.");
                p.CanLogin = login ?? true;

                // Un observateur qui ne peut pas se connecter n'a aucune utilité —
                // même règle que le formulaire d'invitation, pas une variante.
                if (!p.CanLogin && p.Role == UserRoles.SchoolViewer)
                {
                    p.Warnings.Add("Un observateur doit pouvoir se connecter : accès accordé.");
                    p.CanLogin = true;
                }

                if (p.Email != null && !p.Email.Contains('@'))
                {
                    p.Warnings.Add($"Email illisible (« {p.Email} ») : ignoré.");
                    p.Email = null;
                }

                if (p.JobTitle is { Length: > 80 }) p.JobTitle = p.JobTitle[..80];

                outp.Add(p);
            }
            return outp;
        }

        private static string? Nullify(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>
        /// Traduit ce qu'écrit une école en constante de <see cref="UserRoles"/>.
        /// Renvoie null si rien ne correspond — l'appelant en fait une erreur.
        /// </summary>
        public static string? ParseRole(string raw)
        {
            var n = SheetReader.Normalize(raw);
            if (n.Length == 0) return null;

            // « Surveillant » et « observateur » AVANT le reste : « surveillant
            // general » contient « general », et un test trop large les
            // capterait au mauvais endroit.
            if (n.Contains("surveill")) return UserRoles.Surveillant;
            if (n.Contains("observ") || n.Contains("lecture seule") || n.Contains("consultation")
                || n.Contains("auditeur") || n.Contains("proprietaire"))
                return UserRoles.SchoolViewer;

            if (n.Contains("enseign") || n.Contains("professeur") || n.Contains("prof")
                || n.Contains("maitre") || n.Contains("instituteur") || n.Contains("teacher")
                || n.Contains("ustaz") || n.Contains("oustaz") || n.Contains("serigne")
                || n.Contains("moukhadam"))
                return UserRoles.Teacher;

            if (n.Contains("personnel") || n.Contains("secretaire") || n.Contains("administra")
                || n.Contains("comptab") || n.Contains("gestion") || n.Contains("staff")
                || n.Contains("caissier") || n.Contains("econome") || n.Contains("cuisin")
                || n.Contains("gardien") || n.Contains("agent") || n.Contains("intendant"))
                return UserRoles.SchoolStaff;

            return null;
        }

        /// <summary>Oui / non tolérant. null = illisible (≠ « non »).</summary>
        public static bool? ParseYesNo(string raw)
        {
            var n = SheetReader.Normalize(raw);
            if (n.Length == 0) return null;
            if (n is "oui" or "o" or "yes" or "y" or "1" or "vrai" or "true" or "avec") return true;
            if (n is "non" or "n" or "no" or "0" or "faux" or "false" or "sans") return false;
            if (n.Contains("sans")) return false;
            if (n.Contains("avec")) return true;
            return null;
        }

        /// <summary>
        /// Deux lignes désignent-elles la même personne ? Le numéro, et lui seul :
        /// c'est la clé d'unicité réelle des comptes (index unique en base, §98).
        /// Deux homonymes sont deux personnes ; un même numéro est un doublon.
        /// </summary>
        public static string DedupKey(string phoneE164) => phoneE164;

        /// <summary>Libellé français d'un rôle, pour le modèle et les récapitulatifs.</summary>
        public static string RoleLabel(string role) => role switch
        {
            UserRoles.Teacher => "Enseignant",
            UserRoles.SchoolStaff => "Personnel",
            UserRoles.Surveillant => "Surveillant",
            UserRoles.SchoolViewer => "Observateur",
            _ => role,
        };
    }
}
