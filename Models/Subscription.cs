using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Abonnement plateforme d'une école (1-1 : index unique sur
    /// <see cref="SchoolId"/>). Créé en <see cref="SubscriptionStatus.Trial"/>
    /// 30 jours à la validation de l'école. Le prix et le quota sont snapshotés
    /// (<see cref="AmountFcfa"/> / <see cref="NotificationQuota"/>) pour figer le
    /// tarif du cycle même si le <see cref="SubscriptionPlan"/> change ensuite.
    ///
    /// Pas de soft-delete (entité de facturation). Changer de plan = on met à
    /// jour PlanId + on re-snapshote au PROCHAIN renouvellement (pas en cours de
    /// cycle).
    /// </summary>
    public class Subscription
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Plan courant (nullable si le plan a été supprimé — le snapshot reste autoritatif).</summary>
        public int? PlanId { get; set; }
        public SubscriptionPlan? Plan { get; set; }

        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;

        /// <summary>Montant snapshoté à facturer ce cycle (FCFA).</summary>
        public long AmountFcfa { get; set; }

        /// <summary>Quota notif snapshoté pour ce cycle.</summary>
        public int NotificationQuota { get; set; }

        /// <summary>Compteur de notifications consommées sur le cycle courant (remis à 0 au renouvellement).</summary>
        public int NotificationUsedThisCycle { get; set; }

        public DateTime TrialEndsAt { get; set; }

        /// <summary>Prochaine échéance de prélèvement (= TrialEndsAt au départ, puis +1 cycle).</summary>
        public DateTime NextBillingAt { get; set; }

        /// <summary>Fin de la période de grâce (7j après un prélèvement échoué). Au-delà → ReadOnly.</summary>
        public DateTime? GracePeriodEndsAt { get; set; }

        /// <summary>Fin de la période ReadOnly (14j). Au-delà → Suspended.</summary>
        public DateTime? ReadOnlyEndsAt { get; set; }

        public DateTime? ActivatedAt { get; set; }
        public DateTime? SuspendedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
