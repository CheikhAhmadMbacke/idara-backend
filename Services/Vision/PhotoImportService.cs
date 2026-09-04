using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Vision
{
    /// <summary>Ce que l'école récupère après avoir photographié son cahier.</summary>
    /// <param name="Batch">Le lot analysé — exactement celui d'un import Excel.</param>
    /// <param name="Uncertain">Cellules douteuses, en (ligne du lot, nom de colonne).</param>
    /// <param name="RemainingPages">Pages de lecture restantes APRÈS cet envoi.</param>
    /// <param name="AllowancePages">Quota total de l'école.</param>
    public record PhotoImportResult(
        ImportBatch Batch,
        IReadOnlyList<(int Row, string Column)> Uncertain,
        int RemainingPages,
        int AllowancePages);

    public interface IPhotoImportService
    {
        Task<PhotoImportResult> AnalyzePhotosAsync(
            int schoolId, int userId, ImportKind kind,
            IReadOnlyList<VisionImage> images, CancellationToken ct);
    }

    /// <summary>
    /// Transforme des photos de cahier en lot d'import prêt à confirmer.
    ///
    /// <para><b>Ce service ne sait presque rien faire, et c'est voulu.</b> Il
    /// enchaîne : garde-fou → lecture par l'IA → écriture au registre →
    /// <c>AnalyzeTableAsync</c>. Cette dernière étape est celle de l'import
    /// Excel, appelée sans un paramètre de plus. Toute la valeur du parcours
    /// (parseur indulgent, dédoublonnage contre la base, « rien n'est écrit
    /// tant que vous n'avez pas confirmé », création par le service métier)
    /// vient donc de l'existant. Une photo et un fichier Excel ne peuvent pas
    /// diverger, puisqu'ils empruntent le même chemin dès la deuxième étape.</para>
    ///
    /// <para><b>L'écriture au registre est INCONDITIONNELLE.</b> Un appel qui
    /// échoue a quand même coûté des tokens : il doit laisser une ligne, sinon
    /// la dépense devient invisible et le plafond quotidien ne compte plus ce
    /// qui a réellement été dépensé. C'est la leçon du §194 — une trace qui ne
    /// sort pas de la base n'existe pas.</para>
    /// </summary>
    public class PhotoImportService : IPhotoImportService
    {
        private readonly AppDbContext _db;
        private readonly IOcrBudgetGuard _guard;
        private readonly IDocumentVisionService _vision;
        private readonly IStudentImportService _students;
        private readonly IStaffImportService _staff;
        private readonly ILogger<PhotoImportService> _logger;

        public PhotoImportService(
            AppDbContext db,
            IOcrBudgetGuard guard,
            IDocumentVisionService vision,
            IStudentImportService students,
            IStaffImportService staff,
            ILogger<PhotoImportService> logger)
        {
            _db = db;
            _guard = guard;
            _vision = vision;
            _students = students;
            _staff = staff;
            _logger = logger;
        }

        public async Task<PhotoImportResult> AnalyzePhotosAsync(
            int schoolId, int userId, ImportKind kind,
            IReadOnlyList<VisionImage> images, CancellationToken ct)
        {
            // --- 1. Le garde-fou D'ABORD, avant même de regarder si le
            // fournisseur est configuré.
            //
            // L'ordre inverse paraissait naturel — pourquoi évaluer un quota si
            // l'on ne peut de toute façon rien lire ? — et il était mauvais pour
            // une raison qui n'apparaît qu'au banc d'essai : il rendait le
            // garde-fou INVÉRIFIABLE sans clé, donc invérifiable sans dépenser
            // de l'argent. Or c'est précisément la pièce qui protège l'argent.
            // Un garde-fou qu'on ne peut exercer qu'en production est un
            // garde-fou dont on ne sait rien.
            //
            // Accessoirement, un refus pour quota ou KYC est un fait qui
            // concerne l'école : il mérite sa ligne de registre que le
            // fournisseur soit joignable ou non.
            var verdict = await _guard.EvaluateAsync(new OcrGuardContext(schoolId, images.Count), ct);
            if (!verdict.Allowed)
            {
                // Un refus laisse une trace, à coût nul : c'est ce qui permet de
                // savoir qu'une école se heurte au plafond au lieu de le deviner.
                await RecordAsync(schoolId, userId, kind, images.Count,
                    charged: 0, success: false, blocked: verdict.BlockedReason,
                    error: null, model: string.Empty, inTok: 0, outTok: 0,
                    costCentimes: 0, rows: 0, uncertain: 0, batchId: null, durationMs: 0, ct);

                throw new InvalidOperationException(
                    verdict.UserMessage ?? "La lecture n'est pas possible pour l'instant.");
            }

            // --- 2. Le fournisseur est-il seulement joignable ? Aucune ligne de
            // registre ici : l'école n'a rien fait de mal et rien n'a été
            // dépensé — c'est une indisponibilité de notre côté (§89).
            if (!_vision.IsConfigured)
                throw new InvalidOperationException(
                    "La lecture d'un cahier n'est pas disponible pour l'instant. "
                    + "Vous pouvez utiliser le fichier Excel.");

            var settings = await _db.GetPlatformSettingsAsync(ct);

            // --- 3. La lecture. C'est le seul moment où de l'argent part.
            VisionReadResult read;
            try
            {
                read = await _vision.ReadAsync(images, kind, ct);
            }
            catch (Exception ex)
            {
                // On ne connaît pas les tokens consommés quand l'appel lève ;
                // on estime la dépense sur les images envoyées plutôt que de
                // l'oublier — un coût inconnu compté à zéro rendrait le plafond
                // quotidien aveugle exactement les jours où il sert.
                var estimated = EstimateCentimes(settings, images.Count);
                await RecordAsync(schoolId, userId, kind, images.Count,
                    charged: 0, success: false, blocked: null, error: Truncate(ex.Message),
                    model: string.Empty, inTok: 0, outTok: 0, costCentimes: estimated,
                    rows: 0, uncertain: 0, batchId: null, durationMs: 0, ct);

                _logger.LogError(ex, "[photo-import] Lecture échouée pour l'école {SchoolId}", schoolId);
                throw new InvalidOperationException(
                    "La lecture des photos a échoué. Vérifiez qu'elles sont nettes et bien cadrées, "
                    + "puis réessayez.");
            }

            var cost = CostCentimes(settings, read.InputTokens, read.OutputTokens);

            if (read.Table.Rows.Count == 0)
            {
                await RecordAsync(schoolId, userId, kind, images.Count,
                    charged: 0, success: false, blocked: "no_rows", error: null,
                    model: read.Model, inTok: read.InputTokens, outTok: read.OutputTokens,
                    costCentimes: cost, rows: 0, uncertain: 0, batchId: null,
                    durationMs: read.DurationMs, ct);

                throw new InvalidOperationException(
                    "Aucune ligne n'a pu être lue sur ces photos. Cadrez le tableau entier, "
                    + "évitez les reflets, et prenez une photo par page.");
            }

            // --- 4. L'analyse : celle de l'import Excel, sans un paramètre de plus.
            var source = images.Count == 1 ? "Photo du cahier" : $"Photos du cahier ({images.Count})";
            var batch = kind == ImportKind.Staff
                ? await _staff.AnalyzeTableAsync(schoolId, userId, read.Table, source, ct)
                : await _students.AnalyzeTableAsync(schoolId, userId, read.Table, source, ct);

            await RecordAsync(schoolId, userId, kind, images.Count,
                charged: images.Count, success: true, blocked: null, error: null,
                model: read.Model, inTok: read.InputTokens, outTok: read.OutputTokens,
                costCentimes: cost, rows: read.Table.Rows.Count,
                uncertain: read.Uncertain.Count, batchId: batch.Id,
                durationMs: read.DurationMs, ct);

            _logger.LogInformation(
                "[photo-import] École {SchoolId} : {Pages} page(s) → lot {BatchId} "
                + "({Rows} ligne(s), {Unc} douteuse(s)), coût {Cost} centimes",
                schoolId, images.Count, batch.Id, read.Table.Rows.Count, read.Uncertain.Count, cost);

            // Traduit les doutes en (ligne du lot, NOM de colonne) : un index
            // numérique ne veut rien dire pour l'écran, qui affiche des colonnes.
            var columns = Common.Utilities.ImportColumns.For(kind);
            var uncertain = read.Uncertain
                .Where(u => u.Col >= 0 && u.Col < columns.Length)
                .Select(u => (u.Row, columns[u.Col]))
                .ToList();

            return new PhotoImportResult(
                batch, uncertain,
                Math.Max(0, verdict.RemainingPages - images.Count),
                verdict.AllowancePages);
        }

        // ---------------------------------------------------------------

        /// <summary>
        /// Coût réel, calculé sur les tokens RÉELLEMENT consommés et les tarifs
        /// en vigueur à cet instant. Figé au registre : un changement de tarif
        /// plus tard ne doit pas réécrire l'histoire.
        /// </summary>
        private static long CostCentimes(PlatformSettings p, int inTok, int outTok)
            => (long)Math.Round(
                   inTok / 1_000_000.0 * p.OcrInputPriceCentimesPerMTok
                   + outTok / 1_000_000.0 * p.OcrOutputPriceCentimesPerMTok);

        /// <summary>
        /// Estimation quand l'appel a levé sans rendre d'usage. Volontairement
        /// large (une page pleine, en entrée ET en sortie) : mieux vaut
        /// surestimer une dépense inconnue que la compter pour rien.
        /// </summary>
        private static long EstimateCentimes(PlatformSettings p, int pages)
            => CostCentimes(p, pages * 2500, pages * 2500);

        private static string Truncate(string s) => s.Length > 400 ? s[..400] : s;

        private async Task RecordAsync(
            int schoolId, int userId, ImportKind kind, int pages, int charged, bool success,
            string? blocked, string? error, string model, int inTok, int outTok,
            long costCentimes, int rows, int uncertain, int? batchId, int durationMs,
            CancellationToken ct)
        {
            _db.OcrJobs.Add(new OcrJob
            {
                SchoolId = schoolId,
                CreatedById = userId,
                Kind = kind,
                PageCount = pages,
                ChargedPages = charged,
                Success = success,
                BlockedReason = blocked,
                Error = error,
                Model = model,
                InputTokens = inTok,
                OutputTokens = outTok,
                CostCentimes = costCentimes,
                ExtractedRows = rows,
                UncertainCells = uncertain,
                ImportBatchId = batchId,
                DurationMs = durationMs,
                CreatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(ct);
        }
    }
}
