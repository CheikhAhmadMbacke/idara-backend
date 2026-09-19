using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Models;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    /// <summary>
    /// 🔗 <b>Le lien de paiement permanent de l'abonnement d'une école</b> — un
    /// par école, qui recalcule ce qui est dû à chaque ouverture.
    ///
    /// <para>Même forme que <see cref="PaymentLinkService"/> pour les familles,
    /// et pour la même raison (§199) : la règle a déjà deux appelants — le SMS
    /// de relance et l'écran d'abonnement — et une troisième copie serait la
    /// garantie que la prochaine correction n'en touchera qu'une. La course y
    /// est traitée de la même façon, par l'index unique.</para>
    /// </summary>
    public interface ISubscriptionPaymentLinkService
    {
        /// <summary>
        /// Le lien actif de cette école — créé s'il n'existe pas, réutilisé
        /// sinon. <c>Created</c> dit lequel des deux, pour les journaux.
        /// </summary>
        Task<(SubscriptionPaymentLink Link, bool Created)> EnsureAsync(
            int schoolId, CancellationToken ct = default);

        /// <summary>L'adresse publique à mettre dans le SMS de relance.</summary>
        string BuildUrl(string token);
    }

    public class SubscriptionPaymentLinkService : ISubscriptionPaymentLinkService
    {
        private readonly AppDbContext _context;
        private readonly WaveSettings _waveSettings;

        public SubscriptionPaymentLinkService(
            AppDbContext context, IOptions<WaveSettings> waveSettings)
        {
            _context = context;
            _waveSettings = waveSettings.Value;
        }

        public async Task<(SubscriptionPaymentLink Link, bool Created)> EnsureAsync(
            int schoolId, CancellationToken ct = default)
        {
            var existing = await _context.SubscriptionPaymentLinks
                .FirstOrDefaultAsync(l => l.SchoolId == schoolId && l.RevokedAt == null, ct);

            var now = DateTime.UtcNow;
            if (existing != null)
            {
                existing.LastSharedAt = now;
                await _context.SaveChangesAsync(ct);
                return (existing, false);
            }

            var link = new SubscriptionPaymentLink
            {
                Token = Guid.NewGuid().ToString("N"),
                SchoolId = schoolId,
                CreatedAt = now,
                LastSharedAt = now
            };
            _context.SubscriptionPaymentLinks.Add(link);
            try
            {
                await _context.SaveChangesAsync(ct);
                return (link, true);
            }
            catch (DbUpdateException)
            {
                // Course entre le cron de facturation et l'écran de l'école :
                // l'index unique a tranché, on relit le gagnant plutôt que de
                // remonter une erreur que personne ne saurait lire.
                _context.Entry(link).State = EntityState.Detached;
                var winner = await _context.SubscriptionPaymentLinks
                    .FirstAsync(l => l.SchoolId == schoolId && l.RevokedAt == null, ct);
                return (winner, false);
            }
        }

        public string BuildUrl(string token) =>
            PublicLinks.SubscriptionLink(_waveSettings.PublicBaseUrl, token);
    }
}
