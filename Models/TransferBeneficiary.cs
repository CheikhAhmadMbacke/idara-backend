using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Bénéficiaire enregistré dans le carnet d'une école (enseignant, personnel,
    /// fournisseur, bailleur…) pour accélérer les transferts récurrents (salaires,
    /// loyer…) sans ressaisir les coordonnées à chaque fois.
    ///
    /// On NE supprime jamais physiquement un bénéficiaire référencé par des
    /// transferts passés : on l'archive (<see cref="IsArchived"/>) pour préserver
    /// l'historique financier (FK Withdrawal.BeneficiaryId).
    /// </summary>
    public class TransferBeneficiary
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Nom du bénéficiaire (personne ou organisation).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Numéro national 9 chiffres (sans indicatif). On préfixe "221" à l'appel SenePay.</summary>
        public string Phone { get; set; } = string.Empty;

        public PaymentOperator Operator { get; set; }

        /// <summary>Catégorie par défaut proposée quand on paie ce bénéficiaire.</summary>
        public TransferCategory DefaultCategory { get; set; } = TransferCategory.Other;

        /// <summary>
        /// Nom de la nature quand <see cref="DefaultCategory"/> == Other — même
        /// rôle que <see cref="Withdrawal.CategoryLabel"/>, pré-rempli au
        /// transfert. Sans lui, choisir « Autre » au carnet ne permettait de rien
        /// préciser du tout.
        /// </summary>
        public string? DefaultCategoryLabel { get; set; }

        /// <summary>Note libre optionnelle (poste, rôle…).</summary>
        public string? Note { get; set; }

        /// <summary>Archivé = masqué du carnet mais conservé pour l'historique.</summary>
        public bool IsArchived { get; set; }

        public int CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
