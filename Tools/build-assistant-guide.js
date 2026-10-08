#!/usr/bin/env node
/**
 * build-assistant-guide.js — rend le guide pas à pas de l'assistant IA avec les
 * libellés EXACTS de l'application, et prouve que chaque bouton cité se trouve
 * bien sur l'écran où le guide l'annonce (2026-10-08, §300).
 *
 * Le modèle (Assets/AssistantGuide/guide.template.md) ne recopie JAMAIS un
 * libellé ni un chemin à la main. Il écrit :
 *
 *     «{{dashboard.payments_overview@presentation/pages/shell/school_money_page.dart}}»
 *
 *  - la CLÉ donne le texte affiché, lu dans idara/assets/translations/{fr,ar}.json ;
 *  - le FICHIER dit sur quel écran ce bouton se trouve. Le contrôle vérifie que
 *    ce fichier existe et contient bien la clé : si une tuile change d'onglet,
 *    le guide est REFUSÉ au lieu d'envoyer l'utilisateur la chercher au mauvais
 *    endroit. Les traductions donnent les MOTS ; le code donne le CHEMIN.
 *
 *   node Idara.API/Tools/build-assistant-guide.js          # régénère guide.fr.md / guide.ar.md
 *   node Idara.API/Tools/build-assistant-guide.js --check  # échoue si périmé, clé absente ou bouton déplacé
 *
 * ⚠️ Lit le dépôt Flutter (idara/) : se lance depuis le poste, comme les autres check-*.
 */
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..', '..');
const dir = path.join(root, 'Idara.API', 'Assets', 'AssistantGuide');
const lib = path.join(root, 'idara', 'lib');
const template = fs.readFileSync(path.join(dir, 'guide.template.md'), 'utf8')
  .replace(/\r\n/g, '\n')
  .replace(/<!--[\s\S]*?-->\n?/g, '');   // les commentaires ne partent pas à l'IA
const check = process.argv.includes('--check');

let failures = 0;
const fail = (m) => { failures++; console.error('  ✗ ' + m); };

// 1) Le CHEMIN : chaque bouton est-il toujours sur l'écran annoncé ?
const PLACEHOLDER = /\{\{([a-z0-9_.]+)(?:@([A-Za-z0-9_./-]+))?\}\}/g;
const sources = new Map();
let placements = 0;
for (const m of template.matchAll(PLACEHOLDER)) {
  const [, key, file] = m;
  if (!file) { fail(`« ${key} » n'indique pas son écran (écrire {{${key}@chemin/vers/ecran.dart}})`); continue; }
  placements++;
  if (!sources.has(file)) {
    const full = path.join(lib, file);
    sources.set(file, fs.existsSync(full) ? fs.readFileSync(full, 'utf8') : null);
  }
  const src = sources.get(file);
  if (src === null) fail(`écran introuvable : lib/${file} (cité pour « ${key} »)`);
  else if (!src.includes(`'${key}'`)) fail(`« ${key} » n'est plus sur l'écran lib/${file} — le guide y enverrait l'utilisateur pour rien`);
}

// 2) Les MOTS : le texte affiché, dans chaque langue.
for (const lang of ['fr', 'ar']) {
  const dict = JSON.parse(fs.readFileSync(path.join(root, 'idara', 'assets', 'translations', `${lang}.json`), 'utf8'));
  const missing = new Set();
  const rendered = template.replace(PLACEHOLDER, (_, key) => {
    let v = dict[key];
    if (v === undefined) { missing.add(key); return `{{${key}}}`; }
    // Un libellé à paramètres ({count}, {}) se cite sans ses paramètres.
    v = v.replace(/\s*\{[^}]*\}\s*/g, ' ').replace(/\s+/g, ' ').trim();
    // Les isolats bidi n'ont rien à faire dans un texte lu par l'IA.
    return v.replace(/[⁦-⁩]/g, '');
  });
  for (const k of missing) fail(`${lang}.json : clé « ${k} » absente — le guide citerait un libellé qui n'existe pas`);

  const out = path.join(dir, `guide.${lang}.md`);
  const content = `<!-- GÉNÉRÉ par Tools/build-assistant-guide.js depuis guide.template.md — NE PAS MODIFIER À LA MAIN -->\n` + rendered;
  if (check) {
    const current = fs.existsSync(out) ? fs.readFileSync(out, 'utf8').replace(/\r\n/g, '\n') : '';
    if (current !== content) fail(`guide.${lang}.md est PÉRIMÉ : relancer node Idara.API/Tools/build-assistant-guide.js`);
  } else {
    fs.writeFileSync(out, content, 'utf8');
  }
}

const topics = (template.match(/^## /gm) || []).length;
if (failures) {
  console.error(`\n${failures} échec(s) — le guide de l'assistant ne correspond plus à l'application.`);
  process.exit(1);
}
console.log(`Guide de l'assistant : ${topics} sujet(s), ${placements} bouton(s) vérifiés sur leur écran, libellés FR + AR à jour · 0 en échec.`);
