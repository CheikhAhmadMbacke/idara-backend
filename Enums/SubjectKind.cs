namespace Idara.API.Enums
{
    /// <summary>
    /// Catégorie de matière. Permet de différencier le suivi spécifique
    /// au Coran (mémorisation/récitation) des matières classiques.
    /// </summary>
    public enum SubjectKind
    {
        Coran,        // Mémorisation, récitation, tajwid (suivi spécial)
        Religious,    // Fiqh, Hadith, Sira, Arabe, Tawhid... (notation classique)
        General       // FR, Maths, Sciences, Anglais... (notation classique)
    }
}
