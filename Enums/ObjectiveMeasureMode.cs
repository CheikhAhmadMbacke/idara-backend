namespace Idara.API.Enums
{
    /// <summary>
    /// Comment se mesure l'avancement d'un objectif du daara.
    /// </summary>
    /// <remarks>
    /// ⚠️ Valeurs PERSISTÉES en base : ne jamais réordonner ni réutiliser un
    /// numéro. Tout nouveau mode s'ajoute à la suite (≥ 5).
    /// </remarks>
    public enum ObjectiveMeasureMode
    {
        /// <summary>
        /// Fait / pas fait. S'il y a des étapes, l'avancement suit le nombre
        /// d'étapes cochées — c'est ainsi qu'un objectif s'écrit sur un carnet.
        /// </summary>
        Simple = 1,

        /// <summary>
        /// Compteur saisi à la main, avec une unité libre : « 12 / 40 m »,
        /// « 8 / 30 tablettes ».
        /// </summary>
        Manual = 2,

        /// <summary>Montant en FCFA : « 350 000 / 800 000 F ».</summary>
        Amount = 3,

        /// <summary>
        /// Effectif d'élèves — la seule mesure qu'Idara connaît déjà : la barre
        /// se remplit toute seule, sans aucune saisie. C'est ce qui distingue
        /// le plus l'application d'un carnet papier.
        /// </summary>
        StudentCount = 4
    }
}
