using Idara.API.Constants;
using Idara.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Le guichet est-il ouvert ? Point UNIQUE interrogé par tout chemin qui
    /// fait entrer ou sortir de l'argent.
    ///
    /// <para>🔑 Pourquoi un service et pas un <c>if</c> dans chaque contrôleur :
    /// il y a <b>cinq</b> portes d'encaissement (facture, lien de paiement, don
    /// depuis un compte, don public, recharge, achat de pages) et <b>deux</b> de
    /// décaissement. Une porte oubliée, et le guichet reste ouvert là où
    /// personne ne regarde — ce qui est pire que pas de fermeture du tout,
    /// puisqu'on la croit fermée.</para>
    ///
    /// <para>⚠️ <b>L'école de démonstration passe toujours.</b> Fermer le
    /// guichet sert précisément à faire des essais à argent réel pendant que
    /// les vraies écoles attendent (§107) ; la bloquer aussi rendrait
    /// l'interrupteur inutilisable pour ce à quoi il sert d'abord.</para>
    /// </summary>
    public interface IPaymentAvailabilityService
    {
        /// <summary>
        /// <c>null</c> si l'encaissement est autorisé ; sinon le message à
        /// afficher à l'utilisateur, tel quel.
        /// </summary>
        Task<string?> PayinBlockedReasonAsync(int? schoolId, CancellationToken ct = default);

        /// <summary><c>null</c> si le décaissement est autorisé ; sinon le message à afficher.</summary>
        Task<string?> PayoutBlockedReasonAsync(int? schoolId, CancellationToken ct = default);
    }

    public class PaymentAvailabilityService : IPaymentAvailabilityService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentAvailabilityService> _logger;

        /// <summary>
        /// Messages par défaut. Ils disent « momentanément » et « réessayez »
        /// parce qu'une indisponibilité de prestataire dure des heures, pas des
        /// semaines — et parce qu'un message définitif fait appeler l'école.
        /// </summary>
        public const string DefaultPayinMessage =
            "Les paiements en ligne sont momentanément indisponibles. Réessayez dans quelques instants.";

        public const string DefaultPayoutMessage =
            "Les retraits sont momentanément indisponibles. Réessayez dans quelques instants.";

        public PaymentAvailabilityService(AppDbContext context, ILogger<PaymentAvailabilityService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public Task<string?> PayinBlockedReasonAsync(int? schoolId, CancellationToken ct = default) =>
            BlockedReasonAsync(schoolId, payin: true, ct);

        public Task<string?> PayoutBlockedReasonAsync(int? schoolId, CancellationToken ct = default) =>
            BlockedReasonAsync(schoolId, payin: false, ct);

        private async Task<string?> BlockedReasonAsync(int? schoolId, bool payin, CancellationToken ct)
        {
            var settings = await _context.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            // Pas de réglages en base : on n'invente pas une fermeture. Le
            // guichet reste ouvert — une plateforme neuve doit encaisser.
            if (settings is null) return null;

            var enabled = payin ? settings.PayinEnabled : settings.PayoutEnabled;
            if (enabled) return null;

            if (await IsDemoSchoolAsync(schoolId, ct))
            {
                _logger.LogInformation(
                    "[disponibilité] Guichet {Kind} fermé, mais l'école de démonstration passe (School {SchoolId})",
                    payin ? "encaissement" : "décaissement", schoolId);
                return null;
            }

            var reason = payin ? settings.PayinDisabledReason : settings.PayoutDisabledReason;
            return string.IsNullOrWhiteSpace(reason)
                ? (payin ? DefaultPayinMessage : DefaultPayoutMessage)
                : reason.Trim();
        }

        private async Task<bool> IsDemoSchoolAsync(int? schoolId, CancellationToken ct)
        {
            if (schoolId is not int id) return false;
            return await _context.Schools
                .AsNoTracking()
                .AnyAsync(s => s.Id == id && s.Name == DemoAccounts.SchoolName, ct);
        }
    }
}
