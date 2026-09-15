#!/usr/bin/env node
/**
 * Garde-fou des DEUX règles d'or de la recherche.
 *
 *   ① Un accent n'empêche jamais de trouver.
 *   ② Un nom en caractères arabes se trouve en latin, et réciproquement.
 *
 * Ce script vérifie les trois façons dont ces règles peuvent se défaire en
 * silence — et « en silence » est le mot important : aucune ne casse la
 * compilation, aucune ne fait échouer un test existant, et l'utilisateur voit
 * seulement « je ne trouve pas cet enfant ».
 *
 *   1. La table de translittération arabe→latin existe en DOUBLE (C# pour le
 *      serveur, Dart pour les filtres locaux). Si l'une bouge sans l'autre, la
 *      recherche devient incohérente d'un écran à l'autre.
 *
 *   2. Une colonne comparée par ILIKE doit être PLIÉE — soit par
 *      `AppDbContext.Unaccent(...)`, soit parce qu'elle est déjà un
 *      `SearchIndex`. Une colonne non pliée face à un terme plié ne renvoie
 *      plus rien dès qu'il y a un accent.
 *
 *   3. Un motif de recherche doit venir d'un point unique
 *      (`TransactionSearch.Pattern`, `PersonSearch.*`, `SearchText.Fold`). Un
 *      motif bricolé à la main n'est pas plié, et se heurte alors à une colonne
 *      qui l'est — même symptôme, cause inverse.
 *
 * Usage : node Idara.API/Tools/check-search-parity.js
 */

const fs = require('fs');
const path = require('path');

const RACINE = path.resolve(__dirname, '..', '..');
const CS = path.join(RACINE, 'Idara.API', 'Common', 'Utilities', 'SearchText.cs');
const DART = path.join(RACINE, 'idara', 'lib', 'core', 'utils', 'search_text.dart');

let echecs = 0;
const ko = (msg) => { echecs++; console.log(`  ✗ ${msg}`); };
const okk = (msg) => console.log(`  ✓ ${msg}`);

// ---------------------------------------------------------------------------
// 1. Les deux tables de translittération disent-elles la même chose ?
// ---------------------------------------------------------------------------

function tableCs(src) {
  // ['ش'] = "ch",
  const table = new Map();
  const re = /\['(.)'\]\s*=\s*"([^"]*)"/g;
  let m;
  while ((m = re.exec(src)) !== null) table.set(m[1], m[2]);
  return table;
}

function tableDart(src) {
  // 'ش': 'ch',
  const bloc = src.match(/arabeVersLatin\s*=\s*\{([\s\S]*?)\n\};/);
  if (!bloc) return new Map();
  const table = new Map();
  const re = /'(.)'\s*:\s*'([^']*)'/g;
  let m;
  while ((m = re.exec(bloc[1])) !== null) table.set(m[1], m[2]);
  return table;
}

console.log('1) Table de translittération arabe → latin (C# ↔ Dart)');

if (!fs.existsSync(CS)) ko(`introuvable : ${CS}`);
if (!fs.existsSync(DART)) ko(`introuvable : ${DART}`);

if (echecs === 0) {
  const srcCs = fs.readFileSync(CS, 'utf8');
  const srcDart = fs.readFileSync(DART, 'utf8');
  const a = tableCs(srcCs);
  const b = tableDart(srcDart);

  if (a.size === 0) ko('table C# vide ou illisible');
  if (b.size === 0) ko('table Dart vide ou illisible');

  const manquantsDart = [...a.keys()].filter((k) => !b.has(k));
  const manquantsCs = [...b.keys()].filter((k) => !a.has(k));
  const divergents = [...a.keys()].filter((k) => b.has(k) && b.get(k) !== a.get(k));

  if (manquantsDart.length)
    ko(`présents en C# et absents du Dart : ${manquantsDart.join(' ')}`);
  if (manquantsCs.length)
    ko(`présents en Dart et absents du C# : ${manquantsCs.join(' ')}`);
  for (const k of divergents)
    ko(`« ${k} » vaut "${a.get(k)}" en C# mais "${b.get(k)}" en Dart`);

  if (!manquantsDart.length && !manquantsCs.length && !divergents.length)
    okk(`${a.size} correspondances identiques des deux côtés`);

  // Les deux choix d'usage sénégalais : ils ne se retrouvent dans aucune norme
  // de translittération, donc un « nettoyage » bien intentionné les casserait.
  for (const [lettre, attendu, pourquoi] of [
    ['ش', 'ch', 'on écrit Cheikh, pas Sheikh'],
    ['ث', 's', 'عثمان s’écrit Ousmane, pas Outhmane'],
    ['غ', 'g', 'دياغن donne Diagne'],
    ['ع', '', 'muet en usage francophone'],
  ]) {
    if (a.get(lettre) !== attendu)
      ko(`« ${lettre} » doit valoir "${attendu}" (${pourquoi}) — trouvé "${a.get(lettre)}"`);
  }
}

// ---------------------------------------------------------------------------
// 2 & 3. Les colonnes sont-elles pliées, et les motifs viennent-ils d'un
//        point unique ?
// ---------------------------------------------------------------------------

function fichiersCs(dir, acc = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (!['bin', 'obj', 'Migrations'].includes(e.name)) fichiersCs(p, acc);
    } else if (e.name.endsWith('.cs')) acc.push(p);
  }
  return acc;
}

console.log('');
console.log('2) Toute colonne comparée par ILIKE est pliée');

const PLIE = ['AppDbContext.Unaccent(', 'Unaccent(', 'SearchIndex'];
let colonnes = 0;
const nonPliees = [];

for (const f of fichiersCs(path.join(RACINE, 'Idara.API'))) {
  const src = fs.readFileSync(f, 'utf8');
  const lignes = src.split('\n');
  lignes.forEach((ligne, i) => {
    const marque = 'EF.Functions.ILike(';
    let k = ligne.indexOf(marque);
    while (k >= 0) {
      // Premier argument : jusqu'à la virgule de profondeur 0.
      let prof = 0;
      let fin = -1;
      for (let j = k + marque.length; j < ligne.length; j++) {
        const c = ligne[j];
        if (c === '(' || c === '[') prof++;
        else if (c === ')' || c === ']') prof--;
        else if (c === ',' && prof === 0) { fin = j; break; }
      }
      // Argument coupé en fin de ligne : on prend ce qu'on voit.
      const colonne = ligne.slice(k + marque.length, fin < 0 ? ligne.length : fin);
      colonnes++;
      if (!PLIE.some((p) => colonne.includes(p))) {
        nonPliees.push(`${path.relative(RACINE, f)}:${i + 1}  ${colonne.trim()}`);
      }
      k = ligne.indexOf(marque, k + 1);
    }
  });
}

if (nonPliees.length) {
  ko(`${nonPliees.length} colonne(s) comparée(s) sans pliage :`);
  nonPliees.forEach((l) => console.log(`      ${l}`));
  console.log('      → enveloppez la colonne dans AppDbContext.Unaccent(...),');
  console.log('        ou comparez SearchIndex (déjà plié).');
} else {
  okk(`${colonnes} comparaisons ILIKE, toutes pliées`);
}

console.log('');
console.log('3) Les motifs de recherche viennent d’un point unique');

const SOURCES_OK = [
  'TransactionSearch.Pattern', 'TransactionSearch.PhonePattern',
  'PersonSearch.TroisMotifs', 'PersonSearch.MotifTexteLibre',
  'SearchText.Fold', 'SearchText.MotifsPourIndex', 'OuLeNomCorrespond',
];
const bricoles = [];

for (const f of fichiersCs(path.join(RACINE, 'Idara.API'))) {
  const src = fs.readFileSync(f, 'utf8');
  if (!src.includes('EF.Functions.ILike(')) continue;
  const lignes = src.split('\n');
  lignes.forEach((ligne, i) => {
    // Un motif écrit à la main dans l'appel : $"%{x}%"
    if (/EF\.Functions\.ILike\([^;]*\$"%\{/.test(ligne)) {
      // Acceptable si le terme provient d'un point unique dans le même fichier.
      if (!SOURCES_OK.some((s) => src.includes(s))) {
        bricoles.push(`${path.relative(RACINE, f)}:${i + 1}  ${ligne.trim().slice(0, 110)}`);
      }
    }
  });
}

if (bricoles.length) {
  ko(`${bricoles.length} motif(s) construit(s) hors des points uniques :`);
  bricoles.forEach((l) => console.log(`      ${l}`));
  console.log('      → pliez le terme avec SearchText.Fold(...) avant de le mettre en motif.');
} else {
  okk('aucun motif bricolé sans pliage');
}

console.log('');
if (echecs === 0) {
  console.log('✅ Les deux règles d’or tiennent : 3 contrôles, 0 en échec.');
  process.exit(0);
} else {
  console.log(`❌ ${echecs} contrôle(s) en échec.`);
  process.exit(1);
}
