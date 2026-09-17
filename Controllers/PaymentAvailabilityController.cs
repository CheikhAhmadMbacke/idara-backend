using Idara.API.Common.Extensions;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Idara.API.Controllers
{
    /// <summary>État des guichets, tel que le back-office l'affiche et le modifie.</summary>
    public class PaymentAvailabilityDto
    {
        public bool PayinEnabled { get; set; }
        public string? PayinDisabledReason { get; set; }
        public bool PayoutEnabled { get; set; }
        public string? PayoutDisabledReason { get; set; }

        /// <summary>Nom du prestataire en service — information, pas un réglage.</summary>
        public string Provider { get; set; } = PaymentProviders.Wave;
    }

    /// <summary>Corps de la bascule d'un guichet.</summary>
    public class SetPaymentAvailabilityDto
    {
        public bool Enabled { get; set; }

        /// <summary>
        /// Motif affiché aux utilisateurs pendant la fermeture. Facultatif,
        /// mais vivement conseillé : « Wave en maintenance jusqu'à 14 h » se
        /// supporte, « service indisponible » fait appeler l'école.
        /// Ignoré à la réouverture.
        /// </summary>
        [System.ComponentModel.DataAnnotations.StringLength(300)]
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Ouvrir et fermer les guichets d'encaissement et de décaissement, sans
    /// redéploiement.
    ///
    /// <para>🔑 Ces deux interrupteurs existent parce qu'un prestataire annonce
    /// ses interruptions <b>par téléphone</b>. Avant, la seule façon de fermer
    /// était de déployer ; le temps que ça passe, les familles voyaient un
    /// échec technique et appelaient leur école.</para>
    ///
    /// <para>⚠️ L'école de démonstration n'est jamais bloquée : c'est là que se
    /// font les essais à argent réel (§107).</para>
    /// </summary>
    [ApiController]
    [Route("api/admin/payment-availability")]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    public class PaymentAvailabilityController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentAvailabilityController> _logger;

        public PaymentAvailabilityController(
            AppDbContext context, ILogger<PaymentAvailabilityController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PaymentAvailabilityDto>>> Get(CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            return Ok(ApiResponse<PaymentAvailabilityDto>.Ok(new PaymentAvailabilityDto
            {
                PayinEnabled = s.PayinEnabled,
                PayinDisabledReason = s.PayinDisabledReason,
                PayoutEnabled = s.PayoutEnabled,
                PayoutDisabledReason = s.PayoutDisabledReason
            }));
        }

        /// <summary>Ouvre ou ferme les ENCAISSEMENTS (facture, lien, don, recharge, achat de pages).</summary>
        [HttpPut("payin")]
        public Task<ActionResult<ApiResponse<PaymentAvailabilityDto>>> SetPayin(
            [FromBody] SetPaymentAvailabilityDto dto, CancellationToken ct) =>
            SetAsync(payin: true, dto, ct);

        /// <summary>Ouvre ou ferme les DÉCAISSEMENTS (retrait école et retrait des gains).</summary>
        [HttpPut("payout")]
        public Task<ActionResult<ApiResponse<PaymentAvailabilityDto>>> SetPayout(
            [FromBody] SetPaymentAvailabilityDto dto, CancellationToken ct) =>
            SetAsync(payin: false, dto, ct);

        private async Task<ActionResult<ApiResponse<PaymentAvailabilityDto>>> SetAsync(
            bool payin, SetPaymentAvailabilityDto dto, CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();

            if (payin)
            {
                s.PayinEnabled = dto.Enabled;
                // Le motif ne survit pas à la réouverture : le garder ferait
                // réapparaître un message d'il y a trois mois à la fermeture
                // suivante, sans que personne comprenne d'où il sort.
                s.PayinDisabledReason = dto.Enabled ? null : reason;
            }
            else
            {
                s.PayoutEnabled = dto.Enabled;
                s.PayoutDisabledReason = dto.Enabled ? null : reason;
            }

            await _context.SaveChangesAsync(ct);

            _logger.LogWarning(
                "[disponibilité] {Guichet} {Etat} par l'utilisateur {UserId}. Motif : {Motif}",
                payin ? "ENCAISSEMENTS" : "DÉCAISSEMENTS",
                dto.Enabled ? "OUVERTS" : "FERMÉS",
                User.GetUserId(),
                reason ?? "(aucun)");

            return Ok(ApiResponse<PaymentAvailabilityDto>.Ok(new PaymentAvailabilityDto
            {
                PayinEnabled = s.PayinEnabled,
                PayinDisabledReason = s.PayinDisabledReason,
                PayoutEnabled = s.PayoutEnabled,
                PayoutDisabledReason = s.PayoutDisabledReason
            }, dto.Enabled ? "Guichet rouvert." : "Guichet fermé."));
        }
    }
}
