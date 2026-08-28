namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'un événement consigné au journal du daara.
    /// </summary>
    /// <remarks>
    /// ⚠️ Valeurs PERSISTÉES en base (colonne integer) : ne JAMAIS réordonner
    /// ni réutiliser un numéro. Toute nouvelle nature s'ajoute à la suite
    /// (≥ 10). Les libellés affichés, eux, changent librement (i18n).
    ///
    /// Liste FIXE, comme les catégories de transfert (§ TransferCategory) : des
    /// catégories créées par chaque école rendraient toute comparaison
    /// impossible et pousseraient à créer une catégorie par événement.
    /// </remarks>
    public enum DaaraEventCategory
    {
        /// <summary>Visite (autorité religieuse, parent, partenaire, inspection).</summary>
        Visit = 1,

        /// <summary>Réunion (parents, équipe pédagogique, comité).</summary>
        Meeting = 2,

        /// <summary>Cérémonie ou fête religieuse.</summary>
        Ceremony = 3,

        /// <summary>Travaux, construction, aménagement.</summary>
        Works = 4,

        /// <summary>Don ou soutien reçu (argent, vivres, matériel).</summary>
        Donation = 5,

        /// <summary>Achat ou acquisition.</summary>
        Purchase = 6,

        /// <summary>Incident (panne, dégât, accident, conflit).</summary>
        Incident = 7,

        /// <summary>Personnel (arrivée, départ, absence prolongée).</summary>
        Staff = 8,

        Other = 9,

        /// <summary>
        /// Information générale (annonce, consigne, nouvelle). Ajoutée le
        /// 2026-08-28 à la demande d'un daara. Valeur ≥ 10 : les valeurs sont
        /// persistées en integer, ne jamais réordonner.
        /// </summary>
        Info = 10
    }
}
