namespace Idara.API.Models
{
    /// <summary>
    /// Un avantage affiché sous un plan sur la page publique des tarifs
    /// (« Élèves illimités », « Suivi Coran », « 500 SMS inclus »…).
    ///
    /// Texte LIBRE et ordonnable : ajouter, retirer ou réécrire une ligne se
    /// fait depuis le back-office, sans toucher au code ni redéployer — c'est
    /// tout l'intérêt de la table.
    /// </summary>
    public class SubscriptionPlanFeature
    {
        public int Id { get; set; }

        public int PlanId { get; set; }
        public SubscriptionPlan Plan { get; set; } = null!;

        /// <summary>Libellé français (obligatoire).</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Libellé arabe (facultatif ; à défaut, le français s'affiche).</summary>
        public string? LabelAr { get; set; }

        /// <summary>
        /// Avantage INCLUS (coche verte) ou explicitement NON inclus (gris
        /// barré). Permet d'écrire « Journal du daara » en non inclus sur un
        /// petit plan pour montrer ce qu'apporte le plan supérieur.
        /// </summary>
        public bool Included { get; set; } = true;

        public int DisplayOrder { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
