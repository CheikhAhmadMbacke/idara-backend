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
console.log('');
if (echecs.length === 0) {
  console.log(
    'Aucun taux de commission en dur dans Controllers/, Services/, Models/, ' +
      'DTOs/, Common/ · 0 en échec.'
  );
  console.log(
    'Les taux vivent dans PlatformSettings, saisis depuis le back-office.'
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
}
console.log('');
console.log(
  `${echecs.length} taux en dur. Ils vivent dans PlatformSettings, saisis depuis`
);
console.log(
  `le back-office — sinon la rechute est garantie (§255). Dérogation : // ${WAIVER} <raison>`
);
process.exit(1);
