namespace Idara.API.Enums
{
    /// <summary>
    /// Qui peut lire un événement du journal du daara.
    /// </summary>
    /// <remarks>
    /// ⚠️ Valeurs PERSISTÉES en base : ne jamais réordonner.
    ///
    /// Trois niveaux et non un simple interrupteur « visible par les parents » :
    /// un directeur consigne aussi « conflit avec l'enseignant X » ou « retard
    /// de salaire », et cela ne doit pas être lisible par son équipe. Sans le
    /// niveau <see cref="Direction"/>, il cesserait tout simplement d'écrire
    /// ces lignes-là — et le journal ne remplacerait pas son carnet.
    ///
    /// L'ordre est croissant en ouverture : un niveau donne toujours accès à
    /// tout ce que le niveau inférieur autorise.
    /// </remarks>
    public enum EventVisibility
    {
        /// <summary>Direction seule : SchoolAdmin et SchoolStaff.</summary>
        Direction = 1,

        /// <summary>Toute l'équipe de l'école, enseignants et surveillants compris.</summary>
        School = 2,

        /// <summary>Ouvert aux parents, en plus de toute l'équipe.</summary>
        Guardians = 3
    }
}
