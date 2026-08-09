using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Polices des documents PDF générés par le serveur.
    ///
    /// ⚠️ QuestPDF n'embarque QUE Lato, qui n'a AUCUN glyphe arabe : sans la
    /// police enregistrée ici, tout texte arabe (nom d'un daara, nom d'un
    /// bénéficiaire) s'imprime en carrés — constaté en production le 2026-08-08
    /// sur un reçu de virement. On enregistre donc Noto Naskh Arabic au
    /// démarrage et on la déclare en REPLI du style par défaut : chaque document
    /// garde Lato pour le latin, et bascule automatiquement sur le naskh pour
    /// les caractères que Lato ne sait pas dessiner. Aucun appelant n'a à savoir
    /// dans quelle langue est la chaîne qu'il imprime.
    ///
    /// Les fichiers vivent dans <c>Assets/Fonts/</c> (copiés à la publication,
    /// cf. csproj) et NON dans <c>wwwroot/</c> : ils n'ont aucune raison d'être
    /// servis publiquement.
    /// </summary>
    public static class PdfFonts
    {
        /// <summary>Nom de famille tel que déclaré dans le fichier de police.</summary>
        public const string ArabicFamily = "Noto Naskh Arabic";

        /// <summary>Police latine par défaut de QuestPDF (embarquée par la bibliothèque).</summary>
        public const string LatinFamily = "Lato";

        private static bool _arabicRegistered;

        /// <summary>
        /// true si le repli arabe est réellement disponible. Sert aux tests et au
        /// diagnostic : si c'est false en production, les carrés sont de retour.
        /// </summary>
        public static bool ArabicAvailable => _arabicRegistered;

        /// <summary>
        /// Enregistre les polices au démarrage. Best-effort : un fichier absent
        /// n'empêche JAMAIS l'API de démarrer (on préfère un PDF dégradé à un
        /// service mort), mais il est journalisé en avertissement — c'est le seul
        /// signe qui distingue « police non déployée » de « bug de rendu ».
        /// </summary>
        public static void Register(string contentRootPath, ILogger logger)
        {
            var folder = Path.Combine(contentRootPath, "Assets", "Fonts");
            var files = new[]
            {
                "NotoNaskhArabic-Regular.ttf",
                "NotoNaskhArabic-Bold.ttf"
            };

            var loaded = 0;
            foreach (var file in files)
            {
                var path = Path.Combine(folder, file);
                try
                {
                    if (!File.Exists(path))
                    {
                        logger.LogWarning(
                            "[pdf] Police arabe introuvable : {Path} — les textes arabes des PDF s'imprimeront en carrés",
                            path);
                        continue;
                    }

                    using var stream = File.OpenRead(path);
                    FontManager.RegisterFont(stream);
                    loaded++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[pdf] Échec d'enregistrement de la police {File}", file);
                }
            }

            // La graisse normale suffit à ne plus avoir de carrés ; la grasse
            // n'est qu'un confort typographique. On considère donc le repli
            // disponible dès qu'un fichier est chargé.
            _arabicRegistered = loaded > 0;

            if (_arabicRegistered)
                logger.LogInformation("[pdf] Police arabe enregistrée ({Count} graisse(s)) — repli actif", loaded);
        }

        /// <summary>
        /// Style de base d'un document : latin d'abord, naskh en repli. À appliquer
        /// sur le <c>page.DefaultTextStyle(...)</c> de CHAQUE document — la chaîne
        /// est héritée par tous les textes de la page, donc aucun appelant n'a à
        /// savoir dans quelle écriture est la chaîne qu'il imprime.
        ///
        /// ⚠️ DEUX pièges vérifiés au rendu (2026-08-08), pas déduits :
        ///
        ///  1. <c>Fallback(...)</c> est OBSOLÈTE depuis QuestPDF 2024.3 (même
        ///     famille de piège que <c>WrapAnywhere</c>, cf. §116). C'est
        ///     <c>FontFamily(latin, arabe)</c> qui déclare la chaîne aujourd'hui.
        ///
        ///  2. Le « mécanisme automatique » que la dépréciation recommande FONCTIONNE,
        ///     mais il choisit n'importe quelle police disponible portant le glyphe —
        ///     y compris une police SYSTÈME. Rendu côte à côte, il produit un
        ///     sans-serif système sur Windows là où la chaîne explicite produit bien
        ///     le naskh. Sur le VPS Linux, qui n'a aucune police arabe installée, le
        ///     résultat serait donc différent du poste de développement — au mieux une
        ///     autre typographie, au pire le retour des carrés. On ne s'en remet pas
        ///     au hasard : la famille est nommée.
        /// </summary>
        public static TextStyle Bilingual(this TextStyle style) =>
            style.FontFamily(LatinFamily, ArabicFamily);
    }
}
