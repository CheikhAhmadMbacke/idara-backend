using System.Text.Json;
using ClosedXML.Excel;
using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.DTOs.Student;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public interface IStudentImportService
    {
        Task<byte[]> BuildTemplateAsync(int schoolId, CancellationToken ct);
        Task<ImportBatch> AnalyzeAsync(int schoolId, int userId, byte[] file, string fileName, CancellationToken ct);

        /// <summary>
        /// Analyse un tableau DÉJÀ LU, quelle qu'en soit l'origine.
        ///
        /// <para>C'est le point de couture qui permet à l'import par photo
        /// d'exister sans créer un second chemin : la lecture d'un cahier par
        /// l'IA produit un <see cref="SheetTable"/>, exactement comme la lecture
        /// d'un .xlsx, et tout l'aval est partagé. Un élève photographié et un
        /// élève importé d'Excel ne peuvent donc pas diverger.</para>
        /// </summary>
        Task<ImportBatch> AnalyzeTableAsync(
            int schoolId, int userId, SheetTable table, string sourceName, CancellationToken ct);

        Task<ImportBatch> CommitAsync(int schoolId, int userId, int batchId, bool sendSms, CancellationToken ct);
    }

    /// <summary>
    /// Import en masse des élèves depuis le tableau d'une école.
    ///
    /// Beaucoup d'écoles tenaient déjà leurs élèves dans Excel ou sur un cahier
    /// avant de nous découvrir. Leur demander de ressaisir 200 fiches une par
    /// une, c'est leur demander d'abandonner — c'est exactement là qu'on les
    /// perd, au moment où elles nous font le plus confiance.
    ///
    /// ⚠️ Ce service NE RÉIMPLÉMENTE PAS la création d'un élève : il appelle
    /// <see cref="IStudentService.CreateStudentAsync"/>. Toute la logique
    /// (matricule, création du responsable et de ses identifiants, facture de
    /// frais d'inscription, re-tarification) vient donc du même endroit que la
    /// saisie manuelle. La dupliquer garantirait qu'un élève importé finisse
    /// par différer d'un élève saisi.
    /// </summary>
    public class StudentImportService : IStudentImportService
    {
        private readonly AppDbContext _context;
        private readonly IStudentService _students;
        private readonly INotificationService _notif;
        private readonly ILogger<StudentImportService> _logger;

        public StudentImportService(AppDbContext context, IStudentService students,
            INotificationService notif, ILogger<StudentImportService> logger)
        {
            _context = context;
            _students = students;
            _notif = notif;
            _logger = logger;
        }

        // ---------------------------------------------------------------
        //  Le modèle
        // ---------------------------------------------------------------

        /// <summary>
        /// Produit le fichier modèle, PRÉ-REMPLI avec les classes de l'école.
        ///
        /// C'est ce pré-remplissage qui fait la différence : l'école reconnaît
        /// ses propres classes, elle n'invente pas des noms qui ne
        /// correspondront à rien, et l'orthographe cesse de varier.
        /// </summary>
        public async Task<byte[]> BuildTemplateAsync(int schoolId, CancellationToken ct)
        {
            var classes = await _context.Classes
                .Where(c => c.SchoolId == schoolId && !c.IsDeleted)
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync(ct);

            using var wb = new XLWorkbook();

            // --- Feuille 1 : les données. En PREMIER, parce que c'est celle
            // qu'Excel ouvre — l'école ne doit pas avoir à chercher.
            var ws = wb.AddWorksheet("Élèves");
            // Source UNIQUE : les mêmes colonnes servent d'en-tête au modèle et
            // de consigne à l'IA qui lit un cahier photographié. Les recopier
            // ici aurait garanti qu'un jour l'IA produise une colonne que ce
            // modèle ignore, sans que rien ne le signale (§140).
            var headers = ImportColumns.Students;
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B744D");
                cell.Style.Font.FontColor = XLColor.White;
            }

            // Une ligne d'exemple, en italique gris : elle montre le format
            // attendu sans qu'on ait à l'expliquer. L'école l'écrase.
            string[] example =
            {
                "Modou", "Diop", classes.FirstOrDefault() ?? "CI", "03/04/2015", "M",
                "Externe", "15000", "10000", "Fatou", "Diop", "77 123 45 67",
                "Mère", "Mbour", "",
            };
            for (int i = 0; i < example.Length; i++)
            {
                var cell = ws.Cell(2, i + 1);
                cell.Value = example[i];
                cell.Style.Font.Italic = true;
                cell.Style.Font.FontColor = XLColor.FromHtml("#8A9A93");
            }

            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);

            // --- Feuille 2 : l'aide. Ce qui est obligatoire, ce qui est
            // accepté, et surtout ce qui se passe si on laisse vide.
            var help = wb.AddWorksheet("Comment remplir");
            var lines = new (string Col, string Texte)[]
            {
                ("Prénom / Nom", "Au moins l'un des deux. Une ligne sans aucun nom sera signalée et ignorée."),
                ("Classe", "Si la classe n'existe pas encore, elle sera créée. Laissez vide si l'élève n'a pas de classe."),
                ("Date de naissance", "Jour/mois/année, par exemple 03/04/2015. L'année seule (2015) est acceptée. Facultatif."),
                ("Sexe", "M ou F (garçon, fille, masculin, féminin sont aussi compris). Facultatif."),
                ("Régime", "Interne, Demi-interne ou Externe. Facultatif."),
                ("Tarif", "Mensualité de CET élève, en FCFA. Laissez vide pour appliquer le tarif de sa classe."),
                ("Frais d'inscription", "En FCFA. 0 signifie exonéré. Vide = le réglage de l'école s'applique."),
                ("Responsable", "Prénom, nom et téléphone. Le téléphone est OBLIGATOIRE si vous renseignez un responsable : c'est lui qui lui donnera accès."),
                ("Téléphone", "Format sénégalais : 77 123 45 67, 771234567 ou +221771234567."),
                ("Lien de parenté", "Père, Mère, Tuteur… Facultatif."),
                ("", ""),
                ("Important", "Rien n'est enregistré tant que vous n'avez pas confirmé : vous verrez d'abord ce qui sera créé."),
                ("Important", "Un élève déjà présent dans Idara sera signalé et ignoré — vos fiches existantes ne seront jamais écrasées."),
            };
            help.Cell(1, 1).Value = "Colonne";
            help.Cell(1, 2).Value = "Ce qu'il faut savoir";
            help.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < lines.Length; i++)
            {
                help.Cell(i + 2, 1).Value = lines[i].Col;
                help.Cell(i + 2, 2).Value = lines[i].Texte;
            }
            help.Column(1).Width = 22;
            help.Column(2).Width = 95;
            help.Column(2).Style.Alignment.WrapText = true;

            // --- Feuille 3 : les classes existantes, à recopier telles quelles.
            var cl = wb.AddWorksheet("Vos classes");
            cl.Cell(1, 1).Value = classes.Count == 0
                ? "Aucune classe pour l'instant — écrivez le nom que vous voulez, elle sera créée."
                : "Recopiez ces noms dans la colonne « Classe » :";
            cl.Cell(1, 1).Style.Font.Bold = true;
            for (int i = 0; i < classes.Count; i++) cl.Cell(i + 2, 1).Value = classes[i];
            cl.Column(1).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ---------------------------------------------------------------
        //  L'analyse (aperçu) — rien n'est écrit
        // ---------------------------------------------------------------

        public async Task<ImportBatch> AnalyzeAsync(int schoolId, int userId, byte[] file,
            string fileName, CancellationToken ct)
        {
            SheetTable table;
            try
            {
                table = SheetReader.Read(file);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Le fichier n'a pas pu être lu. Vérifiez qu'il s'agit bien d'un fichier Excel "
                    + "ou CSV, et qu'il n'est pas protégé par un mot de passe. (" + ex.Message + ")");
            }

            if (table.Headers.Count == 0 || table.Rows.Count == 0)
                throw new InvalidOperationException(
                    "Le fichier est vide, ou sa première ligne ne contient pas les intitulés de colonnes.");

            if (SheetReader.IndexOf(table.Headers, StudentImportParser.HFirstName) < 0
                && SheetReader.IndexOf(table.Headers, StudentImportParser.HLastName) < 0)
                throw new InvalidOperationException(
                    "Aucune colonne « Prénom » ni « Nom » n'a été trouvée. Utilisez le modèle "
                    + "à télécharger : c'est la première ligne du fichier qui donne les colonnes.");

            return await AnalyzeTableAsync(schoolId, userId, table, fileName, ct);
        }

        public async Task<ImportBatch> AnalyzeTableAsync(
            int schoolId, int userId, SheetTable table, string sourceName, CancellationToken ct)
        {
            var rows = StudentImportParser.Parse(table);

            // --- Élèves DÉJÀ présents. On ne les écrase pas (décision produit) :
            // une fiche complétée à la main pendant des semaines ne doit pas
            // disparaître parce qu'un vieux fichier a été réimporté.
            var existing = await _context.Students
                .Where(s => s.SchoolId == schoolId && !s.IsDeleted)
                .Select(s => new { s.FirstName, s.LastName, ClassName = s.Class != null ? s.Class.Name : null })
                .ToListAsync(ct);
            var existingKeys = existing
                .Select(e => StudentImportParser.DedupKey($"{e.FirstName} {e.LastName}".Trim(), e.ClassName))
                .ToHashSet();

            // Doublons À L'INTÉRIEUR du fichier : deux fois le même enfant.
            var seen = new HashSet<string>();
            int duplicates = 0;
            var payload = new List<object>();

            foreach (var r in rows)
            {
                var key = StudentImportParser.DedupKey(r.FullName, r.ClassName);
                bool dupInDb = r.IsValid && existingKeys.Contains(key);
                bool dupInFile = r.IsValid && !dupInDb && !seen.Add(key);
                if (dupInDb || dupInFile) duplicates++;

                payload.Add(new
                {
                    row = r.RowNumber,
                    firstName = r.FirstName,
                    lastName = r.LastName,
                    className = r.ClassName,
                    birthDate = r.BirthDate?.ToString("yyyy-MM-dd"),
                    gender = r.Gender?.ToString(),
                    boarding = r.Boarding?.ToString(),
                    monthlyFee = r.MonthlyFee,
                    registrationFee = r.RegistrationFee,
                    guardianFirstName = r.GuardianFirstName,
                    guardianLastName = r.GuardianLastName,
                    guardianPhone = r.GuardianPhone,
                    guardianRelation = r.GuardianRelation,
                    address = r.Address,
                    notes = r.Notes,
                    errors = r.Errors,
                    warnings = r.Warnings,
                    duplicate = dupInDb ? "base" : dupInFile ? "fichier" : null,
                    willImport = r.IsValid && !dupInDb && !dupInFile,
                });
            }

            var batch = new ImportBatch
            {
                SchoolId = schoolId,
                CreatedById = userId,
                Kind = ImportKind.Students,
                FileName = sourceName.Length > 200 ? sourceName[..200] : sourceName,
                Status = ImportBatchStatus.Analyzed,
                RowsJson = JsonSerializer.Serialize(payload),
                TotalRows = rows.Count,
                ValidRows = payload.Count(p => (bool)p.GetType().GetProperty("willImport")!.GetValue(p)!),
                ErrorRows = rows.Count(r => !r.IsValid),
                DuplicateRows = duplicates,
                CreatedAt = DateTime.UtcNow,
            };
            _context.ImportBatches.Add(batch);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[import] École {SchoolId} : {Total} ligne(s) analysée(s), {Valid} à créer, "
                + "{Errors} en erreur, {Dup} doublon(s)",
                schoolId, batch.TotalRows, batch.ValidRows, batch.ErrorRows, batch.DuplicateRows);

            return batch;
        }

        // ---------------------------------------------------------------
        //  L'écriture
        // ---------------------------------------------------------------

        public async Task<ImportBatch> CommitAsync(int schoolId, int userId, int batchId,
            bool sendSms, CancellationToken ct)
        {
            var batch = await _context.ImportBatches
                .FirstOrDefaultAsync(b => b.Id == batchId && b.SchoolId == schoolId, ct)
                ?? throw new InvalidOperationException("Import introuvable.");

            // Garde d'idempotence : un double clic, ou un réseau qui rejoue la
            // requête, ne doit pas créer les élèves DEUX fois.
            if (batch.Status != ImportBatchStatus.Analyzed)
                throw new InvalidOperationException(
                    batch.Status == ImportBatchStatus.Committed
                        ? "Cet import a déjà été effectué."
                        : "Cet import n'est plus valide. Redéposez le fichier.");

            using var doc = JsonDocument.Parse(batch.RowsJson);
            var toCreate = doc.RootElement.EnumerateArray()
                .Where(e => e.TryGetProperty("willImport", out var w) && w.GetBoolean())
                .ToList();

            // Classes manquantes : créées EN UNE FOIS, avant les élèves.
            var wanted = toCreate
                .Select(e => e.GetProperty("className").GetString())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .ToList();

            var known = await _context.Classes
                .Where(c => c.SchoolId == schoolId && !c.IsDeleted)
                .ToDictionaryAsync(c => SheetReader.Normalize(c.Name), c => c.Id, ct);

            int createdClasses = 0;
            foreach (var name in wanted.DistinctBy(SheetReader.Normalize))
            {
                var k = SheetReader.Normalize(name);
                if (known.ContainsKey(k)) continue;
                var cl = new Class
                {
                    SchoolId = schoolId,
                    Name = name.Length > 100 ? name[..100] : name,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.Classes.Add(cl);
                await _context.SaveChangesAsync(ct);
                known[k] = cl.Id;
                createdClasses++;
            }

            int createdStudents = 0, createdGuardians = 0;
            var credentials = new List<object>();
            // Les mêmes identifiants, sous forme typée, pour l'envoi SMS
            // éventuel. La liste ci-dessus reste faite d'objets anonymes parce
            // que sa forme JSON (« name », « phone », « code ») est LUE PAR
            // L'APPLI : la typer changerait la casse des clés et casserait le
            // modal récap en silence (§140).
            var newGuardians = new List<DTOs.Common.UserCredentialDto>();
            var failures = new List<string>();

            foreach (var e in toCreate)
            {
                var rowNo = e.GetProperty("row").GetInt32();
                try
                {
                    var className = e.GetProperty("className").GetString();
                    int? classId = null;
                    if (!string.IsNullOrWhiteSpace(className)
                        && known.TryGetValue(SheetReader.Normalize(className!), out var cid))
                        classId = cid;

                    var dto = new StudentCreateDto
                    {
                        FirstName = Str(e, "firstName"),
                        LastName = Str(e, "lastName"),
                        ClassId = classId,
                        DateOfBirth = Date(e, "birthDate"),
                        Gender = EnumOf<Gender>(e, "gender"),
                        BoardingStatus = EnumOf<BoardingStatus>(e, "boarding"),
                        Address = Str(e, "address"),
                        Notes = Str(e, "notes"),
                        MonthlyFeeFcfa = Long(e, "monthlyFee"),
                        RegistrationFeeFcfa = Long(e, "registrationFee"),
                    };

                    var gPhone = Str(e, "guardianPhone");
                    if (!string.IsNullOrWhiteSpace(gPhone))
                    {
                        var gFirst = Str(e, "guardianFirstName");
                        var gLast = Str(e, "guardianLastName");
                        // Un responsable sans nom prend celui de l'élève : c'est
                        // ce que l'école aurait écrit, et un compte « (sans
                        // nom) » ne se retrouve pas dans une liste.
                        if (string.IsNullOrWhiteSpace(gFirst) && string.IsNullOrWhiteSpace(gLast))
                        {
                            gFirst = dto.FirstName;
                            gLast = dto.LastName;
                        }
                        dto.Guardians = new List<GuardianInputDto>
                        {
                            new()
                            {
                                FirstName = gFirst,
                                LastName = gLast,
                                PhoneNumber = gPhone,
                                Relationship = Str(e, "guardianRelation"),
                                IsPrimaryGuardian = true,
                            }
                        };
                    }

                    var created = await _students.CreateStudentAsync(schoolId, userId, dto);
                    createdStudents++;

                    foreach (var c in created.NewGuardianCredentials ?? new())
                    {
                        createdGuardians++;
                        newGuardians.Add(c);
                        credentials.Add(new
                        {
                            student = $"{dto.FirstName} {dto.LastName}".Trim(),
                            name = c.FullName,
                            phone = c.Phone,
                            code = c.Code,
                            userId = c.UserId,
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Une ligne qui échoue à l'écriture ne doit pas emporter les
                    // autres : l'école a validé l'aperçu, 199 élèves créés sur
                    // 200 valent mieux que zéro. On dit lesquels ont échoué.
                    failures.Add($"Ligne {rowNo} : {ex.Message}");
                    _logger.LogWarning(ex, "[import] École {SchoolId}, ligne {Row} non importée", schoolId, rowNo);
                }
            }

            batch.Status = ImportBatchStatus.Committed;
            batch.CommittedAt = DateTime.UtcNow;
            batch.CreatedStudents = createdStudents;
            batch.CreatedClasses = createdClasses;
            batch.CreatedGuardians = createdGuardians;
            batch.Error = failures.Count == 0 ? null : string.Join(" | ", failures.Take(20));
            // Les identifiants créés sont conservés AVEC l'import : c'est ce qui
            // permet à l'école de rouvrir la liste plus tard, sans avoir à
            // régénérer les codes (ce qui invaliderait ceux déjà distribués).
            batch.RowsJson = JsonSerializer.Serialize(new
            {
                rows = JsonDocument.Parse(batch.RowsJson).RootElement,
                credentials,
            });
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[import] École {SchoolId} : {Students} élève(s), {Classes} classe(s), "
                + "{Guardians} responsable(s) créé(s) ; {Failed} échec(s)",
                schoolId, createdStudents, createdClasses, createdGuardians, failures.Count);

            // SMS des identifiants aux responsables créés. POST-COMMIT et
            // best-effort (§42/§90) : les élèves sont enregistrés, un SMS qui ne
            // part pas ne doit pas faire croire à un import raté.
            //
            // ⚠️ Avant le 2026-09-02, ce paramètre était accepté, transmis… et
            // jamais utilisé. L'école cochait « envoyer les identifiants », on
            // lui annonçait le nombre de SMS, et rien ne partait.
            if (sendSms && newGuardians.Count > 0)
                await SendCredentialsAsync(schoolId, userId, newGuardians, ct);

            return batch;
        }

        /// <summary>
        /// Envoie à chaque responsable créé son code d'accès. Ne lève jamais.
        /// Le garde-fou de dépense (§191) tranche envoi par envoi — on ne
        /// cherche pas à le devancer ici.
        /// </summary>
        private async Task SendCredentialsAsync(
            int schoolId, int triggerUserId,
            List<DTOs.Common.UserCredentialDto> credentials, CancellationToken ct)
        {
            var platform = await _context.GetPlatformSettingsAsync();
            int sent = 0;

            foreach (var c in credentials)
            {
                if (!c.CanLogin || string.IsNullOrWhiteSpace(c.Code)
                    || string.IsNullOrWhiteSpace(c.Phone)) continue;
                try
                {
                    // La langue est décidée par NotificationService (règle d'or) :
                    // un destinataire jamais connecté reçoit le bilingue.
                    var ok = await _notif.SendSmsAsync(new NotificationSmsRequest(
                        UserId: c.UserId,
                        RawPhone: c.Phone,
                        PreferredLanguage: "fr",
                        Message: NotificationTemplates.CredentialsSms(c.FullName, c.Phone, c.Code),
                        Bilingual: platform.SmsBilingual,
                        TemplateCode: "CREDENTIALS_SMS",
                        RelatedEntityId: c.UserId,
                        SchoolId: schoolId,
                        TriggerSource: "api:import/students/commit",
                        TriggerUserId: triggerUserId), ct);
                    if (ok) sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[import] SMS d'identifiants non envoyé à {UserId}", c.UserId);
                }
            }

            _logger.LogInformation(
                "[import] École {SchoolId} : {Sent}/{Total} SMS d'identifiants envoyés",
                schoolId, sent, credentials.Count);
        }

        // ---- petits accesseurs JSON, tolérants aux valeurs absentes ----
        private static string Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty : string.Empty;

        private static long? Long(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt64() : null;

        private static DateTime? Date(JsonElement e, string name)
        {
            var s = Str(e, name);
            return DateTime.TryParse(s, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        }

        private static T? EnumOf<T>(JsonElement e, string name) where T : struct, Enum
        {
            var s = Str(e, name);
            return Enum.TryParse<T>(s, out var v) ? v : null;
        }
    }
}
