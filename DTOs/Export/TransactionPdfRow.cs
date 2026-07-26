namespace Idara.API.DTOs.Export
{
    /// <summary>
    /// Une ligne de l'export « historique de transactions » en PDF, quel que
    /// soit l'écran d'origine (wallet école, paiements reçus, retraits/virements,
    /// caisse, paiements parent, dons, virements reçus par un bénéficiaire).
    ///
    /// Le rendu est volontairement GÉNÉRIQUE : chaque contrôleur projette ses
    /// entités vers ces mêmes champs, et <see cref="Services.IExportPdfService.BuildTransactionsPdf"/>
    /// produit toujours la même table à 3 colonnes (Date / Détail / Montant).
    /// Les détails secondaires (moyen, référence) sont empilés dans la colonne
    /// « Détail » et passent à la ligne d'eux-mêmes — c'est ce qui évite les
    /// débordements avec les références SenePay, très longues.
    /// </summary>
    public class TransactionPdfRow
    {
        /// <summary>Date du mouvement (UTC). L'heure n'est affichée que si non nulle.</summary>
        public DateTime Date { get; set; }

        /// <summary>Libellé principal : « Paiement mensualité », « Retrait », « Don »…</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Qui/quoi : élève + classe, bénéficiaire, donateur, catégorie de caisse…</summary>
        public string? Subtitle { get; set; }

        /// <summary>
        /// Motif / détails libres de l'opération. Sur sa propre ligne, en italique :
        /// c'est du texte saisi par l'école, souvent long, à distinguer nettement
        /// des informations structurées.
        /// </summary>
        public string? Note { get; set; }

        /// <summary>Moyen : Wave, Orange Money, Espèces…</summary>
        public string? Method { get; set; }

        /// <summary>Référence technique (SenePay, n° de virement). Coupée n'importe où si trop longue.</summary>
        public string? Reference { get; set; }

        /// <summary>Statut lisible : Réussi, En cours, Échoué, Complété…</summary>
        public string? Status { get; set; }

        /// <summary>
        /// Montant SIGNÉ : positif = entrée d'argent (crédit), négatif = sortie.
        /// Le rendu déduit la couleur et le signe de ce seul champ.
        /// </summary>
        public long AmountFcfa { get; set; }
    }
}
