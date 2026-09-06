#!/usr/bin/env node
/*
 * Contrôle des dictionnaires de traduction des pages HTML servies par le backend.
 *
 * POURQUOI CET OUTIL EXISTE
 * -------------------------
 * Ces pages portent leur propre traduction, en dur, dans un objet `I18N` — elles
 * sont servies par le serveur et ne passent donc PAS par le mécanisme Flutter
 * (`fr.json` / `ar.json` + génération de code), qui, lui, échoue au test quand
 * une clé manque.
 *
 * Ici, rien n'échoue : la fonction `t()` retombe silencieusement sur le
 * français. Une clé arabe oubliée ne se voit donc qu'à l'œil, sur la page,
 * en arabe — c'est-à-dire jamais, puisque personne dans l'équipe ne lit
 * l'arabe au quotidien. Le parent arabophone, lui, reçoit une phrase en
 * français au milieu de sa page, au moment précis où il attend la
 * confirmation de son paiement.
 *
 * Même esprit que Tools/check-html-pages.js (§218) : ce qui ne se voit pas
 * à la relecture doit se vérifier par une commande.
 *
 * CE QUI EST VÉRIFIÉ
 *   1. Toutes les langues d'un même dictionnaire portent EXACTEMENT les mêmes clés.
 *   2. Toute clé employée — `t('x')` ou `data-i18n="x"` — existe dans le dictionnaire.
 *   3. Toute clé déclarée est employée quelque part (sinon c'est du texte mort,
 *      ou le signe d'un renommage à moitié fait).
 *
 * Usage : node Idara.API/Tools/check-i18n-pages.js
 * Sort en code 1 dès qu'une page est en défaut.
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..', 'wwwroot');

function htmlFiles(dir) {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...htmlFiles(full));
    else if (e.name.endsWith('.html')) out.push(full);
  }
  return out;
}

// Extrait le littéral objet qui suit `I18N =` en comptant les accolades. Un
// simple gabarit d'expression régulière casserait sur la première accolade
// imbriquée — or ces dictionnaires en contiennent un par langue.
function extractI18n(src) {
  const m = /(?:var|const|let)\s+I18N\s*=\s*\{/.exec(src);
  if (!m) return null;
  const start = m.index + m[0].length - 1;
  let depth = 0, inStr = null, esc = false;
  for (let i = start; i < src.length; i++) {
    const c = src[i];
    if (inStr) {
      if (esc) esc = false;
      else if (c === '\\') esc = true;
      else if (c === inStr) inStr = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') { inStr = c; continue; }
    if (c === '{') depth++;
    else if (c === '}' && --depth === 0) return src.slice(start, i + 1);
  }
  return null;
}

/**
 * Rend le texte des arguments de chaque appel `t(...)`, parenthèses
 * équilibrées et chaînes ignorées — de quoi retrouver les clés d'un ternaire
 * comme d'un appel à deux arguments.
 */
function* tCallArguments(src) {
  const re = /\bt\(/g;
  let m;
  while ((m = re.exec(src)) !== null) {
    let depth = 0, inStr = null, esc = false;
    for (let i = m.index + m[0].length - 1; i < src.length; i++) {
      const c = src[i];
      if (inStr) {
        if (esc) esc = false;
        else if (c === '\\') esc = true;
        else if (c === inStr) inStr = null;
        continue;
      }
      if (c === '"' || c === "'" || c === '`') { inStr = c; continue; }
      if (c === '(') depth++;
      else if (c === ')' && --depth === 0) {
        yield src.slice(m.index + m[0].length, i);
        break;
      }
    }
  }
}

let failed = 0, checked = 0;

for (const file of htmlFiles(ROOT)) {
  const src = fs.readFileSync(file, 'utf8');
  const literal = extractI18n(src);
  const rel = path.relative(path.join(__dirname, '..'), file);
  if (!literal) continue; // page sans dictionnaire : rien à vérifier

  let dict;
  try {
    dict = eval('(' + literal + ')');
  } catch (e) {
    console.log(`  ÉCHEC ${rel} — dictionnaire illisible : ${e.message}`);
    failed++;
    continue;
  }

  const langs = Object.keys(dict);
  const problems = [];

  // 1) Mêmes clés dans toutes les langues, en prenant le français pour référence.
  const ref = dict.fr ? 'fr' : langs[0];
  const refKeys = Object.keys(dict[ref]);
  for (const l of langs) {
    if (l === ref) continue;
    const keys = Object.keys(dict[l]);
    const missing = refKeys.filter((k) => !keys.includes(k));
    const extra = keys.filter((k) => !refKeys.includes(k));
    if (missing.length) problems.push(`manquantes en ${l} : ${missing.join(', ')}`);
    if (extra.length) problems.push(`en trop en ${l} : ${extra.join(', ')}`);
  }

  // 2) et 3) Clés employées vs clés déclarées.
  //
  // ⚠️ On lit TOUS les littéraux de l'appel `t(...)`, pas seulement un premier
  // argument collé à la parenthèse. Les pages écrivent couramment
  // `t(isOrg ? 'rep_name' : 'full_name')` : un gabarit ancré sur `t('` déclare
  // ces clés-là mortes, et la première version de cet outil l'a fait sur six
  // clés bien vivantes. Un contrôle qui crie au loup finit ignoré, donc faux.
  const used = new Set();
  for (const m of src.matchAll(/data-i18n\s*=\s*"([A-Za-z0-9_]+)"/g)) used.add(m[1]);
  for (const call of tCallArguments(src)) {
    for (const lit of call.matchAll(/['"]([A-Za-z0-9_]+)['"]/g)) used.add(lit[1]);
  }

  const unknown = [...used].filter((k) => !refKeys.includes(k));
  const unused = refKeys.filter((k) => !used.has(k));
  if (unknown.length) problems.push(`clés employées mais absentes : ${unknown.join(', ')}`);
  if (unused.length) problems.push(`clés déclarées jamais employées : ${unused.join(', ')}`);

  checked++;
  if (problems.length) {
    failed++;
    console.log(`  ÉCHEC ${rel}`);
    for (const p of problems) console.log(`         ${p}`);
  } else {
    console.log(`  OK   ${rel} — ${langs.join('/')}, ${refKeys.length} clés`);
  }
}

console.log(`\n${checked} dictionnaire(s) vérifié(s) · ${failed} en échec.`);
process.exit(failed ? 1 : 0);
