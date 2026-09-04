using System.Globalization;
using System.Text;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>Totaux d'une collecte, recalculés — jamais stockés (§112).</summary>
    public record CampaignTotals(long CollectedFcfa, int DonationCount, int PendingCount);

    public interface IDonationCampaignService
    {
        Task<DonationCampaign> EnsurePermanentAsync(int schoolId, int createdById, CancellationToken ct = default);
        Task<string> GenerateSlugAsync(string name, CancellationToken ct = default);
        Task<Dictionary<int, CampaignTotals>> GetTotalsAsync(IReadOnlyCollection<int> campaignIds, CancellationToken ct = default);
        Task<CampaignTotals> GetTotalsAsync(int campaignId, CancellationToken ct = default);
    }

    /// <summary>
    /// Règles communes aux collectes de dons, partagées par l'écran de l'école et
    /// par la page publique — donc écrites une fois (§199 : deux appelants pour
    /// une règle métier, c'est un service, jamais une copie).
    /// </summary>
    public class DonationCampaignService : IDonationCampaignService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DonationCampaignService> _logger;

        public DonationCampaignService(AppDbContext context, ILogger<DonationCampaignService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Nom de la collecte permanente, celle qu'on partage sans réfléchir.</summary>
        public const string PermanentName = "Dons au daara";

        /// <summary>
        /// La collecte permanente de l'école, créée à la volée si elle n'existe
        /// pas encore. Appelée à l'ouverture de l'écran des dons : aucune école
        /// existante n'a besoin d'être migrée, elle obtient la sienne à sa
        /// première visite (§175 — un seed dans la création d'école ne rattrape
        /// jamais les écoles déjà là).
        /// </summary>
        public async Task<DonationCampaign> EnsurePermanentAsync(int schoolId, int createdById, CancellationToken ct = default)
        {
            var existing = await _context.DonationCampaigns
                .FirstOrDefaultAsync(c => c.SchoolId == schoolId && c.IsPermanent, ct);
            if (existing != null) return existing;

            var schoolName = await _context.Schools
                .Where(s => s.Id == schoolId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct) ?? "daara";

            var feesPayer = await _context.SchoolPaymentSettings
                .Where(s => s.SchoolId == schoolId)
                .Select(s => (FeesPayer?)s.DonationFeesPayer)
                .FirstOrDefaultAsync(ct) ?? FeesPayer.School;

            var campaign = new DonationCampaign
            {
                SchoolId = schoolId,
                Slug = await GenerateSlugAsync(schoolName, ct),
                Name = PermanentName,
                AmountMode = DonationAmountMode.Free,
                FeesPayer = feesPayer,
                ShowDonorWall = true,
                Status = DonationCampaignStatus.Active,
                IsPermanent = true,
                CreatedById = createdById,
                CreatedAt = DateTime.UtcNow
            };
            _context.DonationCampaigns.Add(campaign);

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Course : deux onglets ouvrent l'écran en même temps. L'index
                // unique filtré a tranché — on relit celle qui a gagné.
                _logger.LogWarning(ex, "[donations] Course sur la collecte permanente de l'école {SchoolId}", schoolId);
                _context.Entry(campaign).State = EntityState.Detached;
                return await _context.DonationCampaigns
                    .FirstAsync(c => c.SchoolId == schoolId && c.IsPermanent, ct);
            }
            return campaign;
        }

        /// <summary>
        /// Adresse publique lisible, unique sur toute la plateforme. Le suffixe
        /// n'apparaît qu'en cas de collision réelle : <c>salle-bleue</c> reste
        /// <c>salle-bleue</c> pour la première école qui la crée.
        /// </summary>
        public async Task<string> GenerateSlugAsync(string name, CancellationToken ct = default)
        {
            var baseSlug = Slugify(name);
            if (baseSlug.Length == 0) baseSlug = "don";

            if (!await _context.DonationCampaigns.AnyAsync(c => c.Slug == baseSlug, ct))
                return baseSlug;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var candidate = $"{baseSlug}-{Random.Shared.Next(0, 46656):x}";
                if (!await _context.DonationCampaigns.AnyAsync(c => c.Slug == candidate, ct))
                    return candidate;
            }
            // Douze collisions d'affilée : on abandonne la lisibilité plutôt que
            // de refuser la création à l'école.
            return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(72, baseSlug.Length + 33)];
        }

        /// <summary>
        /// Ce que les donateurs ont donné, par collecte.
        /// </summary>
        /// <remarks>
        /// <b>La barre mesure le DON, pas l'encaissement net.</b> On somme
        /// <c>TargetAmountFcfa</c> : c'est le montant que le donateur a voulu
        /// donner, dans les deux modes de frais. Le net réellement crédité au
        /// portefeuille se lit dans l'écran du compte, où il a un sens comptable
        /// — sur une page de collecte, afficher un total rogné des frais ferait
        /// croire à une ponction et découragerait le don suivant.
        /// </remarks>
        public async Task<Dictionary<int, CampaignTotals>> GetTotalsAsync(
            IReadOnlyCollection<int> campaignIds, CancellationToken ct = default)
        {
            if (campaignIds.Count == 0) return new Dictionary<int, CampaignTotals>();

            // Une seule requête, une seule agrégation par colonne : les
            // agrégations conditionnelles imbriquées ne se traduisent pas
            // toujours en SQL et ne se découvrent qu'au premier appel réel (§195).
            var rows = await _context.Payments
                .Where(p => p.DonationCampaignId != null
                            && campaignIds.Contains(p.DonationCampaignId.Value)
                            && (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.Pending))
                .GroupBy(p => new { CampaignId = p.DonationCampaignId!.Value, p.Status })
                .Select(g => new
                {
                    g.Key.CampaignId,
                    g.Key.Status,
                    Total = g.Sum(p => p.TargetAmountFcfa),
                    Count = g.Count()
                })
                .ToListAsync(ct);

            var result = new Dictionary<int, CampaignTotals>();
            foreach (var id in campaignIds)
            {
                var completed = rows.FirstOrDefault(r => r.CampaignId == id && r.Status == PaymentStatus.Completed);
                var pending = rows.FirstOrDefault(r => r.CampaignId == id && r.Status == PaymentStatus.Pending);
                result[id] = new CampaignTotals(
                    completed?.Total ?? 0,
                    completed?.Count ?? 0,
                    pending?.Count ?? 0);
            }
            return result;
        }

        public async Task<CampaignTotals> GetTotalsAsync(int campaignId, CancellationToken ct = default)
        {
            var map = await GetTotalsAsync(new[] { campaignId }, ct);
            return map.TryGetValue(campaignId, out var t) ? t : new CampaignTotals(0, 0, 0);
        }

        /// <summary>
        /// « Réfection de la salle bleue » → <c>refection-de-la-salle-bleue</c>.
        /// Les accents sont décomposés puis retirés : sans ça, un titre français
        /// donnerait une adresse pleine de <c>%C3%A9</c>, indictable au téléphone.
        /// </summary>
        public static string Slugify(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark) continue;   // accent décomposé
                if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }

            var slug = sb.ToString().Trim('-');
            if (slug.Length > 40) slug = slug[..40].Trim('-');
            return slug;
        }
    }
}
