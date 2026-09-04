using System.Net;
using System.Text;
using Idara.API.Models;

namespace Idara.API.Services
{
    /// <summary>
    /// Rend les deux documents juridiques d'Idara en HTML, côté serveur.
    ///
    /// <para><b>Pourquoi côté serveur, comme la page des tarifs</b> : ces pages
    /// doivent être lisibles par un visiteur sans compte, par un moteur de
    /// recherche, et par un avocat qui ouvre un lien — pas seulement dans
    /// l'application. Et surtout : les mentions d'identité viennent de la base,
    /// donc une correction (un NINEA obtenu, un siège qui déménage) ne demande
    /// aucun redéploiement. Une mention légale qui exige une mise en production
    /// pour être corrigée finit par rester fausse.</para>
    ///
    /// <para><b>Ce qui manque est OMIS, jamais inventé ni laissé en blanc.</b>
    /// Tant que le NINEA n'est pas renseigné, la ligne n'existe pas — une page
    /// juridique qui affiche « NINEA : » suivi de rien est pire que muette.</para>
    ///
    /// <para>⚠️ §189 : ces pages contiennent des apostrophes françaises à
    /// foison. Tout est écrit en <i>raw string literals</i> C# (<c>"""</c>) —
    /// aucun échappement, donc aucun risque de casser silencieusement la page.
    /// Le seul JavaScript présent est nul : il n'y en a pas.</para>
    /// </summary>
    public static class LegalHtmlRenderer
    {
        private static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        /// <summary>Le nom de l'éditeur, ou un repli neutre et exact.</summary>
        private static string Editor(PlatformSettings p) =>
            string.IsNullOrWhiteSpace(p.LegalCompanyName) ? "Pyranil Solution" : p.LegalCompanyName!;

        private static string Contact(PlatformSettings p) =>
            string.IsNullOrWhiteSpace(p.LegalContactEmail)
                ? "contact.pyranil@gmail.com"
                : p.LegalContactEmail!;

        // ================================================================
        // ===== Habillage commun =====
        // ================================================================

        private static string Page(string title, string subtitle, string version, string body) => $$"""
<!DOCTYPE html>
<html lang="fr" dir="ltr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="theme-color" content="#0B744D">
<title>{{title}} — Idara</title>
<meta name="description" content="{{subtitle}}">
<style>
  :root { --green:#0B744D; --green-dark:#064A31; --text:#0F172A; --muted:#475569;
          --border:#E2E8F0; --soft:#F8FAFC; --warn:#E8830C; }
  * { box-sizing:border-box; }
  body { margin:0; font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Arial,sans-serif;
         color:var(--text); line-height:1.65; background:#fff; }
  .wrap { max-width:820px; margin:0 auto; padding:32px 20px 80px; }
  header { border-bottom:3px solid var(--green); padding-bottom:18px; margin-bottom:8px; }
  .brand { font-weight:800; color:var(--green); font-size:15px; letter-spacing:.02em; }
  h1 { color:var(--green); margin:8px 0 6px; font-size:28px; line-height:1.2; }
  .updated { color:var(--muted); font-size:13.5px; }
  nav.toc { background:var(--soft); border:1px solid var(--border); border-radius:12px;
            padding:16px 20px; margin:24px 0 8px; }
  nav.toc div { font-weight:700; font-size:13px; text-transform:uppercase;
                letter-spacing:.06em; color:var(--muted); margin-bottom:8px; }
  nav.toc ol { margin:0; padding-left:20px; columns:2; column-gap:28px; }
  nav.toc li { margin:2px 0; break-inside:avoid; }
  nav.toc a { color:var(--text); text-decoration:none; font-size:14px; }
  nav.toc a:hover { color:var(--green); text-decoration:underline; }
  h2 { color:var(--green); font-size:20px; margin-top:38px; scroll-margin-top:16px; }
  h3 { font-size:16px; margin-top:24px; }
  a { color:var(--green); }
  ul, ol.body { padding-left:22px; }
  li { margin:4px 0; }
  table { width:100%; border-collapse:collapse; margin:14px 0; font-size:14px; }
  th, td { text-align:left; padding:9px 11px; border-bottom:1px solid var(--border); vertical-align:top; }
  th { background:var(--soft); font-size:12.5px; text-transform:uppercase;
       letter-spacing:.05em; color:var(--muted); }
  .box { background:var(--soft); border:1px solid var(--border); border-radius:12px;
         padding:14px 18px; margin:16px 0; }
  .box.key { border-left:3px solid var(--green); border-radius:0 12px 12px 0; }
  .box.warn { border-left:3px solid var(--warn); border-radius:0 12px 12px 0; }
  .box p { margin:0; }
  .box p + p { margin-top:8px; }
  footer { margin-top:56px; padding-top:18px; border-top:1px solid var(--border);
           color:var(--muted); font-size:13px; }
  .scroll { overflow-x:auto; }
  @media (max-width:640px) { nav.toc ol { columns:1; } h1 { font-size:24px; } }
</style>
</head>
<body>
<div class="wrap">
<header>
  <div class="brand">Idara</div>
  <h1>{{title}}</h1>
  <div class="updated">Version {{version}} &middot; {{subtitle}}</div>
</header>
{{body}}
<footer>
  Idara est édité par %%EDITOR%%. Ce document est disponible en permanence à cette adresse.
  Pour toute question : <a href="mailto:%%CONTACT%%">%%CONTACT%%</a>.
</footer>
</div>
</body>
</html>
""";

        /// <summary>Bloc « Identité de l'éditeur », monté ligne par ligne.</summary>
        private static string IdentityBlock(PlatformSettings p)
        {
            var sb = new StringBuilder();
            sb.Append("<table>");
            void Row(string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                sb.Append($"<tr><th>{H(label)}</th><td>{H(value)}</td></tr>");
            }
            Row("Éditeur", Editor(p));
            Row("Forme juridique", p.LegalForm);
            Row("Siège social", p.LegalAddress);
            Row("NINEA", p.LegalNinea);
            Row("RCCM", p.LegalRccm);
            Row("Représentant légal", p.LegalRepresentative);
            Row("Courriel", Contact(p));
            Row("Téléphone", p.LegalContactPhone);
            Row("Déclaration CDP", p.LegalCdpNumber);
            sb.Append("</table>");
            return sb.ToString();
        }

        // ================================================================
        // ===== Conditions générales d'utilisation =====
        // ================================================================

        public static string Terms(PlatformSettings p)
        {
            var editor = H(Editor(p));
            var contact = H(Contact(p));
            var identity = IdentityBlock(p);

            var body = $$"""
<nav class="toc">
  <div>Sommaire</div>
  <ol>
    <li><a href="#objet">Objet et acceptation</a></li>
    <li><a href="#definitions">Définitions</a></li>
    <li><a href="#editeur">Identité de l'éditeur</a></li>
    <li><a href="#service">Description du service</a></li>
    <li><a href="#comptes">Comptes, rôles et accès</a></li>
    <li><a href="#etablissement">Obligations de l'établissement</a></li>
    <li><a href="#utilisateurs">Obligations des utilisateurs</a></li>
    <li><a href="#contenus">Contenus publiés</a></li>
    <li><a href="#paiement">Encaissement des paiements</a></li>
    <li><a href="#dons">Collectes de dons</a></li>
    <li><a href="#portefeuille">Portefeuille et retraits</a></li>
    <li><a href="#abonnement">Abonnement et facturation</a></li>
    <li><a href="#impayes">Impayés et lecture seule</a></li>
    <li><a href="#sms">Notifications, SMS et coûts</a></li>
    <li><a href="#ia">Fonctions d'intelligence artificielle</a></li>
    <li><a href="#surveillance">Contrôle des flux financiers</a></li>
    <li><a href="#disponibilite">Disponibilité et maintenance</a></li>
    <li><a href="#pi">Propriété intellectuelle</a></li>
    <li><a href="#donnees">Données personnelles</a></li>
    <li><a href="#responsabilite">Responsabilité</a></li>
    <li><a href="#duree">Durée, résiliation, restitution</a></li>
    <li><a href="#modification">Modification des conditions</a></li>
    <li><a href="#droit">Droit applicable et litiges</a></li>
    <li><a href="#annexe">Annexe : sous-traitance des données</a></li>
  </ol>
</nav>

<h2 id="objet">1. Objet et acceptation</h2>
<p>Les présentes conditions générales d'utilisation régissent l'accès et l'usage
de la plateforme <strong>Idara</strong>, éditée par {{editor}} : application mobile,
application web, pages publiques et interfaces associées.</p>
<p>Elles forment un contrat entre {{editor}} et, d'une part, <strong>l'établissement
scolaire</strong> qui souscrit au service, d'autre part <strong>chaque personne</strong>
qui utilise la plateforme à quelque titre que ce soit : direction, personnel,
enseignant, surveillant, parent ou responsable d'élève, donateur.</p>
<div class="box key">
  <p><strong>L'acceptation est expresse et horodatée.</strong> À la création d'un
  compte, l'utilisateur est informé qu'en poursuivant il accepte les présentes
  conditions et la politique de confidentialité. La date et la version acceptées
  sont conservées. L'usage du service après une modification vaut acceptation de
  la version en vigueur.</p>
</div>
<p>Un utilisateur qui n'accepte pas ces conditions doit cesser d'utiliser la
plateforme et peut demander la suppression de son compte.</p>

<h2 id="definitions">2. Définitions</h2>
<ul>
  <li><strong>Plateforme</strong> : l'ensemble des services Idara, quel que soit le support.</li>
  <li><strong>Établissement</strong> : l'école, le daara ou l'institution qui souscrit
      au service et administre son espace.</li>
  <li><strong>Utilisateur</strong> : toute personne disposant d'un accès, à quelque titre que ce soit.</li>
  <li><strong>Responsable</strong> : le parent ou tuteur rattaché à un ou plusieurs élèves.</li>
  <li><strong>Donateur</strong> : toute personne effectuant un don via un lien de collecte.</li>
  <li><strong>Prestataire de paiement</strong> : l'établissement agréé qui exécute les
      opérations d'encaissement et de décaissement.</li>
  <li><strong>Portefeuille</strong> : le solde de l'établissement au sein de la plateforme,
      constitué des sommes encaissées pour son compte et non encore retirées.</li>
</ul>

<h2 id="editeur">3. Identité de l'éditeur</h2>
{{identity}}

<h2 id="service">4. Description du service</h2>
<p>Idara est un <strong>logiciel de gestion scolaire</strong>. Il met à la disposition
de l'établissement, selon la formule souscrite :</p>
<ul>
  <li>la gestion des élèves, des classes, des niveaux, des matières et de l'année scolaire ;</li>
  <li>le suivi pédagogique : présences, notes, bulletins, suivi coranique, cahier de suivi,
      emploi du temps, journal de l'établissement ;</li>
  <li>la gestion du personnel et des accès ;</li>
  <li>la facturation des frais de scolarité, l'encaissement en ligne et en espèces,
      les reçus, les relances et le suivi des impayés ;</li>
  <li>la caisse, les bénéficiaires, les transferts et les retraits ;</li>
  <li>les collectes de dons par lien public ;</li>
  <li>les notifications aux familles par notification mobile et, le cas échéant, par SMS.</li>
</ul>
<div class="box">
  <p><strong>Idara n'est ni un établissement de paiement, ni un établissement de
  monnaie électronique, ni un intermédiaire financier.</strong> Les opérations
  d'encaissement et de décaissement sont exécutées par un prestataire agréé.
  {{editor}} fournit un logiciel qui déclenche ces opérations et en tient le
  registre pour le compte de l'établissement.</p>
</div>

<h2 id="comptes">5. Comptes, rôles et accès</h2>
<p>L'accès est nominatif. Chaque compte porte un rôle qui détermine ce que son
titulaire peut voir et faire :</p>
<div class="scroll"><table>
  <tr><th>Rôle</th><th>Portée</th></tr>
  <tr><td>Direction</td><td>Administre l'établissement : élèves, classes, personnel, argent, réglages.</td></tr>
  <tr><td>Personnel administratif</td><td>Gère les élèves, les classes et les encaissements. N'accède pas aux réglages financiers.</td></tr>
  <tr><td>Enseignant</td><td>Accède uniquement aux classes et matières auxquelles il est affecté.</td></tr>
  <tr><td>Surveillant</td><td>Pointage et vie de l'établissement, dans le périmètre défini par la direction.</td></tr>
  <tr><td>Observateur</td><td>Consultation seule. Toute écriture est refusée par le serveur.</td></tr>
  <tr><td>Responsable d'élève</td><td>Accède au suivi et aux factures de ses propres enfants, et à eux seuls.</td></tr>
</table></div>
<p>L'utilisateur est responsable de la confidentialité de ses identifiants et de
toute activité effectuée depuis son compte. Il informe sans délai l'établissement
ou {{editor}} de toute utilisation non autorisée.</p>
<p>La direction de l'établissement crée, modifie et supprime les accès de son
personnel. Elle est seule juge de l'étendue des droits qu'elle accorde et en
assume les conséquences.</p>

<h2 id="etablissement">6. Obligations de l'établissement</h2>
<p>L'établissement s'engage à :</p>
<ul>
  <li>fournir des informations exactes lors de son inscription, et les tenir à jour ;</li>
  <li>n'enregistrer que les données <strong>nécessaires</strong> à sa mission éducative
      et administrative ;</li>
  <li>informer les familles de l'usage de la plateforme et recueillir, lorsque la loi
      l'exige, les consentements nécessaires — notamment pour les photographies
      d'élèves et les données de santé ;</li>
  <li>n'utiliser les coordonnées des familles que pour la vie de l'établissement,
      à l'exclusion de toute prospection commerciale ;</li>
  <li>maintenir à jour la liste de son personnel et retirer les accès de toute
      personne qui quitte l'établissement ;</li>
  <li>respecter la réglementation applicable à son activité, y compris en matière
      fiscale, sociale et de protection des données.</li>
</ul>
<div class="box warn">
  <p><strong>L'établissement décide seul de ce qu'il enregistre.</strong> {{editor}}
  héberge et traite ces données <em>pour son compte</em> et sur ses instructions.
  L'établissement demeure responsable de la licéité des données qu'il saisit, des
  documents qu'il téléverse et des messages qu'il fait envoyer.</p>
</div>

<h2 id="utilisateurs">7. Obligations des utilisateurs</h2>
<p>Chaque utilisateur s'interdit :</p>
<ul>
  <li>d'accéder ou de tenter d'accéder à des données qui ne relèvent pas de son rôle ;</li>
  <li>de partager ses identifiants, ou d'utiliser ceux d'autrui ;</li>
  <li>d'extraire massivement les données de la plateforme, de les revendre ou de les
      détourner de leur finalité ;</li>
  <li>de perturber le fonctionnement du service, d'en contourner les limitations
      techniques ou d'en tester la sécurité sans autorisation écrite ;</li>
  <li>de publier un contenu illicite, diffamatoire, haineux, ou portant atteinte
      aux droits d'un tiers.</li>
</ul>

<h2 id="contenus">8. Contenus publiés</h2>
<p>L'établissement peut publier des contenus visibles hors de son espace privé :
logo, photographie de couverture d'une collecte de dons, description d'une
collecte, nom public de l'établissement.</p>
<div class="box warn">
  <p><strong>Ces contenus sont publics et l'établissement en est seul
  responsable.</strong> Une image associée à une collecte de dons est accessible à
  toute personne disposant du lien : l'établissement garantit qu'il dispose des
  droits et des autorisations nécessaires, notamment le consentement des parents
  pour toute image d'élève identifiable.</p>
  <p>{{editor}} n'exerce aucun contrôle éditorial <em>a priori</em> sur ces contenus
  et décline toute responsabilité à leur égard. {{editor}} se réserve le droit de
  retirer sans préavis tout contenu manifestement illicite ou signalé comme tel,
  et d'en informer l'établissement.</p>
</div>

<h2 id="paiement">9. Encaissement des paiements</h2>
<p>Les paiements en ligne sont exécutés par un prestataire de paiement agréé. Le
payeur est redirigé vers l'interface de ce prestataire ; {{editor}} ne collecte
et ne conserve <strong>aucun code secret, aucun identifiant bancaire et aucune
donnée de carte</strong>.</p>
<ul>
  <li>Le paiement est réputé effectué lorsque le prestataire le confirme.</li>
  <li>Une opération refusée, annulée ou expirée ne donne lieu à aucun mouvement.</li>
  <li>Les frais du prestataire sont supportés selon le réglage choisi par
      l'établissement, porté à la connaissance du payeur lorsque celui-ci les supporte.</li>
  <li>Un encaissement en espèces est saisi par l'établissement, sous sa responsabilité,
      et ne transite pas par la plateforme.</li>
  <li>Toute contestation d'un paiement se règle entre le payeur, l'établissement et
      le prestataire. {{editor}} fournit les éléments techniques dont il dispose.</li>
</ul>

<h2 id="dons">10. Collectes de dons</h2>
<p>L'établissement peut créer des liens de collecte permettant à toute personne de
lui adresser un don sans créer de compte.</p>
<ul>
  <li>Les dons sont versés <strong>à l'établissement</strong>, jamais à {{editor}}.</li>
  <li>L'établissement définit l'intitulé, la description, l'objectif éventuel et
      le montant ; il répond de l'exactitude de ces informations et de l'usage des
      sommes reçues.</li>
  <li>Le donateur déclare son nom et son numéro : ces informations ne sont pas
      vérifiées et sont communiquées à l'établissement bénéficiaire.</li>
  <li>Un don est <strong>définitif</strong>. Il n'ouvre droit à aucune contrepartie,
      ni à aucune déduction fiscale, et le reçu émis le mentionne expressément.</li>
  <li>Des montants minimum et maximum s'appliquent, ainsi que des limitations
      destinées à prévenir les usages abusifs.</li>
</ul>

<h2 id="portefeuille">11. Portefeuille et retraits</h2>
<p>Les sommes encaissées pour le compte de l'établissement alimentent son
portefeuille au sein de la plateforme. Elles lui appartiennent et sont
individualisées dans les écritures.</p>
<ul>
  <li>L'établissement demande un retrait vers un numéro de paiement mobile qu'il désigne
      et dont il garantit l'exactitude.</li>
  <li>Un montant minimum de retrait s'applique.</li>
  <li>Les délais dépendent du prestataire et des opérateurs ; ils ne sont pas garantis.</li>
  <li>Un retrait dont le sort reste indéterminé est maintenu en vérification, les fonds
      restant réservés, jusqu'à confirmation du prestataire. Aucune restitution n'est
      opérée sur un état incertain.</li>
  <li>Les frais de décaissement sont indiqués avant validation.</li>
</ul>

<h2 id="abonnement">12. Abonnement et facturation</h2>
<p>L'accès au service est soumis à un abonnement dont le montant dépend de la
formule et de l'effectif de l'établissement. La grille est publiée et accessible
en permanence.</p>
<ul>
  <li>L'abonnement est prélevé sur le portefeuille de l'établissement à chaque échéance.</li>
  <li>Une facture est émise pour chaque échéance et adressée à la direction.</li>
  <li>Si l'effectif dépasse le plafond de la formule souscrite, celle-ci est ajustée
      au palier correspondant et l'établissement en est informé.</li>
  <li>Les prix peuvent être révisés ; toute révision est notifiée avant son entrée
      en vigueur et n'affecte pas une période déjà réglée.</li>
</ul>

<h2 id="impayes">13. Impayés et lecture seule</h2>
<p>À défaut de provision suffisante à l'échéance :</p>
<ol class="body">
  <li>une période de tolérance s'ouvre, pendant laquelle le service reste entier ;</li>
  <li>à son terme, l'espace de l'établissement passe en <strong>lecture seule</strong> :
      la consultation demeure possible, les écritures sont refusées ;</li>
  <li>faute de régularisation, l'accès est suspendu.</li>
</ol>
<div class="box">
  <p><strong>Les familles ne sont jamais privées d'accès</strong> en raison d'un impayé
  de l'établissement : elles conservent la consultation du suivi de leurs enfants
  et la possibilité de régler leurs frais.</p>
</div>

<h2 id="sms">14. Notifications, SMS et coûts</h2>
<p>La plateforme envoie des notifications mobiles, gratuites, et peut envoyer des
SMS lorsque cela est nécessaire : identifiants de connexion, code de
vérification, information de paiement, rappels.</p>
<ul>
  <li>Les SMS inclus dans le service sont supportés par {{editor}}.</li>
  <li>Certaines options, activées <strong>explicitement</strong> par l'établissement,
      lui sont refacturées <strong>au coût réel</strong> et ajoutées à sa facture
      d'abonnement. Le coût unitaire est indiqué avant activation, et le montant
      accumulé est consultable à tout moment.</li>
  <li>{{editor}} applique des limites de dépense et peut suspendre les envois en cas
      d'usage anormal, afin de protéger l'établissement comme la plateforme.</li>
  <li>{{editor}} n'est pas revendeur de services de télécommunication.</li>
</ul>

<h2 id="ia">15. Fonctions d'intelligence artificielle</h2>
<p>Certaines fonctions optionnelles reposent sur un service d'intelligence
artificielle, notamment la lecture automatique d'un registre papier photographié
en vue d'un import.</p>
<div class="box warn">
  <p>L'usage de ces fonctions suppose la <strong>transmission du document
  photographié à un prestataire technique situé hors du Sénégal</strong>, aux seules
  fins de transcription. Ces fonctions sont <strong>facultatives</strong> : elles ne
  s'activent que sur action de l'établissement, qui en est informé au moment de
  l'usage.</p>
  <p>La transcription est un travail de lecture, non de vérification :
  l'établissement <strong>contrôle et corrige</strong> le résultat avant tout
  enregistrement. Aucune donnée n'est écrite sans validation humaine.</p>
</div>

<h2 id="surveillance">16. Contrôle des flux financiers</h2>
<p>Afin de prévenir la fraude, l'usage abusif et le blanchiment de capitaux, et
pour répondre à ses obligations comme à celles de son prestataire de paiement,
{{editor}} conserve et peut consulter le détail des opérations financières de
chaque établissement : montants, dates, statuts, références, identité déclarée
des payeurs et des donateurs.</p>
<p>Cette consultation est réservée aux personnes habilitées de {{editor}}, tracée,
et strictement limitée à cette finalité. Les données correspondantes peuvent être
communiquées aux autorités compétentes sur réquisition régulière.</p>

<h2 id="disponibilite">17. Disponibilité et maintenance</h2>
<p>{{editor}} met en œuvre les moyens raisonnables pour assurer la disponibilité du
service, sans garantie d'absence d'interruption. Le service peut être suspendu
pour maintenance, mise à jour ou incident.</p>
<p>Le service dépend de tiers — hébergeur, opérateurs de téléphonie, prestataire
de paiement, fournisseurs de notifications — dont les défaillances échappent au
contrôle de {{editor}}.</p>
<p>{{editor}} procède à des sauvegardes régulières de la base de données. Ces
sauvegardes visent la continuité du service et ne constituent pas un service
d'archivage pour le compte de l'établissement.</p>

<h2 id="pi">18. Propriété intellectuelle</h2>
<p>La plateforme, son code, ses interfaces, ses marques et sa documentation sont
la propriété exclusive de {{editor}}. L'abonnement confère un <strong>droit d'usage
personnel, non exclusif et non cessible</strong>, pour la durée de l'abonnement.</p>
<p>Il est interdit de copier, décompiler, modifier ou dériver tout ou partie de la
plateforme, sauf dans les limites permises par la loi.</p>
<p><strong>Les données saisies par l'établissement restent sa propriété.</strong>
{{editor}} n'y acquiert aucun droit et ne les exploite à aucune autre fin que la
fourniture du service.</p>

<h2 id="donnees">19. Données personnelles</h2>
<p>Le traitement des données personnelles est décrit dans la
<a href="/confidentialite">politique de confidentialité</a>, qui fait partie
intégrante des présentes conditions. L'annexe ci-dessous précise les rôles
respectifs de l'établissement et de {{editor}}.</p>

<h2 id="responsabilite">20. Responsabilité</h2>
<p>{{editor}} est tenu d'une obligation de moyens. Sa responsabilité ne peut être
engagée :</p>
<ul>
  <li>pour les données saisies par l'établissement ou ses utilisateurs, leur exactitude
      et leur licéité ;</li>
  <li>pour les décisions prises par l'établissement à partir de ces données ;</li>
  <li>pour les contenus publiés par l'établissement ;</li>
  <li>en cas de perte d'identifiants, de partage de compte ou d'usage non autorisé ;</li>
  <li>pour les défaillances des tiers mentionnés à l'article 17 ;</li>
  <li>pour les préjudices indirects, notamment perte d'exploitation, de clientèle
      ou d'image.</li>
</ul>
<p>En tout état de cause et sauf faute lourde ou dolosive, la responsabilité de
{{editor}} envers un établissement est plafonnée aux sommes effectivement versées
par celui-ci au titre de l'abonnement durant les <strong>douze mois</strong>
précédant le fait générateur.</p>
<p>Aucune de ces limitations ne s'applique aux dommages corporels ni aux cas où la
loi les interdit.</p>

<h2 id="duree">21. Durée, résiliation, restitution</h2>
<p>Le contrat est conclu pour la durée de l'abonnement et se renouvelle par
périodes successives, sauf résiliation.</p>
<ul>
  <li>L'établissement peut résilier à tout moment ; l'abonnement s'achève au terme
      de la période en cours, sans remboursement du prorata.</li>
  <li>{{editor}} peut résilier en cas de manquement grave, après mise en demeure
      restée sans effet pendant quinze jours, ou immédiatement en cas d'usage illicite.</li>
  <li>Un utilisateur peut demander la suppression de son compte ; celle-ci ne fait pas
      disparaître les écritures financières que la loi impose de conserver.</li>
</ul>
<div class="box key">
  <p><strong>Restitution.</strong> À la résiliation, l'établissement peut demander une
  copie de ses données dans un format exploitable. Cette demande doit intervenir
  dans les <strong>trente jours</strong> suivant la fin du contrat. Passé un délai de
  conservation de <strong>quatre-vingt-dix jours</strong>, les données sont supprimées
  ou anonymisées, à l'exception de celles dont la conservation est légalement
  requise.</p>
</div>

<h2 id="modification">22. Modification des conditions</h2>
<p>{{editor}} peut modifier les présentes conditions. Toute modification
substantielle est portée à la connaissance des utilisateurs par une information
dans l'application ou par courriel, au moins <strong>trente jours</strong> avant son
entrée en vigueur. L'usage du service après cette date vaut acceptation.</p>

<h2 id="droit">23. Droit applicable et litiges</h2>
<p>Les présentes conditions sont régies par le <strong>droit sénégalais</strong>.</p>
<p>En cas de différend, les parties recherchent d'abord une solution amiable, par
écrit adressé à <a href="mailto:{{contact}}">{{contact}}</a>. À défaut d'accord dans
un délai de trente jours, le litige relève de la compétence des
<strong>tribunaux de Dakar</strong>, sauf disposition légale impérative contraire.</p>

<h2 id="annexe">Annexe — Sous-traitance des données personnelles</h2>
<div class="box key">
  <p><strong>Cette annexe fait partie du contrat.</strong> Elle définit ce que chacun
  fait des données des élèves et des familles, et engage {{editor}} envers
  l'établissement.</p>
</div>
<h3>A.1 Rôles</h3>
<p>Pour les données des élèves, des familles et du personnel, l'établissement
détermine les finalités et les moyens : il est <strong>responsable du
traitement</strong>. {{editor}} traite ces données pour son compte et sur ses
instructions : il est <strong>sous-traitant</strong>.</p>
<p>Pour les données strictement nécessaires au fonctionnement de la plateforme —
comptes, journaux techniques, facturation de l'abonnement, prévention de la
fraude — {{editor}} agit comme <strong>responsable de traitement</strong>.</p>
<h3>A.2 Engagements de {{editor}} comme sous-traitant</h3>
<ul>
  <li>Ne traiter les données que sur instruction documentée de l'établissement, et
      pour les seules finalités du service.</li>
  <li>Ne pas exploiter ces données à ses propres fins, ne pas les vendre, ne pas les
      céder, ne pas les utiliser pour entraîner un modèle d'intelligence artificielle.</li>
  <li>Garantir la confidentialité par les personnes habilitées, tenues à un engagement
      de discrétion.</li>
  <li>Mettre en œuvre des mesures de sécurité appropriées : chiffrement des accès,
      cloisonnement des données par établissement, contrôle des rôles, sauvegardes,
      journalisation des accès sensibles.</li>
  <li>Assister l'établissement pour répondre aux demandes d'exercice des droits.</li>
  <li>Notifier l'établissement <strong>sans délai injustifié</strong> après avoir eu
      connaissance d'une violation de données le concernant, avec les éléments connus.</li>
  <li>Supprimer ou restituer les données au terme du contrat, dans les conditions de
      l'article 21.</li>
</ul>
<h3>A.3 Sous-traitants ultérieurs</h3>
<p>{{editor}} recourt aux prestataires listés dans la
<a href="/confidentialite#sous-traitants">politique de confidentialité</a>, qui
présente pour chacun sa fonction et son pays d'établissement. L'établissement en
est informé et peut formuler des objections motivées ; l'ajout d'un prestataire
est publié sur cette même page.</p>
<h3>A.4 Localisation</h3>
<p>Les données sont hébergées dans l'Union européenne. Certains prestataires
techniques sont situés hors du Sénégal et de l'Union européenne. Les transferts
correspondants sont décrits dans la politique de confidentialité.</p>
<h3>A.5 Audit</h3>
<p>L'établissement peut demander à {{editor}}, une fois par an et par écrit, les
informations nécessaires pour vérifier le respect de la présente annexe.</p>
""";

            return Page("Conditions générales d'utilisation",
                "Le contrat entre Idara, les établissements et leurs utilisateurs",
                H(p.LegalVersion), body)
                .Replace("%%EDITOR%%", editor)
                .Replace("%%CONTACT%%", contact);
        }

        // ================================================================
        // ===== Politique de confidentialité =====
        // ================================================================

        public static string Privacy(PlatformSettings p)
        {
            var editor = H(Editor(p));
            var contact = H(Contact(p));
            var identity = IdentityBlock(p);
            var cdp = string.IsNullOrWhiteSpace(p.LegalCdpNumber)
                ? ""
                : $"<p>Ce traitement a fait l'objet d'une déclaration auprès de la Commission de protection des données personnelles sous la référence <strong>{H(p.LegalCdpNumber)}</strong>.</p>";

            var body = $$"""
<nav class="toc">
  <div>Sommaire</div>
  <ol>
    <li><a href="#resume">En bref</a></li>
    <li><a href="#qui">Qui traite vos données</a></li>
    <li><a href="#roles">Responsable ou sous-traitant</a></li>
    <li><a href="#donnees">Données collectées</a></li>
    <li><a href="#finalites">Finalités et fondements</a></li>
    <li><a href="#acces">Qui accède aux données</a></li>
    <li><a href="#sous-traitants">Prestataires</a></li>
    <li><a href="#transferts">Transferts hors du Sénégal</a></li>
    <li><a href="#duree">Durées de conservation</a></li>
    <li><a href="#securite">Sécurité</a></li>
    <li><a href="#droits">Vos droits</a></li>
    <li><a href="#mineurs">Données des mineurs</a></li>
    <li><a href="#sante">Données de santé</a></li>
    <li><a href="#images">Photographies</a></li>
    <li><a href="#stockage">Stockage sur votre appareil</a></li>
    <li><a href="#ia">Intelligence artificielle</a></li>
    <li><a href="#violation">Violation de données</a></li>
    <li><a href="#modification">Modifications</a></li>
    <li><a href="#contact">Contact et réclamation</a></li>
  </ol>
</nav>

<h2 id="resume">1. En bref</h2>
<div class="box key">
  <p>Idara est un logiciel utilisé par des écoles. Les données des élèves y sont
  saisies <strong>par l'école</strong>, pour son suivi pédagogique et administratif.
  Les parents accèdent au suivi de leurs propres enfants et peuvent régler les
  frais en ligne.</p>
  <p><strong>Nous ne vendons jamais vos données. Nous ne faisons aucune publicité.
  Nous n'utilisons pas vos données pour entraîner un modèle d'intelligence
  artificielle.</strong></p>
</div>

<h2 id="qui">2. Qui traite vos données</h2>
{{identity}}
{{cdp}}

<h2 id="roles">3. Responsable ou sous-traitant : qui décide de quoi</h2>
<div class="scroll"><table>
  <tr><th>Données</th><th>Qui décide</th><th>Notre rôle</th></tr>
  <tr>
    <td>Élèves, familles, personnel, suivi pédagogique, documents</td>
    <td>L'école</td>
    <td>Sous-traitant : nous hébergeons et traitons pour son compte, sur ses instructions</td>
  </tr>
  <tr>
    <td>Comptes, connexions, journaux techniques, incidents</td>
    <td>{{editor}}</td>
    <td>Responsable de traitement</td>
  </tr>
  <tr>
    <td>Abonnement de l'école, facturation, prévention de la fraude</td>
    <td>{{editor}}</td>
    <td>Responsable de traitement</td>
  </tr>
  <tr>
    <td>Dons reçus par une école, identité déclarée du donateur</td>
    <td>L'école bénéficiaire et {{editor}}</td>
    <td>Chacun pour sa part : l'école pour la relation avec le donateur, nous pour l'exécution et la traçabilité</td>
  </tr>
</table></div>
<p>Concrètement : pour une question sur les données d'un élève, adressez-vous
<strong>d'abord à l'école</strong>. Nous l'assistons et ne modifions ces données que
sur sa demande.</p>

<h2 id="donnees">4. Données collectées</h2>
<h3>4.1 Comptes</h3>
<ul>
  <li>Direction et personnel : nom, courriel, numéro de téléphone, mot de passe
      conservé sous forme chiffrée et irréversible, rôle, langue.</li>
  <li>Enseignants, surveillants, parents : nom, numéro de téléphone servant
      d'identifiant, courriel facultatif.</li>
  <li>Dates de création, de dernière connexion, et statut du compte.</li>
</ul>
<h3>4.2 Données scolaires, saisies par l'école</h3>
<ul>
  <li>Élève : identité, date et lieu de naissance, sexe, photographie facultative,
      adresse, matricule, classe, régime d'hébergement, date d'entrée et de sortie.</li>
  <li>Famille : identité et coordonnées du père, de la mère, du responsable légal,
      lien de parenté, contact d'urgence.</li>
  <li>Suivi : présences, notes, appréciations, bulletins, progression coranique,
      cahier de suivi, emploi du temps.</li>
  <li>Documents téléversés par l'école : pièces d'identité, extraits de naissance,
      certificats, et tout document qu'elle choisit d'y joindre.</li>
  <li>Santé, facultatif : allergies, traitements, contact du médecin.</li>
</ul>
<h3>4.3 Paiements</h3>
<ul>
  <li>Montant, date, statut, référence de transaction, moyen utilisé, et le cas
      échéant le numéro de téléphone du payeur.</li>
  <li>Factures, reçus, écritures de caisse, retraits et bénéficiaires.</li>
  <li><strong>Aucun code secret, aucun identifiant bancaire, aucune donnée de carte
      n'est collecté ni conservé par Idara</strong> : le paiement est exécuté sur
      l'interface du prestataire agréé.</li>
</ul>
<h3>4.4 Dons</h3>
<ul>
  <li>Nom déclaré, numéro de téléphone déclaré, organisation le cas échéant, montant,
      date. Ces informations ne sont pas vérifiées.</li>
  <li>Le donateur peut demander que son nom ne figure pas sur la page publique.
      L'école bénéficiaire le voit toujours.</li>
</ul>
<h3>4.5 Notifications</h3>
<ul>
  <li>Identifiant technique d'appareil, nécessaire aux notifications mobiles.</li>
  <li>Registre des envois : destinataire, type de message, date, statut, coût.</li>
</ul>
<h3>4.6 Données techniques</h3>
<ul>
  <li>Journaux de fonctionnement : horodatage, adresse appelée, code de réponse,
      durée, identifiant du compte.</li>
  <li>Rapports d'incident en cas d'erreur de l'application, avec le contexte technique
      nécessaire à la correction.</li>
  <li><strong>Aucun traceur publicitaire. Aucune revente. Aucun profilage commercial.</strong></li>
</ul>

<h2 id="finalites">5. Finalités et fondements</h2>
<div class="scroll"><table>
  <tr><th>Finalité</th><th>Fondement</th></tr>
  <tr><td>Gérer la scolarité et le suivi des élèves</td><td>Contrat entre la famille et l'école, mission éducative de l'école</td></tr>
  <tr><td>Fournir l'accès et gérer les comptes</td><td>Exécution du contrat</td></tr>
  <tr><td>Encaisser les frais et émettre les reçus</td><td>Exécution du contrat, obligation comptable</td></tr>
  <tr><td>Informer les familles (notification, SMS)</td><td>Intérêt légitime de l'école à joindre les familles</td></tr>
  <tr><td>Facturer l'abonnement</td><td>Exécution du contrat, obligation comptable</td></tr>
  <tr><td>Prévenir la fraude et le blanchiment</td><td>Obligation légale, intérêt légitime</td></tr>
  <tr><td>Assurer la sécurité et corriger les pannes</td><td>Intérêt légitime</td></tr>
  <tr><td>Photographie de l'élève, données de santé</td><td>Consentement recueilli par l'école</td></tr>
  <tr><td>Lecture automatisée d'un registre photographié</td><td>Demande explicite de l'école</td></tr>
</table></div>

<h2 id="acces">6. Qui accède aux données</h2>
<ul>
  <li><strong>Au sein de l'école</strong> : selon le rôle. Un enseignant n'accède qu'aux
      classes et matières auxquelles il est affecté ; un observateur consulte sans
      pouvoir écrire ; un parent ne voit que ses propres enfants.</li>
  <li><strong>Entre écoles</strong> : aucun accès. Les données de chaque établissement
      sont cloisonnées, et ce cloisonnement est vérifié à chaque requête par le serveur.</li>
  <li><strong>Chez {{editor}}</strong> : un nombre restreint de personnes habilitées,
      pour l'exploitation technique, le support et le contrôle des flux financiers.</li>
</ul>
<div class="box">
  <p><strong>Contrôle des flux financiers.</strong> Pour prévenir la fraude et le
  blanchiment de capitaux, et pour répondre à nos obligations comme à celles de
  notre prestataire de paiement, les personnes habilitées de {{editor}} peuvent
  consulter le détail des opérations financières de chaque école : montants,
  dates, statuts, références, identité déclarée des payeurs et donateurs. Cet
  accès est tracé et limité à cette finalité.</p>
</div>
<p>Les données peuvent être communiquées aux autorités compétentes sur réquisition
régulière.</p>

<h2 id="sous-traitants">7. Prestataires</h2>
<div class="scroll"><table>
  <tr><th>Prestataire</th><th>Fonction</th><th>Pays</th></tr>
  <tr><td>Hetzner Online GmbH</td><td>Hébergement des serveurs et de la base de données</td><td>Allemagne</td></tr>
  <tr><td>Prestataire de paiement agréé</td><td>Encaissements et décaissements mobiles</td><td>Sénégal</td></tr>
  <tr><td>Sonatel / Orange</td><td>Acheminement des SMS</td><td>Sénégal</td></tr>
  <tr><td>Google (Firebase)</td><td>Notifications mobiles</td><td>États-Unis</td></tr>
  <tr><td>Google (courriel)</td><td>Envoi des courriels de service</td><td>États-Unis</td></tr>
  <tr><td>Prestataire d'intelligence artificielle</td><td>Lecture automatisée d'un registre photographié, sur demande de l'école</td><td>États-Unis</td></tr>
</table></div>
<p>Chacun n'accède qu'aux données nécessaires à sa fonction et ne peut les utiliser
à d'autres fins. Toute évolution de cette liste est publiée sur cette page.</p>

<h2 id="transferts">8. Transferts hors du Sénégal</h2>
<p>Les serveurs qui hébergent vos données sont situés en <strong>Allemagne</strong>.
Certains prestataires sont établis aux <strong>États-Unis</strong> ; les transferts
correspondants sont limités aux données strictement nécessaires à leur fonction :
un identifiant d'appareil pour les notifications, un message pour l'acheminement
d'un courriel, une photographie de registre pour une transcription demandée par
l'école.</p>
<p>Ces transferts s'effectuent sur la base des garanties contractuelles proposées
par ces prestataires.</p>

<h2 id="duree">9. Durées de conservation</h2>
<div class="scroll"><table>
  <tr><th>Données</th><th>Durée</th></tr>
  <tr><td>Données scolaires d'un élève</td><td>Tant que l'école est abonnée, puis 90 jours après la fin du contrat</td></tr>
  <tr><td>Compte utilisateur</td><td>Jusqu'à sa suppression, puis anonymisation</td></tr>
  <tr><td>Écritures financières, factures, reçus</td><td>10 ans, conformément aux obligations comptables</td></tr>
  <tr><td>Registre des envois de messages</td><td>12 mois, puis anonymisation</td></tr>
  <tr><td>Journaux techniques</td><td>30 jours</td></tr>
  <tr><td>Rapports d'incident</td><td>12 mois</td></tr>
  <tr><td>Sauvegardes de la base</td><td>30 jours par rotation</td></tr>
</table></div>
<p>La suppression d'un compte ne fait pas disparaître les écritures financières que
la loi impose de conserver : elles sont anonymisées lorsque c'est possible.</p>

<h2 id="securite">10. Sécurité</h2>
<ul>
  <li>Chiffrement de bout en bout des communications (HTTPS).</li>
  <li>Mots de passe conservés sous forme chiffrée irréversible ; jamais en clair,
      jamais consultables, y compris par nous.</li>
  <li>Cloisonnement des données par établissement, vérifié à chaque requête.</li>
  <li>Accès aux serveurs restreint et journalisé ; base de données non exposée
      sur l'internet public.</li>
  <li>Sauvegardes quotidiennes, dont la restauration est testée.</li>
  <li>Limitation des tentatives de connexion et des envois, pour contenir les abus.</li>
</ul>

<h2 id="droits">11. Vos droits</h2>
<p>Vous disposez d'un droit d'accès, de rectification, d'effacement, d'opposition,
de limitation, et du droit d'obtenir une copie de vos données.</p>
<ul>
  <li><strong>Données d'un élève ou d'une famille</strong> : adressez-vous à l'école,
      qui décide de ces données. Nous l'assistons dans sa réponse.</li>
  <li><strong>Votre compte, vos données de connexion</strong> : écrivez-nous à
      <a href="mailto:{{contact}}">{{contact}}</a>.</li>
</ul>
<p>Nous répondons dans un délai d'un mois. La suppression d'un compte peut aussi
être demandée depuis la page prévue à cet effet.</p>

<h2 id="mineurs">12. Données des mineurs</h2>
<p>Les élèves sont, pour la plupart, mineurs. Leurs données sont saisies par
l'école dans le cadre de sa mission éducative, sous la responsabilité de celle-ci
et avec l'information des parents.</p>
<p>Les élèves ne disposent pas de compte : l'accès à leur suivi est ouvert à
l'école et à leurs responsables légaux.</p>

<h2 id="sante">13. Données de santé</h2>
<p>L'école peut consigner des informations de santé utiles à la sécurité de
l'élève : allergies, traitements en cours, contact d'urgence, médecin traitant.</p>
<p>Ces informations sont <strong>facultatives</strong>, recueillies sous la
responsabilité de l'école avec le consentement des parents, et accessibles aux
seules personnes de l'établissement qui en ont besoin.</p>

<h2 id="images">14. Photographies</h2>
<p>La photographie d'un élève est facultative. Elle est visible de l'école et des
responsables de l'élève.</p>
<div class="box warn">
  <p>Les images qu'une école publie sur une <strong>page de collecte de dons</strong>
  sont en revanche <strong>publiques</strong> : toute personne disposant du lien peut
  les voir. Il revient à l'école de s'assurer qu'elle dispose des autorisations
  nécessaires avant de publier une image où un élève est identifiable.</p>
</div>

<h2 id="stockage">15. Stockage sur votre appareil</h2>
<p>L'application et les pages web conservent sur votre appareil des informations
strictement fonctionnelles : votre session, la langue choisie, un cache des
écrans consultés pour les afficher hors ligne, les saisies en attente d'envoi, et
— sur une page de don — le nom et le numéro que vous avez saisis, afin de ne pas
vous les redemander.</p>
<p>Ces informations restent sur votre appareil, ne sont transmises à personne, et
disparaissent lorsque vous effacez les données du navigateur ou désinstallez
l'application. <strong>Aucun traceur publicitaire n'est utilisé.</strong></p>

<h2 id="ia">16. Intelligence artificielle</h2>
<p>Une fonction facultative permet à l'école d'importer son registre papier en le
photographiant : l'image est transmise à un prestataire d'intelligence
artificielle qui en transcrit le contenu.</p>
<ul>
  <li>Cette fonction ne s'active que sur action explicite de l'école.</li>
  <li>La transcription est <strong>relue et corrigée par l'école</strong> avant tout
      enregistrement : rien n'est écrit sans validation humaine.</li>
  <li>Les images transmises ne sont pas utilisées pour entraîner un modèle.</li>
  <li>Aucune décision automatisée n'est prise à l'égard d'un élève.</li>
</ul>

<h2 id="violation">17. Violation de données</h2>
<p>En cas de violation de données susceptible d'engendrer un risque, nous en
informons sans délai injustifié l'école concernée, avec les éléments connus : ce
qui s'est passé, quelles données sont touchées, ce que nous avons fait, et ce que
nous recommandons. Nous informons également l'autorité compétente lorsque la loi
l'exige.</p>

<h2 id="modification">18. Modifications</h2>
<p>Cette politique peut évoluer avec le service. Toute modification substantielle
est portée à la connaissance des utilisateurs au moins trente jours avant son
entrée en vigueur. La version en vigueur est toujours celle publiée à cette
adresse.</p>

<h2 id="contact">19. Contact et réclamation</h2>
<p>Pour toute question sur cette politique ou pour exercer vos droits :
<a href="mailto:{{contact}}">{{contact}}</a>.</p>
<p>Si notre réponse ne vous satisfait pas, vous pouvez saisir la
<strong>Commission de protection des données personnelles (CDP)</strong> du Sénégal.</p>
""";

            return Page("Politique de confidentialité",
                "Ce que devient chaque donnée confiée à Idara",
                H(p.LegalVersion), body)
                .Replace("%%EDITOR%%", editor)
                .Replace("%%CONTACT%%", contact);
        }
    }
}
