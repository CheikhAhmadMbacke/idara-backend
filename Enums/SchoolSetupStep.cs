namespace Idara.API.Enums
{
    /// <summary>
    /// Les réglages qu'un daara ne fait <b>qu'une fois</b>, dans l'ordre où ils
    /// se commandent les uns les autres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ L'ordre n'est pas thématique, il est <b>causal</b> : on ne peut pas
    /// affecter un enseignant sans classe, sans matière et sans compte, et on ne
    /// peut pas noter sans période. Présenter ces réglages par famille
    /// (« pédagogie », « argent ») laisserait un directeur commencer par une
    /// étape qui échouera.
    /// </para>
    /// <para>
    /// ⚠️ Valeurs persistées en integer dans <c>SchoolSetupDismissals</c> :
    /// <b>ne jamais réordonner</b>, seulement ajouter à la suite.
    /// </para>
    /// </remarks>
    public enum SchoolSetupStep
    {
        Classes = 1,
        Subjects = 2,
        AcademicYear = 3,
        Students = 4,
        Fees = 5,
        Users = 6,
        Assignments = 7,
        Timetable = 8,
    }
}
