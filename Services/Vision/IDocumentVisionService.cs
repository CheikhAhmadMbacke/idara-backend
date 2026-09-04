using Idara.API.Common.Utilities;
using Idara.API.Enums;

namespace Idara.API.Services.Vision
{
    /// <summary>Une image de cahier, telle qu'elle arrive du téléphone.</summary>
    /// <param name="Bytes">Contenu binaire, déjà redimensionné côté téléphone.</param>
    /// <param name="MediaType">« image/jpeg », « image/png »…</param>
    public record VisionImage(byte[] Bytes, string MediaType);

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
        /// Lit les images et rend le tableau. Les pages partent dans UN SEUL
        /// appel : le modèle voit l'en-tête une fois et garde la même
        /// disposition sur toutes les pages — une page 2 sans en-tête serait
        /// illisible envoyée seule.
        /// </summary>
        Task<VisionReadResult> ReadAsync(
            IReadOnlyList<VisionImage> images, ImportKind kind, CancellationToken ct = default);
    }
}
