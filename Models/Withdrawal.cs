using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Retrait école → compte Mobile Money via SenePay Payout. Append-only
    /// (entité financière, pas de soft-delete — cf. gotcha §55). Une correction
    /// = une nouvelle écriture wallet, jamais un update rétroactif du montant.
    ///
    /// Modèle de frais : l'école retire <see cref="AmountFcfa"/> = ce qu'elle
    /// voit dans son wallet = ce que le bénéficiaire reçoit, à l'unité près.
    ///
    /// <para>🔴 <b>On envoie à SenePay le montant EXACT</b>, jamais un montant
    /// majoré : <c>fee_mode = "on_top"</c> fait prélever les 1,77 % <b>en
    /// plus</b>, sur la réserve marchand. Le commentaire d'origine décrivait ici
    /// une majoration <c>AmountFcfa / (1 − 0,0177)</c> qui n'existe plus depuis
    /// le modèle de frais SenePay 2026 (elle sur-versait le bénéficiaire :
    /// retrait de 500, 510 reçus). Cette description périmée a survécu assez
    /// longtemps pour fausser le calcul de la majoration au payeur — d'où sa
    /// réécriture le 2026-09-13.</para>
    ///
    /// <para>Conséquence, et c'est elle qui commande
    /// <c>PlatformSettings.ParentFeeMultiplier</c> : sortir T de la réserve en
    /// coûte <c>T × (1 + 0,0177)</c>, et non <c>T / (1 − 0,0177)</c>.</para>
    /// </summary>
    public class Withdrawal
    {
        public int Id { get; set; }

        /// <summary>
        /// École propriétaire du retrait. NULL pour un retrait de GAINS PLATEFORME
        /// (<see cref="IsPlatform"/> = true), qui ne débite aucun wallet école : la
        /// plateforme puise dans ses propres gains (solde P dérivé).
        /// </summary>
        public int? SchoolId { get; set; }
        public School? School { get; set; }

        /// <summary>
        /// true = retrait des gains plateforme (SuperAdmin), pas d'école. Le
        /// règlement (§78) branche sur ce flag : côté plateforme, simple transition
        /// de statut (P étant recalculé, aucun mouvement de wallet).
        /// </summary>
        public bool IsPlatform { get; set; }

        /// <summary>Ce que TOUCHE le bénéficiaire (FCFA). C'est le montant demandé,
        /// et celui que Wave reçoit en <c>receive_amount</c>.</summary>
        /// <remarks>
        /// 🔴 <b>Ce n'est plus ce qui sort du portefeuille</b> depuis le
        /// 2026-09-17 : les frais de décaissement sont prélevés EN SUS (§274),
        /// donc le débit vaut <see cref="WalletDebitedFcfa"/> = ce montant + les
        /// frais. Avant cette date les deux étaient confondus, la sortie étant
        /// provisionnée dès l'encaissement.
        /// </remarks>
        public long AmountFcfa { get; set; }

        /// <summary>
        /// 🔑 <b>Ce qui sort réellement du portefeuille</b> = <see cref="AmountFcfa"/>
        /// + les frais de décaissement.
        /// </summary>
        /// <remarks>
        /// <para>Rempli à la RÉSERVATION avec les frais estimés, puis ajusté au
        /// règlement sur le <c>fee</c> réellement renvoyé par Wave — c'est lui
        /// qui fait foi, jamais notre estimation. L'écart d'arrondi éventuel
        /// donne une écriture d'ajustement, jamais une réécriture (§55).</para>
        ///
        /// <para>⚠️ <b>Les retraits d'AVANT le 2026-09-17 portent ici leur
        /// <see cref="AmountFcfa"/></b>, reposé par la migration : sous l'ancien
        /// modèle, le débit valait bien le montant reçu, la sortie ayant été
        /// provisionnée à l'encaissement. L'historique dit donc vrai, et aucune
        /// lecture n'a besoin d'un discriminant.</para>
        /// </remarks>
        public long WalletDebitedFcfa { get; set; }

        /// <summary>Montant réellement envoyé à SenePay. Depuis le modèle de frais
        /// 2026 il est ÉGAL à <see cref="AmountFcfa"/> (les frais sont en <c>on_top</c>).
        /// Colonne conservée : les retraits d'avant portent bien un montant majoré.</summary>
        public long SepayAmountFcfa { get; set; }

        /// <summary>Frais opérateur prélevés (rempli au webhook depuis fees.provider).</summary>
        public long FeesFcfa { get; set; }

        /// <summary>Net effectivement reçu par le bénéficiaire (webhook net_amount). ≈ AmountFcfa.</summary>
        public long NetReceivedFcfa { get; set; }

        public PaymentOperator Operator { get; set; }

        /// <summary>
        /// Nature du transfert (retrait simple, salaire, loyer…). Par défaut
        /// Withdrawal (= ancien comportement « retrait »), rétro-compatible avec
        /// les lignes existantes (valeur 0).
        /// </summary>
        public TransferCategory Category { get; set; } = TransferCategory.Withdrawal;

        /// <summary>
        /// Libellé libre saisi quand <see cref="Category"/> == Other (sinon null).
        /// Permet à l'école de nommer une nature de transfert hors liste fixe.
        /// ⚠️ C'est le NOM d'une nature (« Ndogou »), PAS le motif détaillé de
        /// l'opération — celui-ci va dans <see cref="Motif"/>.
        /// </summary>
        public string? CategoryLabel { get; set; }

        /// <summary>
        /// Motif / détails libres de CE transfert précis (« avance pour l'achat
        /// de 3 tablettes »). Optionnel. Séparé de la catégorie exprès : sans ce
        /// champ, les écoles écrivaient le motif dans le libellé de catégorie
        /// « Autre », ce qui polluait l'affichage des listes.
        /// Affiché uniquement dans le détail de la transaction, le reçu et les exports.
        /// </summary>
        public string? Motif { get; set; }

        /// <summary>
        /// Bénéficiaire du carnet utilisé (null = saisie ponctuelle). On snapshote
        /// quand même Name/Phone ci-dessous pour figer l'historique même si le
        /// bénéficiaire est modifié/archivé ensuite.
        /// </summary>
        public int? BeneficiaryId { get; set; }
        public TransferBeneficiary? Beneficiary { get; set; }

        /// <summary>Coordonnées bénéficiaire (saisie manuelle OU copiées du carnet, figées ici).</summary>
        public string RecipientName { get; set; } = string.Empty;

        /// <summary>Numéro national 9 chiffres (sans indicatif). On préfixe "221" à l'appel SenePay.</summary>
        public string RecipientPhone { get; set; } = string.Empty;

        /// <summary>
        /// Poche du wallet dans laquelle ce retrait puise (décision produit : le
        /// daara choisit). Total par défaut (rétro-compatible : les retraits
        /// existants puisaient dans le total). Cf. <see cref="DonationAmountFcfa"/>.
        /// </summary>
        public WithdrawalSource Source { get; set; } = WithdrawalSource.Total;

        /// <summary>
        /// Part de <see cref="AmountFcfa"/> effectivement prélevée sur la poche
        /// « Don » (<see cref="SchoolWallet.DonationBalanceFcfa"/>), figée à la
        /// réservation. Sert à restituer EXACTEMENT cette part si le retrait échoue
        /// (et à la re-débiter sur le correcteur `completed`-après-`Failed`). 0 pour
        /// un retrait 100 % sur le solde paiement (dont tous les retraits existants).
        /// </summary>
        public long DonationAmountFcfa { get; set; }

        public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Initiated;

        /// <summary>
        /// Prestataire qui a traité ce mouvement : <c>"Wave"</c> depuis la
        /// migration du 2026-09-17, <c>"SenePay"</c> pour tout l'historique.
        /// <para>🔑 Ce n'est pas une décoration : les deux prestataires n'ont ni
        /// les mêmes identifiants, ni les mêmes états, ni la même façon d'être
        /// interrogés. Sans ce discriminant, le travail de vérification
        /// demanderait à Wave des nouvelles d'un paiement qu'il n'a jamais vu.</para>
        /// </summary>
        public string Provider { get; set; } = "Wave";

        /// <summary>
        /// Identifiant du décaissement chez le prestataire (<c>pt-…</c> chez
        /// Wave), rempli après l'appel. 🔴 Chez Wave, AUCUN webhook n'annonce
        /// l'issue d'un décaissement : cet identifiant est le seul moyen d'en
        /// obtenir des nouvelles.
        /// </summary>
        public string? ProviderDisbursementId { get; set; }

        /// <summary>SchoolAdmin qui a initié le retrait (audit).</summary>
        public int InitiatedById { get; set; }

        public string? FailureReason { get; set; }

        // --- Suivi de la vérification d'état (statut UnderVerification) ---
        // Quand l'issue du payout est indéterminée (timeout/5xx du POST, ou
        // statut non terminal), on garde les fonds réservés et on interroge
        // GET /payouts/{id} (autoritatif) avec back-off jusqu'à un état terminal.

        /// <summary>Nombre de tentatives de poll GET /payouts/{id} déjà effectuées.</summary>
        public int VerificationAttempts { get; set; }

        /// <summary>Premier passage en UnderVerification (sert au seuil StuckUnderVerification 48h).</summary>
        public DateTime? VerificationStartedAt { get; set; }

        /// <summary>Prochain poll dû (back-off : 30s → 1min → 5min → 15min → cap 1h).</summary>
        public DateTime? NextVerificationAt { get; set; }

        /// <summary>Dernier poll GET status effectué (terminal ou non).</summary>
        public DateTime? LastCheckedAt { get; set; }

        /// <summary>
        /// Horodatage d'un re-débit correcteur : un `completed` est arrivé après
        /// qu'on ait restitué et marqué Failed → on a annulé la restitution
        /// (scénario S3 / point D du durcissement). null = pas de correction.
        /// </summary>
        public DateTime? ReversedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }

        /// <summary>
        /// Masqué de l'affichage école (cosmétique, comme Payment/WalletTransaction) :
        /// le daara ne veut plus voir cette ligne (souvent un échec) dans son
        /// historique de retraits. NON destructif — la ligne reste en base (aucun
        /// impact compta/réconciliation), elle sort seulement des listes.
        /// </summary>
        public bool IsHidden { get; set; }
    }
}
