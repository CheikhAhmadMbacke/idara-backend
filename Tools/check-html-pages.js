#!/usr/bin/env node
/**
 * Vérifie la syntaxe JavaScript des pages HTML servies par le backend.
 *
 * 🔴 POURQUOI CE FICHIER EXISTE (§189, reproduit le 2026-09-04).
 *
 * Une page HTML répond 200, affiche son en-tête et son CSS, et reste bloquée
 * sur « Chargement… » pour toujours : il suffit d'UNE apostrophe française non
 * échappée dans une chaîne JavaScript pour que le script entier ne s'exécute
 * jamais. Rien ne le signale — ni la compilation, ni les tests, ni un contrôle
 * de code HTTP, ni même une lecture attentive du fichier.
 *
 * C'est arrivé deux fois. La première (2026-08-31) sur la page de résultat de
 * paiement. La seconde (2026-09-04) sur la page de collecte de dons, après un
 * simple remplacement de mot : « le daara » → « l'école ».
 *
 * Ce contrôle extrait chaque bloc <script> et le passe à l'analyseur de Node.
 * Il ne teste pas ce que la page FAIT — il garantit qu'elle s'exécute.
 *
 * Usage :  node Tools/check-html-pages.js
 * Sortie :  code 0 si tout est syntaxiquement valide, 1 sinon.
 */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.join(__dirname, '..', 'wwwroot');

/** Toutes les pages HTML servies, où qu'elles soient. */
function findHtml(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      // Les fichiers téléversés par les écoles ne sont pas notre code.
      if (entry.name === 'uploads') continue;
      findHtml(full, out);
    } else if (entry.name.endsWith('.html')) {
      out.push(full);
    }
  }
  return out;
}

/** Les blocs <script> INLINE (un `src` n'a rien à vérifier ici). */
function inlineScripts(html) {
  const blocks = [];
  const re = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
  let m;
  while ((m = re.exec(html)) !== null) {
    const attrs = m[1] || '';
    if (/\bsrc\s*=/i.test(attrs)) continue;
    if (/type\s*=\s*["'](application\/json|application\/ld\+json)["']/i.test(attrs)) continue;
    // Ligne de départ, pour que l'erreur pointe le bon endroit du FICHIER.
    const line = html.slice(0, m.index).split('\n').length;
    blocks.push({ code: m[2], line });
  }
  return blocks;
}

let failures = 0;
let checked = 0;

for (const file of findHtml(ROOT)) {
  const html = fs.readFileSync(file, 'utf8');
  const rel = path.relative(path.join(__dirname, '..'), file);
  const blocks = inlineScripts(html);
  if (blocks.length === 0) {
    console.log(`  --   ${rel} (aucun script)`);
    continue;
  }
  for (const [i, block] of blocks.entries()) {
    checked++;
    try {
      new vm.Script(block.code, { filename: rel });
      console.log(`  OK   ${rel} — script ${i + 1} (ligne ${block.line})`);
    } catch (err) {
      failures++;
      console.error(`  ECHEC ${rel} — script ${i + 1} (vers la ligne ${block.line})`);
      console.error(`         ${err.message}`);
    }
  }
}

console.log(
  `\n${checked} script(s) inline vérifié(s) · ${failures} en échec.`);
if (failures > 0) {
  console.error(
    "\n🔴 Une page dont le script ne s'analyse pas est MORTE en production :\n" +
    '   elle répondra 200 et restera figée. Corriger avant de déployer.');
  process.exit(1);
}
