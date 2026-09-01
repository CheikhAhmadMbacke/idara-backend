using System.ComponentModel.DataAnnotations.Schema;

namespace Idara.API.Models
{
    /// <summary>
    /// Réglages globaux de la plateforme, éditables par le SuperAdmin pour ne
    /// pas avoir à toucher au code source quand SenePay change sa tarification
    /// ou qu'on veut ajuster un seuil. Table singleton : une seule ligne, PK
    /// figée à <see cref="SingletonId"/>.
    ///
    /// Les pourcentages sont stockés sous forme humaine (8 = +8 %, 1.77 =
    /// 1,77 %) ; les taux exploités par le code sont dérivés via
    /// <see cref="ParentFeeMultiplier"/> / <see cref="PayoutFeeRate"/>.
    /// </summary>
    public class PlatformSettings
    {
        public const int SingletonId = 1;

        public int Id { get; set; } = SingletonId;

        /// <summary>Montant minimum d'un paiement parent (contrainte SenePay : 200 FCFA).</summary>
        public long MinPayinFcfa { get; set; } = 200;

        /// <summary>Montant minimum d'un retrait / transfert sortant.</summary>
        public long MinWithdrawalFcfa { get; set; } = 25000;

        /// <summary>Majoration appliquée au parent quand FeesPayer=Parent (8 = +8 %).</summary>
        public double ParentFeePercent { get; set; } = 8.0;

        /// <summary>Frais opérateur+taxe au payout (1,77 %), absorbés par la majoration sortante.</summary>
        public double PayoutFeePercent { get; set; } = 1.77;

        /// <summary>
        /// Mode d'envoi des SMS de notification. <c>true</c> : les DEUX versions
        /// FR + AR dans le même corps. <c>false</c> (défaut depuis le
        /// 2026-09-01) : une seule langue, celle du destinataire.
        ///
        /// <para><b>Le défaut a été inversé sur une mesure, pas sur une
        /// impression.</b> Le bilingue coûte <b>×4</b> et non ×3 comme le disait
        /// le commentaire d'origine : l'arabe fait basculer tout le corps en
        /// UCS-2, où le segment ne vaut plus que 70 caractères, si bien qu'un
        /// rappel de mensualité pèse 4 segments en bilingue contre 1 en français
        /// seul. Sur les plans mesurés, le SMS mangeait de 30 % à 100 % du prix
        /// de l'abonnement.</para>
        ///
        /// <para><b>Et le risque de compréhension reste couvert</b> : la règle
        /// d'or de <c>NotificationService</c> relit la langue RÉELLE du
        /// destinataire à chaque envoi, et force de toute façon le bilingue pour
        /// un compte qui ne s'est <b>jamais connecté</b> — donc le tout premier
        /// message, celui des identifiants, reste dans les deux langues.</para>
        ///
        /// Bascule à chaud par le SuperAdmin sans redéploiement.
        /// </summary>
        public bool SmsBilingual { get; set; } = false;

        // ================================================================
        // ===== SMS : tarifs, plafonds et coupe-circuit (2026-09-01) =====
        //
        // Motif : rien, nulle part, n'empêchait Idara d'envoyer cent mille SMS
        // en une nuit. Le risque n'est pas théorique — un développeur sénégalais
        // a reçu une facture d'un million de FCFA après qu'un tiers a utilisé son
        // compte SMS. Tous ces réglages sont éditables par le SuperAdmin : un
        // plafond qu'il faut redéployer pour relever est un plafond qu'on finit
        // par retirer du code.
        // ================================================================

        /// <summary>Prix d'un segment vers Orange Sénégal (77/78), en centimes de
        /// FCFA. Grille contractuelle : 3,50 F HT.</summary>
        public long SmsOnNetPriceCentimes { get; set; } = 350;

        /// <summary>Prix d'un segment vers un autre opérateur sénégalais (70/75/76),
        /// en centimes de FCFA. Grille contractuelle : 5 F HT.</summary>
        public long SmsOffNetPriceCentimes { get; set; } = 500;

        /// <summary>Prix d'un segment à l'international, en centimes de FCFA
        /// (40,35 F HT). Idara ne doit JAMAIS en envoyer : la valeur sert à
        /// chiffrer une ligne aberrante, pas à autoriser l'envoi.</summary>
        public long SmsInternationalPriceCentimes { get; set; } = 4035;

        /// <summary>Redevance mensuelle Orange, due même à zéro SMS (10 000 F HT).
        /// Sans elle, le total attendu ne peut pas coller à la facture.</summary>
        public long SmsMonthlyFeeHtFcfa { get; set; } = 10000;

        /// <summary>TVA appliquée par Sonatel (18 % au Sénégal), pour comparer un
        /// total TTC à un total TTC.</summary>
        public double SmsVatPercent { get; set; } = 18.0;

        // ----- Coupe-circuit global (deux paliers, décision 2026-09-01) -----

        /// <summary>
        /// Coupe TOUS les SMS immédiatement, sans redéploiement. C'est le geste
        /// à faire si tu constates une facture qui dérape : on arrête d'abord,
        /// on comprend ensuite.
        /// </summary>
        public bool SmsKillSwitch { get; set; } = false;

        /// <summary>Palier SOUPLE, en FCFA par jour : au-delà, seuls les SMS
        /// critiques (code de connexion, identifiants) partent encore.</summary>
        public long SmsSoftDailyCapFcfa { get; set; } = 8000;

        /// <summary>Palier SOUPLE, en FCFA par mois calendaire.</summary>
        public long SmsSoftMonthlyCapFcfa { get; set; } = 120000;

        /// <summary>Palier ABSOLU, en FCFA par jour : au-delà, plus RIEN ne part,
        /// codes de connexion compris — une attaque qui viserait justement l'OTP
        /// ne doit pas trouver de porte ouverte.</summary>
        public long SmsHardDailyCapFcfa { get; set; } = 20000;

        /// <summary>Palier ABSOLU, en FCFA par mois calendaire.</summary>
        public long SmsHardMonthlyCapFcfa { get; set; } = 250000;

        // ----- Garde-fous par école (calés sur l'EFFECTIF, pas sur le plan) -----
        //
        // Décision produit 2026-09-01 : les SMS sont INCLUS dans le produit et ne
        // se vendent pas au détail — aucun quota n'est affiché nulle part. Ces
        // plafonds ne sont donc pas un palier commercial mais un détecteur
        // d'emballement, et ils se calculent sur la réalité de l'école (son
        // effectif) et non sur ce qu'elle paie. Consommation réelle mesurée :
        // ~2,5 segments par élève et par mois. Les seuils sont à 4× celle-ci.

        /// <summary>Segments autorisés par élève et par mois (plafond école).</summary>
        public int SmsSchoolMonthlySegmentsPerStudent { get; set; } = 10;

        /// <summary>Plancher mensuel par école, pour qu'un tout petit daara puisse
        /// créer ses comptes et faire ses connexions.</summary>
        public int SmsSchoolMonthlyFloorSegments { get; set; } = 300;

        /// <summary>Segments autorisés par élève et par jour.</summary>
        public int SmsSchoolDailySegmentsPerStudent { get; set; } = 3;

        /// <summary>Plancher journalier par école.</summary>
        public int SmsSchoolDailyFloorSegments { get; set; } = 150;

        /// <summary>Segments autorisés par élève et par heure. Assez large pour
        /// laisser passer un LOT entier (génération des mensualités), assez
        /// serré pour couper une boucle en quelques minutes.</summary>
        public int SmsSchoolHourlySegmentsPerStudent { get; set; } = 2;

        /// <summary>Plancher horaire par école.</summary>
        public int SmsSchoolHourlyFloorSegments { get; set; } = 100;

        /// <summary>
        /// Messages par destinataire distinct, sur une heure glissante, au-delà
        /// duquel l'école est considérée en emballement.
        ///
        /// <para><b>Le meilleur signal du dispositif</b>, et le seul qui ne
        /// dépende d'aucune taille d'école : un envoi légitime touche des numéros
        /// TOUS distincts (un par famille), une boucle retape le même petit
        /// ensemble. Un ratio supérieur à 3 est anormal même à faible volume.</para>
        /// </summary>
        public int SmsMaxMessagesPerDistinctRecipient { get; set; } = 3;

        /// <summary>Volume minimal avant que le ratio ci-dessus ait un sens — sur
        /// cinq messages, un ratio ne veut rien dire.</summary>
        public int SmsRatioMinMessages { get; set; } = 20;

        /// <summary>
        /// Messages maximum vers UN numéro sur 24 h. Un parent en reçoit environ
        /// quatre par MOIS : c'est un détecteur de boucle, pas un budget.
        ///
        /// <para>Huit et non six : le groupage par responsable couvre les rappels
        /// de facture, mais PAS les encaissements en espèces, qui restent un SMS
        /// par facture réglée. Une famille de trois enfants payant au guichet le
        /// même jour, plus un code de connexion, atteindrait six — un plafond
        /// qui se déclenche sur un usage normal est un plafond qu'on finit par
        /// retirer.</para>
        /// </summary>
        public int SmsMaxPerRecipientPerDay { get; set; } = 8;

        /// <summary>Messages maximum vers UN numéro sur 30 jours.</summary>
        public int SmsMaxPerRecipientPerMonth { get; set; } = 20;

        /// <summary>
        /// Active l'application de la machine à états d'abonnement (Phase 4) : si
        /// <c>true</c>, le middleware bloque réellement les écoles en ReadOnly
        /// (écritures interdites) / Suspended (tout interdit) avec un 402. Si
        /// <c>false</c> (défaut), tout le code de facturation tourne (génération
        /// de factures, transitions d'état) mais AUCUNE école n'est bloquée —
        /// permet de déployer et d'observer avant d'activer le verrou. Bascule à
        /// chaud par le SuperAdmin (Réglages plateforme).
        /// </summary>
        public bool SubscriptionEnforcementEnabled { get; set; } = false;

        /// <summary>
        /// Rôles (CSV, ex. "SchoolAdmin,SchoolStaff") pour lesquels l'installation
        /// de la DERNIÈRE version Android est OBLIGATOIRE (modal bloquant in-app).
        /// Les rôles absents ne voient qu'un bandeau doux « mise à jour disponible ».
        /// Vide (défaut) = personne n'est forcé. Édité par le SuperAdmin via
        /// l'endpoint dédié <c>/api/app-version/config</c> (PAS via le PUT des
        /// réglages plateforme, pour ne pas être écrasé par un ancien client).
        /// </summary>
        public string AndroidForcedUpdateRoles { get; set; } = string.Empty;

        /// <summary>
        /// 📖 Quand la reprise du type des matières de Coran a été jouée.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>Une reprise de données se joue UNE FOIS.</b> Sans ce marqueur,
        /// elle tournerait à chaque démarrage de l'API : une école qui repasse
        /// volontairement sa matière sur un autre type la verrait rebasculer au
        /// déploiement suivant, sans jamais comprendre pourquoi — elle perdrait
        /// toujours. C'est la discipline du §74, appliquée à une conversion que
        /// PostgreSQL ne sait pas exprimer (la normalisation est en C#) et qui ne
        /// pouvait donc pas vivre dans le <c>Up()</c> d'une migration.
        /// </remarks>
        public DateTime? QuranSubjectsRetypedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        /// <summary>Multiplicateur appliqué au montant cible parent : 1 + p/100.</summary>
        [NotMapped]
        public double ParentFeeMultiplier => 1 + ParentFeePercent / 100.0;

        /// <summary>Taux de frais payout : p/100. Sert à majorer le montant envoyé à SenePay.</summary>
        [NotMapped]
        public double PayoutFeeRate => PayoutFeePercent / 100.0;

        /// <summary>Prix unitaire du segment (centimes) pour un réseau donné.</summary>
        public long SmsUnitPriceCentimes(Common.Utilities.SmsNetwork network) => network switch
        {
            Common.Utilities.SmsNetwork.OnNet => SmsOnNetPriceCentimes,
            Common.Utilities.SmsNetwork.OffNet => SmsOffNetPriceCentimes,
            _ => SmsInternationalPriceCentimes,
        };

        /// <summary>
        /// Plafond mensuel de segments d'une école, dérivé de son effectif.
        /// Le plancher existe pour qu'un daara de dix élèves puisse quand même
        /// créer ses comptes et faire connecter ses parents.
        /// </summary>
        public int SmsSchoolMonthlyCap(int studentCount) => Math.Max(
            SmsSchoolMonthlyFloorSegments, SmsSchoolMonthlySegmentsPerStudent * Math.Max(0, studentCount));

        /// <summary>Plafond journalier de segments d'une école.</summary>
        public int SmsSchoolDailyCap(int studentCount) => Math.Max(
            SmsSchoolDailyFloorSegments, SmsSchoolDailySegmentsPerStudent * Math.Max(0, studentCount));

        /// <summary>Plafond horaire de segments d'une école.</summary>
        public int SmsSchoolHourlyCap(int studentCount) => Math.Max(
            SmsSchoolHourlyFloorSegments, SmsSchoolHourlySegmentsPerStudent * Math.Max(0, studentCount));
    }
}
