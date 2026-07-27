namespace Idara.API.DTOs.Export
{
    /// <summary>
    /// Une ligne de l'export « historique de transactions » en PDF, quel que
    /// soit l'écran d'origine (wallet école, paiements reçus, retraits/virements,
    /// caisse, paiements parent, dons, virements reçus par un bénéficiaire).
    ///
    /// Le rendu est volontairement GÉNÉRIQUE : chaque contrôleur projette ses
    /// entités vers ces mêmes champs, et <see cref="Services.IExportPdfService.BuildTransactionsPdf"/>
    /// produit toujours la même table, en PAYSAGE, avec UNE COLONNE PAR
    /// INFORMATION (date, type, statut, qui, moyen, référence, montant) — de quoi
    /// trier et lire le document comme un tableur (demande du 2026-07-27, qui
    /// remplace la table à 3 colonnes empilées d'origine).
    ///
    /// La référence SenePay (~68 caractères sans espace) a sa propre colonne et
    /// s'y replie sur plusieurs lignes : QuestPDF coupe seul un bloc sans espace
    /// depuis 2024.3, donc elle ne déborde jamais.
    /// </summary>
    public class TransactionPdfRow
    {
        /// <summary>Date du mouvement (UTC). L'heure n'est affichée que si non nulle.</summary>
        public DateTime Date { get; set; }

        /// <summary>Libellé principal : « Paiement mensualité », « Retrait », « Don »…</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// LA CONTREPARTIE : qui est en face de l'opération — élève + classe,
        /// bénéficiaire payé, donateur, daara, catégorie de caisse.
        ///
        /// <para>Payeur et bénéficiaire ne sont pas deux informations distinctes
        /// mais le MÊME rôle vu des deux sens : une seule colonne suffit, comme
        /// sur un relevé bancaire. C'est la colonne « Type » qui lève l'ambiguïté
        /// (un « Transfert » désigne un bénéficiaire, un « Paiement » un payeur).
        /// Deux colonnes payeur/bénéficiaire seraient à moitié vides sur chaque
        /// ligne. L'INTITULÉ de la colonne, lui, s'adapte à l'export via le
        /// paramètre <c>counterpartyHeader</c> de BuildTransactionsPdf.</para>
        /// </summary>
        public string? Subtitle { get; set; }

        /// <summary>
        /// Numéro de téléphone de la contrepartie (bénéficiaire d'un virement).
        /// Sa propre colonne, affichée seulement si au moins une ligne en a un —
        /// il était auparavant concaténé au nom (« Nom - 77 123 45 67 »), ce qui
        /// empêchait de trier ou de filtrer sur l'un ou l'autre.
        /// </summary>
        public string? Phone { get; set; }

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
        /// Solde après l'opération, déjà formaté (historique du wallet uniquement).
        /// Rendu dans sa propre colonne, et seulement si au moins une ligne en a
        /// une : les autres exports n'affichent pas de colonne vide.
        /// </summary>
        public string? Balance { get; set; }

        /// <summary>
        /// Montant SIGNÉ : positif = entrée d'argent (crédit), négatif = sortie.
        /// Le rendu déduit la couleur et le signe de ce seul champ.
        /// </summary>
        public long AmountFcfa { get; set; }
    }
}
