namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'une faute de récitation (ne s'applique qu'aux incidents de type
    /// <see cref="CoranIncidentKind.Fault"/>). Pour un blocage, laisser null.
    /// </summary>
    public enum CoranFaultType
    {
        /// <summary>Mot remplacé / mal prononcé (ex. « qîla » au lieu de « qâla »).</summary>
        WordReplaced = 0,

        /// <summary>Faute de tajwid.</summary>
        Tajwid = 1,

        /// <summary>Mot oublié / sauté.</summary>
        WordOmitted = 2,

        /// <summary>Erreur de voyelle / déclinaison (i'rab).</summary>
        Vowel = 3,

        Other = 4
    }
}
