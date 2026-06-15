namespace Idara.API.Enums
{
    /// <summary>
    /// Les 3 portions travaillées chaque jour dans le suivi quotidien d'un daara
    /// (cf. tableau « جدول المتابعة اليومية »).
    /// </summary>
    public enum CoranPortionKind
    {
        /// <summary>الدرس الجديد — la nouvelle leçon (nouvelle mémorisation).</summary>
        NewLesson = 0,

        /// <summary>الجنب — la révision récente (consolidation des leçons proches).</summary>
        RecentRevision = 1,

        /// <summary>اللوح — l'ancienne révision (« la planche » : mémorisation ancienne).</summary>
        OldRevision = 2
    }
}
