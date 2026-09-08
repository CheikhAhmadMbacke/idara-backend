using Idara.API.Common.Extensions;
using Idara.API.Data;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Vision
{
    /// <summary>Ce que coûte une page de lecture à CETTE école.</summary>
    /// <param name="PricePerPageFcfa">Le prix unitaire, seul nombre montré à l'école.</param>
    /// <param name="StudentsPerPage">Élèves par page mesurés sur son cahier.</param>
    /// <param name="Calibrated">
    /// Vrai si le prix vient de SON cahier. Faux = repli sur la moyenne, et il
    /// faut alors le DIRE plutôt que d'annoncer un prix comme s'il était établi.
    /// </param>
    /// <param name="MeasuredOnPages">Pages ayant servi à la mesure.</param>
    /// <param name="PurchaseEnabled">La vente est-elle ouverte.</param>
    /// <param name="MaxPagesPerPurchase">Borne haute d'un achat.</param>
    public record OcrPriceQuote(
        long PricePerPageFcfa,
        int StudentsPerPage,
        bool Calibrated,
        int MeasuredOnPages,
        bool PurchaseEnabled,
        int MaxPagesPerPurchase);

    public interface IOcrPricingService
    {
        Task<OcrPriceQuote> QuoteAsync(int schoolId, CancellationToken ct = default);
    }

    /// <summary>
    /// Calcule le prix d'une page pour une école donnée.
    ///
    /// <para><b>Les pages offertes ne sont pas un cadeau : elles MESURENT le
    /// cahier.</b> C'est la clé du modèle. Avant d'avoir lu quoi que ce soit
    /// chez une école, on ne sait pas si son cahier tient 24 élèves par page ou
    /// un seul — et l'écart de coût entre les deux est d'un facteur quatre.
    /// Annoncer un prix avant de savoir, c'est se tromper dans un sens ou dans
    /// l'autre : soit on ruine le petit daara, soit on travaille à perte.</para>
    ///
    /// <para>D'où la règle d'affichage : <b>tant que rien n'est mesuré, on
    /// n'annonce pas un prix, on annonce un plancher</b> — « à partir de N F la
    /// page, vos pages offertes nous le diront ».</para>
    /// </summary>
    public class OcrPricingService : IOcrPricingService
    {
        private readonly AppDbContext _db;

        public OcrPricingService(AppDbContext db) => _db = db;

        public async Task<OcrPriceQuote> QuoteAsync(int schoolId, CancellationToken ct = default)
        {
            var p = await _db.GetPlatformSettingsAsync(ct);

            // On ne mesure que sur des lectures RÉUSSIES et DÉCOMPTÉES : un
            // échec n'a rien produit, et une lecture non décomptée (page
            // illisible) ne dit rien du cahier.
            var measured = await _db.OcrJobs
                .Where(j => j.SchoolId == schoolId && j.Success && j.ChargedPages > 0)
                .GroupBy(j => 1)
                .Select(g => new
                {
                    Pages = g.Sum(j => j.ChargedPages),
                    Rows = g.Sum(j => j.ExtractedRows),
                })
                .FirstOrDefaultAsync(ct);

            var calibrated = measured != null && measured.Pages > 0 && measured.Rows > 0;

            // Arrondi au supérieur : une page qui porte 24,4 élèves en porte 25
            // à payer. Un demi-élève ne se facture pas, et arrondir vers le bas
            // ferait travailler à perte sur exactement les cahiers les plus
            // denses — ceux qui coûtent le plus cher à produire.
            var perPage = calibrated
                ? (int)Math.Ceiling((double)measured!.Rows / measured.Pages)
                : p.OcrDefaultStudentsPerPage;

            // Borne basse à 1 : une page sans aucun élève n'existe pas dans un
            // cahier, mais une lecture ratée peut la produire. Sans cette borne,
            // le prix tomberait à la part fixe seule.
            perPage = Math.Max(1, perPage);

            return new OcrPriceQuote(
                PricePerPageFcfa: p.OcrPriceBaseFcfa + p.OcrPricePerStudentFcfa * perPage,
                StudentsPerPage: perPage,
                Calibrated: calibrated,
                MeasuredOnPages: measured?.Pages ?? 0,
                PurchaseEnabled: p.OcrPurchaseEnabled && p.OcrEnabled,
                MaxPagesPerPurchase: p.OcrMaxPagesPerPurchase);
        }
    }
}
