#!/usr/bin/env node
/**
 * build-assistant-guide.js — rend le guide pas à pas de l'assistant IA avec les
 * libellés EXACTS de l'application (2026-10-07, §300).
 *
 * Le guide (Assets/AssistantGuide/guide.template.md) ne recopie JAMAIS un
 * libellé : il écrit «{{nav.students}}», et ce script le remplace par le texte
 * que l'utilisateur voit à l'écran, lu dans idara/assets/translations/{fr,ar}.json.
 * Un libellé qui change dans l'application change donc dans le guide — à la
 * condition de relancer ce script, d'où le mode --check.
 *
 *   node Idara.API/Tools/build-assistant-guide.js          # régénère guide.fr.md / guide.ar.md
 *   node Idara.API/Tools/build-assistant-guide.js --check  # échoue si périmé ou si une clé n'existe pas
 */
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..', '..');
const dir = path.join(root, 'Idara.API', 'Assets', 'AssistantGuide');
const template = fs.readFileSync(path.join(dir, 'guide.template.md'), 'utf8').replace(/\r\n/g, '\n');
const check = process.argv.includes('--check');

let failures = 0;
const fail = (m) => { failures++; console.error('  ✗ ' + m); };

for (const lang of ['fr', 'ar']) {
  const dict = JSON.parse(fs.readFileSync(path.join(root, 'idara', 'assets', 'translations', `${lang}.json`), 'utf8'));
  const missing = new Set();
  const rendered = template.replace(/\{\{([a-z0-9_.]+)\}\}/g, (_, key) => {
    let v = dict[key];
    if (v === undefined) { missing.add(key); return `{{${key}}}`; }
    // Un libellé à paramètres ({count}, {}) se cite sans ses paramètres.
    v = v.replace(/\s*\{[^}]*\}\s*/g, ' ').replace(/\s+/g, ' ').trim();
    // Les isolats bidi n'ont rien à faire dans un texte lu par l'IA.
    return v.replace(/[⁦-⁩]/g, '');
  });
  for (const k of missing) fail(`${lang}.json : clé « ${k} » absente — le guide citerait un libellé qui n'existe pas`);

  const out = path.join(dir, `guide.${lang}.md`);
  const header = `<!-- GÉNÉRÉ par Tools/build-assistant-guide.js depuis guide.template.md — NE PAS MODIFIER À LA MAIN -->\n`;
  const content = header + rendered;
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
console.log(`Guide de l'assistant : ${topics} sujet(s), libellés FR + AR à jour · 0 en échec.`);
