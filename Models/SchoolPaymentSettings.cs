using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Configuration de paiement par école (1-1 avec School). Créée
    /// automatiquement au seed initial. La PK est SchoolId (pas d'Id séparé) :
    /// une école = une seule config.
    /// </summary>
    public class SchoolPaymentSettings
    {
        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public BillingMode BillingMode { get; set; } = BillingMode.FixedAmount;

        /// <summary>Qui paie les frais SenePay sur les PAIEMENTS PARENTS.
        /// Parent = le parent (majoration +8 %, l'école reçoit le montant plein) ;
        /// School = l'école (absorbe les frais, reçoit le net).</summary>
        public FeesPayer FeesPayer { get; set; } = FeesPayer.Parent;

        /// <summary>Qui paie les frais SenePay sur les DONS reçus par le daara.
        /// Parent = le donateur (majoration, le daara reçoit le montant plein) ;
        /// School = le daara (absorbe les frais, reçoit le net). Défaut = School
        /// (décision produit 2026-07-11 : le donateur ne doit pas payer les frais).</summary>
        public FeesPayer DonationFeesPayer { get; set; } = FeesPayer.School;

        /// <summary>
        /// <b>Jour d'OUVERTURE du paiement</b> : jour du mois (1-28) où le cron
        /// génère les mensualités et prévient les familles.
        /// </summary>
        /// <remarks>
        /// ⚠️ Le nom de la colonne est historique — il date de l'époque où ce
        /// jour servait AUSSI d'échéance. Ce n'est plus le cas depuis le
        /// 2026-08-23 : l'échéance est <see cref="PaymentDeadlineDay"/>. Renommer
        /// la colonne coûterait une migration et un DTO cassé pour zéro gain.
        /// </remarks>
        public int MonthlyDueDay { get; set; } = 5;

        /// <summary>
        /// <b>Jour LIMITE de paiement</b> : jour du mois (1-28) au-delà duquel une
        /// mensualité non réglée est en retard — la famille est relancée, et le
        /// daara sort sa liste payés / non payés.
        /// </summary>
        /// <remarks>
        /// <para>Avant ce réglage, l'échéance tombait le jour même de l'émission :
        /// un parent prévenu le 5 recevait un SMS de retard le 6.</para>
        /// <para>⚠️ Une valeur <b>inférieure</b> à <see cref="MonthlyDueDay"/>
        /// désigne le mois SUIVANT (ouverture le 25, limite le 5). Toute la
        /// résolution vit dans <see cref="Common.Utilities.PaymentSchedule"/>.</para>
        /// </remarks>
        public int PaymentDeadlineDay { get; set; } = 15;

        public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

        /// <summary>
        /// Tarif général de l'école : montant mensuel appliqué à TOUS les élèves
        /// qui n'ont ni tarif personnalisé, ni tarif de statut, ni tarif de
        /// classe. Null = pas de tarif général.
        /// Hiérarchie complète : tarif élève &gt; tarif STATUT &gt; tarif classe
        /// &gt; tarif général (cf. <see cref="Services.FeeResolver"/>).
        /// Pratique pour les daara mono-tarif (un seul montant pour toute l'école).
        /// </summary>
        public long? GeneralMonthlyFeeFcfa { get; set; }

        // ----- Tarifs par régime d'hébergement (2026-08-09) -----
        // Montants mensuels COMPLETS (pas des suppléments) appliqués selon le
        // BoardingStatus de l'élève. Null = pas de tarif pour ce régime → on
        // retombe sur le tarif de classe puis le tarif général.
        // Décision produit : le statut prime sur la classe (un interne paie le
        // tarif internat quelle que soit sa classe).
        // Un élève sans statut renseigné n'est JAMAIS concerné par ces montants.

        /// <summary>Tarif mensuel des internes. Null = non configuré.</summary>
        public long? BoardingMonthlyFeeFcfa { get; set; }

        /// <summary>Tarif mensuel des demi-internes. Null = non configuré.</summary>
        public long? HalfBoardingMonthlyFeeFcfa { get; set; }

        /// <summary>Tarif mensuel des externes. Null = non configuré.</summary>
        public long? DayMonthlyFeeFcfa { get; set; }

        /// <summary>
        /// Frais d'inscription (FCFA, une fois par élève). Pré-remplit le champ du
        /// formulaire d'ajout d'élève, où il reste modifiable élève par élève
        /// (exonération possible). Null = pas de frais d'inscription par défaut.
        /// </summary>
        public long? RegistrationFeeFcfa { get; set; }

        /// <summary>
        /// Prévenir la direction par SMS à chaque paiement de scolarité reçu.
        /// </summary>
        /// <remarks>
        /// <para><b>Éteint par défaut, et c'est délibéré.</b> La direction reçoit
        /// déjà une notification (<c>SCHOOL_PAYMENT_RECEIVED</c>) — gratuite, mais
        /// muette si le téléphone du directeur a désactivé les notifications, sans
        /// que personne ne le sache jamais. Le SMS, lui, arrive toujours ; il se
        /// paie. Un daara de 100 élèves dépensera environ 240 F par mois.</para>
        /// <para>C'est donc à l'école de décider de dépenser, pas à nous à sa
        /// place — et l'écran de réglage affiche l'estimation avant de cocher.
        /// Les dons, eux, sont notifiés par SMS sans condition : ils sont rares,
        /// et une rentrée d'argent inattendue mérite d'être sue tout de suite.</para>
        /// </remarks>
        public bool NotifySchoolBySmsOnPayment { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
