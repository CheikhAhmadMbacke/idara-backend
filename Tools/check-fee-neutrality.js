#!/usr/bin/env node
/*
 * Contrôle de NEUTRALITÉ de la majoration au payeur.
 *
 * POURQUOI CET OUTIL EXISTE
 * -------------------------
 * Quand l'école fait porter les frais au parent, une seule chose doit être
 * vraie : l'école encaisse T, retire T, et la plateforme n'avance rien.
 *
 * Ça n'a pas été le cas pendant quatre mois. La majoration valait 7,14 %,
 * obtenue en ADDITIONNANT les taux prélevés (3,6 + 1,77 + 1,77). Or majorer de
 * t ne compense pas un prélèvement de t : il manquait ~3 800 FCFA par million
 * facturé, payés par la plateforme. Rien ne l'indiquait — aucune erreur, aucun
 * écran, aucun journal. L'école recevait bien son dû, donc personne ne pouvait
 * s'en plaindre.
 *
 * Le calcul juste tient en une ligne, et c'est justement pour ça qu'il se
 * réécrit de travers sans que personne ne le remarque :
 *
 *     majoration = (1 + frais_de_retrait) / (1 − frais_d_encaissement)
 *
 * Le dénominateur vient de ce que le prélèvement d'entrée porte sur le montant
 * DÉBITÉ, pas sur la cible. Le numérateur vient de ce que SenePay prélève le
 * frais de sortie EN PLUS du montant envoyé (`fee_mode = "on_top"`) — écrire
 * ici `1 / (1 − frais_de_retrait)`, c'est décrire un modèle de frais qu'on
 * n'utilise plus depuis 2026.
 *
 * CE QUI EST VÉRIFIÉ
 *   1. La formule de `ParentFeeMultiplier` est bien celle-là — et pas une
 *      addition des taux, la faute d'origine.
 *   2. Sur toute la plage des montants réels, l'aller-retour ne coûte JAMAIS
 *      un franc à la plateforme — au taux SAISI.
 *   3. Le taux par défaut du code et celui posé par la migration coïncident.
 *      Sinon, une base neuve et la production ne calculent pas pareil (§193).
 *
 * CE QUE LE CONTRÔLE 2 NE DIT PAS, ET IL FAUT LE SAVOIR
 *   Il applique le taux saisi des DEUX côtés : ce qu'on majore et ce qu'on
 *   suppose prélevé. Il valide donc la cohérence du calcul, pas le comportement
 *   réel du prestataire — qui arrondit ses frais au franc SUPÉRIEUR sur chaque
 *   transaction. Mesuré en production : le taux effectif va de 5,373 % sur les
 *   gros montants à 5,749 % sous 1 000 FCFA, alors que le contractuel additionné
 *   vaut 5,37. Autrement dit, le taux contractuel est un PLANCHER.
 *
 *   Vouloir modéliser cet arrondi ici serait une illusion de précision : il
 *   dépend de la décomposition interne du prestataire (3,6 % puis 1,77 %, chacun
 *   arrondi), qu'on n'observe pas. C'est l'écran SuperAdmin qui couvre ce
 *   terrain — il compare les taux saisis à ceux RÉELLEMENT prélevés et chiffre
 *   l'écart. Un garde-fou qui ment sur sa portée est pire qu'un garde-fou absent.
 *
 * Même esprit que check-migrations.js (§254), check-html-pages.js (§218) et
 * check-i18n-pages.js (§228) : ce qui ne se voit pas à la relecture doit se
 * vérifier par une commande.
 *
 * Usage : node Idara.API/Tools/check-fee-neutrality.js
 * Sort en code 1 dès qu'un contrôle échoue.
 */

const fs = require('fs');
const path = require('path');

const RACINE = path.join(__dirname, '..');
const MODELE = path.join(RACINE, 'Models', 'PlatformSettings.cs');
const MIGRATIONS = path.join(RACINE, 'Migrations');

/** Plage des montants réellement facturés : du minimum SenePay à une année. */
const MONTANT_MIN = 200;
const MONTANT_MAX = 200000;

const echecs = [];

function echec(titre, detail) {
  echecs.push({ titre, detail });
}

/** Valeur par défaut d'une propriété double de PlatformSettings. */
function defautCsharp(source, propriete) {
  const re = new RegExp(
    'public\\s+double\\s+' + propriete + '\\s*\\{[^}]*\\}\\s*=\\s*([0-9.]+)\\s*;'
  );
  const m = source.match(re);
  return m ? parseFloat(m[1]) : null;
}

// ====================================================================
// 1. La formule
// ====================================================================
const modele = fs.readFileSync(MODELE, 'utf8');

const blocMultiplicateur = modele.slice(
  modele.indexOf('public double ParentFeeMultiplier')
);
const corps = blocMultiplicateur.slice(0, blocMultiplicateur.indexOf('\n        }'));

if (!/\(1\.0 \+ b\) \/ \(1\.0 - a\)/.test(corps)) {
  echec(
    'La formule de ParentFeeMultiplier a changé',
    'Attendu : (1.0 + b) / (1.0 - a), avec a = encaissement et b = retrait.\n' +
      "         Une addition des taux (1 + a + b) est la faute d'origine : elle\n" +
      '         sous-calibre la majoration et fait avancer la différence à la\n' +
      "         plateforme. Une division 1 / ((1-a)(1-b)) décrit, elle, un frais\n" +
      "         de sortie prélevé DANS le montant envoyé — ce n'est pas le\n" +
      '         modèle SenePay actuel (on_top).'
  );
}

// ====================================================================
// 2. La neutralité, arrondis compris
// ====================================================================
const payin = defautCsharp(modele, 'PayinFeePercent');
const payout = defautCsharp(modele, 'PayoutFeePercent');

if (payin === null || payout === null) {
  echec(
    'Taux par défaut introuvables dans PlatformSettings.cs',
    'PayinFeePercent / PayoutFeePercent doivent porter une valeur par défaut.'
  );
} else {
  const a = payin / 100;
  const b = payout / 100;
  const multiplicateur = (1 + b) / (1 - a);

  let pire = { marge: Infinity, cible: null };
  for (let T = MONTANT_MIN; T <= MONTANT_MAX; T++) {
    const debite = Math.ceil(T * multiplicateur); // ce que paie la famille
    const entre = debite - Math.round(debite * a); // ce qui entre en réserve
    const sort = T + Math.round(T * b); // ce que coûte le retrait de T
    const marge = entre - sort;
    if (marge < pire.marge) pire = { marge, cible: T };
  }

  if (pire.marge < 0) {
    echec(
      `La plateforme AVANCE de l'argent sur certains montants`,
      `Pire cas : une cible de ${pire.cible} FCFA laisse ${pire.marge} FCFA.\n` +
        `         Taux en vigueur : encaissement ${payin} %, retrait ${payout} %,\n` +
        `         majoration déduite ${((multiplicateur - 1) * 100).toFixed(3)} %.`
    );
  }
}

// ====================================================================
// 3. Le code et la migration disent-ils la même chose ?
// ====================================================================
// La ligne de réglages est un SINGLETON créé en juin : la valeur par défaut C#
// ne l'atteint jamais. Seule une migration peut la poser. Si les deux
// divergent, une base neuve et la production appliquent deux majorations
// différentes — et c'est la production qu'on ne regarde pas.
if (payin !== null) {
  const fichiers = fs
    .readdirSync(MIGRATIONS)
    .filter((f) => f.endsWith('.cs') && !f.endsWith('.Designer.cs'))
    .sort();

  let posee = null;
  let posePar = null;
  for (const fichier of fichiers) {
    const brut = fs.readFileSync(path.join(MIGRATIONS, fichier), 'utf8');

    // 🔴 Ne lire que le corps de `Up()`. Le `Down()` d'une migration de
    // recalibrage repose la valeur PRÉCÉDENTE — la prendre pour la valeur
    // courante ferait échouer le contrôle sur une migration pourtant juste
    // (constaté au premier recalibrage : 5.37 lu dans le Down au lieu de 5.40).
    const debutUp = brut.indexOf('void Up(');
    const debutDown = brut.indexOf('void Down(');
    const source =
      debutUp === -1
        ? brut
        : brut.slice(debutUp, debutDown === -1 ? undefined : debutDown);
    // Un UPDATE (ou un defaultValue) qui fixe PayinFeePercent. Le `\\?` couvre
    // le guillemet échappé du SQL PostgreSQL écrit dans une chaîne C# :
    //   "UPDATE \"PlatformSettings\" SET \"PayinFeePercent\" = 5.37;"
    const re = /PayinFeePercent\\?"?\s*=\s*([0-9.]+)/g;
    let m;
    while ((m = re.exec(source)) !== null) {
      posee = parseFloat(m[1]);
      posePar = fichier;
    }
  }

  if (posee === null) {
    echec(
      'Aucune migration ne pose PayinFeePercent',
      "La ligne de réglages EXISTE DÉJÀ en base : la valeur par défaut C#\n" +
        "         ne l'atteindra jamais (§193, §202, §207, §232, §254). Il faut un\n" +
        '         migrationBuilder.Sql("UPDATE \\"PlatformSettings\\" SET ...").'
    );
  } else if (Math.abs(posee - payin) > 1e-9) {
    echec(
      'Le code et la migration ne posent pas le même taux',
      `Défaut C# : ${payin} · migration ${posePar} : ${posee}.\n` +
        '         Une base neuve et la production calculeraient deux majorations\n' +
        '         différentes.'
    );
  }
}

// ====================================================================
// Verdict
// ====================================================================
console.log('');
if (echecs.length === 0) {
  const a = payin / 100;
  const b = payout / 100;
  const pct = (((1 + b) / (1 - a) - 1) * 100).toFixed(3);
  console.log(
    `Majoration déduite ${pct} % (encaissement ${payin} %, retrait ${payout} %) · ` +
      `neutre de ${MONTANT_MIN} à ${MONTANT_MAX} FCFA AU TAUX SAISI · ` +
      `3 contrôles, 0 en échec.`
  );
  console.log(
    "L'arrondi réel du prestataire n'est pas modélisé ici — c'est l'écran " +
      'SuperAdmin qui le mesure.'
  );
  process.exit(0);
}

for (const e of echecs) {
  console.log(`  ÉCHEC — ${e.titre}`);
  console.log(`         ${e.detail}`);
}
console.log('');
console.log(
  `${echecs.length} contrôle(s) EN ÉCHEC. Une majoration mal calibrée ne lève`
);
console.log(
  'aucune erreur : elle se paie, en silence, sur la trésorerie (§255).'
);
process.exit(1);
