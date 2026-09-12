#!/usr/bin/env node
/*
 * Contrôle des migrations EF : le piège du défaut à ZÉRO sur une table de RÉGLAGES.
 *
 * POURQUOI CET OUTIL EXISTE
 * -------------------------
 * Ce piège s'est reproduit CINQ FOIS (§193, §202, §207, §232), et chaque fois
 * il a fallu le découvrir en production, parce qu'il ne produit **aucune
 * erreur** :
 *
 *   1. on ajoute une colonne de réglage — un plafond SMS, un tarif, un nombre
 *      de pages offertes — à `PlatformSettings` ou `SchoolPaymentSettings` ;
 *   2. la propriété C# porte une belle valeur par défaut (`= 25_000;`) ;
 *   3. mais EF génère `defaultValue: 0` dans la migration ;
 *   4. or la ligne de réglages EXISTE DÉJÀ en base. Elle hérite donc de **zéro**,
 *      pas de la valeur C# — celle-ci ne sert qu'aux lignes créées ensuite.
 *
 * Résultat : un plafond à zéro coupe le service, un tarif à zéro rend une vente
 * gratuite, un quota à zéro rend une fonctionnalité morte-née. Tout fonctionne,
 * rien ne proteste, et personne ne le voit avant qu'un utilisateur ne se plaigne.
 *
 * Même esprit que check-html-pages.js (§218) et check-i18n-pages.js (§228) :
 * ce qui ne se voit pas à la relecture doit se vérifier par une commande.
 *
 * CE QUI EST VÉRIFIÉ
 *   Tout `AddColumn` visant une table de réglages avec un défaut nul
 *   (0, 0L, 0m, 0.0, false) doit porter une DÉROGATION EXPLICITE :
 *
 *       // defaut-zero-voulu: <raison en clair>
 *
 *   placée dans le fichier de migration. Écrire la raison oblige à se demander
 *   « que vaudra cette colonne pour la ligne déjà en base ? » — c'est
 *   exactement la question qui n'a pas été posée cinq fois.
 *
 * CE QUI N'EST PAS VÉRIFIÉ, ET C'EST VOULU
 *   Les autres tables. Une colonne ajoutée à `Payments` ou `Students` concerne
 *   des lignes métier dont zéro est souvent la bonne valeur de départ. Élargir
 *   ici noierait le signal — et un contrôle qu'on finit par ignorer ne protège
 *   plus de rien.
 *
 * Usage : node Idara.API/Tools/check-migrations.js
 * Sort en code 1 dès qu'une migration est en défaut.
 */

const fs = require('fs');
const path = require('path');

/** Tables dont la ou les lignes PRÉEXISTENT à toute nouvelle colonne. */
const SETTINGS_TABLES = ['PlatformSettings', 'SchoolPaymentSettings'];

/** Marqueur de dérogation à poser dans le fichier de migration. */
const WAIVER = 'defaut-zero-voulu:';

/**
 * Migrations ANTÉRIEURES à la mise en place de ce contrôle (2026-09-12).
 * Elles sont déjà appliquées en production : les annoter après coup n'aurait
 * aucun effet sur les lignes existantes. On les gèle donc telles quelles, et
 * le contrôle ne porte que sur ce qui vient APRÈS.
 * ⚠️ Ne jamais ajouter de nom ici pour faire taire un échec : cette liste est
 * un héritage daté, pas une soupape.
 */
const HERITAGE_AVANT_LE_CONTROLE = new Set([
  '20260610214953_Phase4SubscriptionsFoundations.cs',
  '20260901212104_SmsGovernanceAndOpsAlerts.cs',
  '20260902145845_PhotoImportOcr.cs',
  '20260904173558_DonationCampaigns.cs',
  '20260908185725_OcrPagePurchase.cs',
]);

const migrationsDir = path.join(__dirname, '..', 'Migrations');

/** Défauts considérés comme « nuls » dans une migration EF. */
const ZERO = /^(0L?|0m|0\.0|0\.0m|false)$/;

function blocsAddColumn(source) {
  const blocs = [];
  const re = /migrationBuilder\.AddColumn<[^>]+>\(/g;
  let m;
  while ((m = re.exec(source)) !== null) {
    // Le bloc court jusqu'au « ); » qui ferme l'appel.
    const fin = source.indexOf(');', m.index);
    if (fin === -1) continue;
    blocs.push({
      texte: source.slice(m.index, fin),
      ligne: source.slice(0, m.index).split('\n').length,
    });
  }
  return blocs;
}

function champ(bloc, nom) {
  const m = bloc.match(new RegExp(nom + ':\\s*"?([^",\\n)]+)"?'));
  return m ? m[1].trim() : null;
}

function main() {
  if (!fs.existsSync(migrationsDir)) {
    console.error(`Dossier introuvable : ${migrationsDir}`);
    process.exit(1);
  }

  const fichiers = fs
    .readdirSync(migrationsDir)
    .filter((f) => f.endsWith('.cs') && !f.endsWith('.Designer.cs'))
    .sort();

  let enDefaut = 0;
  let verifies = 0;

  for (const fichier of fichiers) {
    const chemin = path.join(migrationsDir, fichier);
    const source = fs.readFileSync(chemin, 'utf8');
    const derogation = source.includes(WAIVER);
    const herite = HERITAGE_AVANT_LE_CONTROLE.has(fichier);

    for (const bloc of blocsAddColumn(source)) {
      const table = champ(bloc.texte, 'table');
      if (!table || !SETTINGS_TABLES.includes(table)) continue;

      const defaut = champ(bloc.texte, 'defaultValue');
      if (!defaut || !ZERO.test(defaut)) continue;

      const colonne = champ(bloc.texte, 'name') || '?';
      verifies++;

      if (herite) continue;
      if (derogation) continue;

      enDefaut++;
      console.log(
        `  ÉCHEC ${fichier}:${bloc.ligne} — ${table}.${colonne} = ${defaut}`
      );
      console.log(
        `         La ligne de réglages EXISTE DÉJÀ : elle héritera de ${defaut}, ` +
          `et NON de la valeur par défaut C#.`
      );
      console.log(
        `         Si c'est voulu, écrire dans ce fichier :  // ${WAIVER} <raison>`
      );
    }
  }

  console.log('');
  if (enDefaut === 0) {
    console.log(
      `${verifies} défaut(s) nul(s) sur table de réglages · 0 en échec.`
    );
    process.exit(0);
  }
  console.log(
    `${verifies} défaut(s) nul(s) sur table de réglages · ${enDefaut} EN ÉCHEC.`
  );
  console.log(
    'Ce piège a déjà coûté cinq fonctionnalités nées mortes (§193, §202, §207, §232).'
  );
  process.exit(1);
}

main();
