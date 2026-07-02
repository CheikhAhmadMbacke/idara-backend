namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'un compte donateur (<see cref="Constants.UserRoles.Donor"/>),
    /// choisie à l'inscription et affichée au daara pour chaque don :
    /// - Individual : un particulier (<c>User.FullName</c> = nom de la personne).
    /// - Organization : un groupement / une organisation — Dahira, fondation,
    ///   association… (<c>User.FullName</c> = nom de l'organisation).
    /// Nullable sur User : ne concerne QUE le rôle Donor (null pour les autres).
    /// </summary>
    public enum DonorType
    {
        Individual = 0,
        Organization = 1
    }
}
