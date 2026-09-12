using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// 🔗 <b>Le lien de paiement permanent d'un responsable</b> — un par école
    /// et par responsable, qui recalcule la dette à chaque ouverture (§161).
    ///
    /// <para><b>Pourquoi un service, et pas une méthode privée de plus.</b>
    /// Cette règle avait déjà DEUX copies — l'écran école
    /// (<c>PaymentLinksController</c>) et la campagne SuperAdmin
    /// (<c>PaymentLinkCampaignController</c>) — et le SMS d'inscription en
    /// demandait une troisième. Or ce n'est pas du code plat : il y a une
    /// course à traiter (deux membres de l'école qui génèrent le même lien au
    /// même instant, arbitrée par l'index unique) et une adresse publique à
    /// composer. Trois copies, c'est la garantie que la prochaine correction
    /// n'en touchera qu'une (§199).</para>
    /// </summary>
    public interface IPaymentLinkService
    {
        /// <summary>
        /// Le lien actif de ce responsable pour cette école — créé s'il
        /// n'existe pas, réutilisé sinon. <c>Created</c> dit lequel des deux,
        /// pour les journaux et les compteurs de campagne.
        /// </summary>
        Task<(PaymentLink Link, bool Created)> EnsureAsync(
            int schoolId, int guardianId, int createdById, CancellationToken ct = default);

        /// <summary>L'adresse publique à mettre dans un SMS ou un WhatsApp.</summary>
        string BuildUrl(string token);
    }

    public class PaymentLinkService : IPaymentLinkService
    {
        private readonly AppDbContext _context;
        private readonly SenePaySettings _senepay;

        public PaymentLinkService(AppDbContext context, IOptions<SenePaySettings> senepay)
        {
            _context = context;
            _senepay = senepay.Value;
        }

        public async Task<(PaymentLink Link, bool Created)> EnsureAsync(
            int schoolId, int guardianId, int createdById, CancellationToken ct = default)
        {
            var existing = await _context.PaymentLinks.FirstOrDefaultAsync(
                l => l.SchoolId == schoolId && l.GuardianId == guardianId && l.RevokedAt == null, ct);

            var now = DateTime.UtcNow;
            if (existing != null)
            {
                // Le lien ressort : on date le partage. C'est ce qui alimente
                // « dernier envoi » côté école, et la mesure de la campagne.
                existing.LastSharedAt = now;
                await _context.SaveChangesAsync(ct);
                return (existing, false);
            }

            var link = new PaymentLink
            {
                Token = Guid.NewGuid().ToString("N"),
                SchoolId = schoolId,
                GuardianId = guardianId,
                CreatedById = createdById,
                CreatedAt = now,
                LastSharedAt = now,
            };
            _context.PaymentLinks.Add(link);
            try
            {
                await _context.SaveChangesAsync(ct);
                return (link, true);
            }
            catch (DbUpdateException)
            {
                // Course entre deux membres de l'école qui génèrent le même lien
                // au même instant : l'index unique a tranché, on relit le
                // gagnant plutôt que de remonter une erreur incompréhensible.
                _context.Entry(link).State = EntityState.Detached;
                var winner = await _context.PaymentLinks.FirstAsync(
                    l => l.SchoolId == schoolId && l.GuardianId == guardianId && l.RevokedAt == null, ct);
                return (winner, false);
            }
        }

        public string BuildUrl(string token) =>
            PublicLinks.PaymentLink(_senepay.PublicBaseUrl, token);
    }
}
