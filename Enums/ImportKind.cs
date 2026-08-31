namespace Idara.API.Enums
{
    /// <summary>
    /// Nature des données d'un fichier d'import.
    /// </summary>
    /// <remarks>
    /// ⚠️ Valeurs PERSISTÉES : ne jamais réordonner ni réutiliser un numéro.
    /// Numérotation à partir de 1 (cf. <see cref="BoardingStatus"/>) : un
    /// <c>default(ImportKind)</c> écrit par mégarde vaudrait 0, donc une valeur
    /// inexistante — une erreur visible — au lieu de faire passer un fichier
    /// pour une liste d'élèves.
    /// </remarks>
    public enum ImportKind
    {
        /// <summary>Élèves, avec leur classe, leur responsable et leurs tarifs.</summary>
        Students = 1,

        /// <summary>Enseignants et personnel.</summary>
        Staff = 2
    }

    public enum ImportBatchStatus
    {
        /// <summary>Analysé, en attente de confirmation. Rien n'est écrit.</summary>
        Analyzed = 1,

        /// <summary>Importé.</summary>
        Committed = 2,

        /// <summary>Abandonné par l'école, ou périmé.</summary>
        Cancelled = 3,

        /// <summary>L'écriture a échoué ; la transaction a tout annulé.</summary>
        Failed = 4
    }
}
