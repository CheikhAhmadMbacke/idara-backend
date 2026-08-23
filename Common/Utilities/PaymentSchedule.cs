namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// 📅 Le calendrier de paiement d'un mois : quand la mensualité s'ouvre, et
    /// jusqu'à quand la famille a pour payer.
    ///
    /// <para><b>Le défaut réparé (retour du daara Jazbul xoulob, 2026-08-23).</b>
    /// Un seul jour était réglable et faisait deux métiers : la facture était
    /// émise le 5 <b>et</b> échue le 5. Dès le 6 au matin, le cron de rappel
    /// voyait <c>DueDate &lt; today</c>, basculait la facture en retard et
    /// envoyait un SMS de relance. Aucune fenêtre de paiement : un parent
    /// prévenu le 5 était traité en retardataire le 6.</para>
    ///
    /// <para>Deux jours désormais : l'<b>ouverture</b> (la facture existe, les
    /// familles sont prévenues, personne n'est en retard) et la <b>limite</b>
    /// (au-delà, retard signalé, familles concernées relancées, et le daara sort
    /// sa liste payés / non payés). Entre les deux, une fenêtre de paiement.</para>
    ///
    /// <para><b>Publique et pure à dessein</b> : ces dates commandent qui reçoit
    /// un SMS de relance. Une règle qu'on ne peut vérifier qu'en attendant le
    /// prochain cron n'est jamais vérifiée (§133).</para>
    /// </summary>
    public static class PaymentSchedule
    {
        /// <summary>
        /// Délai laissé à une famille quand la facture est émise <b>après</b> la
        /// date limite du mois : élève inscrit le 20 pour une limite au 15, ou
        /// rattrapage du cron.
        /// </summary>
        /// <remarks>
        /// ⚠️ Sans lui, ces factures-là naîtraient <b>déjà en retard</b> et
        /// déclencheraient un rappel dès le lendemain — le défaut d'origine,
        /// simplement déplacé sur les nouveaux inscrits. C'est la garde qui
        /// existait déjà avant ce chantier, conservée.
        /// </remarks>
        public const int MinimumGraceDays = 7;

        /// <summary>Bornes acceptées pour les deux jours réglables.</summary>
        public const int MinDay = 1;
        public const int MaxDay = 28;

        /// <summary>
        /// Date limite <b>théorique</b> de la période, telle que l'école l'a
        /// réglée — sans tenir compte du jour où la facture est réellement émise.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>Une limite antérieure à l'ouverture désigne le mois SUIVANT</b> :
        /// ouverture le 25, limite le 5 = « payez avant le 5 du mois prochain ».
        /// Sans cette règle, un daara qui ouvre le paiement en fin de mois pour
        /// le mois d'après ne pourrait rien exprimer.
        /// </remarks>
        public static DateTime DeadlineFor(DateTime periodStart, int openingDay, int deadlineDay)
        {
            var opening = Clamp(openingDay);
            var deadline = Clamp(deadlineDay);

            var month = new DateTime(periodStart.Year, periodStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            if (deadline < opening) month = month.AddMonths(1);

            // Borné aux jours réels du mois : février n'a pas de 30. Les deux
            // réglages sont déjà bornés à 28, la garde couvre une valeur
            // ancienne ou saisie hors application.
            var day = Math.Min(deadline, DateTime.DaysInMonth(month.Year, month.Month));
            return new DateTime(month.Year, month.Month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        /// <summary>
        /// Échéance à inscrire sur une facture émise le <paramref name="today"/>.
        /// </summary>
        /// <remarks>
        /// <para>C'est la limite réglée par l'école — sauf si elle est <b>déjà
        /// passée</b> au moment de l'émission, auquel cas la famille reçoit
        /// <see cref="MinimumGraceDays"/> jours.</para>
        ///
        /// <para>⚠️ Le délai minimum ne s'applique <b>que</b> dans ce cas : une
        /// école qui choisit sciemment une fenêtre courte (ouverture le 5, limite
        /// le 8) garde sa fenêtre. Repousser toute échéance à « au moins 7 jours »
        /// écraserait sa décision.</para>
        /// </remarks>
        public static DateTime DueDateFor(
            DateTime periodStart, int openingDay, int deadlineDay, DateTime today)
        {
            var deadline = DeadlineFor(periodStart, openingDay, deadlineDay);
            var day = today.Date;
            return deadline >= day
                ? deadline
                : DateTime.SpecifyKind(day.AddDays(MinimumGraceDays), DateTimeKind.Utc);
        }

        /// <summary>
        /// Limite proposée quand une école n'en a jamais réglé : dix jours après
        /// l'ouverture, borné au 28 (décision produit 2026-08-23).
        /// </summary>
        public static int DefaultDeadlineDay(int openingDay)
            => Math.Min(Clamp(openingDay) + 10, MaxDay);

        private static int Clamp(int day) => Math.Min(Math.Max(day, MinDay), MaxDay);
    }
}
