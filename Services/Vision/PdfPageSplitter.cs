using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Idara.API.Services.Vision
{
    /// <summary>
    /// Découpe un PDF en pages autonomes, pour que la lecture d'un cahier scanné
    /// emprunte EXACTEMENT le chemin d'une photo.
    ///
    /// <para><b>Pourquoi découper plutôt qu'envoyer le PDF entier.</b> Trois
    /// raisons, dans cet ordre d'importance :</para>
    /// <list type="number">
    ///   <item>Le <b>quota</b> se compte en pages. Un PDF envoyé d'un bloc serait
    ///   décompté 1 page alors qu'il en porte 40 : l'école lirait tout son cahier
    ///   pour le prix d'une photo, et le garde-fou de dépense ne verrait rien
    ///   venir.</item>
    ///   <item>La <b>réponse est plafonnée</b> à 32 000 tokens. Quarante pages
    ///   d'un cahier serré dépassent ce plafond : la lecture serait tronquée au
    ///   milieu d'une liste — inexploitable, ET payée.</item>
    ///   <item>Une fois découpé, un PDF n'est plus qu'une suite de pages : le
    ///   découpage par lots, l'ordre des lignes, le comptage des tokens et le
    ///   registre n'ont pas à savoir qu'un PDF existe.</item>
    /// </list>
    ///
    /// <para>PDFsharp est purement managé — pas de dépendance native, donc rien à
    /// installer sur l'ARM du VPS.</para>
    /// </summary>
    public static class PdfPageSplitter
    {
        /// <summary>
        /// Garde-fou de structure, pas de quota : un fichier annonçant des
        /// milliers de pages est soit une erreur, soit une attaque. Le vrai
        /// plafond, celui qui protège l'argent, est <c>OcrMaxPagesPerRequest</c>.
        /// </summary>
        public const int HardMaxPages = 500;

        /// <summary>Levée quand le PDF est illisible, protégé, ou aberrant.</summary>
        public class PdfUnreadableException : Exception
        {
            public PdfUnreadableException(string message) : base(message) { }
        }

        /// <summary>
        /// Nombre de pages. Sert à évaluer le quota AVANT de dépenser quoi que
        /// ce soit.
        ///
        /// <para>Ouvert en mode <c>Import</c> et non <c>InformationOnly</c> :
        /// PDFsharp 6 a déprécié ce dernier, qui n'était pas implémenté et se
        /// comportait déjà comme <c>Import</c>.</para>
        /// </summary>
        public static int CountPages(byte[] pdf)
        {
            using var doc = OpenOrThrow(pdf, PdfDocumentOpenMode.Import);
            return Validate(doc.PageCount);
        }

        /// <summary>
        /// Rend une liste de PDF d'UNE page, dans l'ordre du document.
        /// </summary>
        public static List<byte[]> SplitToSinglePages(byte[] pdf)
        {
            using var source = OpenOrThrow(pdf, PdfDocumentOpenMode.Import);
            var count = Validate(source.PageCount);

            var pages = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                using var single = new PdfDocument();
                single.AddPage(source.Pages[i]);
                using var ms = new MemoryStream();
                // false = ne pas fermer le flux : on veut relire le tampon.
                single.Save(ms, false);
                pages.Add(ms.ToArray());
            }
            return pages;
        }

        // ---------------------------------------------------------------

        private static PdfDocument OpenOrThrow(byte[] pdf, PdfDocumentOpenMode mode)
        {
            try
            {
                // Le flux doit vivre aussi longtemps que le document en mode
                // Import : PDFsharp y relit les pages à la demande. On le confie
                // donc au document plutôt que de le libérer ici.
                var ms = new MemoryStream(pdf, writable: false);
                return PdfReader.Open(ms, mode);
            }
            catch (PdfReaderException ex)
            {
                // Le cas le plus fréquent, et de loin : un PDF protégé par mot de
                // passe. Le dire explicitement évite un aller-retour de support.
                throw new PdfUnreadableException(
                    "Ce PDF n'a pas pu être ouvert. S'il est protégé par un mot de passe, "
                    + "retirez la protection puis réessayez. Message : " + ex.Message);
            }
            catch (Exception ex)
            {
                throw new PdfUnreadableException(
                    "Ce fichier PDF est illisible ou endommagé. Réessayez avec un autre "
                    + "fichier, ou photographiez directement les pages. Message : " + ex.Message);
            }
        }

        private static int Validate(int pageCount)
        {
            if (pageCount <= 0)
                throw new PdfUnreadableException("Ce PDF ne contient aucune page.");
            if (pageCount > HardMaxPages)
                throw new PdfUnreadableException(
                    $"Ce PDF contient {pageCount} pages — c'est trop pour une seule lecture. "
                    + "Découpez-le, ou envoyez les pages qui portent la liste.");
            return pageCount;
        }
    }
}
