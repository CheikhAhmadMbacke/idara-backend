using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>Une ligne d'élève lue dans le fichier de l'école, déjà interprétée.</summary>
    public sealed class ParsedStudentRow
    {
        public int RowNumber { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? ClassName { get; set; }
        public DateTime? BirthDate { get; set; }
        public Gender? Gender { get; set; }
        public BoardingStatus? Boarding { get; set; }
        public long? MonthlyFee { get; set; }
        public long? RegistrationFee { get; set; }
        public string? GuardianFirstName { get; set; }
        public string? GuardianLastName { get; set; }
        public string? GuardianPhone { get; set; }
        public string? GuardianRelation { get; set; }
        public string? Address { get; set; }
        public string? Notes { get; set; }

        /// <summary>Problèmes bloquants : la ligne ne sera pas importée.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Points signalés, sans empêcher l'import.</summary>
        public List<string> Warnings { get; } = new();

        public bool IsValid => Errors.Count == 0;
        public string FullName => $"{FirstName} {LastName}".Trim();
    }

    /// <summary>
    /// Traduit le tableau d'une école en lignes d'élèves exploitables.
    ///
    /// Pure et publique : c'est la règle qui décide de ce qui entrera dans la
    /// base de l'école, et une règle qu'on ne peut vérifier qu'en important
    /// vraiment 200 élèves ne se vérifie jamais.
    ///
    /// Principe directeur : **être indulgent sur la forme, strict sur le sens.**
    /// Une école qui écrit « M », « Masculin » ou « garçon » veut dire la même
    /// chose ; refuser son fichier pour cela la renverrait à son cahier. En
    /// revanche, un élève sans aucun nom n'est pas importable, et on le dit.
    /// </summary>
    public static class StudentImportParser
    {
        // Intitulés acceptés, du plus précis au plus large.
        public static readonly string[] HFirstName = { "prenom", "prénom", "prenom eleve", "prénom de l'élève", "first name" };
        public static readonly string[] HLastName = { "nom", "nom de famille", "nom eleve", "last name" };
        public static readonly string[] HClass = { "classe", "class", "niveau classe", "halaqa" };
        public static readonly string[] HBirth = { "date de naissance", "naissance", "ne le", "née le", "birth" };
        public static readonly string[] HGender = { "sexe", "genre", "gender" };
        public static readonly string[] HBoarding = { "regime", "régime", "hebergement", "hébergement", "internat" };
        public static readonly string[] HFee = { "tarif", "mensualite", "mensualité", "montant mensuel", "scolarite mensuelle" };
        public static readonly string[] HRegFee = { "frais d'inscription", "frais inscription", "inscription" };
        public static readonly string[] HGFirst = { "prenom du responsable", "prénom du responsable", "prenom parent", "prénom du parent" };
        public static readonly string[] HGLast = { "nom du responsable", "nom parent", "nom du parent" };
        public static readonly string[] HGPhone = { "telephone du responsable", "téléphone du responsable", "telephone parent", "téléphone", "telephone", "numero", "numéro" };
        public static readonly string[] HGRelation = { "lien de parente", "lien de parenté", "lien", "relation" };
        public static readonly string[] HAddress = { "adresse", "quartier" };
        public static readonly string[] HNotes = { "remarques", "remarque", "observations", "notes" };

        public static List<ParsedStudentRow> Parse(SheetTable table)
        {
            var h = table.Headers;
            int iFirst = SheetReader.IndexOf(h, HFirstName);
            int iLast = SheetReader.IndexOf(h, HLastName);
            int iClass = SheetReader.IndexOf(h, HClass);
            int iBirth = SheetReader.IndexOf(h, HBirth);
            int iGender = SheetReader.IndexOf(h, HGender);
            int iBoard = SheetReader.IndexOf(h, HBoarding);
            int iFee = SheetReader.IndexOf(h, HFee);
            int iRegFee = SheetReader.IndexOf(h, HRegFee);
            int iGFirst = SheetReader.IndexOf(h, HGFirst);
            int iGLast = SheetReader.IndexOf(h, HGLast);
            int iGPhone = SheetReader.IndexOf(h, HGPhone);
            int iGRel = SheetReader.IndexOf(h, HGRelation);
            int iAddr = SheetReader.IndexOf(h, HAddress);
            int iNotes = SheetReader.IndexOf(h, HNotes);

            var outp = new List<ParsedStudentRow>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                var p = new ParsedStudentRow
                {
                    RowNumber = i < table.RowNumbers.Count ? table.RowNumbers[i] : i + 2,
                    FirstName = SheetReader.Cell(row, iFirst),
                    LastName = SheetReader.Cell(row, iLast),
                    ClassName = Nullify(SheetReader.Cell(row, iClass)),
                    Address = Nullify(SheetReader.Cell(row, iAddr)),
                    Notes = Nullify(SheetReader.Cell(row, iNotes)),
                    GuardianFirstName = Nullify(SheetReader.Cell(row, iGFirst)),
                    GuardianLastName = Nullify(SheetReader.Cell(row, iGLast)),
                    GuardianRelation = Nullify(SheetReader.Cell(row, iGRel)),
                };

                // --- Nom : au moins l'un des deux, comme pour le nom d'école
                // (§135). Beaucoup d'élèves sont connus sous un nom unique.
                if (string.IsNullOrWhiteSpace(p.FirstName) && string.IsNullOrWhiteSpace(p.LastName))
                    p.Errors.Add("Ni prénom ni nom : impossible d'identifier l'élève.");

                // --- Date de naissance : facultative, mais si elle est là elle
                // doit être plausible. Une date illisible est SIGNALÉE et
                // ignorée, jamais bloquante : elle ne doit pas coûter l'élève.
                var birthRaw = SheetReader.Cell(row, iBirth);
                if (!string.IsNullOrWhiteSpace(birthRaw))
                {
                    var d = ParseDate(birthRaw);
                    if (d == null) p.Warnings.Add($"Date de naissance illisible (« {birthRaw} ») : ignorée.");
                    else if (d > DateTime.UtcNow.Date) p.Warnings.Add("Date de naissance dans le futur : ignorée.");
                    else if (d < new DateTime(1950, 1, 1)) p.Warnings.Add("Date de naissance trop ancienne : ignorée.");
                    else p.BirthDate = d;
                }

                p.Gender = ParseGender(SheetReader.Cell(row, iGender));
                p.Boarding = ParseBoarding(SheetReader.Cell(row, iBoard));

                // --- Montants : un tarif mal lu change ce qu'une famille doit
                // payer. On refuse la ligne plutôt que de deviner.
                p.MonthlyFee = ParseMoney(SheetReader.Cell(row, iFee), "Tarif mensuel", p);
                p.RegistrationFee = ParseMoney(SheetReader.Cell(row, iRegFee), "Frais d'inscription", p);

                // --- Responsable : son numéro est ce qui lui donnera accès.
                var phoneRaw = SheetReader.Cell(row, iGPhone);
                if (!string.IsNullOrWhiteSpace(phoneRaw))
                {
                    var norm = SenegalPhone.Normalize(phoneRaw);
                    if (norm == null)
                        p.Errors.Add($"Numéro du responsable invalide (« {phoneRaw} »).");
                    else
                        p.GuardianPhone = norm;
                }
                else if (p.GuardianFirstName != null || p.GuardianLastName != null)
                {
                    // Un responsable sans numéro ne peut pas se connecter : le
                    // créer donnerait un compte inutilisable, et l'école
                    // croirait la famille rattachée.
                    p.Errors.Add("Responsable indiqué sans numéro de téléphone.");
                }

                if (p.GuardianPhone != null
                    && string.IsNullOrWhiteSpace(p.GuardianFirstName)
                    && string.IsNullOrWhiteSpace(p.GuardianLastName))
                {
                    p.Warnings.Add("Responsable sans nom : il sera enregistré sous le nom de l'élève.");
                }

                outp.Add(p);
            }
            return outp;
        }

        private static string? Nullify(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>
        /// Dates au format que produisent réellement Excel et la saisie
        /// manuelle. Le jour vient AVANT le mois : le fichier est français, et
        /// lire « 03/04/2015 » comme le 4 mars donnerait un âge faux sans que
        /// personne ne s'en aperçoive.
        /// </summary>
        public static DateTime? ParseDate(string raw)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return null;
            string[] formats =
            {
                "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy",
                "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yy", "d/M/yy",
            };
            if (DateTime.TryParseExact(raw, formats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d))
                return d.Date;

            // Une année seule (« 2015 ») est courante quand la date exacte est
            // inconnue : on retient le 1er janvier plutôt que de tout perdre.
            if (raw.Length == 4 && int.TryParse(raw, out var y) && y is >= 1950 and <= 2100)
                return new DateTime(y, 1, 1);

            if (DateTime.TryParse(raw, new System.Globalization.CultureInfo("fr-FR"),
                    System.Globalization.DateTimeStyles.None, out var d2))
                return d2.Date;
            return null;
        }

        public static Gender? ParseGender(string raw)
        {
            var n = SheetReader.Normalize(raw);
            if (n.Length == 0) return null;
            if (n is "m" or "h" or "masculin" or "homme" or "garcon" or "male" or "g") return Enums.Gender.Male;
            if (n is "f" or "feminin" or "femme" or "fille" or "female") return Enums.Gender.Female;
            return null;
        }

        public static BoardingStatus? ParseBoarding(string raw)
        {
            var n = SheetReader.Normalize(raw);
            if (n.Length == 0) return null;
            if (n.Contains("demi")) return BoardingStatus.HalfBoarding;   // avant « interne »
            if (n.Contains("intern")) return BoardingStatus.Boarding;
            if (n.Contains("extern") || n.Contains("jour")) return BoardingStatus.Day;
            return null;
        }

        /// <summary>
        /// Montant en FCFA. Accepte « 15 000 », « 15.000 », « 15000 FCFA ».
        /// ⚠️ Un montant illisible est une ERREUR, pas un avertissement : c'est
        /// ce que la famille devra payer, on ne devine pas.
        /// </summary>
        public static long? ParseMoney(string raw, string label, ParsedStudentRow? p = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                p?.Errors.Add($"{label} illisible (« {raw} »).");
                return null;
            }
            if (digits.Length > 9)
            {
                p?.Errors.Add($"{label} hors limites (« {raw} »).");
                return null;
            }
            var v = long.Parse(digits);
            // 0 est légitime : « exonéré » (convention du §158).
            return v;
        }

        /// <summary>
        /// Deux lignes désignent-elles le même élève ? Comparaison sur le nom
        /// complet normalisé et la classe : c'est ce dont dispose une école qui
        /// n'a pas de matricule.
        /// </summary>
        public static string DedupKey(string fullName, string? className)
            => SheetReader.Normalize(fullName) + "|" + SheetReader.Normalize(className ?? string.Empty);
    }
}
