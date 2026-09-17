using System.Text.Json;
using Idara.API.Common.Utilities;
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
        private readonly Services.Vision.IOcrPricingService _pricing;
        private readonly IWavePayinService _wavePayin;
        private readonly ILogger<ImportController> _logger;
        private readonly AppDbContext _context;

        public ImportController(
            IStudentImportService import,
            IStaffImportService staffImport,
            Services.Vision.IPhotoImportService photo,
            Services.Vision.IOcrBudgetGuard ocrGuard,
            Services.Vision.IDocumentVisionService vision,
            Services.Vision.IOcrPricingService pricing,
            IWavePayinService wavePayin,
            ILogger<ImportController> logger,
            AppDbContext context)
        {
            _import = import;
            _staffImport = staffImport;
            _photo = photo;
            _ocrGuard = ocrGuard;
            _vision = vision;
            _pricing = pricing;
            _wavePayin = wavePayin;
            _logger = logger;
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
            /// <summary>
            /// Les fichiers déposés, en base64 : photos du cahier et/ou PDF.
            ///
            /// <para>⚠️ Le nom du champ dit « images » et ne changera pas : il est
            /// figé depuis la première application publiée (§220). Une application
            /// déjà installée continue d'envoyer <c>imagesBase64</c> ; la renommer
            /// couperait la lecture sur tout le parc, en silence.</para>
            /// </summary>
            public List<string> ImagesBase64 { get; set; } = new();

            /// <summary>
            /// Types MIME, dans le même ordre. Purement indicatif : c'est la
            /// SIGNATURE du contenu qui décide (§216).
            /// </summary>
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
                return BadRequest(ApiResponse<bool>.Fail("Aucun fichier reçu."));

            var files = new List<Services.Vision.VisionFile>();
            for (int i = 0; i < dto.ImagesBase64.Count; i++)
            {
                var declared = dto.MediaTypes != null && i < dto.MediaTypes.Count
                    ? dto.MediaTypes[i]
                    : null;

                // 🔴 C'est le CONTENU qui décide, jamais la déclaration du client
                // (§216). Ce chemin utilisait jusqu'ici un décodeur maison qui ne
                // reniflait rien : n'importe quel binaire annoncé « image/jpeg »
                // partait à l'IA — donc était PAYÉ — pour ne rien rendre.
                var decoded = FileUploadValidator.DecodeAndValidate(
                    dto.ImagesBase64[i],
                    maxSizeMb: PhotoMaxFileSizeMb,
                    allowedMimeTypes: PhotoAllowedMimeTypes,
                    declaredContentType: declared);

                if (decoded == null)
                    return BadRequest(ApiResponse<bool>.Fail(
                        $"Fichier {i + 1} : illisible, trop lourd (plus de {PhotoMaxFileSizeMb} Mo) "
                        + "ou dans un format non accepté. Formats acceptés : photo (JPEG, PNG, WEBP) ou PDF."));

                files.Add(new Services.Vision.VisionFile(decoded.Bytes, decoded.ContentType));
            }

            try
            {
                var r = await _photo.AnalyzePhotosAsync(
                    schoolId.Value, userId.Value, importKind, files, ct);

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

        // ===============================================================
        //  💰 Acheter des pages de lecture
        // ===============================================================

        /// <summary>
        /// Ce qu'une page coûte À CETTE ÉCOLE, avant qu'elle ne demande à
        /// acheter. Le prix dépend de la densité de SON cahier — annoncer un
        /// prix moyen ferait payer quatre fois trop au daara qui tient une fiche
        /// par élève, celui-là même pour qui la fonction existe.
        /// </summary>
        [HttpGet("photo/pricing")]
        public async Task<IActionResult> PhotoPricing(CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var q = await _pricing.QuoteAsync(schoolId.Value, ct);
            var guard = await _ocrGuard.DescribeAsync(schoolId.Value, ct);

            return Ok(ApiResponse<object>.Ok(new
            {
                available = _vision.IsConfigured,
                purchaseEnabled = q.PurchaseEnabled,
                pricePerPageFcfa = q.PricePerPageFcfa,
                studentsPerPage = q.StudentsPerPage,
                // 🔴 Ce drapeau commande la PHRASE affichée : un prix calibré
                // s'annonce (« 102 F la page »), un prix non calibré s'annonce
                // comme un plancher (« à partir de … »). Confondre les deux,
                // c'est promettre un prix qu'on ne tiendra pas.
                calibrated = q.Calibrated,
                measuredOnPages = q.MeasuredOnPages,
                maxPagesPerPurchase = q.MaxPagesPerPurchase,
                remainingPages = guard.RemainingPages,
                allowancePages = guard.AllowancePages,
            }, "OK"));
        }

        public class BuyPagesDto
        {
            /// <summary>Pages achetées.</summary>
            public int Pages { get; set; }
        }

        /// <summary>
        /// Achète des pages de lecture. Wave uniquement, montant figé, numéro
        /// repris en base — aucune saisie, comme le parcours parent à montant
        /// fixe.
        ///
        /// <para>🔴 Rien n'est octroyé ici : les pages arrivent au webhook, dans
        /// la transaction qui bascule le paiement. Un octroi à l'initiation
        /// donnerait les pages à qui ouvre le formulaire.</para>
        /// </summary>
        [HttpPost("photo/purchase")]
        public async Task<IActionResult> BuyPages([FromBody] BuyPagesDto dto, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            var userId = User.GetUserId();
            if (schoolId == null || userId == null) return Forbid();

            var q = await _pricing.QuoteAsync(schoolId.Value, ct);
            if (!q.PurchaseEnabled)
                return BadRequest(ApiResponse<bool>.Fail(
                    "L'achat de pages n'est pas disponible pour l'instant."));

            // La porte du garde-fou vaut pour l'achat comme pour la lecture :
            // une école dont le dossier n'est pas validé ne doit pas pouvoir
            // acheter un service qu'elle ne pourra pas utiliser.
            var guard = await _ocrGuard.DescribeAsync(schoolId.Value, ct);
            if (guard.BlockedReason is "kyc_not_validated" or "school_unknown" or "disabled")
                return BadRequest(ApiResponse<bool>.Fail(
                    guard.UserMessage ?? "L'achat de pages n'est pas disponible pour l'instant."));

            if (dto.Pages <= 0)
                return BadRequest(ApiResponse<bool>.Fail("Indiquez combien de pages vous voulez acheter."));
            if (dto.Pages > q.MaxPagesPerPurchase)
                return BadRequest(ApiResponse<bool>.Fail(
                    $"Vous pouvez acheter au maximum {q.MaxPagesPerPurchase} pages à la fois."));

            var payerPhone = await _context.Users.Where(u => u.Id == userId.Value)
                .Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(payerPhone))
                payerPhone = await _context.Schools.Where(s => s.Id == schoolId.Value)
                    .Select(s => s.PhoneNumber).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(payerPhone))
                return BadRequest(ApiResponse<bool>.Fail(
                    "Aucun numéro de téléphone n'est associé à votre compte ni à votre école. "
                    + "Ajoutez-en un dans les paramètres de l'école."));

            var amount = q.PricePerPageFcfa * dto.Pages;

            // 🔴 FeesPayer = School et TargetAmountFcfa = 0, et ce n'est pas un
            // détail de forme :
            //   - l'article 8.2 du contrat Wave interdit de facturer des frais
            //     au payeur (§145) — donc pas de majoration : l'école paie le
            //     prix affiché, la plateforme absorbe la commission ;
            //   - ce couple exclut mécaniquement ce paiement du calcul de la
            //     marge de majoration dans P (§112), qui exige FeesPayer=Parent ET
            //     TargetAmountFcfa > 0. Sans quoi la recette serait comptée deux
            //     fois.
            var payment = new Models.Payment
            {
                SchoolId = schoolId.Value,
                Purpose = PaymentPurpose.OcrPages,
                OcrPagesPurchased = dto.Pages,
                OcrPricePerPageFcfa = q.PricePerPageFcfa,
                AmountFcfa = amount,
                TargetAmountFcfa = 0,
                FeesFcfa = 0,
                NetCreditedFcfa = 0,
                Operator = PaymentOperator.Wave,
                FeesPayer = FeesPayer.School,
                Status = PaymentStatus.Pending,
                InitiatedAt = DateTime.UtcNow,
                PublicResultToken = Guid.NewGuid().ToString("N"),
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(ct);

            var outcome = await _wavePayin.StartAsync(payment, User.GetEmail(), ct);
            if (!outcome.Ok)
            {
                _logger.LogError("[ocr/purchase] Session refusée pour Payment {PaymentId} : {Err}",
                    payment.Id, outcome.ErrorMessage);
                return StatusCode(outcome.HttpStatus, ApiResponse<bool>.Fail(
                    outcome.ErrorMessage ?? "Le paiement est temporairement indisponible."));
            }

            return Ok(ApiResponse<object>.Ok(new
            {
                paymentId = payment.Id,
                status = "Pending",
                nextAction = "REDIRECT_TO_PROVIDER_LINK",
                redirectUrl = outcome.RedirectUrl,
                errorCode = (string?)null,
                failureReason = (string?)null,
                pages = dto.Pages,
                pricePerPageFcfa = q.PricePerPageFcfa,
                amountFcfa = amount,
            }, "Paiement initié."));
        }

        /// <summary>
        /// Où en est l'achat. L'application interroge après le retour de Wave —
        /// c'est le webhook qui fait foi, ce poll ne fait que le lire.
        /// </summary>
        [HttpGet("photo/purchase/{id:int}")]
        public async Task<IActionResult> PurchaseStatus(int id, CancellationToken ct)
        {
            var schoolId = User.GetSchoolId();
            if (schoolId == null) return Forbid();

            var p = await _context.Payments.FirstOrDefaultAsync(
                x => x.Id == id && x.SchoolId == schoolId.Value
                     && x.Purpose == PaymentPurpose.OcrPages, ct);
            if (p == null) return NotFound(ApiResponse<bool>.Fail("Achat introuvable."));

            var guard = await _ocrGuard.DescribeAsync(schoolId.Value, ct);
            return Ok(ApiResponse<object>.Ok(new
            {
                paymentId = p.Id,
                status = p.Status.ToString(),
                pages = p.OcrPagesPurchased,
                amountFcfa = p.AmountFcfa,
                failureReason = p.FailureReason,
                remainingPages = guard.RemainingPages,
                allowancePages = guard.AllowancePages,
            }, "OK"));
        }

        /// <summary>
        /// 20 Mo par fichier. Très au-delà d'une photo redimensionnée par le
        /// téléphone (~300 Ko), et calibré sur le vrai cas limite : un ancien
        /// cahier scanné en PDF, qui porte une image par page.
        /// </summary>
        private const int PhotoMaxFileSizeMb = 20;

        /// <summary>
        /// Les cinq formats acceptés portent TOUS une signature universelle —
        /// exiger qu'elle soit reconnue ne refuse aucun fichier légitime (§216).
        /// </summary>
        private static readonly string[] PhotoAllowedMimeTypes =
        {
            "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf",
        };

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
