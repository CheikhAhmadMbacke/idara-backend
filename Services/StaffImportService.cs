using System.Text.Json;
using ClosedXML.Excel;
using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    public interface IStaffImportService
    {
        Task<byte[]> BuildTemplateAsync(int schoolId, CancellationToken ct);
        Task<ImportBatch> AnalyzeAsync(int schoolId, int userId, byte[] file, string fileName, CancellationToken ct);

        /// <summary>
        /// Analyse un tableau DÉJÀ LU, quelle qu'en soit l'origine — fichier
        /// Excel ou cahier photographié (cf. <c>IStudentImportService</c>).
        /// </summary>
        Task<ImportBatch> AnalyzeTableAsync(
            int schoolId, int userId, SheetTable table, string sourceName, CancellationToken ct);

        Task<ImportBatch> CommitAsync(int schoolId, int userId, int batchId, bool sendSms, CancellationToken ct);
    }

    /// <summary>
    /// Import en masse des enseignants et du personnel depuis le tableau d'une école.
    ///
    /// Même parcours en deux temps que l'import des élèves — l'école dépose,
    /// elle voit ce qui sera créé, et **rien n'est écrit tant qu'elle n'a pas
    /// confirmé**.
    ///
    /// <para>⚠️ Ce service NE RÉIMPLÉMENTE PAS la création d'un compte : il
    /// appelle <see cref="IUserInvitationService.InviteAsync"/>, exactement
    /// comme le formulaire d'invitation. Toute la logique (numéro normalisé,
    /// unicité, code à 6 chiffres, statut et langue hérités, compte « sans
    /// appli ») vient donc du même endroit.</para>
    /// </summary>
    public class StaffImportService : IStaffImportService
    {
        private readonly AppDbContext _context;
        private readonly IUserInvitationService _invitations;
        private readonly INotificationService _notif;
        private readonly ILogger<StaffImportService> _logger;

        public StaffImportService(
            AppDbContext context,
            IUserInvitationService invitations,
            INotificationService notif,
            ILogger<StaffImportService> logger)
        {
            _context = context;
            _invitations = invitations;
            _notif = notif;
            _logger = logger;
        }

        // ---------------------------------------------------------------
        //  Le modèle
        // ---------------------------------------------------------------

        public async Task<byte[]> BuildTemplateAsync(int schoolId, CancellationToken ct)
        {
            // Les fonctions déjà employées dans l'école : comme les classes du
            // modèle élèves, elles évitent qu'on invente un libellé qui ne
            // correspondra à rien.
            var jobTitles = await _context.Users
                .Where(u => u.SchoolId == schoolId && !u.IsDeleted && u.JobTitle != null)
                .Select(u => u.JobTitle!)
                .Distinct()
                .OrderBy(t => t)
                .Take(40)
                .ToListAsync(ct);

            using var wb = new XLWorkbook();

            var ws = wb.AddWorksheet("Personnel");
            // Source UNIQUE, partagée avec la consigne donnée à l'IA (§140).
            var headers = ImportColumns.Staff;
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B744D");
                cell.Style.Font.FontColor = XLColor.White;
            }

            // Deux lignes d'exemple : une qui se connecte, une « sans appli ».
            // La seconde existe parce que c'est le cas qu'on n'imagine pas et
            // qui, non montré, fait créer des comptes inutilisables.
            var examples = new[]
            {
                new[] { "Ousmane Fall", "77 123 45 67", "Enseignant", "Maître coranique", "Oui", "" },
                new[] { "Aïssatou Sow", "76 987 65 43", "Personnel", "Cuisinière", "Non", "" },
            };
            for (int r = 0; r < examples.Length; r++)
            {
                for (int c = 0; c < examples[r].Length; c++)
                {
                    var cell = ws.Cell(r + 2, c + 1);
                    cell.Value = examples[r][c];
                    cell.Style.Font.Italic = true;
                    cell.Style.Font.FontColor = XLColor.FromHtml("#8A9A93");
                }
            }

            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);

            var help = wb.AddWorksheet("Comment remplir");
            var lines = new (string Col, string Texte)[]
            {
                ("Nom complet", "Obligatoire. Prénom et nom dans la même case."),
                ("Téléphone", "OBLIGATOIRE : c'est le numéro qui identifie le compte et permet de se connecter. Format 77 123 45 67, 771234567 ou +221771234567."),
                ("Fonction", "OBLIGATOIRE. Écrivez Enseignant, Personnel, Surveillant ou Observateur. Une fonction vide ou non reconnue fait ignorer la ligne — un rôle ne se devine pas."),
                ("— Enseignant", "Voit et suit ses classes."),
                ("— Personnel", "Gère les élèves, les classes et les encaissements."),
                ("— Surveillant", "Pointage et discipline."),
                ("— Observateur", "Voit tout, ne modifie rien (propriétaire, superviseur, auditeur)."),
                ("Intitulé du poste", "Le titre affiché : Maître coranique, Comptable, Cuisinière… Facultatif."),
                ("Accès à l'application", "Oui (par défaut) ou Non. « Non » crée un compte de POINTAGE uniquement, sans connexion ni code — pour la cuisinière, le gardien. Un observateur se connecte toujours."),
                ("Email", "Facultatif. Ce n'est pas le canal d'identification : le numéro l'est."),
                ("", ""),
                ("Important", "Rien n'est enregistré tant que vous n'avez pas confirmé : vous verrez d'abord ce qui sera créé."),
                ("Important", "Un numéro déjà utilisé dans Idara sera signalé et ignoré — aucun compte existant ne sera modifié."),
                ("Important", "Les identifiants s'affichent après la confirmation, pour que vous les transmettiez par WhatsApp ou par SMS."),
            };
            help.Cell(1, 1).Value = "Colonne";
            help.Cell(1, 2).Value = "Ce qu'il faut savoir";
            help.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < lines.Length; i++)
            {
                help.Cell(i + 2, 1).Value = lines[i].Col;
                help.Cell(i + 2, 2).Value = lines[i].Texte;
            }
            help.Column(1).Width = 24;
            help.Column(2).Width = 100;
            help.Column(2).Style.Alignment.WrapText = true;

            if (jobTitles.Count > 0)
            {
                var jt = wb.AddWorksheet("Vos intitulés");
                jt.Cell(1, 1).Value = "Intitulés déjà utilisés dans votre établissement :";
                jt.Cell(1, 1).Style.Font.Bold = true;
                for (int i = 0; i < jobTitles.Count; i++) jt.Cell(i + 2, 1).Value = jobTitles[i];
                jt.Column(1).AdjustToContents();
            }

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

            bool hasName = SheetReader.IndexOf(table.Headers, StaffImportParser.HFullName) >= 0
                           || SheetReader.IndexOf(table.Headers, StaffImportParser.HFirstName) >= 0
                           || SheetReader.IndexOf(table.Headers, StaffImportParser.HLastName) >= 0;
            if (!hasName)
                throw new InvalidOperationException(
                    "Aucune colonne « Nom complet » n'a été trouvée. Utilisez le modèle à télécharger : "
                    + "c'est la première ligne du fichier qui donne les colonnes.");

            if (SheetReader.IndexOf(table.Headers, StaffImportParser.HPhone) < 0)
                throw new InvalidOperationException(
                    "Aucune colonne « Téléphone » n'a été trouvée. Le numéro est ce qui identifie "
                    + "chaque compte : sans cette colonne, aucun compte ne peut être créé.");

            return await AnalyzeTableAsync(schoolId, userId, table, fileName, ct);
        }

        public async Task<ImportBatch> AnalyzeTableAsync(
            int schoolId, int userId, SheetTable table, string sourceName, CancellationToken ct)
        {
            var rows = StaffImportParser.Parse(table);

            // Numéros DÉJÀ pris. La contrainte d'unicité est GLOBALE (§98) : un
            // numéro déjà employé par une autre école bloquerait la création au
            // moment de l'écriture. Mieux vaut le dire dans l'aperçu que
            // produire quarante échecs à la confirmation. On ne révèle rien de
            // l'autre école — le message est celui du formulaire d'invitation.
            var candidatePhones = rows
                .Where(r => r.Phone != null)
                .Select(r => r.Phone!)
                .Distinct()
                .ToList();

            var takenPhones = candidatePhones.Count == 0
                ? new HashSet<string>()
                : (await _context.Users
                    .Where(u => !u.IsDeleted && u.PhoneNumber != null && candidatePhones.Contains(u.PhoneNumber))
                    .Select(u => u.PhoneNumber!)
                    .ToListAsync(ct)).ToHashSet();

            var seen = new HashSet<string>();
            int duplicates = 0;
            var payload = new List<StaffRowPayload>();

            foreach (var r in rows)
            {
                bool dupInDb = r.IsValid && r.Phone != null && takenPhones.Contains(r.Phone);
                bool dupInFile = r.IsValid && r.Phone != null && !dupInDb
                                 && !seen.Add(StaffImportParser.DedupKey(r.Phone));
                if (dupInDb || dupInFile) duplicates++;

                payload.Add(new StaffRowPayload
                {
                    Row = r.RowNumber,
                    FullName = r.FullName,
                    Phone = r.Phone,
                    Role = r.Role,
                    RoleLabel = r.Role == null ? null : StaffImportParser.RoleLabel(r.Role),
                    JobTitle = r.JobTitle,
                    CanLogin = r.CanLogin,
                    Email = r.Email,
                    Errors = r.Errors,
                    Warnings = r.Warnings,
                    Duplicate = dupInDb ? "base" : dupInFile ? "fichier" : null,
                    WillImport = r.IsValid && !dupInDb && !dupInFile,
                });
            }

            var batch = new ImportBatch
            {
                SchoolId = schoolId,
                CreatedById = userId,
                Kind = ImportKind.Staff,
                FileName = sourceName.Length > 200 ? sourceName[..200] : sourceName,
                Status = ImportBatchStatus.Analyzed,
                RowsJson = JsonSerializer.Serialize(payload, JsonOpts),
                TotalRows = rows.Count,
                ValidRows = payload.Count(p => p.WillImport),
                ErrorRows = rows.Count(r => !r.IsValid),
                DuplicateRows = duplicates,
                CreatedAt = DateTime.UtcNow,
            };
            _context.ImportBatches.Add(batch);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[import-staff] École {SchoolId} : {Total} ligne(s) analysée(s), {Valid} à créer, "
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
                .FirstOrDefaultAsync(b => b.Id == batchId && b.SchoolId == schoolId
                                          && b.Kind == ImportKind.Staff, ct)
                ?? throw new InvalidOperationException("Import introuvable.");

            // Garde d'idempotence : un double clic, ou un réseau qui rejoue la
            // requête, ne doit pas créer les comptes DEUX fois.
            if (batch.Status != ImportBatchStatus.Analyzed)
                throw new InvalidOperationException(
                    batch.Status == ImportBatchStatus.Committed
                        ? "Cet import a déjà été effectué."
                        : "Cet import n'est plus valide. Redéposez le fichier.");

            var inviter = await _context.Users.Include(u => u.School)
                .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
                ?? throw new InvalidOperationException("Votre compte est introuvable.");

            var toCreate = (JsonSerializer.Deserialize<List<StaffRowPayload>>(batch.RowsJson, JsonOpts)
                            ?? new List<StaffRowPayload>())
                .Where(r => r.WillImport)
                .ToList();

            int created = 0;
            var credentials = new List<CreatedCredential>();
            var failures = new List<string>();

            foreach (var r in toCreate)
            {
                if (r.Phone == null || r.Role == null) continue;
                try
                {
                    var cred = await _invitations.InviteAsync(inviter, new InviteUserCommand(
                        FullName: r.FullName,
                        PhoneNumber: r.Phone,
                        Function: r.Role,
                        Email: r.Email,
                        CanLogin: r.CanLogin,
                        JobTitle: r.JobTitle), ct);

                    created++;
                    credentials.Add(new CreatedCredential
                    {
                        UserId = cred.UserId,
                        Name = cred.FullName,
                        Phone = cred.Phone,
                        Code = cred.Code,
                        CanLogin = cred.CanLogin,
                        RoleLabel = StaffImportParser.RoleLabel(r.Role),
                    });
                }
                catch (InviteRejectedException ex)
                {
                    // Refus métier : la ligne est perdue, les autres continuent.
                    // 19 comptes créés sur 20 valent mieux que zéro, et on dit
                    // lequel a échoué.
                    failures.Add($"Ligne {r.Row} : {ex.Message}");
                }
                catch (Exception ex)
                {
                    failures.Add($"Ligne {r.Row} : {ex.Message}");
                    _logger.LogWarning(ex,
                        "[import-staff] École {SchoolId}, ligne {Row} non importée", schoolId, r.Row);
                }
            }

            batch.Status = ImportBatchStatus.Committed;
            batch.CommittedAt = DateTime.UtcNow;
            batch.CreatedUsers = created;
            batch.Error = failures.Count == 0 ? null : string.Join(" | ", failures.Take(20));
            batch.RowsJson = JsonSerializer.Serialize(new
            {
                rows = JsonDocument.Parse(batch.RowsJson).RootElement,
                credentials,
            }, JsonOpts);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[import-staff] École {SchoolId} : {Created} compte(s) créé(s) ; {Failed} échec(s)",
                schoolId, created, failures.Count);

            // SMS des identifiants : POST-COMMIT et best-effort (§42/§90). Le
            // garde-fou de dépense (§191) tranche envoi par envoi ; on n'essaie
            // pas de le devancer ici.
            if (sendSms && credentials.Count > 0)
                await SendCredentialsAsync(schoolId, userId, credentials, ct);

            return batch;
        }

        /// <summary>
        /// Envoie à chaque personne créée son code d'accès. Ne lève jamais : les
        /// comptes sont déjà créés et les identifiants déjà affichés à l'école —
        /// un SMS qui ne part pas ne doit pas faire croire à un import raté.
        /// </summary>
        private async Task SendCredentialsAsync(
            int schoolId, int triggerUserId, List<CreatedCredential> credentials, CancellationToken ct)
        {
            var platform = await _context.GetPlatformSettingsAsync();
            int sent = 0;

            foreach (var c in credentials)
            {
                // Un compte « sans appli » n'a pas de code : lui écrire un SMS
                // serait une dépense pour un message inutilisable.
                if (!c.CanLogin || string.IsNullOrWhiteSpace(c.Code)) continue;

                try
                {
                    var ok = await _notif.SendSmsAsync(new NotificationSmsRequest(
                        UserId: c.UserId,
                        RawPhone: c.Phone,
                        PreferredLanguage: "fr",
                        Message: NotificationTemplates.CredentialsSms(c.Name, c.Phone, c.Code!),
                        Bilingual: platform.SmsBilingual,
                        TemplateCode: "CREDENTIALS_SMS",
                        RelatedEntityId: c.UserId,
                        SchoolId: schoolId,
                        TriggerSource: "api:import/staff/commit",
                        TriggerUserId: triggerUserId), ct);
                    if (ok) sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[import-staff] SMS d'identifiants non envoyé à {UserId}", c.UserId);
                }
            }

            _logger.LogInformation(
                "[import-staff] École {SchoolId} : {Sent}/{Total} SMS d'identifiants envoyés",
                schoolId, sent, credentials.Count(c => c.CanLogin));
        }

        // ---------------------------------------------------------------
        //  La charge stockée
        // ---------------------------------------------------------------

        /// <summary>
        /// camelCase : c'est la forme que lit le frontend, et la charge est
        /// renvoyée telle quelle par le contrôleur.
        /// </summary>
        internal static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Une ligne analysée, telle qu'elle est stockée puis relue.
        ///
        /// Type nommé plutôt qu'un objet anonyme (comme le fait l'import des
        /// élèves) : la confirmation relit ce qu'elle a écrit, et un type nommé
        /// rend cette relecture vérifiable par le compilateur au lieu de la
        /// laisser reposer sur des chaînes de caractères.
        /// </summary>
        internal sealed class StaffRowPayload
        {
            public int Row { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public string? Role { get; set; }
            public string? RoleLabel { get; set; }
            public string? JobTitle { get; set; }
            public bool CanLogin { get; set; } = true;
            public string? Email { get; set; }
            public List<string> Errors { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public string? Duplicate { get; set; }
            public bool WillImport { get; set; }
        }

        internal sealed class CreatedCredential
        {
            public int? UserId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string? Code { get; set; }
            public bool CanLogin { get; set; } = true;
            public string? RoleLabel { get; set; }
        }
    }
}
