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

        /// <summary>
        /// Captures d'écran de l'application montrées sur la page publique,
        /// sous forme de tableau JSON de chemins relatifs
        /// (<c>["/uploads/landing/a.jpg", …]</c>). Trois au plus.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>Elles ne sont PAS livrées avec l'application, et c'est le
        /// point.</b> Une capture codée en dur vieillit à chaque refonte
        /// d'écran et finit par montrer un produit qui n'existe plus — sur la
        /// première page que voit un directeur. Posées ici, elles se
        /// remplacent depuis le back-office, sans redéploiement.
        ///
        /// <c>null</c> ou tableau vide = la section entière ne s'affiche pas.
        /// Mieux vaut pas de captures que de fausses captures.
        /// </remarks>
        public string? LandingScreenshotsJson { get; set; }

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
        // ===== Ce que le prestataire prélève — SAISI, jamais deviné ======
        //
        // 🔴 AUCUNE VALEUR PAR DÉFAUT ICI, ET C'EST DÉLIBÉRÉ.
        //
        // Ces quatre champs sont `double?` sans initialisateur. Tant qu'ils ne
        // sont pas renseignés, aucun paiement « frais au payeur » ne part : le
        // système REFUSE, avec un message qui dit où aller les saisir.
        //
        // L'alternative — une valeur de repli « raisonnable » dans le code —
        // paraît prudente et ne l'est pas. C'est très exactement ce qui a laissé
        // une majoration fausse de 0,45 point tourner QUATRE MOIS : le chiffre
        // existait, il avait l'air juste, personne n'avait de raison de le
        // regarder. Un service arrêté se voit et se répare en trente secondes
        // depuis SuperAdmin ; un calcul faux ne se voit pas.
        //
        // 🔑 ET CE NE SONT PAS DES POURCENTAGES DE FRAIS — ce sont les
        // paramètres d'une RÈGLE. Les frais réels, retrouvés sur les 197
        // paiements de production (197/197 exacts) :
        //
        //     encaissement = round(C × provider %) + ceil( ceil(C × op % HT) × (1+TVA) )
        //     décaissement =                         ceil( ceil(T × op % HT) × (1+TVA) )
        //
        // Le « 1,77 % » qu'on lisait partout est 1,5 % HT + 18 % de TVA, chacun
        // arrondi au franc. Le « 5,37 % » d'encaissement est une moyenne vraie
        // pour AUCUN montant : mesurée, elle va de 5,37 % à 6,05 % selon la
        // taille. D'où Common/Utilities/ProviderFees.cs, qui RÉSOUT au lieu
        // d'appliquer un taux.
        // ================================================================

        /// <summary>
        /// Commission du prestataire de paiement à l'encaissement, en % du
        /// montant débité (SenePay : 3,6). Arrondie au franc le plus proche.
        /// </summary>
        public double? PayinProviderFeePercent { get; set; }

        /// <summary>
        /// Part opérateur à l'encaissement, en % <b>hors taxe</b> du montant
        /// débité (Wave / Orange Money : 1,5). Arrondie au franc supérieur,
        /// puis la TVA s'y ajoute.
        /// </summary>
        public double? PayinOperatorFeePercentHt { get; set; }

        /// <summary>
        /// Part opérateur au décaissement, en % <b>hors taxe</b> du montant
        /// envoyé (1,5). Prélevée <b>en plus</b> du montant
        /// (<c>fee_mode = "on_top"</c>) : sortir T coûte T + ce frais.
        /// </summary>
        public double? PayoutOperatorFeePercentHt { get; set; }

        /// <summary>
        /// TVA appliquée aux commissions opérateur, en % (Sénégal : 18).
        /// </summary>
        /// <remarks>
        /// Champ à part entière plutôt que fondue dans les taux : elle change
        /// par décision de l'État, pas par décision du prestataire, et les deux
        /// ne bougent jamais en même temps.
        /// </remarks>
        public double? FeeVatPercent { get; set; }

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

        /// <summary>
        /// RUTEL — redevance d'utilisation des télécommunications, 5 % au Sénégal.
        /// Elle s'applique au HT <b>avant</b> la TVA, et non à côté d'elle : le
        /// vrai multiplicateur est 1,05 × 1,18 = <b>1,239</b>, pas 1,18.
        ///
        /// <para>Vérifié à l'unité près sur la facture d'août (n° 202608A-094574) :
        /// 10 509 × 1,05 = 11 035, puis × 1,18 = 13 021 — net payé 13 000 après
        /// arrondis. Sans elle, l'écran de comparaison sous-estimait CHAQUE
        /// facture de 5 % et affichait un écart qu'on aurait pris pour des SMS
        /// partis hors d'Idara.</para>
        ///
        /// <para>⚠️ Contrairement aux taux de frais de paiement (§256), un défaut
        /// est ici légitime : couper les SMS parce qu'un taux de taxe n'a pas été
        /// saisi serait pire que de les envoyer. Ce champ ne décide de rien — il
        /// ne sert qu'à comparer notre total à celui de l'opérateur.</para>
        /// </summary>
        public double SmsRutelPercent { get; set; } = 5.0;

        // ================================================================
        // Garde-fous des codes d'authentification (anti « SMS pumping »)
        // ----------------------------------------------------------------
        // Deux portes envoient un SMS à la demande d'un inconnu : la création
        // de compte par numéro, et la réinitialisation du mot de passe. Le
        // trafic honnête y est minuscule — quelques inscriptions par mois — et
        // c'est cette asymétrie qui permet d'être très sévère sans jamais
        // gêner un vrai directeur.
        // ================================================================

        /// <summary>
        /// Préfixes mobiles réellement attribués au Sénégal (70, 75, 76, 77, 78).
        /// 71, 72, 73, 74 et 79 n'existent chez aucun opérateur : les refuser
        /// divise par deux l'espace de tir d'un robot.
        ///
        /// <para>⚠️ En réglage, jamais en dur : l'ARTP peut en ouvrir un demain.
        /// Vide = aucun contrôle de préfixe (on n'enferme personne dehors sur un
        /// réglage oublié).</para>
        ///
        /// <para>⚠️ Ne vaut que pour les parcours PUBLICS. L'enregistrement d'un
        /// responsable d'élève continue d'accepter tout mobile en 7 — durcir
        /// partout refuserait des numéros déjà en base.</para>
        /// </summary>
        public string SenegalMobilePrefixes { get; set; } = "70,75,76,77,78";

        /// <summary>
        /// Bourse quotidienne des SMS d'authentification, en FCFA — <b>commune
        /// aux deux portes</b> : même budget, même risque, un seul chiffre à régler.
        ///
        /// <para>🔑 C'est la barrière qui <b>borne le dégât</b>, et la seule qui
        /// tienne contre une attaque venue de centaines d'adresses. Sans elle,
        /// une attaque consomme le budget SMS commun et éteint <b>tout</b> —
        /// reçus de paiement, identifiants, rappels — pour la journée entière.
        /// Avec elle, elle ne ferme que l'envoi de codes ; l'email reste ouvert.</para>
        ///
        /// <para>150 F ≈ 23 SMS/jour, soit environ trente fois le trafic réel.</para>
        /// </summary>
        public long SmsAuthDailyCapFcfa { get; set; } = 150;

        /// <summary>Numéros distincts qu'une même adresse peut viser en une heure.</summary>
        public int AuthCodeMaxPerIpPerHour { get; set; } = 3;

        /// <summary>Numéros distincts qu'une même adresse peut viser en un jour.</summary>
        public int AuthCodeMaxPerIpPerDay { get; set; } = 5;

        /// <summary>Délai minimal entre deux codes pour un même destinataire (secondes).</summary>
        public int AuthCodeMinSecondsBetween { get; set; } = 120;

        /// <summary>Codes maximum pour un même destinataire sur 24 h.</summary>
        public int AuthCodeMaxPerRecipientPerDay { get; set; } = 3;

        /// <summary>Codes maximum pour un même destinataire sur 30 jours.</summary>
        public int AuthCodeMaxPerRecipientPerMonth { get; set; } = 5;

        /// <summary>
        /// Taux de vérification minimal, en pourcentage, sur la dernière heure.
        ///
        /// <para>🔑 <b>Le seul signal qui mesure l'intention plutôt que le
        /// volume.</b> Un vrai directeur saisit le code qu'il reçoit ; un robot
        /// qui tire des numéros au hasard ne le saisit jamais — il n'a pas les
        /// téléphones. Sous ce seuil, l'envoi de codes par SMS se ferme seul et
        /// l'alerte part. Un vrai pic d'inscriptions garde 80 à 90 % et passe
        /// sans encombre.</para>
        /// </summary>
        public int AuthCodeMinVerifyRatePercent { get; set; } = 30;

        /// <summary>
        /// Nombre d'envois en dessous duquel le taux de vérification ne décide
        /// de rien — trois codes non saisis un dimanche matin ne sont pas une
        /// attaque.
        /// </summary>
        public int AuthCodeVerifyRateMinSamples { get; set; } = 10;

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
        /// 🔑 <b>Le calculateur de frais, construit depuis les taux saisis.</b>
        /// Tout ce qui touche à l'argent du payeur passe par lui — jamais par
        /// un pourcentage recopié (§199 : une règle, un seul lieu).
        /// </summary>
        /// <remarks>
        /// Il peut être <b>non configuré</b> : c'est un état normal, pas une
        /// anomalie. Tester <c>Fees.IsConfigured</c> avant d'initier un
        /// encaissement en mode « frais au payeur », et refuser proprement
        /// sinon. Les valeurs nulles deviennent <c>-1</c>, donc hors des bornes
        /// admises — impossible de calculer par accident sur un zéro.
        /// </remarks>
        [NotMapped]
        public Common.Utilities.ProviderFees Fees => new()
        {
            PayinProviderPercent = PayinProviderFeePercent ?? -1,
            PayinOperatorPercentHt = PayinOperatorFeePercentHt ?? -1,
            PayoutOperatorPercentHt = PayoutOperatorFeePercentHt ?? -1,
            VatPercent = FeeVatPercent ?? -1,
        };

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
