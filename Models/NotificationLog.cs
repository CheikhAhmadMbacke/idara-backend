using Idara.API.Common.Utilities;
using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Trace append-only de chaque notification. Sert (1) à prouver qu'un
    /// destinataire a été notifié, (2) à dédupliquer les rappels (via
    /// <see cref="TemplateCode"/> + <see cref="RelatedEntityId"/>), (3) au
    /// diagnostic, et depuis le 2026-09-01 (4) de <b>registre de facturation</b>
    /// et (5) de <b>compteur du garde-fou anti-abus</b>.
    ///
    /// <para><b>Pourquoi ces deux derniers rôles vivent ICI et pas dans une table
    /// de compteurs.</b> Un compteur en mémoire repart à zéro à chaque
    /// déploiement (§92) et un compteur stocké finit toujours par diverger de ce
    /// qu'on a réellement envoyé. En dérivant les plafonds du registre lui-même,
    /// il n'existe qu'une seule vérité : ce qui est parti. C'est la discipline du
    /// §112 (« P se recalcule, ne se stocke pas ») appliquée aux SMS.</para>
    ///
    /// <para>Pas de soft-delete, et — décision du 2026-09-01 — <b>jamais purgé
    /// avec l'école</b> : c'est une pièce comptable qui doit se confronter à la
    /// facture Sonatel des mois après la suppression du daara. Les lignes d'une
    /// école supprimée sont <b>anonymisées</b> (lien vers le compte coupé,
    /// <see cref="SchoolNameSnapshot"/> conservé), jamais effacées.</para>
    /// </summary>
    public class NotificationLog
    {
        public int Id { get; set; }

        /// <summary>Utilisateur destinataire, si connu (nullable : envoi possible à un numéro brut).</summary>
        public int? UserId { get; set; }

        /// <summary>Canal : "Sms" (futur : "Email", "WhatsApp").</summary>
        public string Channel { get; set; } = "Sms";

        /// <summary>Destinataire normalisé (E.164 pour un SMS).</summary>
        public string Recipient { get; set; } = string.Empty;

        /// <summary>Code du template (ex. "INVOICE_DUE", "PAYMENT_RECEIVED", "INVOICE_OVERDUE", "INVITE").</summary>
        public string TemplateCode { get; set; } = string.Empty;

        /// <summary>
        /// Id de l'entité métier liée (ex. InvoiceId pour INVOICE_DUE/OVERDUE,
        /// PaymentId pour PAYMENT_RECEIVED). Permet la déduplication des rappels.
        /// </summary>
        public int? RelatedEntityId { get; set; }

        public bool Success { get; set; }

        /// <summary>Id message renvoyé par le provider (ATXid_… pour Africa's Talking).</summary>
        public string? ProviderMessageId { get; set; }

        /// <summary>Détail d'erreur si échec (HTTP 401, timeout, invalid_phone…).</summary>
        public string? Error { get; set; }

        /// <summary>Coût brut renvoyé par le provider (informatif ; Orange n'en renvoie pas).</summary>
        public string? Cost { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ================================================================
        // ===== Attribution : à qui imputer la dépense (2026-09-01) =====
        // ================================================================

        /// <summary>
        /// École à l'origine de l'envoi. <b>La colonne qui rend le registre
        /// exploitable</b> : sans elle, impossible de dire quel daara consomme,
        /// donc impossible de plafonner par école ou de répartir la facture.
        /// Null pour un envoi hors école (SuperAdmin, compte sans rattachement).
        /// </summary>
        public int? SchoolId { get; set; }

        /// <summary>
        /// Nom de l'école FIGÉ au moment de l'envoi. Une pièce comptable ne doit
        /// pas devenir illisible parce qu'un daara a été renommé ou supprimé
        /// après coup — c'est le même raisonnement que les coordonnées du
        /// bénéficiaire figées sur un retrait.
        /// </summary>
        public string? SchoolNameSnapshot { get; set; }

        /// <summary>
        /// D'où vient l'envoi, sous une forme greppable : <c>cron:monthly-invoices</c>,
        /// <c>api:auth/credentials-sms</c>, <c>webhook:payin</c>…
        ///
        /// <para>C'est ce champ qui transforme « beaucoup de SMS sont partis » en
        /// « 400 SMS via <c>api:auth/credentials-sms</c> en 12 minutes ». Sans
        /// lui, une alerte d'abus ne dit pas quoi fermer.</para>
        /// </summary>
        public string? TriggerSource { get; set; }

        /// <summary>
        /// Utilisateur dont le geste a déclenché l'envoi — <b>l'auteur, pas le
        /// destinataire</b> (<see cref="UserId"/>). Null pour un automate.
        /// Nomme le compte à suspendre en cas d'abus (règle « nommer qui »,
        /// §177).
        /// </summary>
        public int? TriggerUserId { get; set; }

        /// <summary>Ce que l'envoi vaut la peine de coûter — décide de ce qui
        /// survit à un plafond (cf. <see cref="SmsPriority"/>).</summary>
        public SmsPriority Priority { get; set; } = SmsPriority.Normal;

        // ================================================================
        // ===== Facturation : ce que l'envoi a réellement coûté =====
        // ================================================================

        /// <summary>Encodage imposé par le contenu — c'est lui qui décide de la
        /// taille du segment (70 caractères en UCS-2 contre 160 en GSM-7).</summary>
        public SmsEncoding Encoding { get; set; } = SmsEncoding.Gsm7;

        /// <summary>Longueur du message en unités facturables.</summary>
        public int CharCount { get; set; }

        /// <summary>
        /// Segments facturés selon la norme SMS — <b>l'unité réellement payée</b>,
        /// et non le nombre de messages. Un rappel bilingue en pèse 4.
        /// </summary>
        public int Segments { get; set; }

        /// <summary>
        /// Segments selon l'autre lecture du contrat Orange (« lot de 160
        /// caractères », ambigu en UCS-2). Conservé en parallèle pour que la
        /// première vraie facture Sonatel tranche entre les deux hypothèses
        /// au lieu qu'on choisisse à l'aveugle.
        /// </summary>
        public int SegmentsFixed160 { get; set; }

        /// <summary>Réseau du destinataire — décide du prix unitaire.</summary>
        public SmsNetwork Network { get; set; } = SmsNetwork.OnNet;

        /// <summary>Prix unitaire du segment en centimes de FCFA, FIGÉ à l'envoi
        /// (350 on-net, 500 off-net) : une révision tarifaire ne doit pas
        /// réécrire l'historique.</summary>
        public long UnitPriceCentimes { get; set; }

        /// <summary>
        /// Coût estimé de l'envoi en centimes de FCFA. Zéro si rien n'est parti
        /// (envoi bloqué ou refusé avant l'appel au fournisseur).
        /// </summary>
        public long CostCentimes { get; set; }

        /// <summary>
        /// Renseigné quand le garde-fou a REFUSÉ l'envoi (plafond école, plafond
        /// plateforme, numéro hors Sénégal…). La ligne est écrite quand même :
        /// un envoi bloqué est exactement ce qu'on veut voir, et l'effacer
        /// reviendrait à masquer l'attaque qu'on cherchait à détecter.
        /// </summary>
        public string? BlockedReason { get; set; }
    }
}
