namespace Idara.API.Enums
{
    /// <summary>
    /// Régime d'hébergement d'un élève au daara. Sérialisé en string
    /// (PascalCase) via JsonStringEnumConverter, persisté en integer.
    /// </summary>
    /// <remarks>
    /// ⚠️ Les valeurs sont PERSISTÉES en base : ne JAMAIS réordonner ni
    /// réutiliser un numéro. Tout nouveau régime s'ajoute à la suite (≥ 4).
    ///
    /// La numérotation commence à 1 (et non 0) VOLONTAIREMENT : la colonne est
    /// nullable (« non renseigné »), et si un jour un chemin de code écrivait un
    /// <c>default(BoardingStatus)</c> par mégarde, la valeur 0 ne correspondrait
    /// à AUCUN régime — donc à une erreur visible — au lieu de classer
    /// silencieusement l'élève en internat et de lui appliquer son tarif.
    /// </remarks>
    public enum BoardingStatus
    {
        /// <summary>Interne : logé et nourri au daara.</summary>
        Boarding = 1,

        /// <summary>Demi-interne : présent la journée, repas sur place, rentre le soir.</summary>
        HalfBoarding = 2,

        /// <summary>Externe : vient aux cours uniquement.</summary>
        Day = 3
    }
}
