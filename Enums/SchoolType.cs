namespace Idara.API.Enums
{
    /// <summary>
    /// Nature de l'établissement. Sert d'abord à proposer les BONS niveaux de
    /// classe : « CE1 » n'a aucun sens dans un daara, « Halaqa 3 » n'en a aucun
    /// dans une école classique. Sert aussi aux statistiques de la plateforme.
    /// </summary>
    /// <remarks>
    /// ⚠️ Valeurs PERSISTÉES en base : ne JAMAIS réordonner ni réutiliser un
    /// numéro. Tout nouveau type s'ajoute à la suite (≥ 5).
    ///
    /// La numérotation commence à 1, comme <see cref="BoardingStatus"/> : la
    /// colonne est nullable (« non renseigné »), et un
    /// <c>default(SchoolType)</c> écrit par mégarde vaudrait 0 — donc une
    /// erreur visible — au lieu de classer silencieusement une école en daara.
    ///
    /// ⚠️ Les écoles créées AVANT ce champ restent volontairement sans type.
    /// Les déclarer daara d'office serait inventer une donnée que nous n'avons
    /// pas ; sans type, on propose simplement TOUS les niveaux (repli le plus
    /// large, jamais bloquant). Même principe qu'au §138 pour le régime
    /// d'hébergement des élèves.
    /// </remarks>
    public enum SchoolType
    {
        /// <summary>Daara / école coranique : mémorisation du Coran.</summary>
        Daara = 1,

        /// <summary>École franco-arabe : double cursus, programme français ET coranique.</summary>
        FrancoArabe = 2,

        /// <summary>École classique : programme français, sans cursus coranique.</summary>
        Classique = 3,

        /// <summary>Autre (institut, centre de formation…).</summary>
        Autre = 4
    }
}
