using Idara.API.Enums;
using Idara.API.Services.Alerts;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 🔋 <b>Les crédits Anthropic sont épuisés</b> (2026-10-08).
    ///
    /// <para>Le rechargement automatique est actif (5 $ → 20 $) : passer sous
    /// 5 $ est donc NORMAL, et une clé d'API ne peut de toute façon pas lire le
    /// solde. Le seul événement qui compte est celui-ci : la recharge a échoué
    /// (carte refusée, expirée) et Anthropic refuse tout appel. À ce moment,
    /// l'assistant ET la lecture de cahier s'arrêtent ensemble — d'où un SMS
    /// immédiat à Cheikh, par le canal d'alerte existant (§291).</para>
    /// </summary>
    public static class AnthropicErrors
    {
        /// <summary>
        /// Anthropic répond 400 « Your credit balance is too low to access the
        /// Anthropic API… ». On lit le MESSAGE, toute la chaîne d'exceptions :
        /// le type exact varie selon la version du SDK, la phrase non.
        /// </summary>
        public static bool IsCreditExhausted(Exception? ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var m = e.Message ?? string.Empty;
                if (m.Contains("credit balance", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>L'alerte, regroupée sur 30 min : un SMS, pas un par commande refusée.</summary>
        public static OpsAlertRequest CreditAlert(string where) => new(
            OpsAlertKind.AiCreditsExhausted,
            GroupingKey: "anthropic-credits-exhausted",
            Subject: "IA — crédits Anthropic épuisés : assistant et lecture de cahier à l'arrêt",
            Facts: new List<AlertFact>
            {
                new("Détecté par", where),
                new("Effet", "Toute commande d'assistant et toute lecture de cahier échoue, sans frais pour l'école."),
            },
            Advice: "Le rechargement automatique a sans doute échoué (carte refusée ou expirée). "
                  + "Console Anthropic → Paramètres de l'organisation → Facturation : acheter des crédits "
                  + "et vérifier le moyen de paiement.",
            SmsHeadline: "Credits Anthropic epuises : assistant IA et lecture de cahier a l'arret. Rechargez sur platform.claude.com");
    }
}
