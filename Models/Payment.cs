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
    /// AmountFcfa = montant débité du payeur (inclut +8 % si FeesPayer = Parent).
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

        public long AmountFcfa { get; set; }
        public long FeesFcfa { get; set; }
        public long NetCreditedFcfa { get; set; }

        /// <summary>
        /// Montant cible original demandé à l'init (avant majoration parent +8%).
        /// C'est ce qu'on crédite à l'Invoice quand le webhook confirme — pas
        /// le net SenePay, qui est rogné des frais et laisserait l'invoice
        /// éternellement "presque payée" en mode FeesPayer=Parent.
        /// </summary>
        public long TargetAmountFcfa { get; set; }

        public PaymentOperator Operator { get; set; }

        /// <summary>Snapshot de la politique école au moment de l'init (peut changer après).</summary>
        public FeesPayer FeesPayer { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        /// <summary>Référence publique SenePay (ex: SP-XXXX) — visible côté commerçant.</summary>
        public string? SenePayTransactionId { get; set; }

        /// <summary>Id interne SenePay (uuid) utilisé pour les polls/status.</summary>
        public string? SenePayInternalId { get; set; }

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
