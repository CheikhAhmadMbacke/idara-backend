namespace Idara.API.Enums
{
    /// <summary>
    /// État d'une portion travaillée (colonne « حالة » du tableau) : acquis ou
    /// non acquis. <see cref="NotSet"/> = non renseigné par l'enseignant.
    /// </summary>
    public enum CoranPortionStatus
    {
        NotSet = 0,
        Acquired = 1,
        NotAcquired = 2
    }
}
