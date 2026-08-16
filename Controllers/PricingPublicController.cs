using Idara.API.Common.Utilities;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Subscription;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Page publique des tarifs — le lien qu'on envoie à un daara qui demande
    /// combien ça coûte. Anonyme par nature : un prospect n'a pas de compte.
    ///
    /// Le HTML est rendu côté serveur (cf. <see cref="PricingHtmlRenderer"/>) et
    /// les données viennent de la base : changer un prix, une accroche ou un
    /// avantage depuis le back-office suffit, sans redéploiement.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class PricingPublicController : ControllerBase
    {
        private readonly IPricingPageService _pricing;
        public PricingPublicController(IPricingPageService pricing) => _pricing = pricing;

        /// <summary>La page elle-même : https://idara.sn/plans (ou ?lang=ar).</summary>
        [HttpGet("/plans")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> Page([FromQuery] string? lang, CancellationToken ct)
        {
            var data = await _pricing.GetPublicAsync(ct);

            // Page dépubliée : 404 franc plutôt qu'une grille de tarifs vide —
            // une page « en travaux » vaut mieux qu'une page qui dit n'importe
            // quoi à un prospect.
            if (!data.Content.IsPublished) return NotFound();

            var html = PricingHtmlRenderer.Render(data, lang ?? "fr");
            return Content(html, "text/html; charset=utf-8");
        }

        /// <summary>
        /// Mêmes données en JSON (site vitrine futur, application, partenaire).
        /// Ne renvoie QUE des plans publics listés.
        /// </summary>
        [HttpGet("/api/public/pricing")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
        public async Task<ActionResult<ApiResponse<PublicPricingDto>>> Json(CancellationToken ct)
        {
            var data = await _pricing.GetPublicAsync(ct);
            if (!data.Content.IsPublished) return NotFound();
            return Ok(ApiResponse<PublicPricingDto>.Ok(data));
        }
    }
}
