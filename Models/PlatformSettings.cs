using System.ComponentModel.DataAnnotations.Schema;

namespace Idara.API.Models
{
    /// <summary>
    /// Réglages globaux de la plateforme, éditables par le SuperAdmin pour ne
    /// pas avoir à toucher au code source quand SenePay change sa tarification
    /// ou qu'on veut ajuster un seuil. Table singleton : une seule ligne, PK
    /// figée à <see cref="SingletonId"/>.
    ///
    /// Les pourcentages sont stockés sous forme humaine (5.37 = 5,37 %) ; les
    /// taux exploités par le code sont dérivés via
    /// <see cref="ParentFeeMultiplier"/> / <see cref="PayoutFeeRate"/>.
    ///
    /// 🔴 <b>On ne saisit ici que ce qui est OBSERVABLE sur un relevé</b> — ce
    /// que le prestataire prélève. Tout le reste se calcule. La majoration au
    /// payeur a été un champ saisi jusqu'au 2026-09-13 : elle a dérivé de
    /// 0,45 point sans que rien ne le signale.
    /// </summary>
    public class PlatformSettings
    {
        public const int SingletonId = 1;

        public int Id { get; set; } = SingletonId;

        /// <summary>Montant minimum d'un paiement parent (contrainte SenePay : 200 FCFA).</summary>
        public long MinPayinFcfa { get; set; } = 200;

        /// <summary>Montant minimum d'un retrait / transfert sortant.</summary>
        public long MinWithdrawalFcfa { get; set; } = 25000;

        /// <summary>
        /// Montant minimum d'un DON par lien public (500 FCFA).
        /// </summary>
        /// <remarks>
        /// Plus haut que le minimum d'un paiement parent, et volontairement : en
        /// dessous de 500 F, les frais Wave mangent le don et le daara reçoit une
        /// misère pour une écriture comptable de plus.
        /// ⚠️ Colonne ajoutée à une ligne SINGLETON qui existe depuis juin : la
        /// migration doit porter <c>defaultValue: 500</c>, sinon la ligne réelle
        /// hérite d'un 0 et le garde-fou ne garde plus rien (§193, §202).
        /// </remarks>
        public long MinDonationFcfa { get; set; } = 500;

        /// <summary>
        /// Montant maximum d'un don par lien public (2 000 000 FCFA). Au-delà, la
        /// page invite à appeler le daara : un don de cette taille mérite un
        /// contact humain, et c'est la meilleure barrière anti-blanchiment quand
        /// on ne contrôle aucune identité.
        /// ⚠️ Même piège de <c>defaultValue</c> que ci-dessus.
        /// </summary>
        public long MaxDonationFcfa { get; set; } = 2_000_000;

        /// <summary>
        /// Image d'illustration de la page d'accueil publique, remplaçable
        /// depuis le back-office (chemin relatif <c>/uploads/landing/…</c>).
        /// </summary>
        /// <remarks>
        /// Elle était en dur dans l'application (<c>assets/images/daara.jpg</c>) :
        /// la changer imposait une nouvelle version, donc une mise à jour à tous
        /// les utilisateurs pour une photo — exactement ce qu'on cherche à éviter
        /// ([[feedback_minimize_user_updates]]). <c>null</c> = on garde l'image
        /// livrée avec l'application, qui reste le repli si le serveur est
        /// injoignable.
        /// </remarks>
        public string? LandingHeroImagePath { get; set; }

        // ================================================================
        // ===== Mentions légales (2026-09-04) =====
        //
        // Elles vivent en base, pas dans le code : une mention légale qui
        // demande un redéploiement pour être corrigée finit par rester fausse.
        // Toutes nullables — les pages juridiques se rendent SANS elles, en
        // omettant simplement la ligne concernée plutôt qu'en affichant un
        // libellé vide ou, pire, une valeur inventée.
        // ================================================================

        /// <summary>Raison sociale exacte de l'éditeur.</summary>
        public string? LegalCompanyName { get; set; }

        /// <summary>Forme juridique (SUARL, SARL, entreprise individuelle…).</summary>
        public string? LegalForm { get; set; }

        /// <summary>Numéro d'identification nationale des entreprises (NINEA).</summary>
        public string? LegalNinea { get; set; }

        /// <summary>Registre du commerce et du crédit mobilier.</summary>
        public string? LegalRccm { get; set; }

        /// <summary>Adresse du siège social.</summary>
        public string? LegalAddress { get; set; }

        /// <summary>Représentant légal (nom et qualité).</summary>
        public string? LegalRepresentative { get; set; }

        /// <summary>
        /// Numéro de récépissé de déclaration à la Commission de protection des
        /// données personnelles (loi sénégalaise 2008-12).
        /// </summary>
        public string? LegalCdpNumber { get; set; }

        /// <summary>Adresse de contact pour les questions juridiques et les droits.</summary>
        public string? LegalContactEmail { get; set; }

        /// <summary>Téléphone de contact affiché sur les pages juridiques.</summary>
        public string? LegalContactPhone { get; set; }

        /// <summary>
        /// Version des documents juridiques en vigueur (ex. « 2026-09 »).
        /// </summary>
        /// <remarks>
        /// C'est elle qu'on horodate à l'acceptation : sans version, on sait
        /// qu'un utilisateur a accepté « les conditions », mais pas LESQUELLES —
        /// ce qui ne prouve rien le jour où elles changent.
        /// </remarks>
        public string LegalVersion { get; set; } = "2026-09";

        // ================================================================
        // ===== Les DEUX seuls taux saisis — la majoration se DÉDUIT ======
        //
        // 🔴 Pourquoi ils sont deux, et pourquoi la majoration n'en est plus un.
        //
        // La majoration au payeur a longtemps été un troisième champ saisi à la
        // main. Elle valait 7,14 en production, calée en ADDITIONNANT les taux
        // prélevés (3,6 + 1,77 + 1,77). C'était faux de 0,45 point, pour une
        // raison purement arithmétique : majorer de t ne compense pas un
        // prélèvement de t. Prélever 5,37 % de 107,14 laisse 101,39, d'où il
        // faut encore sortir 1,77 % de frais de retrait — il manquait 4 154 F
        // par million facturé, payés par la plateforme et visibles nulle part.
        //
        // Un chiffre saisi à la main dérive dès que le prestataire change sa
        // grille. On ne saisit donc plus que ce qui est OBSERVABLE sur un relevé
        // — ce que le prestataire prélève — et la majoration en découle.
        // ================================================================

        /// <summary>
        /// Taux réellement retenu sur un ENCAISSEMENT, en pourcentage humain
        /// (5.40 = 5,40 %).
        /// </summary>
        /// <remarks>
        /// 🔴 <b>5,40 et non 5,37, et la nuance a coûté une mesure pour être
        /// vue.</b> Le taux contractuel additionné (3,6 % SenePay + 1,77 %
        /// opérateur) donne 5,37 — mais c'est un <b>plancher</b>, jamais une
        /// moyenne : SenePay arrondit ses frais au franc supérieur sur
        /// <i>chaque</i> transaction, et ce supplément pèse d'autant plus que le
        /// montant est petit. Mesuré en production sur 197 paiements réglés :
        /// minimum <b>5,370</b>, moyenne pondérée <b>5,379</b>, et par tranche
        /// 5,749 % sous 1 000 F · 5,532 % de 1 à 5 k · 5,380 % de 5 à 20 k ·
        /// 5,373 % au-delà. Saisir le plancher laissait donc ~2 F de déficit par
        /// transaction — 29 fois moins qu'avant, mais toujours du déficit.
        /// </remarks>
        /// <remarks>
        /// ⚠️ <b>Ce taux ne se lit pas dans <c>Payments.FeesFcfa</c></b>, qui
        /// n'enregistre que la part SenePay (~3,66 %). Le vrai taux est
        /// <c>(Σ AmountFcfa − Σ NetCreditedFcfa) / Σ AmountFcfa</c> sur les
        /// paiements <c>Completed</c> hors espèces — c'est exactement ce que
        /// l'écran SuperAdmin affiche en regard de ce champ, pour qu'une dérive
        /// de la grille SenePay se VOIE au lieu de se payer.
        /// </remarks>
        public double PayinFeePercent { get; set; } = 5.40;

        /// <summary>
        /// Frais opérateur prélevés sur un DÉCAISSEMENT, en pourcentage humain
        /// (1.77 = 1,77 %). Mesuré : 55 429 F sur 3 133 100 F retirés.
        /// </summary>
        /// <remarks>
        /// 🔴 Ce frais est prélevé <b>en plus</b> du montant envoyé
        /// (<c>fee_mode = "on_top"</c>, cf. <c>SenePayPayoutRequest</c>) : un
        /// retrait de T coûte <c>T × (1 + taux)</c> à la réserve marchand, pas
        /// <c>T / (1 − taux)</c>. C'est ce qui donne sa forme au numérateur de
        /// <see cref="ParentFeeMultiplier"/>.
        /// </remarks>
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

        // ============================================================
        //  📷 Import par PHOTO (lecture d'un cahier par l'IA)
        // ============================================================
        //
        // Décision produit du 2026-09-02 : la lecture d'un cahier est INCLUSE,
        // jamais facturée séparément et jamais réservée à un palier
        // d'abonnement — le coût suit l'effectif, donc il suit déjà le plan
        // (~2 % d'un mois, quel que soit le plan). Ce qui est borné, c'est le
        // VOLUME, pas le prix : le danger n'est pas 50 F la page, c'est
        // 4 000 pages. Même leçon que le §191.
        //
        // ⚠️ Ces colonnes sont ajoutées à une ligne SINGLETON DÉJÀ EXISTANTE :
        // la migration DOIT les semer explicitement (§193), sinon la ligne
        // héritée de juin recevrait 0 partout et l'import photo serait coupé
        // pour tout le monde dès le premier démarrage.

        /// <summary>
        /// Interrupteur général. <c>false</c> = l'écran photo disparaît et
        /// l'endpoint refuse. À couper si le fournisseur est indisponible ou si
        /// la dépense dérape, sans redéploiement.
        /// </summary>
        public bool OcrEnabled { get; set; } = true;

        /// <summary>
        /// Pages offertes à une école, une fois pour toutes. 30 pages ≈ 750
        /// élèves : assez pour couvrir le premier import ET le refaire deux
        /// fois. Exposition maximale ≈ 1 500 FCFA par école — le prix d'un
        /// prospect dont on a déjà validé le dossier.
        /// </summary>
        public int OcrBaseAllowancePages { get; set; } = 30;

        /// <summary>
        /// Pages acceptées en UN envoi. Les pages partent dans une seule requête
        /// (le modèle voit l'en-tête une fois et garde la disposition) — mais
        /// une requête sans borne est une facture sans borne.
        /// </summary>
        public int OcrMaxPagesPerRequest { get; set; } = 40;

        /// <summary>
        /// Plafond de dépense QUOTIDIEN, toutes écoles confondues. C'est ce
        /// plafond-là qui protège d'un bug ou d'une boucle — le quota par école
        /// ne le fait pas. Dérivé du registre, jamais d'un compteur stocké.
        /// </summary>
        public long OcrDailyPlatformCapFcfa { get; set; } = 25000;

        /// <summary>
        /// Échecs consécutifs tolérés pour une école avant de la couper. Un
        /// échec ne consomme pas son quota (elle n'a rien reçu) — sans ce
        /// garde-fou, une boucle d'échecs dépenserait sans jamais buter sur le
        /// quota.
        /// </summary>
        public int OcrMaxConsecutiveFailures { get; set; } = 5;

        /// <summary>
        /// Prix du million de tokens d'ENTRÉE, en centimes de FCFA. Réglable
        /// sans redéploiement : c'est un tarif fournisseur, il changera.
        /// Opus 5 : 5 $/M ≈ 3 035 FCFA/M ≈ 303 500 centimes.
        /// </summary>
        public long OcrInputPriceCentimesPerMTok { get; set; } = 303500;

        /// <summary>Prix du million de tokens de SORTIE. Opus 5 : 25 $/M ≈ 15 175 FCFA/M.</summary>
        public long OcrOutputPriceCentimesPerMTok { get; set; } = 1517500;

        // ---- Ce que l'école PAIE pour une page, au-delà de ses pages offertes ----
        //
        // 🔴 **Le prix d'une page dépend de ce que la page contient**, et c'est
        // la seule façon de ne pas ruiner le daara pour qui la fonction existe.
        // Deux réalités opposées coexistent :
        //   - un cahier serré tient 24 élèves sur une page ;
        //   - certains daara informels tiennent UNE FICHE PAR ÉLÈVE, sur une à
        //     trois pages.
        // Un prix unique par page ferait payer au second QUATRE FOIS le service
        // rendu au premier. D'où deux composantes — une part fixe qui paie
        // l'image et la réflexion du modèle, une part par élève qui paie le
        // texte produit — réunies en UN SEUL prix affiché, grâce à la
        // calibration faite sur les pages offertes de l'école.
        //
        // ⚠️ Tarif de LANCEMENT. La « réflexion » du modèle est estimée, jamais
        // mesurée : c'est 41 % du coût et le seul chiffre qui manque. La part
        // fixe est donc volontairement au-dessus du prix arrêté le 2026-09-02
        // (20 F), parce que **baisser un prix après mesure est facile, le monter
        // ne l'est pas**. À rebaisser dès que de vraies photos de cahier auront
        // été lues.

        /// <summary>
        /// Part FIXE du prix d'une page, en FCFA. Elle paie l'image et la
        /// réflexion du modèle — ce qui ne dépend pas du nombre d'élèves.
        /// </summary>
        public long OcrPriceBaseFcfa { get; set; } = 30;

        /// <summary>
        /// Part VARIABLE, par élève trouvé sur une page. Elle paie le texte
        /// produit, qui est 82 % du coût réel.
        /// </summary>
        public long OcrPricePerStudentFcfa { get; set; } = 3;

        /// <summary>
        /// Élèves par page retenus tant que l'école n'a rien fait lire. Sert
        /// uniquement de repli : toute école reçoit des pages offertes, donc
        /// elle est calibrée bien avant d'avoir à payer.
        /// </summary>
        public int OcrDefaultStudentsPerPage { get; set; } = 15;

        /// <summary>
        /// Interrupteur de la VENTE, distinct de celui de la lecture. Couper la
        /// vente laisse vivre les pages offertes et les pages déjà achetées ;
        /// couper <see cref="OcrEnabled"/> ferme tout. Deux robinets, parce que
        /// les deux pannes ne sont pas la même.
        /// </summary>
        public bool OcrPurchaseEnabled { get; set; } = true;

        /// <summary>
        /// Pages achetables en une fois. Borne haute : une école qui se trompe
        /// d'un zéro ne doit pas payer dix fois ce qu'elle voulait.
        /// </summary>
        public int OcrMaxPagesPerPurchase { get; set; } = 300;

        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 🔑 <b>LE point unique de la majoration au payeur.</b> Multiplicateur
        /// appliqué au montant CIBLE pour obtenir ce qu'on débite réellement.
        /// Les 7 appelants passent tous par lui (§199).
        /// </summary>
        /// <remarks>
        /// <para><b>La règle qu'il fait tenir</b> : si l'école annonce T, elle
        /// doit encaisser T dans son wallet <b>et</b> pouvoir retirer T, sans
        /// que la plateforme avance quoi que ce soit. Deux prélèvements
        /// s'interposent :</para>
        /// <list type="number">
        ///   <item>à l'encaissement, la réserve ne reçoit que <c>C × (1 − a)</c>
        ///   de ce qu'on débite au payeur ;</item>
        ///   <item>au décaissement, sortir T de la réserve en coûte
        ///   <c>T × (1 + b)</c>, le frais étant prélevé <b>en plus</b>
        ///   (<c>on_top</c>).</item>
        /// </list>
        /// <para>La neutralité s'écrit donc <c>C × (1 − a) = T × (1 + b)</c>,
        /// soit <c>C = T × (1 + b) / (1 − a)</c>. Avec a = 5,37 % et b = 1,77 %,
        /// le multiplicateur vaut <b>1,0755</b> — une majoration de
        /// <b>7,55 %</b>, contre 7,14 % saisis auparavant.</para>
        /// <para>🔴 <b>Ne jamais « simplifier » en 1 + a + b.</b> C'est
        /// précisément l'erreur d'origine : les taux ne s'additionnent pas, ils
        /// se composent. Et ne pas écrire <c>1 / ((1 − a)(1 − b))</c> non plus —
        /// cette forme-là suppose un frais de retrait prélevé DANS le montant
        /// envoyé, ce que <c>fee_mode = "on_top"</c> ne fait pas.</para>
        /// <para>⚠️ <b>Date de péremption</b> : l'article 8.2 du contrat Wave
        /// interdit toute majoration au payeur. À la bascule vers Wave direct,
        /// cette majoration disparaît et c'est le wallet qui devra porter le
        /// frais de retrait (§145).</para>
        /// </remarks>
        [NotMapped]
        public double ParentFeeMultiplier
        {
            get
            {
                // Bornage défensif : ces deux taux sont saisis par un humain et
                // se retrouvent au dénominateur. Un 100 saisi par erreur
                // donnerait +∞, donc un montant à débiter absurde — on préfère
                // un multiplicateur borné à un crash au moment de payer.
                var a = Math.Clamp(PayinFeePercent, 0.0, 95.0) / 100.0;
                var b = Math.Clamp(PayoutFeePercent, 0.0, 95.0) / 100.0;
                return (1.0 + b) / (1.0 - a);
            }
        }

        /// <summary>
        /// Majoration au payeur en pourcentage humain (7.55 = +7,55 %), telle
        /// qu'affichée au parent et au donateur. <b>DÉRIVÉE</b> depuis le
        /// 2026-09-13 : plus de colonne, donc plus de valeur qui dérive.
        /// </summary>
        /// <remarks>
        /// Le nom est conservé tel quel dans les réponses API : tous les écrans
        /// parents et les pages publiques qui l'affichent continuent de marcher
        /// et montrent le nouveau taux sans qu'une seule ligne ne change chez
        /// eux. Seul l'écran SuperAdmin qui l'ÉDITAIT a changé — il édite
        /// désormais <see cref="PayinFeePercent"/> et affiche ceci en lecture
        /// seule (un champ accepté mais jamais utilisé serait un §196).
        /// </remarks>
        [NotMapped]
        public double ParentFeePercent => (ParentFeeMultiplier - 1.0) * 100.0;

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
