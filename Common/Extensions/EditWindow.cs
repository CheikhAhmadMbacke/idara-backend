using Idara.API.Constants;

namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// Verrou d'édition du <b>cahier de suivi</b> — suivi coranique et journal
    /// de classe (§151, étendu au journal le 2026-08-21).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi une source unique.</b> Depuis que les deux relevés sont
    /// réunis derrière un même écran (« Cahier de suivi »), un cadenas sur la
    /// partie Coran et pas sur le reste serait incompréhensible pour
    /// l'enseignant. Et la raison d'être du verrou — « une faute repérée trop
    /// tard ne doit pas pouvoir être réécrite en douce » — vaut tout autant
    /// pour le français que pour la mémorisation. Deux copies de cette règle
    /// finiraient par diverger.
    /// </para>
    /// <para>
    /// ⚠️ <b>Trois invariants à ne pas défaire :</b>
    /// <list type="number">
    /// <item>le délai part de la <b>saisie</b> (<c>CreatedAt</c>), jamais de
    /// <c>UpdatedAt</c> — sinon chaque modification repousserait la fenêtre et
    /// le verrou ne se fermerait jamais ;</item>
    /// <item>la <b>suppression</b> suit la même règle, sinon supprimer puis
    /// re-saisir contournerait tout ;</item>
    /// <item>le drapeau est calculé <b>par le serveur</b> et transporté dans le
    /// DTO — le recalculer dans l'application laisserait un téléphone à l'heure
    /// fausse verrouiller ou déverrouiller à tort.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Créer une entrée pour une journée ancienne reste permis : saisir en
    /// retard n'est pas modifier. Et la direction n'est jamais verrouillée — un
    /// verrou absolu graverait la moindre faute de frappe repérée trop tard,
    /// sans que personne dans l'école ne puisse la réparer.
    /// </para>
    /// </remarks>
    public static class EditWindow
    {
        /// <summary>
        /// Délai pendant lequel un enseignant peut encore corriger ce qu'il a
        /// saisi.
        /// </summary>
        public const int Hours = 48;

        /// <summary>Seul l'enseignant est soumis au délai.</summary>
        public static bool IsTeacherLocked(string? role) => role == UserRoles.Teacher;

        /// <summary>
        /// La fenêtre est-elle encore ouverte pour une saisie faite à
        /// <paramref name="createdAt"/> ?
        /// </summary>
        public static bool IsOpen(DateTime createdAt) =>
            DateTime.UtcNow - createdAt <= TimeSpan.FromHours(Hours);

        /// <summary>
        /// L'utilisateur peut-il encore modifier ou supprimer cette saisie ?
        /// </summary>
        public static bool CanEdit(string? role, DateTime createdAt) =>
            !IsTeacherLocked(role) || IsOpen(createdAt);

        /// <summary>
        /// Message rendu à l'enseignant dont la fenêtre est fermée. Il dit ce
        /// qui s'est passé ET quoi faire — un refus sans issue laisserait
        /// croire à une panne.
        /// </summary>
        public static string RefusalMessage(bool deleting = false) =>
            $"Ce suivi a été enregistré il y a plus de {Hours} heures : il n'est plus "
            + (deleting ? "supprimable. " : "modifiable. ")
            + "Demandez à la direction de l'école de faire la correction.";
    }
}
