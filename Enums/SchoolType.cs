namespace Idara.API.Enums
{
    /// <summary>
    /// Nature de l'établissement. Sert d'abord à proposer les BONS niveaux de
    /// classe : « CE1 » n'a aucun sens dans un daara, « Halaqa 3 » n'en a aucun
    /// dans une école classique. Sert aussi aux statistiques de la plateforme.
    /// </summary>
    /// <remarks>
    /// ⚠️ Valeurs PERSISTÉES en base : ne JAMAIS réordonner ni réutiliser un
    /// numéro. Tout nouveau type s'ajoute à la suite (≥ 5).
    ///
    /// La numérotation commence à 1, comme <see cref="BoardingStatus"/> : la
    /// colonne est nullable (« non renseigné »), et un
    /// <c>default(SchoolType)</c> écrit par mégarde vaudrait 0 — donc une
    /// erreur visible — au lieu de classer silencieusement une école en daara.
    ///
    /// ⚠️ <b>Les écoles antérieures à ce champ ont été reprises le 2026-09-19</b>,
    /// et la règle qui figurait ici — ne jamais leur inventer un type — est donc
    /// caduque POUR ELLES. Ce jour-là, Cheikh a désigné nommément les DEUX
    /// écoles franco-arabes de la plateforme, ce qui a rendu le reste certain :
    /// toutes les autres sont des daara. Ce n'était plus une supposition mais
    /// une donnée qu'il détenait, d'où
    /// <c>DbInitializer.BackfillSchoolTypesAsync</c>.
    ///
    /// La règle reste entière pour la SUITE : une école qui ne renseigne pas son
    /// type demeure « non renseignée » — on lui propose alors TOUS les niveaux
    /// (repli le plus large, jamais bloquant) et aucune matière n'est pré-créée.
    /// Même principe qu'au §138 pour le régime d'hébergement des élèves.
    /// </remarks>
    public enum SchoolType
    {
        /// <summary>
        /// Daara / école coranique : mémorisation du Coran.
        /// <para>🔑 Seul type qui reçoit une matière « Coran » <b>pré-créée</b>
        /// (<c>QuranSubjectExtensions.EnsureQuranSubjectAsync</c>) : dans un daara
        /// on apprend forcément le Coran. Une école franco-arabe, pas
        /// nécessairement — elle la crée elle-même si elle le veut.</para>
        /// </summary>
        Daara = 1,

        /// <summary>École franco-arabe : double cursus, programme français ET coranique.</summary>
        FrancoArabe = 2,

        /// <summary>École classique : programme français, sans cursus coranique.</summary>
        Classique = 3,

        /// <summary>Autre (institut, centre de formation…).</summary>
        Autre = 4
    }
}
