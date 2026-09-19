namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 📅 Le calendrier de l'abonnement plateforme : toutes les écoles sont
    /// prélevées le MÊME jour du mois.
    ///
    /// <para><b>Pourquoi ce changement, en un chiffre.</b> L'échéance valait
    /// « 30 jours après la validation de l'école », donc n'importe quel jour du
    /// mois. Or l'argent des écoles a un rythme, mesuré sur toute la production
    /// le 2026-09-19 : <b>61 % des paiements de parents</b> arrivent entre le 1er
    /// et le 10, et <b>9 % seulement</b> entre le 11 et le 20. Deux écoles
    /// avaient leur échéance les 18 et 19 — en plein dans le creux.</para>
    ///
    /// <para><b>Pourquoi le 8 et pas le 5.</b> En cumulant entrées moins sorties
    /// jour par jour, la trésorerie des écoles culmine le <b>8</b> (509 232 F)
    /// et s'effondre le 9 (un retrait de 578 950 F). Le 5 — le jour qu'on
    /// choisirait d'instinct, celui des factures parents — ne donne que
    /// 223 090 F, soit <b>2,3 fois moins</b> : à cette date, les parents sont
    /// encore en train de payer.</para>
    ///
    /// <para>🔑 <b>RÈGLE INTANGIBLE (Cheikh, 2026-09-19)</b> : « au moins
    /// 30 jours d'essai gratuit est garanti pour chaque nouvelle école, et toute
    /// école doit payer un abonnement le 8 du mois APRÈS ces 30 jours ».
    /// <see cref="TrialEnd"/> est le seul endroit qui la réalise, et
    /// <c>subscription_schedule_test</c> la garde : jamais moins de 30 jours,
    /// jamais un autre jour que le jour d'ancrage.</para>
    ///
    /// <para>Pure et publique à dessein : une règle de dates qu'on ne peut
    /// vérifier qu'en attendant le mois suivant ne se vérifie jamais
    /// (§133/§178).</para>
    /// </summary>
    public static class SubscriptionSchedule
    {
        /// <summary>Jour de prélèvement par défaut. Mesuré, pas choisi — voir plus haut.</summary>
        public const int DefaultBillingDay = 8;

        /// <summary>Durée minimale de l'essai gratuit, en jours. Promesse publique.</summary>
        public const int MinimumTrialDays = 30;

        /// <summary>
        /// Borne le jour d'ancrage à <b>1–28</b>.
        /// </summary>
        /// <remarks>
        /// 🔴 Au-delà de 28, le jour n'existe pas tous les mois : un ancrage au
        /// 31 sauterait février, avril, juin… et déplacerait l'échéance de façon
        /// imprévisible. Le réglage est éditable au back-office, donc la borne
        /// vit ici, au point de calcul, et pas dans l'écran qui le saisit — un
        /// contrôle d'écran ne protège pas des données déjà en base (§193).
        /// </remarks>
        public static int Normalize(int billingDay) =>
            billingDay < 1 ? DefaultBillingDay
            : billingDay > 28 ? 28
            : billingDay;

        /// <summary>
        /// Le premier jour d'ancrage à partir de <paramref name="from"/>, cette
        /// date <b>incluse</b>. Rendu à minuit UTC.
        /// </summary>
        /// <remarks>
        /// « Incluse » est délibéré : une école dont l'essai se termine
        /// exactement un 8 est prélevée ce 8-là, pas le mois suivant — elle a eu
        /// ses 30 jours pleins, la promesse est tenue.
        /// </remarks>
        public static DateTime FirstAnchorOnOrAfter(DateTime from, int billingDay)
        {
            var day = Normalize(billingDay);
            var d = from.Date;

            // L'ancrage de CE mois-ci, s'il n'est pas déjà passé.
            var thisMonth = new DateTime(d.Year, d.Month, day, 0, 0, 0, DateTimeKind.Utc);
            if (thisMonth >= d) return thisMonth;

            // Sinon celui du mois suivant. AddMonths sur le 1er puis pose du
            // jour : passer par AddMonths depuis le 31 ramènerait au 28/30 et
            // ferait dériver l'ancrage d'un mois à l'autre.
            var next = new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            return new DateTime(next.Year, next.Month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        /// <summary>
        /// Fin de l'essai gratuit : <b>au moins</b> <see cref="MinimumTrialDays"/>
        /// jours pleins à compter de <paramref name="startUtc"/>, puis le premier
        /// jour d'ancrage qui suit.
        /// </summary>
        /// <remarks>
        /// <para>C'est la réponse au faux dilemme « 30 jours promis » contre
        /// « facturer à date fixe » : l'essai n'est jamais raccourci, il est
        /// <b>prolongé</b> jusqu'à l'ancrage. La promesse est dépassée, jamais
        /// trahie, et l'école n'a pas à comprendre une première facture au
        /// prorata — ce qui, pour un directeur de daara, se traduit par un appel
        /// au support.</para>
        ///
        /// <para>Coût mesuré : ~15 jours offerts en moyenne, <b>une seule fois</b>
        /// par école. Sur les 7 écoles de la plateforme : ~17 500 F au total,
        /// moins cher qu'un seul échec de prélèvement.</para>
        /// </remarks>
        public static DateTime TrialEnd(DateTime startUtc, int billingDay) =>
            FirstAnchorOnOrAfter(startUtc.Date.AddDays(MinimumTrialDays), billingDay);

        /// <summary>
        /// L'échéance de bascule d'un abonnement EXISTANT : le premier ancrage à
        /// partir de son échéance actuelle.
        /// </summary>
        /// <remarks>
        /// 🔑 <b>Ne raccourcit JAMAIS</b> (décision de Cheikh) : on part de
        /// l'échéance en cours, donc aucune école n'est prélevée plus tôt que ce
        /// qui lui a été annoncé, et aucune ne perd un jour d'essai. Une école
        /// au 10/10 passe au 08/11, pas au 08/10.
        /// </remarks>
        public static DateTime RealignExisting(DateTime currentNextBillingAt, int billingDay) =>
            FirstAnchorOnOrAfter(currentNextBillingAt, billingDay);
    }
}
