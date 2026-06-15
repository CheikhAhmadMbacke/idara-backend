namespace Idara.API.Enums
{
    /// <summary>
    /// Lecture (riwâya) du Coran utilisée par l'école pour l'autocomplétion du
    /// texte. <see cref="Warsh"/> par défaut (habituelle dans les daara
    /// sénégalais/mourides). Le texte ET la numérotation/pagination diffèrent
    /// entre les deux → ne pas changer en cours de cycle (réf. décalées).
    /// </summary>
    public enum QuranRiwaya
    {
        Warsh = 0,
        Hafs = 1
    }
}
