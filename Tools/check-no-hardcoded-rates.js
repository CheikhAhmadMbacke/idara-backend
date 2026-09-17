#!/usr/bin/env node
/*
 * Contrôle : AUCUN taux de commission écrit en dur dans le code.
 *
 * POURQUOI CET OUTIL EXISTE
 * -------------------------
 * La majoration demandée aux familles a été fausse pendant quatre mois. Pas
 * parce que le calcul était compliqué — il tient en une ligne — mais parce
 * qu'un chiffre avait été SAISI une fois, puis recopié, commenté, documenté,
 * et que plus personne n'avait de raison de le regarder. Les taux du
 * prestataire, eux, avaient bougé.
 *
 * La règle posée le 2026-09-13 est donc absolue : les taux vivent dans
 * `PlatformSettings`, saisis depuis le back-office, et NULLE PART ailleurs.
 * Pas de valeur par défaut C#, pas de constante « au cas où », pas de repli
 * « raisonnable ». Une plateforme dont les taux ne sont pas renseignés REFUSE
 * d'encaisser — un service arrêté se voit, un calcul faux ne se voit pas.
 *
 * CE QUI EST VÉRIFIÉ
 *   1. Aucun littéral ressemblant à un taux de commission ou à un
 *      multiplicateur de majoration dans le code métier du paiement.
 *   2. Les propriétés de frais de `PlatformSettings` n'ont AUCUN
 *      initialisateur — sinon une base neuve repartirait sur une valeur
 *      inventée au lieu de refuser.
 *   3. 🔴 Personne ne RECOMPOSE un taux hors de `ProviderFees`. Lire les
 *      colonnes brutes pour refaire `provider + opHt × (1+TVA)` crée une
 *      SECONDE implémentation de la règle, qui diverge en silence au premier
 *      changement de structure. C'est arrivé : les pages juridiques — un texte
 *      contractuel — portaient leur propre arithmétique.
 *   4. Aucun taux écrit en toutes lettres dans ce que LIT un utilisateur :
 *      traductions Flutter et pages HTML servies par le backend. Un « 2 % »
 *      figé dans une phrase survit à tous les changements de grille, et
 *      personne ne pense à le relire.
 *
 * LA RÈGLE, EN UNE PHRASE : un seul endroit calcule les frais
 * (`Common/Utilities/ProviderFees.cs`), un seul endroit les stocke
 * (`PlatformSettings`), et tout écran, page, facture ou reçu qui affiche un
 * taux le tient de là.
 *
 * CE QUI EST TOLÉRÉ, ET POURQUOI
 *   - `Migrations/` : une migration POSE des valeurs, c'est son rôle. Elles y
 *     sont datées, relues, et deviennent de la donnée modifiable.
 *   - Les fichiers de ce dossier `Tools/`.
 *   - Une ligne portant la dérogation `// taux-en-dur-voulu: <raison>`.
 *
 * Même esprit que check-migrations.js (§254) et check-fee-neutrality.js :
 * ce qui ne se voit pas à la relecture doit se vérifier par une commande.
 *
 * Usage : node Idara.API/Tools/check-no-hardcoded-rates.js
 */

const fs = require('fs');
const path = require('path');

const RACINE = path.join(__dirname, '..');
const WAIVER = 'taux-en-dur-voulu:';

/** Dossiers où un taux écrit noir sur blanc serait un défaut. */
const SURVEILLES = ['Controllers', 'Services', 'Models', 'DTOs', 'Common'];

/**
 * Littéraux interdits. On vise les nombres qui ne peuvent être QUE des taux de
 * commission ou des multiplicateurs de majoration — pas n'importe quel nombre,
 * sinon le contrôle crierait sur les tailles de page et les délais.
 */
const INTERDITS = [
  // Taux de commission, sous toutes leurs écritures
  /\b0?\.0(36|177|15|537|54|714|8)\b/,
  /\b(3\.6|1\.77|1\.5|5\.37|5\.40|5\.4|7\.14|7\.545|7\.579|7\.59|8\.0)\b/,
  // Multiplicateurs de majoration
  /\b1\.0(8|177|714|75|755|7545|7579)\b/,
  // Le coefficient de TVA sénégalaise
  /\b1\.18\b/,
];

/** Le fichier qui décrit la règle a le droit de la NOMMER dans sa doc. */
function estCommentaire(ligne) {
  const t = ligne.trim();
  return t.startsWith('//') || t.startsWith('///') || t.startsWith('*') || t.startsWith('/*');
}

const echecs = [];

function fichiers(dir) {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...fichiers(p));
    else if (e.name.endsWith('.cs')) out.push(p);
  }
  return out;
}

// ====================================================================
// 1. Pas de littéral de taux dans le code métier
// ====================================================================
for (const dossier of SURVEILLES) {
  const abs = path.join(RACINE, dossier);
  if (!fs.existsSync(abs)) continue;

  for (const fichier of fichiers(abs)) {
    const rel = path.relative(RACINE, fichier);
    const lignes = fs.readFileSync(fichier, 'utf8').split('\n');

    lignes.forEach((ligne, i) => {
      // Un taux CITÉ dans une explication n'est pas un taux APPLIQUÉ. C'est
      // même souhaitable : la documentation doit pouvoir dire ce que la
      // production a mesuré.
      if (estCommentaire(ligne)) return;
      if (ligne.includes(WAIVER)) return;

      for (const re of INTERDITS) {
        const m = ligne.match(re);
        if (m) {
          echecs.push({
            fichier: rel,
            ligne: i + 1,
            valeur: m[0],
            texte: ligne.trim().slice(0, 90),
          });
          return;
        }
      }
    });
  }
}

// ====================================================================
// 2. Aucun défaut C# sur les propriétés de frais
// ====================================================================
const modele = path.join(RACINE, 'Models', 'PlatformSettings.cs');
if (fs.existsSync(modele)) {
  const src = fs.readFileSync(modele, 'utf8');
  // Ciblé sur les frais du PRESTATAIRE DE PAIEMENT. Les tarifs SMS ont leur
  // propre gouvernance (§191) et un défaut y est légitime : couper les SMS
  // parce qu'un prix n'est pas saisi serait pire que de les envoyer.
  const re =
    /public\s+double\??\s+(Payin\w*|Payout\w*|FeeVat\w*|ParentFee\w*)\s*\{[^}]*\}\s*=\s*([^;]+);/g;
  let m;
  while ((m = re.exec(src)) !== null) {
    echecs.push({
      fichier: 'Models/PlatformSettings.cs',
      ligne: src.slice(0, m.index).split('\n').length,
      valeur: m[2].trim(),
      texte: `${m[1]} porte une valeur par défaut`,
      defaut: true,
    });
  }
}

// ====================================================================
// 3. Personne ne RECOMPOSE un taux hors de ProviderFees
// ====================================================================
// Les colonnes brutes n'ont que deux lecteurs légitimes : le modèle, qui les
// assemble en `ProviderFees`, et l'écran de réglages, qui les saisit et les
// relit. Partout ailleurs, on passe par `PlatformSettings.Fees`.
const COLONNES = /Payin(Provider|Operator)FeePercent\w*|PayoutOperatorFeePercentHt|FeeVatPercent/;
const LECTEURS_LEGITIMES = [
  path.join('Models', 'PlatformSettings.cs'),
  path.join('Controllers', 'PlatformSettingsController.cs'),
  path.join('DTOs', 'Platform', 'PlatformSettingsDto.cs'),
];

for (const dossier of SURVEILLES) {
  const abs = path.join(RACINE, dossier);
  if (!fs.existsSync(abs)) continue;

  for (const fichier of fichiers(abs)) {
    const rel = path.relative(RACINE, fichier);
    if (LECTEURS_LEGITIMES.some((l) => rel.endsWith(l))) continue;

    const lignes = fs.readFileSync(fichier, 'utf8').split('\n');
    lignes.forEach((ligne, i) => {
      if (estCommentaire(ligne)) return;
      if (ligne.includes(WAIVER)) return;
      if (!COLONNES.test(ligne)) return;
      echecs.push({
        fichier: rel,
        ligne: i + 1,
        valeur: ligne.match(COLONNES)[0],
        texte: ligne.trim().slice(0, 90),
        recompose: true,
      });
    });
  }
}

// ====================================================================
// 4. Aucun taux en toutes lettres dans ce que LIT un utilisateur
// ====================================================================
// Un taux dans une phrase ne se recalcule jamais : il faut un espace réservé
// (`{percent}`, `{{payinRate}}`) que le serveur remplit.
const TAUX_DANS_UN_TEXTE =
  /(?<![\w.])[0-9]{1,2}([.,][0-9]{1,2})?\s?(%|٪)/;

const TEXTES = [
  path.join(RACINE, '..', 'idara', 'assets', 'translations', 'fr.json'),
  path.join(RACINE, '..', 'idara', 'assets', 'translations', 'ar.json'),
];

// ⚠️ Le Dart aussi : les écrans SuperAdmin écrivent leurs libellés en clair
// (décision produit), et c'est là qu'un « excédent 8% » a survécu des mois à
// la majoration qu'il décrivait. Le fichier de traductions généré est ignoré :
// il recopie les JSON, déjà contrôlés.
function ecransDart(dir) {
  const out = [];
  if (!fs.existsSync(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...ecransDart(p));
    else if (e.name.endsWith('.dart') && !e.name.endsWith('.g.dart')) out.push(p);
  }
  return out;
}
TEXTES.push(...ecransDart(path.join(RACINE, '..', 'idara', 'lib')));

function pagesHtml(dir) {
  const out = [];
  if (!fs.existsSync(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...pagesHtml(p));
    else if (e.name.endsWith('.html')) out.push(p);
  }
  return out;
}
TEXTES.push(...pagesHtml(path.join(RACINE, 'wwwroot')));

for (const fichier of TEXTES) {
  if (!fs.existsSync(fichier)) continue;
  const rel = path.relative(path.join(RACINE, '..'), fichier);
  const lignes = fs.readFileSync(fichier, 'utf8').split('\n');

  lignes.forEach((ligne, i) => {
    // Une feuille de style parle en pourcentages sans arrêt : ce n'est pas du
    // texte lu, c'est de la mise en page.
    if (/width|height|top|left|right|bottom|margin|padding|flex|transform|scale|gradient|font|line-height|translate|gap|rgba|hsl|radius|opacity|background|border|position|inset|stroke|offset|filter|animation|keyframes|calc\(/i.test(ligne)) return;
    if (ligne.includes(WAIVER)) return;
    // Un taux CITÉ dans une explication n'est pas un taux AFFICHÉ — même règle
    // que pour le C#. La documentation doit pouvoir dire ce que la production a
    // mesuré, et raconter d'où vient la règle.
    if (fichier.endsWith('.dart') && estCommentaire(ligne)) return;
    // Un espace réservé rempli par le serveur est exactement ce qu'on veut.
    if (/\{percent\}|\{\{payinRate\}\}|\{\{payoutRate\}\}|\{\}/.test(ligne)) return;
    const m = ligne.match(TAUX_DANS_UN_TEXTE);
    if (!m) return;
    echecs.push({
      fichier: rel,
      ligne: i + 1,
      valeur: m[0],
      texte: ligne.trim().slice(0, 90),
      texteLu: true,
    });
  });
}

// ====================================================================
console.log('');
if (echecs.length === 0) {
  console.log(
    'Aucun taux de commission en dur · aucune recomposition hors de ' +
      'ProviderFees · aucun taux fig\u00e9 dans un texte lu · 4 contr\u00f4les, 0 en \u00e9chec.'
  );
  console.log(
    'Un seul endroit CALCULE (ProviderFees), un seul endroit STOCKE ' +
      '(PlatformSettings).'
  );
  process.exit(0);
}

for (const e of echecs) {
  console.log(`  ÉCHEC ${e.fichier}:${e.ligne} — ${e.valeur}`);
  console.log(`         ${e.texte}`);
  if (e.defaut) {
    console.log(
      '         Une base neuve repartirait sur cette valeur au lieu de REFUSER.'
    );
  }
  if (e.recompose) {
    console.log(
      '         Recomposer un taux ici = une SECONDE règle, qui divergera.'
    );
    console.log(
      '         Passer par PlatformSettings.Fees (PayinRatePercent / PayoutRatePercent).'
    );
  }
  if (e.texteLu) {
    console.log(
      "         Un taux figé dans une phrase survit à tous les changements de grille."
    );
    console.log(
      '         Utiliser un espace réservé que le serveur remplit.'
    );
  }
}
console.log('');
console.log(
  `${echecs.length} taux en dur. Ils vivent dans PlatformSettings, saisis depuis`
);
console.log(
  `le back-office — sinon la rechute est garantie (§255). Dérogation : // ${WAIVER} <raison>`
);
process.exit(1);
