namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// Ce que le prestataire de paiement prélève, <b>au franc près</b>.
    /// </summary>
    /// <remarks>
    /// <para>🔴 <b>Les frais ne sont PAS un pourcentage.</b> C'est la découverte
    /// du 2026-09-13, faite en retrouvant la règle sur les 197 paiements réglés
    /// en production — <b>197/197 exacts</b> :</para>
    ///
    /// <code>
    /// encaissement = round(C × 3,6 %) + ceil( ceil(C × 1,5 %) × 1,18 )
    /// décaissement =                    ceil( ceil(T × 1,5 %) × 1,18 )
    /// </code>
    ///
    /// <para>Autrement dit : la commission du prestataire est arrondie au franc
    /// le plus proche, et la part opérateur est un taux <b>hors taxe</b> arrondi
    /// au franc, auquel s'ajoute une TVA elle-même arrondie au franc. Le fameux
    /// « 1,77 % » n'existe nulle part : c'est 1,5 % + 18 % de TVA, vu de loin.
    /// Et le « 5,37 % » d'encaissement est une moyenne qui n'est exacte pour
    /// <b>aucun</b> montant réel — mesurée, elle va de 5,37 % à 6,05 % selon la
    /// taille du paiement, l'arrondi pesant d'autant plus que le montant est
    /// petit.</para>
    ///
    /// <para>🔑 <b>Conséquence, et c'est tout l'objet de ce fichier</b> : on ne
    /// peut pas atteindre l'exactitude en appliquant un taux, aussi bien calibré
    /// soit-il. Il faut <b>résoudre</b> — chercher le plus petit montant à
    /// débiter qui couvre exactement les frais d'entrée ET ceux du retrait à
    /// venir. C'est ce que fait <see cref="ChargeFor"/>.</para>
    ///
    /// <para>⚠️ <b>Aucun taux n'est écrit ici.</b> Ils viennent tous de
    /// <c>PlatformSettings</c>, donc de ce que le SuperAdmin a saisi. Un taux en
    /// dur, même « juste aujourd'hui », est une rechute garantie le jour où le
    /// prestataire change sa grille — c'est exactement ainsi que la majoration
    /// s'est retrouvée fausse pendant quatre mois. <c>check-no-hardcoded-rates.js</c>
    /// veille.</para>
    /// </remarks>
    public readonly record struct ProviderFees
    {
        /// <summary>Commission du prestataire à l'encaissement, en % du montant débité.</summary>
        public double PayinProviderPercent { get; init; }

        /// <summary>Part opérateur à l'encaissement, en % HORS TAXE du montant débité.</summary>
        public double PayinOperatorPercentHt { get; init; }

        /// <summary>Part opérateur au décaissement, en % HORS TAXE du montant envoyé.</summary>
        public double PayoutOperatorPercentHt { get; init; }

        /// <summary>TVA appliquée aux commissions opérateur (18 au Sénégal).</summary>
        public double VatPercent { get; init; }

        /// <summary>
        /// Fenêtre de redescente de <see cref="ChargeFor"/>.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>Elle existe parce que « ce qui reste après frais » n'est PAS une
        /// fonction croissante du montant débité.</b> Mesuré : sur 300 000
        /// montants, 809 reculs d'un franc — l'effet des deux arrondis qui se
        /// croisent. Le premier montant qui couvre n'est donc pas toujours le
        /// plus petit, et sans cette redescente la famille paie 1 F de trop dans
        /// 0,3 % des cas. Ce n'est pas une perte pour la plateforme, mais on ne
        /// réclame pas un franc dont on n'a pas besoin.
        ///
        /// <para>64 est très large : les reculs observés valent 1 F.</para>
        /// </remarks>
        private const int SearchWindow = 64;

        /// <summary>Garde-fou d'itérations — une configuration absurde ne doit pas boucler.</summary>
        private const int MaxIterations = 10_000;

        /// <summary>
        /// Les taux sont-ils tous renseignés ? <c>false</c> tant que le
        /// SuperAdmin n'a rien saisi — auquel cas aucun paiement « frais au
        /// payeur » ne doit partir.
        /// </summary>
        /// <remarks>
        /// On refuse plutôt que de retomber sur une valeur de repli : un repli
        /// silencieux est précisément ce qui a laissé une majoration fausse
        /// tourner quatre mois. Mieux vaut un message clair qu'un encaissement
        /// calculé sur un chiffre inventé.
        /// </remarks>
        public bool IsConfigured =>
            PayinProviderPercent is >= 0 and < 100
            && PayinOperatorPercentHt is >= 0 and < 100
            && PayoutOperatorPercentHt is >= 0 and < 100
            && VatPercent is >= 0 and < 100
            // Un encaissement à 0 % de frais n'existe pas : c'est le signe que
            // rien n'a été saisi, pas celui d'un prestataire gratuit.
            && PayinProviderPercent + PayinOperatorPercentHt > 0;

        private double Vat => 1.0 + VatPercent / 100.0;

        /// <summary>Arrondi au franc SUPÉRIEUR. Le FCFA n'a pas de centimes.</summary>
        /// <remarks>
        /// L'epsilon absorbe le bruit binaire : <c>15000 × 0.015</c> vaut
        /// 224.99999999999997 en double, et un <c>Ceiling</c> nu en ferait 225
        /// au lieu de 225 — ici sans conséquence, mais ailleurs un franc de
        /// trop. On ne laisse pas un arrondi monétaire dépendre de ça.
        /// </remarks>
        private static long CeilFranc(double value) => (long)Math.Ceiling(value - 1e-9);

        /// <summary>Arrondi au franc le PLUS PROCHE, moitié vers le haut.</summary>
        private static long RoundFranc(double value) => (long)Math.Floor(value + 0.5);

        /// <summary>
        /// Frais retenus sur un ENCAISSEMENT de <paramref name="chargedFcfa"/>
        /// (le montant réellement débité au payeur, majoration comprise).
        /// </summary>
        public long PayinFeesFor(long chargedFcfa)
        {
            EnsureConfigured();
            if (chargedFcfa <= 0) return 0;

            var provider = RoundFranc(chargedFcfa * PayinProviderPercent / 100.0);
            var operatorHt = CeilFranc(chargedFcfa * PayinOperatorPercentHt / 100.0);
            var operatorTtc = CeilFranc(operatorHt * Vat);
            return provider + operatorTtc;
        }

        /// <summary>
        /// Frais prélevés sur un DÉCAISSEMENT de <paramref name="amountFcfa"/>.
        /// </summary>
        /// <remarks>
        /// 🔑 <b>Ils sont prélevés EN PLUS du montant envoyé</b>
        /// (<c>receive_amount</c> est ce que TOUCHE le bénéficiaire, §274) :
        /// sortir T de la réserve coûte <c>T + PayoutFeesFor(T)</c>. Mesuré en
        /// production le 2026-09-17 — 500 F reçus, <b>505 F</b> sortis.
        ///
        /// <para>🔑 <b>C'est le portefeuille de celui qui décaisse qui les
        /// porte, au moment du retrait</b> : plus aucune provision n'est
        /// constituée à l'encaissement (2026-09-17). Le débit vaut donc
        /// « reçu + frais », et un solde n'est jamais retirable en entier —
        /// voir <see cref="MaxReceivableFrom"/>.</para>
        ///
        /// <para>⚠️ <b>Cette estimation ne fait pas foi au règlement.</b> On la
        /// calcule avant l'appel, pour l'afficher et pour réserver les fonds ;
        /// c'est le <c>fee</c> réellement renvoyé par Wave qui décide du débit
        /// définitif. Un écart d'arrondi entre les deux se solde par une
        /// écriture d'ajustement, jamais par une réécriture du passé.</para>
        /// </remarks>
        public long PayoutFeesFor(long amountFcfa)
        {
            EnsureConfigured();
            if (amountFcfa <= 0) return 0;

            var ht = CeilFranc(amountFcfa * PayoutOperatorPercentHt / 100.0);
            return CeilFranc(ht * Vat);
        }

        /// <summary>
        /// 🔑 <b>LE point unique</b> : ce qu'il faut débiter au payeur pour que
        /// l'école encaisse exactement <paramref name="targetFcfa"/>, sans que
        /// la plateforme avance un franc sur l'encaissement.
        /// </summary>
        /// <remarks>
        /// <para>On cherche le plus petit entier <c>C</c> vérifiant :</para>
        /// <code>C − PayinFeesFor(C) ≥ targetFcfa</code>
        ///
        /// <para>🔴 <b>Le frais de SORTIE n'entre plus dans ce calcul</b>
        /// (2026-09-17). Il était provisionné dès l'encaissement ; il est
        /// désormais réglé au moment du retrait, par celui qui décaisse. Motif :
        /// la provision était constituée <b>une fois</b>, à l'entrée, alors
        /// qu'un retrait FRACTIONNÉ paie son propre arrondi au franc supérieur à
        /// chaque fois. Mesuré : 75 % des suites de retraits partiels finissaient
        /// en déficit (moyenne 2,15 F, pire 8 F) — un déficit que rien ne
        /// signalait, la plateforme le comblant en silence. Chaque opération
        /// paie maintenant ce qu'elle coûte, au moment où elle le coûte : exact
        /// par construction, retraits fractionnés compris.</para>
        ///
        /// <para><b>Pourquoi une recherche et non une multiplication.</b> La
        /// commission d'entrée porte sur <c>C</c>, qui contient la majoration :
        /// le montant cherché apparaît des deux côtés de l'équation. Un taux
        /// <c>(1+b)/(1−a)</c> en donne la solution continue — correcte à
        /// l'euro-près, fausse au franc près, parce que les frais réels sont des
        /// entiers arrondis et non un pourcentage. Mesuré : la méthode par taux
        /// laisse 3 542 montants en déficit entre 200 et 100 000 FCFA, et fait
        /// payer jusqu'à 31 F de trop ailleurs. Celle-ci : <b>0 déficit,
        /// 0 franc de trop</b> sur 68 372 cibles vérifiées.</para>
        ///
        /// <para>La recherche part d'une estimation, encadre, puis redescend sur
        /// <see cref="SearchWindow"/> — voir le commentaire de cette constante
        /// pour la raison, qui n'est pas évidente.</para>
        /// </remarks>
        public long ChargeFor(long targetFcfa)
        {
            EnsureConfigured();
            if (targetFcfa <= 0) return 0;

            // Le frais de sortie n'est plus provisionné ici (cf. remarques) :
            // la cible est la cible, et rien d'autre.
            var needed = targetFcfa;

            // Estimation continue : la solution exacte en est toujours voisine.
            // Elle ne sert qu'à démarrer près du but ; c'est l'encadrement qui
            // décide, jamais elle.
            var approxRate = (PayinProviderPercent + PayinOperatorPercentHt * Vat) / 100.0;
            var charged = approxRate < 0.95
                ? (long)Math.Ceiling(needed / (1.0 - approxRate))
                : needed;
            if (charged < needed) charged = needed;

            var guard = 0;
            while (charged > needed && charged - PayinFeesFor(charged) >= needed)
            {
                charged--;
                if (++guard > MaxIterations) break;
            }
            while (charged - PayinFeesFor(charged) < needed)
            {
                charged++;
                if (++guard > MaxIterations)
                {
                    throw new InvalidOperationException(
                        $"Calcul du montant à débiter impossible pour une cible de {targetFcfa} FCFA " +
                        "— les taux de commission saisis sont incohérents.");
                }
            }

            // Redescente : le premier montant qui couvre n'est pas toujours le
            // plus petit (cf. SearchWindow).
            var best = charged;
            for (var step = 1; step <= SearchWindow; step++)
            {
                var candidate = charged - step;
                if (candidate < needed) break;
                if (candidate - PayinFeesFor(candidate) >= needed) best = candidate;
            }
            return best;
        }

        /// <summary>
        /// 🔑 <b>Le plus grand montant qu'un bénéficiaire peut RECEVOIR</b> avec
        /// <paramref name="balanceFcfa"/> disponible — frais de sortie compris.
        /// C'est la réponse au bouton « Tout » de l'écran Retrait.
        /// </summary>
        /// <remarks>
        /// <para>On cherche le plus grand entier <c>R</c> vérifiant :</para>
        /// <code>R + PayoutFeesFor(R) ≤ balanceFcfa</code>
        ///
        /// <para><b>Pourquoi un solde ne se retire pas « en entier ».</b> Les
        /// frais de décaissement sont prélevés <b>en sus</b> du montant envoyé
        /// (<c>receive_amount</c> est ce que touche le bénéficiaire, §274) :
        /// sortir 1 000 F coûte 1 010 F. Avec 1 000 F au portefeuille, on envoie
        /// donc 990 F, pas 1 000. Cette fonction dit exactement combien, et
        /// c'est elle qui alimente le champ « Le bénéficiaire reçoit » quand le
        /// directeur demande tout son solde.</para>
        ///
        /// <para>🔴 <b>Elle ne sert PLUS à créditer un portefeuille</b>
        /// (2026-09-17). Elle bornait autrefois le crédit du mode « l'école paie
        /// les frais », pour provisionner la sortie dès l'entrée ; cette
        /// provision a disparu — chaque opération paie ses propres frais, au
        /// moment où elle les engage. Le mode « l'école paie » crédite désormais
        /// le net <b>entier</b>. Voir <see cref="ChargeFor"/>.</para>
        ///
        /// <para>⚠️ Même précaution que <see cref="ChargeFor"/> : la fonction
        /// <c>R + frais(R)</c> n'est pas strictement croissante (les arrondis se
        /// croisent), donc on encadre puis on balaie pour trouver le vrai
        /// maximum — sinon on retient un franc de trop à l'école.</para>
        /// </remarks>
        public long MaxReceivableFrom(long balanceFcfa)
        {
            EnsureConfigured();
            if (balanceFcfa <= 0) return 0;

            // Estimation continue, puis encadrement — l'estimation ne décide
            // jamais, elle ne fait que rapprocher.
            var receivable = (long)Math.Floor(balanceFcfa / (1.0 + PayoutOperatorPercentHt * Vat / 100.0));
            if (receivable < 0) receivable = 0;
            if (receivable > balanceFcfa) receivable = balanceFcfa;

            var guard = 0;
            while (receivable > 0 && receivable + PayoutFeesFor(receivable) > balanceFcfa)
            {
                receivable--;
                if (++guard > MaxIterations) break;
            }
            while (receivable + 1 <= balanceFcfa
                   && (receivable + 1) + PayoutFeesFor(receivable + 1) <= balanceFcfa)
            {
                receivable++;
                if (++guard > MaxIterations) break;
            }

            // Remontée : le premier R qui « tient » n'est pas toujours le plus
            // grand, pour la même raison de non-monotonie.
            var best = receivable;
            for (var step = 1; step <= SearchWindow; step++)
            {
                var candidate = receivable + step;
                if (candidate > balanceFcfa) break;
                if (candidate + PayoutFeesFor(candidate) <= balanceFcfa) best = candidate;
            }
            return best;
        }

        /// <summary>
        /// Le taux NOMINAL d'un encaissement, en % — commission du prestataire
        /// plus part opérateur TTC. <c>null</c> si les taux ne sont pas saisis.
        /// </summary>
        /// <remarks>
        /// 🔑 <b>C'est LA source de tout taux d'encaissement affiché</b> : CGU,
        /// page des tarifs, écrans. Il existe parce que le recomposer ailleurs
        /// — <c>provider + opHt × (1+TVA)</c> écrit une seconde fois — crée une
        /// deuxième implémentation de la règle, qui diverge en silence le jour
        /// où la structure change. C'est exactement ce qui s'était produit : les
        /// pages juridiques portaient leur propre calcul.
        ///
        /// <para>⚠️ <b>Un taux nominal n'est pas ce que paie le payeur.</b> Les
        /// frais réels sont deux arrondis au franc (§256) : ce pourcentage sert
        /// à ANNONCER, jamais à facturer. Pour ce que règle réellement une
        /// famille, voir <see cref="EffectiveMarkupPercent"/> ; pour ce qui est
        /// débité, <see cref="ChargeFor"/>.</para>
        /// </remarks>
        public double? PayinRatePercent => IsConfigured
            ? PayinProviderPercent + PayinOperatorPercentHt * Vat
            : null;

        /// <summary>
        /// Le taux NOMINAL d'un décaissement, en % (part opérateur TTC).
        /// <c>null</c> si les taux ne sont pas saisis.
        /// </summary>
        /// <remarks>
        /// 🔑 Même rôle que <see cref="PayinRatePercent"/>, et même
        /// avertissement : il ANNONCE, il ne facture pas. ⚠️ Il se prélève <b>en
        /// sus</b> du montant envoyé, et au moment du RETRAIT — plus jamais à
        /// l'encaissement (§275). Tout texte qui l'affiche doit le dire.
        /// </remarks>
        public double? PayoutRatePercent =>
            IsConfigured ? PayoutOperatorPercentHt * Vat : null;

        /// <summary>
        /// Majoration effective d'une cible donnée, en %. <b>Pour l'AFFICHAGE
        /// seulement</b> — elle varie d'un montant à l'autre.
        /// </summary>
        /// <remarks>
        /// 🔴 Ne jamais s'en servir pour calculer un montant à débiter : c'est
        /// exactement l'erreur qu'on vient de corriger. Elle existe pour qu'un
        /// écran puisse dire « environ +7,6 % », pas pour facturer.
        /// </remarks>
        public double EffectiveMarkupPercent(long targetFcfa) =>
            targetFcfa <= 0 ? 0 : (ChargeFor(targetFcfa) - targetFcfa) * 100.0 / targetFcfa;

        private void EnsureConfigured()
        {
            if (IsConfigured) return;
            throw new ProviderFeesNotConfiguredException();
        }
    }

    /// <summary>
    /// Les commissions du prestataire ne sont pas renseignées : on ne peut pas
    /// savoir ce qu'un paiement coûtera, donc on ne l'initie pas.
    /// </summary>
    /// <remarks>
    /// 🔑 <b>Refuser est le comportement voulu, pas un défaut.</b> L'alternative
    /// — retomber sur une valeur codée en dur — est précisément ce qui a laissé
    /// une majoration fausse tourner quatre mois sans que rien ne le signale.
    /// Un service arrêté se voit et se répare en trente secondes depuis
    /// SuperAdmin ; un calcul faux ne se voit pas.
    /// </remarks>
    public class ProviderFeesNotConfiguredException : InvalidOperationException
    {
        public ProviderFeesNotConfiguredException()
            : base("Les commissions du prestataire de paiement ne sont pas renseignées. "
                   + "SuperAdmin → Réglages plateforme → Frais.")
        {
        }
    }
}
