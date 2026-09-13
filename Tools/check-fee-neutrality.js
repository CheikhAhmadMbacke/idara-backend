#!/usr/bin/env node
/*
 * Contrôle de NEUTRALITÉ : quand l'école fait porter les frais au payeur, elle
 * doit encaisser T, retirer T, et la plateforme ne doit avancer AUCUN franc.
 *
 * POURQUOI CET OUTIL EXISTE
 * -------------------------
 * Ça n'a pas été le cas pendant quatre mois. La majoration valait 7,14 %,
 * obtenue en ADDITIONNANT les taux prélevés (3,6 + 1,77 + 1,77). Or majorer de
 * t ne compense pas un prélèvement de t : la commission porte sur le montant
 * DÉBITÉ, majoration comprise. Il manquait ~3 800 FCFA par million facturé,
 * payés par la plateforme. Rien ne l'indiquait — aucune erreur, aucun écran,
 * aucun journal. L'école recevait bien son dû, donc personne ne pouvait s'en
 * plaindre.
 *
 * Puis une seconde découverte a rendu ce premier correctif insuffisant :
 * LES FRAIS NE SONT PAS UN POURCENTAGE. Retrouvée sur les 197 paiements réglés
 * en production (197/197 exacts), la règle réelle est :
 *
 *     encaissement = round(C × provider %) + ceil( ceil(C × opIn % HT) × (1+TVA) )
 *     décaissement =                         ceil( ceil(T × opOut % HT) × (1+TVA) )
 *
 * Le « 1,77 % » n'existe nulle part : c'est 1,5 % HT + 18 % de TVA, chacun
 * arrondi au franc. Le « 5,40 % » d'encaissement est une moyenne vraie pour
 * AUCUN montant (mesurée : de 5,37 % à 6,05 % selon la taille). Appliquer un
 * taux moyen laissait 3 542 montants en déficit entre 200 et 100 000 FCFA.
 *
 * D'où ProviderFees.ChargeFor, qui RÉSOUT au lieu d'appliquer : le plus petit
 * entier C tel que C − frais(C) ≥ T + fraisRetrait(T).
 *
 * CE QUI EST VÉRIFIÉ
 *   1. Le code C# porte toujours la règle attendue (arrondis compris) et la
 *      recherche du plus petit C — pas un taux réintroduit en douce.
 *   2. Les taux du calcul viennent de la BASE (posés par migration), le code
 *      n'en portant aucun par défaut.
 *   3. Sur toute la plage des montants réels, la plateforme n'avance JAMAIS un
 *      franc, et la famille n'en paie jamais un de trop.
 *   4. Un retrait GROUPÉ — plusieurs paiements accumulés, le cas courant —
 *      reste couvert par la somme des provisions constituées un à un.
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
const MIGRATIONS = path.join(RACINE, 'Migrations');

const MONTANT_MIN = 200;
const MONTANT_MAX = 200000;

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
    [/RoundFranc\(chargedFcfa \* PayinProviderPercent \/ 100\.0\)/,
      'la commission prestataire doit être arrondie au franc LE PLUS PROCHE'],
    [/CeilFranc\(chargedFcfa \* PayinOperatorPercentHt \/ 100\.0\)/,
      'la part opérateur HT doit être arrondie au franc SUPÉRIEUR'],
    [/CeilFranc\(operatorHt \* Vat\)/,
      'la TVA doit être arrondie au franc SUPÉRIEUR, APRÈS le HT'],
    [/CeilFranc\(amountFcfa \* PayoutOperatorPercentHt \/ 100\.0\)/,
      'le frais de retrait suit la même structure HT puis TVA'],
    [/charged - PayinFeesFor\(charged\) < needed/,
      "ChargeFor doit RÉSOUDRE (chercher le plus petit C), jamais multiplier"],
    [/for \(var step = 1; step <= SearchWindow; step\+\+\)/,
      "la redescente est obligatoire : le premier C qui couvre n'est pas le plus petit"],
  ];
  for (const [re, quoi] of attendus) {
    if (!re.test(src)) echec('La règle de calcul des frais a changé', quoi + '.');
  }
}

// ====================================================================
// 2. Les taux viennent-ils de la base ?
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
    "Les propriétés C# n'ont AUCUN défaut (c'est voulu) : sans migration, la\n" +
      "         ligne de réglages reste à NULL et la plateforme refuse d'encaisser.\n" +
      '         Poser les valeurs mesurées par un migrationBuilder.Sql(UPDATE ...).'
  );
}

// ====================================================================
// 3 & 4. La règle est-elle neutre, et les retraits groupés couverts ?
// ====================================================================
if (taux && echecs.length === 0) {
  const ceilF = (x) => Math.ceil(x - 1e-9);
  const roundF = (x) => Math.floor(x + 0.5);
  const vat = 1 + taux.vat / 100;

  const fraisIn = (c) =>
    roundF((c * taux.provider) / 100) + ceilF(ceilF((c * taux.opIn) / 100) * vat);
  const fraisOut = (t) => ceilF(ceilF((t * taux.opOut) / 100) * vat);

  const FENETRE = 64;
  const chargeFor = (t) => {
    const besoin = t + fraisOut(t);
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

  let deficits = 0;
  let pire = { montant: 0, cible: null };
  let nonMinimal = 0;
  for (let t = MONTANT_MIN; t <= MONTANT_MAX; t++) {
    const besoin = t + fraisOut(t);
    const c = chargeFor(t);
    const solde = c - fraisIn(c) - besoin;
    if (solde < 0) {
      deficits++;
      if (solde < pire.montant) pire = { montant: solde, cible: t };
    }
    // La famille ne doit pas payer un franc de plus que nécessaire.
    if (c - 1 >= besoin && c - 1 - fraisIn(c - 1) >= besoin) nonMinimal++;
  }

  if (deficits > 0) {
    echec(
      "La plateforme AVANCE de l'argent",
      `${deficits} montants en déficit entre ${MONTANT_MIN} et ${MONTANT_MAX} FCFA.\n` +
        `         Pire cas : ${pire.montant} F sur une cible de ${pire.cible} F.`
    );
  }
  if (nonMinimal > 0) {
    echec(
      'La famille paie plus que nécessaire',
      `${nonMinimal} montants où un franc de moins aurait suffi.\n` +
        '         La redescente de ChargeFor ne fait plus son travail.'
    );
  }

  // --- Retraits GROUPÉS : le cas courant, et le plus facile à oublier ---
  // L'école accumule plusieurs paiements et retire d'un coup. Le frais réel
  // porte alors sur la SOMME, alors que la provision a été constituée
  // paiement par paiement. Il faut que la somme des provisions couvre.
  let decouverts = 0;
  const tailles = [500, 1000, 2500, 5000, 7500, 10000, 15000, 25000, 40000, 63000];
  for (let essai = 0; essai < 20000; essai++) {
    const n = 2 + (essai % 11);
    const parts = [];
    for (let i = 0; i < n; i++) parts.push(tailles[(essai * 7 + i * 3) % tailles.length]);
    const provision = parts.reduce((acc, p) => acc + fraisOut(p), 0);
    const reel = fraisOut(parts.reduce((a, b) => a + b, 0));
    if (provision < reel) decouverts++;
  }
  if (decouverts > 0) {
    echec(
      "Un retrait GROUPÉ n'est pas couvert",
      `${decouverts} cas où la somme des provisions est inférieure au frais réel.\n` +
        "         L'arrondi au franc SUPÉRIEUR de la provision est ce qui garantit\n" +
        '         ceil(a) + ceil(b) ≥ ceil(a+b) : il a dû être affaibli.'
    );
  }
}

// ====================================================================
console.log('');
if (echecs.length === 0) {
  console.log(
    `Règle de frais intacte · neutre au FRANC de ${MONTANT_MIN} à ${MONTANT_MAX} FCFA ` +
      '· retraits groupés couverts · 4 contrôles, 0 en échec.'
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
  console.log(`         ${e.detail}`);
}
console.log('');
console.log('Une majoration mal calibrée ne lève aucune erreur : elle se paie,');
console.log('en silence, sur la trésorerie de la plateforme (§255, §256).');
process.exit(1);
