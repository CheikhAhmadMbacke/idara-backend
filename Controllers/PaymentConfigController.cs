using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Réglages de paiement lisibles par TOUT utilisateur authentifié (parent,
    /// donateur, école) — juste ce qu'il faut pour afficher le % de majoration
    /// courant et prévisualiser le montant. Distinct de
    /// <see cref="PlatformSettingsController"/> (SuperAdmin only, expose tous les
    /// réglages sensibles). Le % est ainsi TOUJOURS à jour côté client, sans
    /// valeur codée en dur ni redéploiement quand le SuperAdmin le change.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/payment-config")]
    public class PaymentConfigController : ControllerBase
    {
        private readonly AppDbContext _context;
        public PaymentConfigController(AppDbContext context) => _context = context;

        /// <summary>
        /// Mensualité type sur laquelle la majoration est évaluée pour
        /// l'affichage. Un montant, pas un taux — voir PaymentConfigDto.
        /// </summary>
        private const long MarkupReferenceFcfa = 10_000;

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PaymentConfigDto>>> Get(CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var fees = s.Fees;
            return Ok(ApiResponse<PaymentConfigDto>.Ok(new PaymentConfigDto
            {
                // Évalué sur une mensualité type. C'est un MONTANT de référence,
                // pas un taux en dur : le pourcentage qui en sort est purement
                // indicatif, et le montant réel est calculé à l'initiation.
                ParentFeePercent = fees.IsConfigured
                    ? Math.Round(fees.EffectiveMarkupPercent(MarkupReferenceFcfa), 2)
                    : 0,
                MarkupReferenceFcfa = MarkupReferenceFcfa,
                FeesConfigured = fees.IsConfigured,
                MinPayinFcfa = s.MinPayinFcfa
            }));
        }
    }
}
