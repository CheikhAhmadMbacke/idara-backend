using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Une étape de démarrage que l'école a explicitement écartée
    /// (« Pas pour mon daara »).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi cette table existe.</b> Beaucoup de daara ne notent pas, ne
    /// délivrent aucun bulletin et n'auront jamais d'emploi du temps. Sans un
    /// moyen d'écarter ces étapes, la liste de démarrage devient un reproche
    /// permanent — et une alerte qu'on ne peut pas faire disparaître est une
    /// alerte qu'on cesse de lire, y compris pour les lignes qui comptent.
    /// </para>
    /// <para>
    /// ⚠️ <b>En base et non sur le téléphone</b> : un directeur change
    /// d'appareil, et le personnel partage le même écran. Un choix mémorisé
    /// localement reviendrait hanter l'accueil au premier changement de
    /// téléphone, sans que personne ne comprenne pourquoi.
    /// </para>
    /// <para>
    /// Écarter n'est jamais définitif : la ligne est supprimée quand l'école
    /// remet l'étape dans sa liste.
    /// </para>
    /// </remarks>
    public class SchoolSetupDismissal
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public SchoolSetupStep Step { get; set; }

        public DateTime DismissedAt { get; set; }

        /// <summary>Qui a écarté l'étape. Nullable : le compte peut avoir été
        /// supprimé depuis, et cela ne doit pas faire réapparaître l'étape.</summary>
        public int? DismissedById { get; set; }
    }
}
