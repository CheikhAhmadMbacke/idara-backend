using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Enums;
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

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PaymentConfigDto>>> Get(CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var fees = s.Fees;
            return Ok(ApiResponse<PaymentConfigDto>.Ok(new PaymentConfigDto
            {
                // 🔑 Le taux NOMINAL du prestataire — « 1 % » — et non la
                // majoration effective d'une mensualité type, qui valait
                // « 1,01 % » et faisait buter le lecteur sur une décimale qui
                // ne lui apprend rien. Décision de Cheikh le 2026-09-18 : on
                // annonce le taux rond, et c'est le MONTANT des frais, exact au
                // franc, qui porte la vérité. `PayinRatePercent` est la source
                // unique de tout taux d'encaissement affiché (§277) : CGU, page
                // des tarifs et écrans lisent désormais le même chiffre.
                ParentFeePercent = fees.PayinRatePercent is double taux
                    ? Math.Round(taux, 2)
                    : 0,
                FeesConfigured = fees.IsConfigured,
                MinPayinFcfa = s.MinPayinFcfa
            }));
        }

        /// <summary>
        /// Ce que coûte réellement un encaissement de <paramref name="target"/>,
        /// au franc près : montant visé, frais, montant à débiter.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>Il existe pour que le client n'ait JAMAIS à calculer des frais.</b>
        /// Les écrans à montant libre (recharge, don, paiement libre) affichaient
        /// un pourcentage sans montant, et la page publique du lien de paiement
        /// allait jusqu'à recomposer <c>ceil(montant × (1 + taux/100))</c> en
        /// JavaScript — une seconde implémentation d'une règle qui ne se
        /// multiplie pas mais se RÉSOUT (§256), donc fausse de quelques francs
        /// et divergente le jour où la grille change.
        ///
        /// <para>🔑 Le devis répond pour le mode « frais au payeur ». L'appelant
        /// ne l'affiche que lorsque l'école a choisi ce mode — et de toute façon
        /// c'est l'initiation, côté serveur, qui fixe ce qui sera réellement
        /// débité.</para>
        /// </remarks>
        [HttpGet("quote")]
        public async Task<ActionResult<ApiResponse<PaymentQuoteDto>>> Quote(
            [FromQuery] long target, CancellationToken ct)
        {
            var s = await _context.GetPlatformSettingsAsync(ct);
            var fees = s.Fees;
            if (target < 0) target = 0;

            var charge = PayerMarkup.ChargeFor(fees, FeesPayer.Parent, target);
            return Ok(ApiResponse<PaymentQuoteDto>.Ok(new PaymentQuoteDto
            {
                TargetFcfa = target,
                ChargeFcfa = charge,
                FeesFcfa = charge - target,
                FeesConfigured = fees.IsConfigured,
            }));
        }
    }
}
