namespace Idara.API.Enums
{
    /// <summary>
    /// Type d'incident relevé pendant une évaluation qualité (récitation devant
    /// l'administration) : une faute de récitation, ou un blocage (l'élève
    /// récite puis bloque, ne sait plus la suite).
    /// </summary>
    public enum CoranIncidentKind
    {
        Fault = 0,
        Blockage = 1
    }
}
