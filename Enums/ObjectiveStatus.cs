namespace Idara.API.Enums
{
    /// <summary>
    /// État d'un objectif du daara.
    /// </summary>
    /// <remarks>
    /// ⚠️ Valeurs PERSISTÉES en base : ne jamais réordonner.
    ///
    /// Le passage à <see cref="Achieved"/> n'est JAMAIS automatique, même quand
    /// le compteur atteint la cible : l'école reste seule juge de ce qui est
    /// terminé (un mur monté à 40 m sur 40 peut encore attendre son crépi), et
    /// un état qui changerait tout seul serait un changement silencieux de plus.
    /// L'interface signale l'atteinte de la cible et propose de clore.
    /// </remarks>
    public enum ObjectiveStatus
    {
        InProgress = 1,
        Achieved = 2,
        Abandoned = 3
    }
}
