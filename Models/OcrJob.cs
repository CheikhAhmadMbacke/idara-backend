using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Une lecture de cahier par l'IA : ce qui a été envoyé, ce que ça a coûté,
    /// et si ça a abouti.
    ///
    /// <para><b>C'est le REGISTRE, et il est la seule vérité.</b> Le quota d'une
    /// école et la dépense du jour se DÉRIVENT de cette table — jamais d'un
    /// compteur stocké quelque part. C'est la discipline du §112 (« P se
    /// recalcule, ne se stocke pas ») reprise par le §191 pour les SMS : un
    /// compteur en mémoire repart à zéro au redéploiement, un compteur en base
    /// se désynchronise du réel dès la première écriture ratée. Ce qui a
    /// réellement été dépensé, c'est ce qui est écrit ici.</para>
    ///
    /// <para>Append-only, comme toute écriture financière (§55). Une correction
    /// est une nouvelle ligne (ou un <see cref="OcrPageGrant"/>), jamais une
    /// modification.</para>
    /// </summary>
    public class OcrJob
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Qui a déclenché la lecture.</summary>
        public int CreatedById { get; set; }

        /// <summary>Nature du document photographié.</summary>
        public ImportKind Kind { get; set; }

        /// <summary>Nombre d'images envoyées dans cet appel.</summary>
        public int PageCount { get; set; }

        /// <summary>
        /// Pages réellement imputées au quota de l'école. Vaut
        /// <see cref="PageCount"/> en cas de succès, et <b>0</b> en cas
        /// d'échec : l'école n'a rien reçu, elle ne paie pas de son quota. Le
        /// coût, lui, reste compté — il a bien été dépensé.
        /// </summary>
        public int ChargedPages { get; set; }

        public bool Success { get; set; }

        /// <summary>Motif court et stable quand le garde-fou a refusé, ou que l'appel a échoué.</summary>
        public string? BlockedReason { get; set; }

        /// <summary>Message technique, pour comprendre après coup.</summary>
        public string? Error { get; set; }

        /// <summary>Modèle réellement employé — le tarif en dépend.</summary>
        public string Model { get; set; } = string.Empty;

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }

        /// <summary>
        /// Coût en CENTIMES de FCFA, calculé au moment de l'appel avec les
        /// tarifs alors en vigueur. Figé ici : un changement de tarif plus tard
        /// ne doit pas réécrire l'histoire.
        /// </summary>
        public long CostCentimes { get; set; }

        /// <summary>Lignes extraites (0 si échec) — utile pour juger la qualité de lecture.</summary>
        public int ExtractedRows { get; set; }

        /// <summary>Cellules que le modèle a signalées comme douteuses.</summary>
        public int UncertainCells { get; set; }

        /// <summary>Lot d'import produit, quand la lecture a abouti.</summary>
        public int? ImportBatchId { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>Durée de l'appel, en millisecondes — pour dimensionner l'attente côté écran.</summary>
        public int DurationMs { get; set; }
    }

    /// <summary>
    /// Pages accordées à une école EN PLUS de son quota de base.
    ///
    /// Deux origines : un geste du SuperAdmin (support, reprise d'un import
    /// raté, geste commercial) ou, plus tard, un achat. Table à part plutôt
    /// qu'un champ sur <see cref="School"/> : on veut savoir <b>qui</b> a
    /// accordé <b>quoi</b> et <b>pourquoi</b>, et un champ mutable perdrait
    /// cette histoire à la première correction.
    /// </summary>
    public class OcrPageGrant
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Pages ajoutées. Toujours positif — on n'annule pas, on n'accorde pas moins.</summary>
        public int Pages { get; set; }

        /// <summary>Pourquoi. Affiché au SuperAdmin, jamais à l'école.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Qui a accordé. Null = accordé par un automate (un achat, plus tard).</summary>
        public int? GrantedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
