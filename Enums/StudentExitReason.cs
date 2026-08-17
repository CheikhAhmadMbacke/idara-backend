namespace Idara.API.Enums
{
    /// <summary>
    /// Motif de sortie d'un élève de l'effectif. Valeurs persistées en integer :
    /// NE JAMAIS RÉORDONNER. Démarre à 1 pour qu'un default(StudentExitReason)
    /// accidentel ne se confonde avec aucun motif réel ; Other = 99 laisse la
    /// place à des motifs futurs sans le déplacer.
    /// </summary>
    public enum StudentExitReason
    {
        /// <summary>Transfert vers un autre daara / une autre école.</summary>
        Transfer = 1,

        /// <summary>Fin de cycle — parcours terminé.</summary>
        Graduated = 2,

        /// <summary>Abandon.</summary>
        Dropout = 3,

        /// <summary>Raison familiale.</summary>
        Family = 4,

        /// <summary>Déménagement.</summary>
        Relocation = 5,

        /// <summary>Raison de santé.</summary>
        Health = 6,

        /// <summary>Décès.</summary>
        Deceased = 7,

        /// <summary>Exclusion.</summary>
        Expelled = 8,

        /// <summary>Autre — précision obligatoire (ExitReasonDetail).</summary>
        Other = 99
    }
}
