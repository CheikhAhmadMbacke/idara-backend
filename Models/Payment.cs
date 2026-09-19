using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Un paiement réel transitant via SenePay. Cas couverts :
    /// - paiement parent → école en mode FixedAmount (avec InvoiceId),
    /// - paiement parent → école en mode FreeAmount (InvoiceId NULL),
    /// - topup wallet école par le SchoolAdmin (Phase 4) : StudentId / GuardianId
    ///   / InvoiceId tous NULL ; SchoolId reste obligatoire.
    ///
    /// Status Pending = créé en DB, attendant le webhook SenePay. Les transitions
    /// terminales (Completed/Failed/...) ne reviennent jamais en arrière.
    ///
    /// AmountFcfa = montant débité du payeur (majoré si FeesPayer = Parent — le
    /// taux vient de PlatformSettings.ParentFeeMultiplier, ~7,55 %, et n'est
    /// écrit en dur nulle part).
    /// NetCreditedFcfa = ce que touche réellement le SchoolWallet (net de TOUT,
    /// y compris la réserve payout — modèle "prélèvement à la source").
    /// </summary>
    public class Payment
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public int? StudentId { get; set; }
        public Student? Student { get; set; }

        public int? GuardianId { get; set; }
        public User? Guardian { get; set; }

        public int? InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }

        /// <summary>
        /// Lignes d'allocation pour un paiement CONSOLIDÉ (« paiement global »
        /// d'un parent réglant plusieurs enfants en une fois). Dans ce cas
        /// <see cref="InvoiceId"/> est null et ces lignes portent les N factures
        /// réglées. Vide pour un paiement mono-facture, un topup ou un don.
        /// </summary>
        public List<PaymentInvoiceAllocation> InvoiceAllocations { get; set; } = new();

        /// <summary>
        /// Ventilation par enfant d'un paiement en MONTANT LIBRE initié depuis un
        /// lien de paiement (aucune facture → pas d'allocation par facture).
        /// Vide partout ailleurs.
        /// </summary>
        public List<PaymentStudentAllocation> StudentAllocations { get; set; } = new();

        /// <summary>
        /// Lien de paiement (WhatsApp, sans connexion) à l'origine de ce paiement.
        /// Null pour un paiement initié depuis l'app. Sert à mesurer « payé via
        /// lien » et à afficher dans le roster que le lien a été utilisé.
        /// </summary>
        public int? PaymentLinkId { get; set; }
        public PaymentLink? PaymentLink { get; set; }

        /// <summary>
        /// Nature métier du paiement (mensualité / topup / don). Explicite depuis
        /// l'ajout du module Donateur : le webhook payin ne DEVINE plus le topup
        /// d'après les champs null (un don a AUSSI Student/Guardian null, il
        /// collisionnait). Défaut SchoolFee = rétro-compatible.
        /// </summary>
        public PaymentPurpose Purpose { get; set; } = PaymentPurpose.SchoolFee;

        /// <summary>
        /// DONATEUR à l'origine du paiement quand <see cref="Purpose"/> == Donation
        /// (null sinon). Pointe vers un <c>User</c> de rôle Donor. Le don crédite
        /// la poche « Don » du wallet école ; l'identité (nom + type) est visible
        /// par le daara.
        /// </summary>
        public int? DonorId { get; set; }
        public User? Donor { get; set; }

        /// <summary>
        /// Pages de lecture achetées, quand <see cref="Purpose"/> ==
        /// <c>OcrPages</c> (zéro partout ailleurs). FIGÉ à l'initiation : entre
        /// le clic et le webhook, l'école a pu faire lire d'autres pages, donc
        /// changer sa calibration et son prix. Elle doit recevoir ce qu'elle a
        /// acheté, pas ce que le tarif du moment lui donnerait.
        /// </summary>
        public int OcrPagesPurchased { get; set; }

        /// <summary>
        /// Prix unitaire figé de ces pages. Gardé en plus du total parce qu'une
        /// facture doit se LIRE : « 50 pages à 102 F » se vérifie, « 5 100 » ne
        /// se vérifie pas.
        /// </summary>
        public long OcrPricePerPageFcfa { get; set; }

        /// <summary>
        /// Facture d'abonnement que ce paiement solde
        /// (<see cref="Enums.PaymentPurpose.Subscription"/> uniquement).
        /// </summary>
        /// <remarks>
        /// 🔑 Figée au démarrage du paiement, et pas retrouvée au règlement.
        /// Le montant d'une facture en attente est réactualisé à chaque nouvelle
        /// tentative du cron (les SMS refacturés sont réagrégés) : sans ce lien,
        /// un webhook qui arrive après un passage du cron pourrait solder une
        /// facture qui n'est plus celle que l'école a vue et payée. Une référence
        /// doit permettre de RETROUVER l'opération (§136).
        /// </remarks>
        public int? SubscriptionInvoiceId { get; set; }

        public long AmountFcfa { get; set; }
        public long FeesFcfa { get; set; }
        public long NetCreditedFcfa { get; set; }

        /// <summary>
        /// Montant cible original demandé à l'init (avant majoration parent).
        /// C'est ce qu'on crédite à l'Invoice quand le webhook confirme — pas
        /// le net SenePay, qui est rogné des frais et laisserait l'invoice
        /// éternellement "presque payée" en mode FeesPayer=Parent.
        /// </summary>
        public long TargetAmountFcfa { get; set; }

        /// <summary>
        /// Ce qui a RÉELLEMENT été crédité au wallet de l'école lors du
        /// règlement. 0 si ce paiement ne crédite aucun wallet (espèces → caisse,
        /// achat de pages → recette plateforme).
        /// </summary>
        /// <remarks>
        /// <para>🔑 <b>Pourquoi le stocker plutôt que le recalculer.</b> La part
        /// de la plateforme est <c>NetCreditedFcfa − WalletCreditedFcfa</c>, et
        /// elle entre dans l'identité <c>R = D + P</c> (§112). La recalculer a
        /// posteriori reviendrait à l'évaluer avec les taux <b>d'aujourd'hui</b>
        /// alors que le crédit a eu lieu sous les taux <b>de l'époque</b> — les
        /// comptes du passé se mettraient à bouger à chaque changement de grille
        /// du prestataire. Un montant qui a été écrit se lit, il ne se déduit
        /// pas.</para>
        ///
        /// <para>Cette colonne unifie aussi les deux modes : Parent crédite la
        /// cible, School crédite le net ENTIER (depuis le 2026-09-17 ; il était
        /// auparavant diminué d'une provision de retrait), et dans les deux cas
        /// la marge plateforme se lit de la même façon — c'est tout l'intérêt
        /// d'avoir écrit le montant : le modèle a changé, la lecture non.</para>
        /// </remarks>
        public long WalletCreditedFcfa { get; set; }

        public PaymentOperator Operator { get; set; }

        /// <summary>Snapshot de la politique école au moment de l'init (peut changer après).</summary>
        public FeesPayer FeesPayer { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

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
        /// Identifiant de l'opération chez le prestataire : la session
        /// <c>cos-…</c> chez Wave, le jeton de transaction chez le précédent.
        /// C'est lui qu'on interroge pour connaître l'état réel.
        /// </summary>
        public string? ProviderTransactionId { get; set; }

        /// <summary>
        /// Second identifiant, propre au prestataire : l'identifiant de
        /// transaction visible par le payeur chez Wave (celui qu'une famille
        /// lira dans son application), l'identifiant interne chez le précédent.
        /// </summary>
        public string? ProviderInternalId { get; set; }

        public DateTime InitiatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? FailedAt { get; set; }
        public string? FailureReason { get; set; }

        /// <summary>
        /// 💵 Membre de l'école qui a encaissé les espèces au guichet. Null pour
        /// un paiement en ligne — personne ne l'a « reçu », il est arrivé seul.
        /// </summary>
        /// <remarks>
        /// De l'argent liquide passe de main en main : savoir qui l'a pris est
        /// le minimum. C'est aussi ce qui permet à la direction de retrouver
        /// l'auteur d'une saisie erronée.
        /// </remarks>
        public int? CollectedById { get; set; }
        public User? CollectedBy { get; set; }

        public string? ReceiptPdfPath { get; set; }

        /// <summary>
        /// Token public opaque (GUID) généré à l'init et passé dans le
        /// successUrl/cancelUrl envoyé à SenePay. Permet à la page HTML de
        /// résultat (servie par notre backend, ouverte par le navigateur de
        /// l'utilisateur sans JWT) d'accéder au statut + reçu de CE paiement
        /// précis sans qu'un attaquant puisse énumérer les PaymentId pour
        /// voler les reçus des autres.
        /// </summary>
        public string? PublicResultToken { get; set; }

        /// <summary>
        /// Masqué de l'AFFICHAGE côté école (le daara ne veut pas voir cette ligne
        /// dans son historique / que ses partenaires la comptabilisent). Flag
        /// purement cosmétique : n'affecte JAMAIS la compta, la réconciliation
        /// (§112), les soldes ou le règlement — seulement les listes d'affichage
        /// école. La ligne reste en base (jamais de suppression, décision produit).
        /// </summary>
        public bool IsHidden { get; set; }

        // ====================================================================
        // ===== Don par lien — un donateur SANS compte (2026-09-03) =====
        // ====================================================================

        /// <summary>
        /// Collecte à l'origine de ce don. Null pour tout autre paiement, et
        /// null aussi pour un don fait depuis un compte donateur (chemin
        /// historique, conservé).
        /// </summary>
        public int? DonationCampaignId { get; set; }
        public DonationCampaign? DonationCampaign { get; set; }

        /// <summary>
        /// Nom du donateur tel qu'il l'a DÉCLARÉ. Aucune vérification : c'est
        /// Wave qui authentifie le payeur, pas nous (décision produit). Ne jamais
        /// l'afficher comme une identité établie.
        /// </summary>
        public string? DonorName { get; set; }

        /// <summary>Numéro déclaré — sert à rappeler quelqu'un, pas à l'identifier.</summary>
        public string? DonorPhone { get; set; }

        /// <summary>Organisation au nom de laquelle le don est fait (null = don personnel).</summary>
        public string? DonorOrganization { get; set; }

        /// <summary>
        /// Le donateur a demandé à ne pas figurer sur le mur public. Masque le
        /// nom sur la PAGE PUBLIQUE uniquement : l'école voit toujours qui a
        /// donné, et la page le dit franchement plutôt que de laisser croire à
        /// un anonymat total.
        /// </summary>
        public bool DonorAnonymous { get; set; }
    }
}
