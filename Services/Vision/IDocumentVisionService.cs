using Idara.API.Common.Utilities;
using Idara.API.Enums;

namespace Idara.API.Services.Vision
{
    /// <summary>
    /// Un fichier tel qu'il arrive du téléphone : soit une photo (une page), soit
    /// un PDF (autant de pages qu'il en contient).
    ///
    /// <para><b>Fichier et page sont deux choses distinctes, et c'est toute la
    /// difficulté du PDF.</b> Une photo, c'est un fichier = une page. Un ancien
    /// cahier scanné, c'est un fichier = quarante pages. Or le quota, le prix et
    /// le découpage des appels se comptent en PAGES. Confondre les deux ferait
    /// lire quarante pages au prix d'une, sans que le garde-fou de dépense y
    /// voie quoi que ce soit.</para>
    /// </summary>
    /// <param name="Bytes">Contenu binaire, déjà validé par signature (§216).</param>
    /// <param name="MediaType">« image/jpeg », « image/png », « application/pdf »…</param>
    public record VisionFile(byte[] Bytes, string MediaType)
    {
        public bool IsPdf => string.Equals(MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// UNE page prête à partir à l'IA. Une photo telle quelle, ou une page
    /// extraite d'un PDF. À partir d'ici, plus rien dans la chaîne n'a besoin de
    /// savoir qu'un PDF a existé.
    /// </summary>
    public record VisionPage(byte[] Bytes, string MediaType)
    {
        public bool IsPdf => string.Equals(MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ce que l'IA a lu.
    /// </summary>
    /// <param name="Table">
    /// Le tableau, dans le vocabulaire EXACT du modèle Excel. C'est ce qui
    /// permet à tout l'import existant de fonctionner sans une ligne de plus.
    /// </param>
    /// <param name="Uncertain">
    /// Cellules dont le modèle n'est pas sûr, en (ligne, colonne) — indices
    /// dans <paramref name="Table"/>. C'est ce qui transforme « relisez
    /// 200 lignes » en « vérifiez ces 6 cases ».
    /// </param>
    /// <param name="InputTokens">Tokens consommés en entrée (images + consigne).</param>
    /// <param name="OutputTokens">Tokens produits.</param>
    /// <param name="Model">Modèle réellement employé.</param>
    /// <param name="DurationMs">Durée de l'appel.</param>
    public record VisionReadResult(
        SheetTable Table,
        IReadOnlyList<(int Row, int Col)> Uncertain,
        int InputTokens,
        int OutputTokens,
        string Model,
        int DurationMs);

    /// <summary>
    /// Lit un cahier photographié et rend un tableau.
    ///
    /// <para><b>La décision d'architecture qui commande tout le reste :</b> ce
    /// service ne crée PAS un second chemin d'import. Il remplace uniquement
    /// <see cref="SheetReader.Read(byte[])"/>. En sortie, un
    /// <see cref="SheetTable"/> — exactement ce que produit la lecture d'un
    /// .xlsx. Tout l'aval (parseur indulgent-sur-la-forme, dédoublonnage,
    /// aperçu avant écriture, confirmation, <c>CreateStudentAsync</c>) est celui
    /// de l'import Excel, inchangé. C'est ce qui garantit qu'un élève
    /// photographié et un élève importé d'Excel ne pourront jamais diverger.</para>
    ///
    /// <para>NO-OP si la clé n'est pas configurée (§89) : lève une
    /// <see cref="InvalidOperationException"/> avec un message destiné au
    /// directeur, plutôt que d'échouer en 500.</para>
    /// </summary>
    public interface IDocumentVisionService
    {
        /// <summary>Vrai si la lecture par IA est configurée sur cette instance.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Lit les pages et rend le tableau. Photos et pages de PDF se mélangent
        /// librement : à ce niveau, ce ne sont plus que des pages.
        /// </summary>
        Task<VisionReadResult> ReadAsync(
            IReadOnlyList<VisionPage> pages, ImportKind kind, CancellationToken ct = default);
    }
}
