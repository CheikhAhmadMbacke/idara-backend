namespace Idara.API.Services.Assistant
{
    /// <summary>
    /// Tarifs Anthropic par modèle, en DOLLARS par million de tokens
    /// (grille publique, relevée le 2026-10-07).
    ///
    /// <para>🔑 <b>Le coût d'une commande se lit dans le modèle réellement
    /// employé</b>, jamais dans un réglage à tenir à jour à la main : changer de
    /// modèle (Opus → Sonnet) recalcule le coût de lui-même. Seul le TAUX DE
    /// CHANGE est un réglage (<c>PlatformSettings.AiUsdRateFcfa</c>), parce que
    /// lui change — et qu'il se lit sur le relevé de carte, frais compris.</para>
    ///
    /// <para>Un modèle absent de la table est compté au tarif du PLUS CHER :
    /// surestimer un coût protège la marge, le sous-estimer la mange sans bruit.</para>
    /// </summary>
    public static class ModelPricing
    {
        /// <param name="Input">Entrée non mise en cache.</param>
        /// <param name="CacheWrite">Écriture en cache (5 min) = 1,25 × l'entrée.</param>
        /// <param name="CacheRead">Lecture en cache.</param>
        /// <param name="Output">Sortie, réflexion comprise.</param>
        public readonly record struct UsdPerMTok(decimal Input, decimal CacheWrite, decimal CacheRead, decimal Output);

        private static readonly Dictionary<string, UsdPerMTok> Table = new()
        {
            ["claude-opus-5-5"] = new(4.00m, 5.00m, 0.20m, 20.00m),
            ["claude-sonnet-5-5"] = new(2.00m, 2.50m, 0.20m, 10.00m),
            ["claude-opus-5"] = new(5.00m, 6.25m, 0.50m, 25.00m),
            ["claude-haiku-4-5"] = new(1.00m, 1.25m, 0.10m, 5.00m),
        };

        private static readonly UsdPerMTok MostExpensive = new(10.00m, 12.50m, 1.00m, 50.00m);

        public static bool IsKnown(string model) => Table.ContainsKey(model);

        public static UsdPerMTok For(string model) =>
            Table.TryGetValue(model, out var p) ? p : MostExpensive;

        /// <summary>Coût en CENTIMES de FCFA, arrondi au centime supérieur.</summary>
        public static long CostCentimes(
            string model, long input, long cacheWrite, long cacheRead, long output, long usdRateFcfa)
        {
            var p = For(model);
            var usd = (input * p.Input + cacheWrite * p.CacheWrite + cacheRead * p.CacheRead + output * p.Output)
                      / 1_000_000m;
            return (long)Math.Ceiling(usd * usdRateFcfa * 100m);
        }
    }
}
