using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Idara.API.Controllers
{
    /// <summary>
    /// Les deux documents juridiques d'Idara, servis en HTML par le serveur —
    /// comme la page des tarifs, et pour les mêmes raisons : un lien ouvrable
    /// par n'importe qui, lisible par un moteur de recherche, et dont les
    /// mentions se corrigent sans redéploiement.
    ///
    /// <para>Adresses en français ET en anglais : <c>/cgu</c> et <c>/terms</c>,
    /// <c>/confidentialite</c> et <c>/privacy</c>. La seconde forme existe parce
    /// que les magasins d'applications et les prestataires réclament une
    /// « privacy policy URL » — et que <c>/privacy</c> était déjà l'adresse
    /// historique, déclarée à la Play Console : la casser ferait échouer une
    /// validation qui a déjà été passée.</para>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class LegalPublicController : ControllerBase
    {
        private readonly AppDbContext _context;
        public LegalPublicController(AppDbContext context) => _context = context;

        [HttpGet("/cgu")]
        [HttpGet("/terms")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> Terms(CancellationToken ct)
        {
            var settings = await _context.GetPlatformSettingsAsync(ct);
            return Content(LegalHtmlRenderer.Terms(settings), "text/html; charset=utf-8");
        }

        [HttpGet("/confidentialite")]
        [HttpGet("/privacy")]
        [HttpGet("/politique-de-confidentialite")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> Privacy(CancellationToken ct)
        {
            var settings = await _context.GetPlatformSettingsAsync(ct);
            return Content(LegalHtmlRenderer.Privacy(settings), "text/html; charset=utf-8");
        }

        /// <summary>
        /// `GET /api/public/legal` — la version en vigueur, pour que
        /// l'application sache CE QU'ELLE fait accepter.
        /// </summary>
        /// <remarks>
        /// Sans cet appel, l'application enverrait une version codée en dur : le
        /// jour où les conditions changent, elle continuerait d'enregistrer
        /// l'ancienne — et l'acceptation ne prouverait plus rien.
        /// </remarks>
        [HttpGet("/api/public/legal")]
        public async Task<IActionResult> Version(CancellationToken ct)
        {
            var settings = await _context.GetPlatformSettingsAsync(ct);
            return Ok(new
            {
                version = settings.LegalVersion,
                termsUrl = "/cgu",
                privacyUrl = "/confidentialite"
            });
        }
    }
}
