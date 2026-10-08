using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Un échange avec l'assistant : la phrase de l'école, ce que l'IA a
    /// répondu, ce que ça a coûté.
    ///
    /// <para><b>C'est le REGISTRE, et il est la seule vérité</b> — même
    /// discipline que <see cref="OcrJob"/> (§112, §191). Le solde de commandes
    /// d'une école et la dépense du jour se DÉRIVENT de cette table, jamais d'un
    /// compteur stocké.</para>
    ///
    /// <para>Append-only (§55) : une correction est un
    /// <see cref="AssistantCreditGrant"/>, jamais une modification.</para>
    /// </summary>
    public class AssistantTurn
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Qui a parlé à l'assistant.</summary>
        public int UserId { get; set; }

        /// <summary>
        /// La discussion à laquelle l'échange appartient (2026-10-08). Null pour
        /// les échanges antérieurs à l'historique des discussions.
        /// </summary>
        public int? ConversationId { get; set; }
        public AssistantConversation? Conversation { get; set; }

        /// <summary>
        /// La demande, entière (2 000 caractères au plus, la limite de saisie) :
        /// l'historique des discussions la ré-affiche.
        /// </summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>La réponse, tronquée à 8 000 caractères — l'historique la ré-affiche.</summary>
        public string? Reply { get; set; }

        /// <summary>
        /// Commandes imputées au solde : <b>1</b> si l'échange a abouti,
        /// <b>0</b> sinon. Un échec n'a rien rendu à l'école, elle ne paie pas.
        /// Le coût, lui, reste compté : il a bien été dépensé.
        /// </summary>
        public int ChargedCommands { get; set; }

        /// <summary>
        /// Commandes imputées à l'INCLUS du plan (Pro, Grand) — 1 ou 0. Une
        /// commande est soit incluse, soit payée en crédit, jamais les deux.
        /// </summary>
        public int IncludedCommands { get; set; }

        public bool Success { get; set; }

        /// <summary>Motif court quand le garde-fou a refusé ou que l'appel a échoué.</summary>
        public string? BlockedReason { get; set; }

        /// <summary>Message technique, jamais montré à l'école.</summary>
        public string? Error { get; set; }

        /// <summary>Modèle réellement employé — le tarif en dépend.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Allers-retours avec le modèle pour cet échange.</summary>
        public int Rounds { get; set; }

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheReadTokens { get; set; }
        public int CacheWriteTokens { get; set; }

        /// <summary>
        /// Coût en CENTIMES de FCFA, figé au moment de l'appel avec les tarifs
        /// alors en vigueur.
        /// </summary>
        public long CostCentimes { get; set; }

        public int DurationMs { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Commandes accordées à une école en plus de ses commandes offertes :
    /// un achat (PaymentId renseigné) ou un geste du SuperAdmin.
    /// Calque exact de <see cref="OcrPageGrant"/>.
    /// </summary>
    public class AssistantCreditGrant
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Commandes ajoutées. Toujours positif.</summary>
        public int Commands { get; set; }

        /// <summary>Pourquoi. Affiché au SuperAdmin, jamais à l'école.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Null = accordé par un automate, donc un achat.</summary>
        public int? GrantedByUserId { get; set; }

        /// <summary>
        /// Le paiement qui a acheté ces commandes. <b>Clé d'idempotence</b> : un
        /// webhook peut être rejoué, et sans unicité un rejeu offrirait les
        /// commandes une seconde fois.
        /// </summary>
        public int? PaymentId { get; set; }
        public Payment? Payment { get; set; }

        /// <summary>Prix unitaire FIGÉ à l'achat. Zéro pour un geste.</summary>
        public long PricePerCommandFcfa { get; set; }

        /// <summary>Total réellement demandé à l'école. Zéro pour un geste.</summary>
        public long AmountFcfa { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Ce que l'assistant PROPOSE de faire, en attente du geste de l'école.
    ///
    /// <para>🔴 <b>L'IA n'écrit jamais rien elle-même.</b> Elle dépose une
    /// proposition ; l'école la lit sur une carte et appuie sur « Confirmer ».
    /// C'est le serveur — pas l'IA — qui rédige le résumé affiché, à partir des
    /// données RÉSOLUES en base (nom de classe, reste dû, numéro du
    /// responsable) : la carte dit ce qui va réellement se passer, pas ce que
    /// le modèle croit avoir compris.</para>
    /// </summary>
    public class AssistantAction
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Qui a demandé — seul lui peut confirmer.</summary>
        public int UserId { get; set; }

        public int? TurnId { get; set; }

        public AssistantActionKind Kind { get; set; }

        /// <summary>Paramètres VALIDÉS, sérialisés. C'est ce qui sera exécuté, tel quel.</summary>
        public string PayloadJson { get; set; } = "{}";

        /// <summary>Titre de la carte (« Ajouter un élève »).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Lignes de la carte, sérialisées : [{label, value}].</summary>
        public string SummaryJson { get; set; } = "[]";

        public AssistantActionStatus Status { get; set; } = AssistantActionStatus.Pending;

        /// <summary>Ce qui s'est passé à l'exécution (succès ou motif d'échec).</summary>
        public string? ResultMessage { get; set; }

        /// <summary>Entité produite (élève créé, paiement enregistré…).</summary>
        public int? ResultEntityId { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Une proposition périme : confirmée le lendemain, elle porterait sur
        /// un reste dû qui a pu changer entre-temps.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        public DateTime? ResolvedAt { get; set; }
    }

    /// <summary>
    /// 💬 Une discussion avec l'assistant (2026-10-08) — ce que la liste
    /// « Historique » affiche, comme les sessions de Claude Code.
    ///
    /// <para>🔒 <b>Une discussion appartient à UNE personne</b> : le directeur ne
    /// lit pas celles de son personnel, ni l'inverse. Elle peut contenir des
    /// noms, des montants, des numéros.</para>
    ///
    /// <para>🔴 <b>Supprimer une discussion la MASQUE</b> (<see cref="HiddenAt"/>) :
    /// ses échanges (<see cref="AssistantTurn"/>) sont le registre de ce qui a
    /// été décompté et facturé, et restent append-only (§55). Ses propositions
    /// encore en attente sont annulées — une carte qu'on ne voit plus ne doit
    /// plus pouvoir se confirmer.</para>
    /// </summary>
    public class AssistantConversation
    {
        public int Id { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public int UserId { get; set; }

        /// <summary>
        /// Le début de la première demande, renommable. Jamais généré par l'IA :
        /// un titre ne doit pas coûter une commande.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        /// <summary>Dernier échange — l'ordre de la liste.</summary>
        public DateTime UpdatedAt { get; set; }

        public DateTime? HiddenAt { get; set; }
    }
}
