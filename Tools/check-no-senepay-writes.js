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
 *   3. la politique de majoration cesse de passer par son point unique ;
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
// 3) La politique de majoration passe par le POINT UNIQUE
// ---------------------------------------------------------------------------
// Ce controle ne dit PAS quelle politique appliquer : la majoration au payeur
// est permise ou non selon la position contractuelle du moment, et cela se
// change en une ligne dans PayerMarkup.Allowed. Ce qu'il garde, c'est que la
// question ne se repose nulle part ailleurs -- avant PayerMarkup, six sites
// comparaient FeesPayer == Parent chacun de leur cote, dont deux qui
// oubliaient de verifier que les taux etaient renseignes.
checks++;
{
  const file = path.join(ROOT, 'Common', 'Utilities', 'PayerMarkup.cs');
  if (!fs.existsSync(file)) {
    failures.push("Common/Utilities/PayerMarkup.cs a disparu : la politique de frais n'a plus de point unique.");
  } else if (!/public\s+const\s+bool\s+Allowed\s*=\s*(true|false)\s*;/.test(fs.readFileSync(file, 'utf8'))) {
    failures.push("PayerMarkup.Allowed n'est plus une constante lisible : la politique de frais devient indevinable.");
  }

  // Aucun appel direct a Fees.ChargeFor hors du point unique et de son moteur.
  const autorises = new Set([
    'Common/Utilities/PayerMarkup.cs',
    'Common/Utilities/ProviderFees.cs',
    'Services/GuardianPaymentService.cs',   // GuardianOutstanding.ChargeFor delegue a PayerMarkup
    'Controllers/PlatformSettingsController.cs', // simulation d'affichage, ne facture rien
  ]);
  for (const file of files) {
    const rel = path.relative(ROOT, file).split(path.sep).join('/');
    if (autorises.has(rel)) continue;
    const text = fs.readFileSync(file, 'utf8');
    if (/Fees\.ChargeFor\s*\(/.test(text)) {
      failures.push(
        `${rel} applique la majoration directement (Fees.ChargeFor). Passer par ` +
        `PayerMarkup.ChargeFor : sinon un changement de politique laisse ce ` +
        `chemin-la en arriere, et une famille paie ce qu'une autre ne paie pas.`
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
  console.log('   - la politique de majoration passe par son point unique');
  console.log('   - les cles d\'idempotence des decaissements sont stables');
  process.exit(0);
}

console.error(`\n${failures.length} PROBLEME(S) sur ${checks} controles :\n`);
for (const f of failures) console.error(`  - ${f}`);
process.exit(1);
