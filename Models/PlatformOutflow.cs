using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Sortie du solde de GAINS PLATEFORME (append-only). Le solde plateforme P
    /// est RECALCULÉ à la volée depuis les tables sources (excédents 8% des payins
    /// + revenus d'abonnement − frais payout des écoles) MOINS la somme de ces
    /// sorties. On ne persiste donc que les sorties (retraits plateforme via API
    /// en Phase B, ou retraits manuels depuis le dashboard marchand enregistrés a
    /// posteriori) — le reste est dérivé, ce qui évite toute dérive comptable.
    ///
    /// Invariant de réconciliation : Réserve SenePay (R, live) = Dette écoles (D)
    /// + Gains plateforme (P). Un écart négatif R − (D + P) &lt; 0 = sortie non
    /// encore enregistrée → à consigner via un ManualAdjustment.
    ///
    /// Convention : <see cref="AmountFcfa"/> est POSITIF = montant qui QUITTE le
    /// solde plateforme (frais de payout inclus pour un retrait plateforme).
    /// </summary>
    public class PlatformOutflow
    {
        public int Id { get; set; }

        public PlatformOutflowType Type { get; set; }

        /// <summary>Montant sorti du solde plateforme (positif), frais inclus.</summary>
        public long AmountFcfa { get; set; }

        /// <summary>Note libre (motif, référence du retrait dashboard…).</summary>
        public string? Note { get; set; }

        /// <summary>
        /// Date de la sortie réelle. Pour un retrait manuel dashboard, peut être
        /// antidatée à la date effective du retrait chez SenePay.
        /// </summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>SuperAdmin ayant enregistré la sortie.</summary>
        public int CreatedById { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
