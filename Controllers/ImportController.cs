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
    /// Import en masse des élèves depuis un fichier Excel ou CSV.
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
        private readonly AppDbContext _context;

        public ImportController(IStudentImportService import, AppDbContext context)
        {
            _import = import;
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

            byte[] bytes;
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
                return BadRequest(ApiResponse<bool>.Fail("Le fichier n'a pas pu être lu."));
            }

            // 12 Mo : très au-delà d'un tableau de 5 000 élèves, et assez bas
            // pour qu'un fichier aberrant ne fasse pas tomber l'API.
            if (bytes.Length == 0)
                return BadRequest(ApiResponse<bool>.Fail("Le fichier est vide."));
            if (bytes.Length > 12 * 1024 * 1024)
                return BadRequest(ApiResponse<bool>.Fail("Le fichier dépasse 12 Mo."));

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
                return Ok(ApiResponse<object>.Ok(Describe(batch),
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
                status = b.Status.ToString(),
                b.TotalRows,
                b.ValidRows,
                b.ErrorRows,
                b.DuplicateRows,
                b.CreatedStudents,
                b.CreatedClasses,
                b.CreatedGuardians,
                b.CreatedAt,
                b.CommittedAt,
                b.Error,
                rows,
                credentials,
            };
        }
    }
}
