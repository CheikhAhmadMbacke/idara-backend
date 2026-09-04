using System.Text.Json;
using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.Enums;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Import en masse depuis un fichier Excel ou CSV : élèves, puis enseignants
    /// et personnel.
    ///
    /// Réservé à la DIRECTION : l'opération crée des dizaines de fiches et de
    /// comptes d'un coup. Le personnel saisit, la direction importe.
    /// </summary>
    [ApiController]
    [Authorize(Roles = UserRoles.SchoolAdmin)]
    [Route("api/import")]
    public class ImportController : ControllerBase
    {
        private readonly IStudentImportService _import;
        private readonly IStaffImportService _staffImport;
        private readonly Services.Vision.IPhotoImportService _photo;
        private readonly Services.Vision.IOcrBudgetGuard _ocrGuard;
        private readonly Services.Vision.IDocumentVisionService _vision;
        private readonly AppDbContext _context;

        public ImportController(
            IStudentImportService import,
            IStaffImportService staffImport,
            Services.Vision.IPhotoImportService photo,
            Services.Vision.IOcrBudgetGuard ocrGuard,
            Services.Vision.IDocumentVisionService vision,
            AppDbContext context)
        {
            _import = import;
            _staffImport = staffImport;
            _photo = photo;
            _ocrGuard = ocrGuard;
            _vision = vision;
            _context = context;
        }

        /// <summary>Le fichier modèle, pré-rempli avec les classes de l'école.</summary>
        [HttpGet("students/template")]
        public async Task<IActionResult> Template(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var bytes = await _import.BuildTemplateAsync(schoolId.Value, ct);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "modele-eleves-idara.xlsx");
        }

        public class UploadDto
        {
            /// <summary>Contenu du fichier, en base64.</summary>
            public string FileBase64 { get; set; } = string.Empty;
            public string FileName { get; set; } = "import.xlsx";
        }

        /// <summary>
        /// Analyse le fichier et renvoie CE QUI SERA CRÉÉ. N'écrit rien.
        /// </summary>
        [HttpPost("students/preview")]
        public async Task<IActionResult> Preview([FromBody] UploadDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            if (!TryDecode(dto, out var bytes, out var decodeError))
                return BadRequest(ApiResponse<bool>.Fail(decodeError!));

            try
            {
                var batch = await _import.AnalyzeAsync(
                    schoolId.Value, userId.Value, bytes, dto.FileName ?? "import.xlsx", ct);
                return Ok(ApiResponse<object>.Ok(Describe(batch), "Fichier analysé."));
            }
            catch (InvalidOperationException ex)
            {
                // Message rédigé pour un directeur, pas une trace technique.
                return BadRequest(ApiResponse<bool>.Fail(ex.Message));
            }
        }

        /// <summary>Écrit réellement les élèves analysés.</summary>
        [HttpPost("students/{batchId}/commit")]
        public async Task<IActionResult> Commit(int batchId, [FromQuery] bool sendSms = false,
            CancellationToken ct = default)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            try
            {
                var batch = await _import.CommitAsync(schoolId.Value, userId.Value, batchId, sendSms, ct);
                // withRows: true — SANS lui, la réponse ne porte PAS les
                // identifiants créés : le lot vient de passer en Committed, et
                // Describe ne lisait la charge que pour un lot Analyzed. L'école
                // se retrouvait alors devant un écran qui lui dit « gardez cette
                // liste, les codes ne pourront plus être réaffichés »… et une
                // liste vide. Vérifié en banc d'essai le 2026-09-02.
                return Ok(ApiResponse<object>.Ok(Describe(batch, withRows: true),
                    $"{batch.CreatedStudents} élève(s) importé(s)."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<bool>.Fail(ex.Message));
            }
        }

        /// <summary>Détail d'un import : lignes analysées, ou identifiants créés.</summary>
        [HttpGet("students/{batchId}")]
        public async Task<IActionResult> Detail(int batchId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var batch = await _context.ImportBatches
                .FirstOrDefaultAsync(b => b.Id == batchId && b.SchoolId == schoolId.Value, ct);
            if (batch == null) return NotFound(ApiResponse<bool>.Fail("Import introuvable."));

            return Ok(ApiResponse<object>.Ok(Describe(batch, withRows: true), "OK"));
        }

        /// <summary>Les imports déjà faits par l'école.</summary>
        [HttpGet("students")]
        public async Task<IActionResult> History(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var list = await _context.ImportBatches
                .Where(b => b.SchoolId == schoolId.Value && b.Kind == ImportKind.Students)
                .OrderByDescending(b => b.CreatedAt)
                .Take(30)
                .ToListAsync(ct);

            return Ok(ApiResponse<object>.Ok(list.Select(b => Describe(b)).ToList(), "OK"));
        }

        // ===============================================================
        //  Enseignants et personnel
        // ===============================================================

        /// <summary>Le fichier modèle du personnel, avec les fonctions acceptées.</summary>
        [HttpGet("staff/template")]
        public async Task<IActionResult> StaffTemplate(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var bytes = await _staffImport.BuildTemplateAsync(schoolId.Value, ct);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "modele-personnel-idara.xlsx");
        }

        /// <summary>Analyse le fichier du personnel et renvoie CE QUI SERA CRÉÉ. N'écrit rien.</summary>
        [HttpPost("staff/preview")]
        public async Task<IActionResult> StaffPreview([FromBody] UploadDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            if (!TryDecode(dto, out var bytes, out var decodeError))
                return BadRequest(ApiResponse<bool>.Fail(decodeError!));

            try
            {
                var batch = await _staffImport.AnalyzeAsync(
                    schoolId.Value, userId.Value, bytes, dto.FileName ?? "import.xlsx", ct);
                return Ok(ApiResponse<object>.Ok(Describe(batch), "Fichier analysé."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<bool>.Fail(ex.Message));
            }
        }

        /// <summary>Crée réellement les comptes analysés.</summary>
        [HttpPost("staff/{batchId}/commit")]
        public async Task<IActionResult> StaffCommit(int batchId, [FromQuery] bool sendSms = false,
            CancellationToken ct = default)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            try
            {
                var batch = await _staffImport.CommitAsync(schoolId.Value, userId.Value, batchId, sendSms, ct);
                // withRows: true — sans lui, les identifiants créés ne seraient
                // pas renvoyés (voir le commentaire de l'import des élèves).
                return Ok(ApiResponse<object>.Ok(Describe(batch, withRows: true),
                    $"{batch.CreatedUsers} compte(s) créé(s)."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<bool>.Fail(ex.Message));
            }
        }

        /// <summary>Détail d'un import de personnel : lignes analysées, ou identifiants créés.</summary>
        [HttpGet("staff/{batchId}")]
        public async Task<IActionResult> StaffDetail(int batchId, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var batch = await _context.ImportBatches
                .FirstOrDefaultAsync(b => b.Id == batchId && b.SchoolId == schoolId.Value
                                          && b.Kind == ImportKind.Staff, ct);
            if (batch == null) return NotFound(ApiResponse<bool>.Fail("Import introuvable."));

            return Ok(ApiResponse<object>.Ok(Describe(batch, withRows: true), "OK"));
        }

        /// <summary>Les imports de personnel déjà faits par l'école.</summary>
        [HttpGet("staff")]
        public async Task<IActionResult> StaffHistory(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var list = await _context.ImportBatches
                .Where(b => b.SchoolId == schoolId.Value && b.Kind == ImportKind.Staff)
                .OrderByDescending(b => b.CreatedAt)
                .Take(30)
                .ToListAsync(ct);

            return Ok(ApiResponse<object>.Ok(list.Select(b => Describe(b)).ToList(), "OK"));
        }

        // ===============================================================
        //  📷 Lecture d'un cahier photographié
        // ===============================================================

        public class PhotoUploadDto
        {
            /// <summary>Les photos, en base64. Une par page du cahier.</summary>
            public List<string> ImagesBase64 { get; set; } = new();

            /// <summary>Types MIME, dans le même ordre. Facultatif — JPEG par défaut.</summary>
            public List<string>? MediaTypes { get; set; }
        }

        /// <summary>
        /// Ce que l'école peut encore lire, AVANT qu'elle ne photographie quoi
        /// que ce soit. Une limite qu'on découvre après avoir pris trente photos
        /// n'est pas une limite, c'est un piège.
        /// </summary>
        [HttpGet("photo/quota")]
        public async Task<IActionResult> PhotoQuota(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var d = await _ocrGuard.DescribeAsync(schoolId.Value, ct);
            return Ok(ApiResponse<object>.Ok(new
            {
                available = _vision.IsConfigured,
                allowed = d.Allowed,
                remainingPages = d.RemainingPages,
                allowancePages = d.AllowancePages,
                blockedReason = d.BlockedReason,
                message = d.UserMessage,
            }, "OK"));
        }

        /// <summary>
        /// Lit les photos d'un cahier et renvoie CE QUI SERA CRÉÉ. N'écrit
        /// aucun élève ni aucun compte — la confirmation reste celle de
        /// l'import Excel (<c>POST /import/{kind}/{batchId}/commit</c>).
        /// </summary>
        [HttpPost("{kind}/photo-preview")]
        public async Task<IActionResult> PhotoPreview(
            string kind, [FromBody] PhotoUploadDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            var importKind = kind.Equals("staff", StringComparison.OrdinalIgnoreCase)
                ? ImportKind.Staff
                : ImportKind.Students;

            if (dto.ImagesBase64.Count == 0)
                return BadRequest(ApiResponse<bool>.Fail("Aucune photo reçue."));

            var images = new List<Services.Vision.VisionImage>();
            for (int i = 0; i < dto.ImagesBase64.Count; i++)
            {
                if (!TryDecodeImage(dto.ImagesBase64[i], out var bytes, out var err))
                    return BadRequest(ApiResponse<bool>.Fail($"Photo {i + 1} : {err}"));

                var mime = dto.MediaTypes != null && i < dto.MediaTypes.Count
                    ? dto.MediaTypes[i]
                    : "image/jpeg";
                images.Add(new Services.Vision.VisionImage(bytes, mime));
            }

            try
            {
                var r = await _photo.AnalyzePhotosAsync(
                    schoolId.Value, userId.Value, importKind, images, ct);

                return Ok(ApiResponse<object>.Ok(new
                {
                    batch = Describe(r.Batch),
                    // Doutes du modèle, en (ligne, colonne). C'est ce qui
                    // transforme « relisez 200 lignes » en « vérifiez ces
                    // 6 cases ».
                    uncertain = r.Uncertain.Select(u => new { row = u.Row, column = u.Column }).ToList(),
                    remainingPages = r.RemainingPages,
                    allowancePages = r.AllowancePages,
                }, "Cahier lu."));
            }
            catch (InvalidOperationException ex)
            {
                // Message rédigé pour un directeur, pas une trace technique.
                return BadRequest(ApiResponse<bool>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Décode UNE photo. Bornes propres aux images : 8 Mo par photo, ce qui
        /// est très au-delà d'une page redimensionnée par le téléphone (~300 Ko)
        /// et assez bas pour qu'un envoi aberrant ne fasse pas tomber l'API.
        /// </summary>
        private static bool TryDecodeImage(string raw, out byte[] bytes, out string? error)
        {
            bytes = Array.Empty<byte>();
            try
            {
                var s = raw ?? string.Empty;
                var comma = s.IndexOf(",", StringComparison.Ordinal);
                if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
                    s = s[(comma + 1)..];
                bytes = Convert.FromBase64String(s);
            }
            catch (FormatException)
            {
                error = "cette photo n'a pas pu être lue.";
                return false;
            }

            if (bytes.Length == 0) { error = "photo vide."; return false; }
            if (bytes.Length > 8 * 1024 * 1024) { error = "photo trop lourde (8 Mo maximum)."; return false; }

            error = null;
            return true;
        }

        // ===============================================================

        /// <summary>
        /// Décode le fichier déposé. Commun aux deux natures d'import : les
        /// pièges (data-URI selon le sélecteur, taille aberrante) ne dépendent
        /// pas de ce que le fichier contient.
        /// </summary>
        private static bool TryDecode(UploadDto dto, out byte[] bytes, out string? error)
        {
            bytes = Array.Empty<byte>();
            try
            {
                var raw = dto.FileBase64 ?? string.Empty;
                // Une pièce jointe arrive parfois en data-URI selon le sélecteur
                // de fichiers utilisé.
                var comma = raw.IndexOf(",", StringComparison.Ordinal);
                if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
                    raw = raw[(comma + 1)..];
                bytes = Convert.FromBase64String(raw);
            }
            catch (FormatException)
            {
                error = "Le fichier n'a pas pu être lu.";
                return false;
            }

            // 12 Mo : très au-delà d'un tableau de 5 000 élèves, et assez bas
            // pour qu'un fichier aberrant ne fasse pas tomber l'API.
            if (bytes.Length == 0) { error = "Le fichier est vide."; return false; }
            if (bytes.Length > 12 * 1024 * 1024) { error = "Le fichier dépasse 12 Mo."; return false; }

            error = null;
            return true;
        }

        private static object Describe(Models.ImportBatch b, bool withRows = false)
        {
            object? rows = null;
            object? credentials = null;
            if (withRows || b.Status == ImportBatchStatus.Analyzed)
            {
                try
                {
                    using var doc = JsonDocument.Parse(b.RowsJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        rows = JsonSerializer.Deserialize<object>(b.RowsJson);
                    }
                    else
                    {
                        // Après confirmation, la charge porte { rows, credentials }.
                        if (doc.RootElement.TryGetProperty("rows", out var r))
                            rows = JsonSerializer.Deserialize<object>(r.GetRawText());
                        if (doc.RootElement.TryGetProperty("credentials", out var c))
                            credentials = JsonSerializer.Deserialize<object>(c.GetRawText());
                    }
                }
                catch (JsonException) { /* charge illisible : on renvoie les compteurs */ }
            }

            return new
            {
                b.Id,
                b.FileName,
                kind = b.Kind.ToString(),
                status = b.Status.ToString(),
                b.TotalRows,
                b.ValidRows,
                b.ErrorRows,
                b.DuplicateRows,
                b.CreatedStudents,
                b.CreatedClasses,
                b.CreatedGuardians,
                b.CreatedUsers,
                b.CreatedAt,
                b.CommittedAt,
                b.Error,
                rows,
                credentials,
            };
        }
    }
}
