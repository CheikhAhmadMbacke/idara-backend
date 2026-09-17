using Idara.API.Enums;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Qui supporte les frais du prestataire : le payeur, ou l'établissement ?
    /// Point unique qui répond à la question, pour les six endroits qui la posent.
    ///
    /// <para><b>La réponse vient du réglage de l'école</b> (<see cref="FeesPayer"/>),
    /// jamais d'une règle locale. C'est elle qui décide si la famille règle le
    /// montant exact de sa dette, ou ce montant majoré de ce que prélèvera le
    /// prestataire.</para>
    ///
    /// <para>⚖️ <b>Position contractuelle, tranchée le 2026-09-17 par Cheikh après
    /// échange avec Wave (Jean Karim)</b> : la majoration reste en place. Elle
    /// avait été neutralisée le matin même par prudence, sur la lecture de
    /// l'article 8.2 du contrat ; l'échange a levé la question. La décision est
    /// donc prise en connaissance de la clause, pas à côté d'elle.</para>
    ///
    /// <para>🔑 <b>Ce que ce fichier apporte, et qui reste vrai quelle que soit la
    /// politique</b> : un seul endroit décide. Avant lui, six sites comparaient
    /// <c>FeesPayer == Parent</c> chacun de leur côté — dont deux qui oubliaient
    /// de vérifier que les taux étaient renseignés. Changer d'avis sur la
    /// majoration se fait ici, en une ligne, et vaut aussitôt pour la facture,
    /// le lien de paiement, les deux chemins de don, la recharge et l'écran des
    /// dettes.</para>
    /// </summary>
    public static class PayerMarkup
    {
        /// <summary>
        /// La majoration au payeur est-elle permise ?
        ///
        /// <para>Passer à <c>false</c> la retire partout d'un coup, sans toucher
        /// aux réglages des écoles : les familles règlent alors le montant
        /// exact, et l'établissement supporte la commission. C'est l'interrupteur
        /// à actionner si la position contractuelle change.</para>
        /// </summary>
        public const bool Allowed = true;

        /// <summary>
        /// Politique réellement appliquée, quel que soit le réglage enregistré.
        /// Tant que <see cref="Allowed"/> vaut <c>true</c>, c'est le choix de
        /// l'école qui vaut.
        /// </summary>
        public static FeesPayer Effective(FeesPayer stored) =>
            Allowed ? stored : FeesPayer.School;

        /// <summary>
        /// Montant à débiter pour que l'établissement encaisse
        /// <paramref name="targetFcfa"/>.
        /// </summary>
        /// <remarks>
        /// ⚠️ On ne multiplie pas par un taux : on <b>résout</b> le plus petit
        /// montant qui couvre exactement les frais d'entrée ET ceux du retrait à
        /// venir (§256 — majorer de <i>t</i> ne compense pas un prélèvement de
        /// <i>t</i>). Taux non renseignés → on réclame la cible nue plutôt qu'un
        /// chiffre inventé ; le refus tombe à l'initiation, là où l'argent bouge.
        /// </remarks>
        public static long ChargeFor(ProviderFees fees, FeesPayer stored, long targetFcfa) =>
            Effective(stored) == FeesPayer.Parent && fees.IsConfigured
                ? fees.ChargeFor(targetFcfa)
                : targetFcfa;
    }
}
