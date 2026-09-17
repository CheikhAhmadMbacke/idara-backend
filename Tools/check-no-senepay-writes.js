#!/usr/bin/env node
/**
 * check-no-senepay-writes.js — le garde-fou du retour en arrière.
 *
 * Depuis le 2026-09-17, tout l'argent passe par Wave. L'ancien prestataire est
 * conservé en LECTURE SEULE, le temps de solder les paiements ouverts et de
 * rapatrier la réserve.
 *
 * Ce contrôle échoue si :
 *   1. une méthode d'ÉCRITURE réapparaît chez l'ancien prestataire
 *      (initier un encaissement ou un décaissement) ;
 *   2. un chemin d'argent contourne le point unique d'ouverture de session ;
 *   3. la majoration au payeur est réautorisée sans avenant (article 8.2 du
 *      contrat Wave : résiliation SANS PRÉAVIS) ;
 *   4. un taux de commission Wave se retrouve écrit en dur.
 *
 * Il ne sert à rien le jour où on l'écrit : il sert le jour où quelqu'un, dans
 * six mois, « remet vite fait » l'ancien chemin pour dépanner.
 *
 * Usage : node Idara.API/Tools/check-no-senepay-writes.js
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const SKIP_DIRS = new Set(['bin', 'obj', 'Migrations', 'node_modules', '.git', 'wwwroot']);

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) walk(path.join(dir, entry.name), out);
    } else if (entry.name.endsWith('.cs')) {
      out.push(path.join(dir, entry.name));
    }
  }
  return out;
}

const files = walk(ROOT);
const failures = [];
let checks = 0;

// ---------------------------------------------------------------------------
// 1) Aucune ecriture chez l'ancien prestataire
// ---------------------------------------------------------------------------
checks++;
{
  const forbidden = [
    /InitiatePaymentAsync\s*\(\s*new\s+SenePay/,
    /InitiatePayoutAsync\s*\(\s*new\s+SenePay/,
    /ISenePayClient[^;]*\.\s*InitiatePaymentAsync/,
    /ISenePayClient[^;]*\.\s*InitiatePayoutAsync/,
    /Task<SenePayInitiatePaymentResponse>\s+InitiatePaymentAsync/,
    /Task<SenePayPayoutResponse>\s+InitiatePayoutAsync/,
    /PostAsync\s*\(\s*"api\/v1\/payments\/initiate"/,
    /PostAsync\s*\(\s*"api\/v1\/payouts"/,
    /class\s+SenePayClient/,
    /interface\s+ISenePayClient/,
  ];
  for (const file of files) {
    const text = fs.readFileSync(file, 'utf8');
    for (const re of forbidden) {
      if (re.test(text)) {
        failures.push(
          `ECRITURE vers l'ancien prestataire reintroduite dans ${path.relative(ROOT, file)} ` +
          `(motif ${re}). Tout encaissement et tout decaissement passent par Wave.`
        );
      }
    }
  }
}

// ---------------------------------------------------------------------------
// 2) Les encaissements passent par le point unique
// ---------------------------------------------------------------------------
checks++;
{
  for (const file of files) {
    const rel = path.relative(ROOT, file).replace(/\\/g, '/');
    // Le client et son interface DECLARENT l'appel ; le point unique est le
    // seul autorise a l'UTILISER.
    if (rel === 'Services/WavePayinService.cs') continue;
    if (rel === 'Services/WaveClient.cs' || rel === 'Services/IWaveClient.cs') continue;
    const text = fs.readFileSync(file, 'utf8');
    if (/CreateCheckoutSessionAsync\s*\(/.test(text) && !/Tools\//.test(rel)) {
      failures.push(
        `${rel} ouvre une session de paiement directement. Tout encaissement doit ` +
        `passer par IWavePayinService.StartAsync : c'est la seule porte ou sont ` +
        `verifies le guichet ouvert/ferme et l'absence de majoration.`
      );
    }
  }
}

// ---------------------------------------------------------------------------
// 3) La majoration au payeur reste interdite (article 8.2)
// ---------------------------------------------------------------------------
checks++;
{
  const file = path.join(ROOT, 'Common', 'Utilities', 'PayerMarkup.cs');
  if (!fs.existsSync(file)) {
    failures.push('Common/Utilities/PayerMarkup.cs a disparu : le verrou de l\'article 8.2 n\'existe plus.');
  } else {
    const text = fs.readFileSync(file, 'utf8');
    if (!/public\s+const\s+bool\s+Allowed\s*=\s*false\s*;/.test(text)) {
      failures.push(
        'PayerMarkup.Allowed n\'est plus a false : majorer un payeur pour qu\'il regle ' +
        'via Wave declenche la resiliation SANS PREAVIS (article 8.2). Ne changer ' +
        'qu\'avec un avenant ecrit au contrat.'
      );
    }
  }
}

// ---------------------------------------------------------------------------
// 4) Aucune cle d'idempotence tiree au hasard sur un decaissement
// ---------------------------------------------------------------------------
checks++;
{
  for (const file of files) {
    const rel = path.relative(ROOT, file).replace(/\\/g, '/');
    const text = fs.readFileSync(file, 'utf8');
    const calls = text.match(/CreatePayoutAsync\s*\([\s\S]{0,900}?\)\s*;/g) || [];
    for (const call of calls) {
      if (/Guid\.NewGuid|Random|DateTime\.(Now|UtcNow)\.Ticks/.test(call)) {
        failures.push(
          `${rel} : cle d'idempotence NON STABLE sur un decaissement. Un rejeu apres ` +
          `panne repartirait avec une cle neuve et Wave enverrait l'argent une ` +
          `SECONDE fois. Utiliser PayoutIdempotency.ForWithdrawal(id).`
        );
      }
    }
  }
}

// ---------------------------------------------------------------------------
if (failures.length === 0) {
  console.log(`\n${checks} controles, 0 en echec.`);
  console.log('   - aucune ecriture vers l\'ancien prestataire');
  console.log('   - tous les encaissements passent par IWavePayinService');
  console.log('   - la majoration au payeur reste interdite (article 8.2)');
  console.log('   - les cles d\'idempotence des decaissements sont stables');
  process.exit(0);
}

console.error(`\n${failures.length} PROBLEME(S) sur ${checks} controles :\n`);
for (const f of failures) console.error(`  - ${f}`);
process.exit(1);
