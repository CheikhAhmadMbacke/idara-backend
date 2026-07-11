using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Écriture du LIVRE DE CAISSE d'une école (gestion financière interne du
    /// daara). Enregistre les mouvements réalisés HORS de l'application (cash
    /// reçu, salaire payé en espèces, charges…) pour la réconciliation et le
    /// bilan total.
    ///
    /// ⚠️ TOTALEMENT INDÉPENDANT du <see cref="SchoolWallet"/> SenePay : une
    /// écriture de caisse NE touche JAMAIS le solde du wallet (= argent SenePay
    /// réel, base de la réconciliation §112). Le « bilan global » COMBINE les
    /// deux à la lecture, mais ils restent deux comptes distincts.
    ///
    /// Éditable / soft-deletable (c'est le journal interne du daara, pas un
    /// enregistrement financier SenePay append-only).
    /// </summary>
    public class CashLedgerEntry
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public CashEntryType Type { get; set; }

        /// <summary>Montant en FCFA (toujours positif ; le sens vient de <see cref="Type"/>).</summary>
        public long AmountFcfa { get; set; }

        /// <summary>Nom de la catégorie (dénormalisé pour l'affichage) : soit le
        /// texte libre (F1), soit le nom de la <see cref="CashCategory"/> liée (F2).</summary>
        public string? Category { get; set; }

        /// <summary>Catégorie gérée liée (F2) — null si texte libre (entrées F1).</summary>
        public int? CategoryId { get; set; }
        public CashCategory? CategoryRef { get; set; }

        /// <summary>Date du mouvement (jour civil, tronqué UTC).</summary>
        public DateTime OccurredAt { get; set; }

        public string? Note { get; set; }

        public int? CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }
    }
}
