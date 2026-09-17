#!/usr/bin/env node
/*
 * Contrôle de NEUTRALITÉ : personne ne doit avancer un franc pour personne.
 *
 * LE MODÈLE, DEPUIS LE 2026-09-17 — les deux frais sont SÉPARÉS :
 *
 *     payin  : le PAYEUR porte la commission d'encaissement (réglage par
 *              défaut). L'école peut choisir de l'absorber elle-même.
 *     payout : CELUI QUI DÉCAISSE porte la commission de décaissement, au
 *              moment où il décaisse. Toujours. Sans exception.
 *
 * Plus aucune provision n'est constituée à l'encaissement pour une sortie à
 * venir. Chaque opération paie ce qu'elle coûte, quand elle le coûte.
 *
 * POURQUOI CE CHANGEMENT
 * ----------------------
 * L'ancien modèle provisionnait la sortie dès l'entrée. Il tenait pour un
 * retrait unique, et FUYAIT sur les retraits FRACTIONNÉS : la provision était
 * constituée une fois, alors que chaque retrait partiel paie son propre arrondi
 * au franc supérieur. Mesuré : 75 % des suites de retraits partiels finissaient
 * en déficit (moyenne 2,15 F, pire 8 F), comblé en silence par la plateforme.
 * Ce cas manquait à cet outil — c'est le contrôle 6, désormais.
 *
 * CE QUI RESTE VRAI, ET NE SE RE-DÉBAT PAS
 * ----------------------------------------
 * LES FRAIS NE SONT PAS UN POURCENTAGE. Retrouvée sur les 197 paiements réglés
 * en production (197/197 exacts), la règle réelle est :
 *
 *     encaissement = round(C × provider %) + ceil( ceil(C × opIn % HT) × (1+TVA) )
 *     décaissement =                         ceil( ceil(T × opOut % HT) × (1+TVA) )
 *
 * Majorer de t ne compense pas un prélèvement de t : la commission porte sur le
 * montant DÉBITÉ, majoration comprise. D'où ProviderFees, qui RÉSOUT au lieu
 * d'appliquer — dans les deux sens :
 *   - ChargeFor(T)          : le plus PETIT montant à débiter pour encaisser T ;
 *   - MaxReceivableFrom(S)  : le plus GRAND montant qui peut SORTIR de S.
 *
 * CE QUI EST VÉRIFIÉ
 *   1. Le code C# porte toujours la règle attendue, arrondis compris, et les
 *      deux résolutions — pas un taux réintroduit en douce.
 *   2. La comptabilité lit `WalletCreditedFcfa`, le montant ÉCRIT, au lieu de
 *      le redéduire : recalculer le passé avec les taux d'aujourd'hui ferait
 *      bouger les comptes de juin à chaque changement de grille.
 *   3. Les taux viennent de la BASE (posés par migration), le code n'en portant
 *      aucun par défaut.
 *   4. Payin, mode « le payeur paie » : la plateforme n'avance jamais un franc,
 *      et la famille n'en paie jamais un de trop.
 *   5. Payout : ce qui sort du portefeuille est exactement ce qui sort de la
 *      réserve, et un solde ne peut jamais sortir plus qu'il ne contient.
 *   6. 🔴 LE CAS QUI MANQUAIT — suites de paiements aux modes MÉLANGÉS suivies
 *      de retraits FRACTIONNÉS : la réserve doit rester supérieure ou égale à
 *      la somme des portefeuilles, à chaque instant.
 *
 * CE QUE CET OUTIL NE DIT PAS
 *   Il rejoue la règle en JavaScript, à l'identique du C#. Il prouve donc que
 *   LA RÈGLE est neutre, et que le C# la contient encore — pas que le C#
 *   l'exécute sans bug. C'est l'écran SuperAdmin qui ferme cette porte, en
 *   confrontant les frais prédits aux frais réellement prélevés, franc par
 *   franc, sur chaque paiement réglé.
 *
 * Usage : node Idara.API/Tools/check-fee-neutrality.js
 */

const fs = require('fs');
const path = require('path');

const RACINE = path.join(__dirname, '..');
const CALCULATEUR = path.join(RACINE, 'Common', 'Utilities', 'ProviderFees.cs');
const FINANCE = path.join(RACINE, 'Services', 'PlatformFinanceService.cs');
const RETRAIT = path.join(RACINE, 'Controllers', 'SchoolWalletController.cs');
const REGLEMENT = path.join(RACINE, 'Services', 'PayinSettlementService.cs');
const MIGRATIONS = path.join(RACINE, 'Migrations');

const MONTANT_MIN = 200;
const MONTANT_MAX = 200000;
const FENETRE = 64;

const echecs = [];
const echec = (titre, detail) => echecs.push({ titre, detail });

// ====================================================================
// 1. Le code C# porte-t-il encore la règle ?
// ====================================================================
if (!fs.existsSync(CALCULATEUR)) {
  echec('ProviderFees.cs est introuvable', 'Le calculateur de frais a disparu.');
} else {
  const src = fs.readFileSync(CALCULATEUR, 'utf8');
  const attendus = [
    [
      /RoundFranc\(chargedFcfa \* PayinProviderPercent \/ 100\.0\)/,
      'la commission prestataire doit être arrondie au franc LE PLUS PROCHE',
    ],
    [
      /CeilFranc\(chargedFcfa \* PayinOperatorPercentHt \/ 100\.0\)/,
      'la part opérateur HT doit être arrondie au franc SUPÉRIEUR',
    ],
    [
      /CeilFranc\(operatorHt \* Vat\)/,
      'la TVA doit être arrondie au franc SUPÉRIEUR, APRÈS le HT',
    ],
    [
      /CeilFranc\(amountFcfa \* PayoutOperatorPercentHt \/ 100\.0\)/,
      'le frais de retrait suit la même structure HT puis TVA',
    ],
    [
      /charged - PayinFeesFor\(charged\) < needed/,
      'ChargeFor doit RÉSOUDRE (chercher le plus petit C), jamais multiplier',
    ],
    [
      /for \(var step = 1; step <= SearchWindow; step\+\+\)/,
      "la redescente est obligatoire : le premier C qui couvre n'est pas le plus petit",
    ],
    [
      /public long MaxReceivableFrom\(long balanceFcfa\)/,
      "le bouton « Tout » a besoin de MaxReceivableFrom : sans lui, l'écran proposerait de retirer un solde qui ne peut pas sortir",
    ],
    [
      /candidate \+ PayoutFeesFor\(candidate\) <= balanceFcfa/,
      "MaxReceivableFrom doit chercher le plus GRAND montant qui tient, sinon on retient un franc de trop à l'école",
    ],
  ];
  for (const [re, quoi] of attendus) {
    if (!re.test(src)) echec('La règle de calcul des frais a changé', quoi + '.');
  }

  // 🔴 L'interdit : la provision de sortie ne doit PAS revenir dans le payin.
  if (/needed\s*=\s*targetFcfa\s*\+\s*PayoutFeesFor/.test(src)) {
    echec(
      "La provision de sortie est réapparue dans l'encaissement",
      "ChargeFor ne doit couvrir QUE les frais d'entrée depuis le 2026-09-17. Provisionner la sortie une fois, à l'entrée, laisse les retraits FRACTIONNÉS en déficit — chacun payant son propre arrondi (75 % des suites, jusqu'à 8 F)."
    );
  }
}

// --- Le retrait débite-t-il bien « reçu + frais » ? ---
if (fs.existsSync(RETRAIT)) {
  const src = fs.readFileSync(RETRAIT, 'utf8');
  if (!/var walletDebit = receiveAmount \+ estimatedFee;/.test(src)) {
    echec(
      'Le retrait ne calcule plus le débit « reçu + frais »',
      "Les frais de décaissement sont prélevés EN SUS (§274) : sortir 1 000 F coûte 1 010 F. Débiter le seul montant reçu fait avancer la différence par la plateforme."
    );
  }
  if (!/wallet\.AvailableBalance -= walletDebit;/.test(src)) {
    echec(
      'La réservation ne porte plus sur le débit',
      'Le portefeuille doit être réservé de « reçu + frais », sinon le solde affiché promet une somme qui ne peut pas sortir.'
    );
  }
  if (!/HasEnoughForSource\(walletDebit,/.test(src)) {
    echec(
      'Le contrôle de solde ignore les frais',
      'Une école au solde exact verrait son retrait accepté, puis son portefeuille passer sous zéro.'
    );
  }
}

// --- Le crédit du portefeuille est-il resté SANS calcul de frais ? ---
if (fs.existsSync(REGLEMENT)) {
  const src = fs.readFileSync(REGLEMENT, 'utf8');
  if (/CreditableFrom|MaxReceivableFrom|PayoutFeesFor/.test(src)) {
    echec(
      "Le règlement d'un encaissement calcule de nouveau un frais de sortie",
      "Le mode « l'école paie » crédite le net ENTIER depuis le 2026-09-17 : la sortie se paie au retrait. Amputer le crédit ferait payer le décaissement DEUX fois."
    );
  }
}

// ====================================================================
// 2. La comptabilité lit-elle le montant ÉCRIT ?
// ====================================================================
if (fs.existsSync(FINANCE)) {
  const src = fs.readFileSync(FINANCE, 'utf8');
  if (!/NetCreditedFcfa - p\.WalletCreditedFcfa/.test(src)) {
    echec(
      'La marge plateforme ne se lit plus sur le montant écrit',
      "Attendu : NetCreditedFcfa − WalletCreditedFcfa. Redéduire le crédit ferait recalculer le passé avec les taux d'aujourd'hui, et les comptes de juin changeraient à chaque changement de grille."
    );
  }
  if (/FeesPayer == FeesPayer\.Parent && p\.TargetAmountFcfa > 0/.test(src)) {
    echec(
      "L'ancienne formule de marge est réapparue",
      "Le filtre FeesPayer=Parent exclut le mode « l'école paie les frais », dont le résidu d'arrondi ne serait alors compté nulle part."
    );
  }
}

// ====================================================================
// 3. Les taux viennent-ils de la base ?
// ====================================================================
let taux = null;
if (fs.existsSync(MIGRATIONS)) {
  const fichiers = fs
    .readdirSync(MIGRATIONS)
    .filter((f) => f.endsWith('.cs') && !f.endsWith('.Designer.cs'))
    .sort();

  for (const fichier of fichiers) {
    const brut = fs.readFileSync(path.join(MIGRATIONS, fichier), 'utf8');
    // Seul le corps de Up() compte : le Down() repose les valeurs PRÉCÉDENTES.
    // Les confondre faisait échouer le contrôle sur une migration pourtant
    // juste — constaté au premier recalibrage.
    const debutUp = brut.indexOf('void Up(');
    const debutDown = brut.indexOf('void Down(');
    if (debutUp === -1) continue;
    const up = brut.slice(debutUp, debutDown === -1 ? undefined : debutDown);

    const lu = (nom) => {
      const m = up.match(new RegExp('"*' + nom + '"*\\s*=\\s*([0-9.]+)'));
      return m ? parseFloat(m[1]) : null;
    };
    const provider = lu('PayinProviderFeePercent');
    if (provider !== null) {
      taux = {
        provider,
        opIn: lu('PayinOperatorFeePercentHt'),
        opOut: lu('PayoutOperatorFeePercentHt'),
        vat: lu('FeeVatPercent'),
        parFichier: fichier,
      };
    }
  }
}

if (!taux || [taux.opIn, taux.opOut, taux.vat].some((v) => v === null)) {
  echec(
    'Aucune migration ne pose les quatre taux',
    "Les propriétés C# n'ont AUCUN défaut (c'est voulu) : sans migration, la ligne de réglages reste à NULL et la plateforme refuse d'encaisser. Poser les valeurs mesurées par un migrationBuilder.Sql(UPDATE ...)."
  );
}

// ====================================================================
// 4, 5 et 6 — la règle est-elle neutre ?
// ====================================================================
if (taux && echecs.length === 0) {
  const ceilF = (x) => Math.ceil(x - 1e-9);
  const roundF = (x) => Math.floor(x + 0.5);
  const vat = 1 + taux.vat / 100;

  const fraisIn = (c) =>
    roundF((c * taux.provider) / 100) + ceilF(ceilF((c * taux.opIn) / 100) * vat);
  const fraisOut = (t) => ceilF(ceilF((t * taux.opOut) / 100) * vat);

  // --- Le payeur paie l'entrée : le plus PETIT montant qui la couvre ---
  // 🔑 La cible est la cible : plus de « + fraisOut(t) ».
  const chargeFor = (t) => {
    const besoin = t;
    const approx = (taux.provider + taux.opIn * vat) / 100;
    let c = approx < 0.95 ? Math.ceil(besoin / (1 - approx)) : besoin;
    if (c < besoin) c = besoin;
    while (c > besoin && c - fraisIn(c) >= besoin) c--;
    while (c - fraisIn(c) < besoin) c++;
    let best = c;
    for (let s = 1; s <= FENETRE; s++) {
      const cand = c - s;
      if (cand < besoin) break;
      if (cand - fraisIn(cand) >= besoin) best = cand;
    }
    return best;
  };

  // --- Le plus GRAND montant qui peut SORTIR d'un solde ---
  const maxReceivableFrom = (solde) => {
    let r = Math.floor(solde / (1 + (taux.opOut * vat) / 100));
    if (r < 0) r = 0;
    if (r > solde) r = solde;
    while (r > 0 && r + fraisOut(r) > solde) r--;
    while (r + 1 <= solde && r + 1 + fraisOut(r + 1) <= solde) r++;
    let best = r;
    for (let s = 1; s <= FENETRE; s++) {
      const cand = r + s;
      if (cand > solde) break;
      if (cand + fraisOut(cand) <= solde) best = cand;
    }
    return best;
  };

  // --- 4. Payin, mode « le payeur paie les frais » ---
  let deficits = 0;
  let pire = { montant: 0, cible: null };
  let nonMinimal = 0;
  for (let t = MONTANT_MIN; t <= MONTANT_MAX; t++) {
    const c = chargeFor(t);
    const solde = c - fraisIn(c) - t;
    if (solde < 0) {
      deficits++;
      if (solde < pire.montant) pire = { montant: solde, cible: t };
    }
    if (c - 1 >= t && c - 1 - fraisIn(c - 1) >= t) nonMinimal++;
  }
  if (deficits > 0) {
    echec(
      "La plateforme AVANCE de l'argent (mode payeur)",
      `${deficits} montants en déficit entre ${MONTANT_MIN} et ${MONTANT_MAX} FCFA. Pire cas : ${pire.montant} F sur une cible de ${pire.cible} F.`
    );
  }
  if (nonMinimal > 0) {
    echec(
      'La famille paie plus que nécessaire',
      `${nonMinimal} montants où un franc de moins aurait suffi. La redescente de ChargeFor ne fait plus son travail.`
    );
  }

  // --- 5. Payout : on ne sort jamais plus qu'on ne détient ---
  let insortables = 0;
  let tropRetenus = 0;
  for (let solde = 1; solde <= MONTANT_MAX; solde++) {
    const r = maxReceivableFrom(solde);
    if (r + fraisOut(r) > solde) insortables++;
    if (r + 1 <= solde && r + 1 + fraisOut(r + 1) <= solde) tropRetenus++;
  }
  if (insortables > 0) {
    echec(
      'Un retrait sortirait plus que le solde',
      `${insortables} cas — le portefeuille passerait sous zéro, ou la plateforme comblerait la différence.`
    );
  }
  if (tropRetenus > 0) {
    echec(
      "On retient plus que nécessaire à l'école",
      `${tropRetenus} cas où un franc de plus aurait pu sortir.`
    );
  }

  // --- 6. 🔴 LE CAS QUI MANQUAIT : modes MÉLANGÉS puis retraits FRACTIONNÉS ---
  //
  // C'est le scénario exact posé par Cheikh, et celui qui mettait l'ancien
  // modèle en défaut. On simule une école qui encaisse des paiements dans les
  // DEUX modes, puis retire son solde en PLUSIEURS fois. À chaque instant, la
  // réserve (le compte marchand) doit couvrir le portefeuille.
  //
  //   payin Parent : réserve += C − fraisIn(C)   ·  wallet += T
  //   payin School : réserve += T − fraisIn(T)   ·  wallet += T − fraisIn(T)
  //   payout R     : réserve −= R + fraisOut(R)  ·  wallet −= R + fraisOut(R)
  //
  const cibles = [500, 1000, 2500, 5000, 7500, 10000, 15000, 25000, 40000, 63000];
  let decouverts = 0;
  let pireEcart = 0;
  for (let essai = 0; essai < 3000; essai++) {
    let reserve = 0;
    let wallet = 0;
    const nbPayins = 2 + (essai % 11);
    for (let i = 0; i < nbPayins; i++) {
      const t = cibles[(essai * 7 + i * 3) % cibles.length];
      if ((essai + i) % 2 === 0) {
        const c = chargeFor(t);
        reserve += c - fraisIn(c);
        wallet += t;
      } else {
        reserve += t - fraisIn(t);
        wallet += t - fraisIn(t);
      }
      if (reserve - wallet < pireEcart) pireEcart = reserve - wallet;
      if (reserve < wallet) decouverts++;
    }
    // Retraits FRACTIONNÉS : on vide le portefeuille en plusieurs fois.
    const parts = 2 + (essai % 5);
    for (let k = 0; k < parts && wallet > 0; k++) {
      const visee = k === parts - 1 ? wallet : Math.floor(wallet / (parts - k));
      const r = maxReceivableFrom(Math.min(visee, wallet));
      if (r <= 0) break;
      const debit = r + fraisOut(r);
      wallet -= debit;
      reserve -= debit;
      if (reserve < wallet) decouverts++;
      if (reserve - wallet < pireEcart) pireEcart = reserve - wallet;
    }
    if (reserve < 0) decouverts++;
  }
  if (decouverts > 0) {
    echec(
      'Retraits FRACTIONNÉS : la réserve ne couvre plus les portefeuilles',
      `${decouverts} instants en découvert, pire écart ${pireEcart} F. C'est précisément la fuite de l'ancien modèle : une provision constituée UNE fois ne couvre pas N arrondis au franc supérieur.`
    );
  }
}

// ====================================================================
console.log('');
if (echecs.length === 0) {
  console.log(
    `Règle intacte · encaissement neutre au franc de ${MONTANT_MIN} à ${MONTANT_MAX} FCFA ` +
      '· aucun retrait ne sort plus que son solde · retraits FRACTIONNÉS couverts ' +
      '· 6 contrôles, 0 en échec.'
  );
  if (taux) {
    console.log(
      `Taux posés par ${taux.parFichier} : encaissement ${taux.provider} % + ` +
        `${taux.opIn} % HT · retrait ${taux.opOut} % HT · TVA ${taux.vat} %.`
    );
  }
  process.exit(0);
}

for (const e of echecs) {
  console.log(`  ÉCHEC — ${e.titre}`);
  console.log(`          ${e.detail}`);
}
console.log('');
console.log('Un frais mal porté ne lève aucune erreur : il se paie, en silence,');
console.log('sur la trésorerie de la plateforme (§255, §256, §257).');
process.exit(1);
