#!/usr/bin/env node
/**
 * wave-webhook-selftest.js — éprouver la réception des webhooks Wave.
 *
 * Wave n'offre AUCUN environnement de test. Sans cet outil, la première fois
 * qu'on voit fonctionner la vérification de signature, c'est avec l'argent
 * d'une vraie famille — et si elle échoue, le paiement reste en attente sans
 * que rien ne le dise.
 *
 * Ce script fabrique un événement signé exactement comme Wave le ferait
 * (HMAC-SHA256 de `timestamp + corps brut`, en-tête `t=…,v1=…`) et l'envoie à
 * l'endpoint. Il éprouve aussi ce qui doit ÉCHOUER : mauvaise signature,
 * horodatage périmé, en-tête absent. Un garde-fou qu'on n'a jamais vu refuser
 * n'est pas un garde-fou.
 *
 * Usage :
 *   node Idara.API/Tools/wave-webhook-selftest.js <url> <secret> [--only-valid]
 *
 * Exemple :
 *   node Idara.API/Tools/wave-webhook-selftest.js \
 *        https://api.idara.sn/api/webhooks/wave wave_sn_AKS_xxx
 *
 * ⚠️ Le secret est lu en ARGUMENT : ne pas le coller dans un historique de
 * shell partagé, et ne jamais le committer.
 */

const crypto = require('crypto');
const readline = require('readline');

const [, , urlArg, secretArg, ...flags] = process.argv;
const onlyValid = flags.includes('--only-valid');
const url = urlArg || 'https://api.idara.sn/api/webhooks/wave';

/**
 * Lecture masquee du secret.
 *
 * Le passer en ARGUMENT l ecrirait en clair dans l historique du shell --
 * PowerShell conserve le sien sur le disque, et il y resterait des mois.
 * C est la fuite du token SMS (§190), sous une autre forme. On le demande
 * donc, sans echo, et il ne touche jamais le disque.
 */
function demanderSecret() {
  return new Promise((resolve) => {
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
    const ecrire = rl._writeToOutput.bind(rl);
    rl._writeToOutput = function (chaine) {
      // On laisse passer l invite, on masque la frappe.
      if (rl.query && chaine.startsWith(rl.query)) return ecrire(chaine);
      if (chaine.trim() === '') return ecrire(chaine);
    };
    rl.query = 'Secret du webhook (la saisie reste invisible) : ';
    rl.question(rl.query, (rep) => {
      rl.close();
      process.stdout.write(String.fromCharCode(10));
      resolve((rep || '').trim());
    });
  });
}

function sign(body, secretKey, timestamp) {
  const mac = crypto.createHmac('sha256', secretKey);
  mac.update(String(timestamp) + body);
  return mac.digest('hex');
}

async function send(label, body, header, expected) {
  const headers = { 'Content-Type': 'application/json' };
  if (header) headers['Wave-Signature'] = header;

  let res, text;
  try {
    res = await fetch(url, { method: 'POST', headers, body });
    text = await res.text();
  } catch (err) {
    console.log(`  ${label.padEnd(34)} ERREUR RESEAU : ${err.message}`);
    return false;
  }

  const ok = res.status === expected;
  console.log(
    `  ${ok ? 'OK  ' : 'ECHEC'} ${label.padEnd(34)} ` +
    `attendu ${expected}, recu ${res.status} ${text.slice(0, 120)}`
  );
  return ok;
}

(async () => {
  const secret = secretArg || await demanderSecret();
  if (!secret) {
    console.error('Aucun secret fourni — on ne peut rien signer.');
    process.exit(2);
  }

  const now = Math.floor(Date.now() / 1000);
  // `id` unique : l'endpoint est idempotent, un identifiant deja vu
  // repondrait « doublon » et ne prouverait rien.
  const eventId = `EV_selftest_${now}_${Math.random().toString(36).slice(2, 8)}`;
  const body = JSON.stringify({ id: eventId, type: 'test.test_event', data: {} });

  console.log(`\nEndpoint : ${url}`);
  console.log(`Evenement : ${eventId}\n`);

  let allOk = true;

  // --- Ce qui doit PASSER ---
  allOk &= await send(
    'signature valide',
    body,
    `t=${now},v1=${sign(body, secret, now)}`,
    200
  );

  // Rejeu du MEME evenement : accepte (200) mais marque doublon.
  allOk &= await send(
    'rejeu du meme evenement',
    body,
    `t=${now},v1=${sign(body, secret, now)}`,
    200
  );

  if (onlyValid) {
    console.log(`\n${allOk ? 'Chaine de reception PROUVEE.' : 'ECHEC : voir ci-dessus.'}\n`);
    process.exit(allOk ? 0 : 1);
  }

  // --- Ce qui doit ETRE REFUSE ---
  const body2 = JSON.stringify({ id: `${eventId}_b`, type: 'test.test_event', data: {} });

  allOk &= await send('en-tete absent', body2, null, 401);
  allOk &= await send('en-tete malforme', body2, 'nimportequoi', 401);
  allOk &= await send(
    'mauvais secret',
    body2,
    `t=${now},v1=${sign(body2, 'mauvais-secret', now)}`,
    401
  );

  // Horodatage de 10 minutes : au-dela de la fenetre de 5 minutes. C'est la
  // protection contre le rejeu d'un message capte.
  const vieux = now - 600;
  allOk &= await send(
    'horodatage perime (10 min)',
    body2,
    `t=${vieux},v1=${sign(body2, secret, vieux)}`,
    401
  );

  // Corps modifie apres signature : l'attaque que le HMAC existe pour bloquer.
  allOk &= await send(
    'corps altere apres signature',
    JSON.stringify({ id: `${eventId}_c`, type: 'test.test_event', data: { montant: 999999 } }),
    `t=${now},v1=${sign(body2, secret, now)}`,
    401
  );

  console.log(
    `\n${allOk ? 'Chaine de reception PROUVEE : ce qui doit passer passe, ce qui doit etre refuse est refuse.'
              : 'ECHEC : au moins un cas ne se comporte pas comme prevu (voir ci-dessus).'}\n`
  );
  process.exit(allOk ? 0 : 1);
})();
