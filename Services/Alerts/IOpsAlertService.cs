using Idara.API.Enums;

namespace Idara.API.Services.Alerts
{
    /// <summary>
    /// Une ligne de fait dans le corps d'une alerte : « Ecole » → « Daara Jazbul
    /// xuloob ». Une liste de paires plutôt qu'un texte libre pour que toutes les
    /// alertes se lisent pareil, et surtout pour qu'on ne puisse pas OUBLIER de
    /// nommer qui est concerné (§177).
    /// </summary>
    /// <param name="Label">Intitulé court, sans accent inutile.</param>
    /// <param name="Value">Valeur déjà mise en forme (montant, date, numéro).</param>
    public record AlertFact(string Label, string Value);

    /// <summary>
    /// Ce qu'il faut réunir pour lever une alerte d'exploitation.
    /// </summary>
    /// <param name="Kind">Nature de l'événement.</param>
    /// <param name="GroupingKey">Deux alertes qui la partagent racontent le même
    /// défaut et ne produisent qu'un seul e-mail par fenêtre.</param>
    /// <param name="Subject">Objet de l'e-mail. Doit se suffire à lui-même : il
    /// sera lu sur un téléphone, en notification, sans le corps.</param>
    /// <param name="Facts">Les faits, dans l'ordre de lecture.</param>
    /// <param name="Advice">Ce qu'il y a à faire, si c'est connu d'avance.</param>
    /// <param name="SchoolId">École concernée, si l'alerte en vise une.</param>
    /// <param name="RelatedId">Entité métier visée (retrait, utilisateur…).</param>
    /// <param name="SmsHeadline">La phrase du SMS, sans préfixe ni date — celle
    /// qui se lit sur un écran verrouillé.
    ///
    /// <para>N'est utilisée que si la nature de l'alerte figure dans la liste
    /// blanche (<see cref="Common.Utilities.OpsAlertSms.ShouldSend"/>) : la
    /// renseigner ne suffit pas à faire sonner un téléphone, et l'omettre sur une
    /// nature qui sonne fait simplement retomber sur le <c>Subject</c>. Deux
    /// verrous plutôt qu'un, pour qu'aucun appelant futur n'ouvre le canal par
    /// inadvertance.</para>
    ///
    /// <para>🔑 Elle doit NOMMER qui est concerné (§177) : « Retrait bloque —
    /// Daara X » et non « un retrait a échoué ». Une alerte qui ne dit pas qui
    /// oblige à ouvrir l'e-mail, donc n'a fait que la moitié du chemin.</para></param>
    public record OpsAlertRequest(
        OpsAlertKind Kind,
        string GroupingKey,
        string Subject,
        IReadOnlyList<AlertFact> Facts,
        string? Advice = null,
        int? SchoolId = null,
        int? RelatedId = null,
        string? SmsHeadline = null);

    /// <summary>
    /// Prévient le SuperAdmin par e-mail d'un événement d'exploitation, et le
    /// consigne dans un journal consultable.
    ///
    /// <para><b>Ne bloque jamais l'appelant et ne lève jamais.</b> Ces alertes
    /// naissent au milieu d'un retrait d'argent ou d'un envoi de SMS : faire
    /// attendre une école pendant qu'on envoie NOTRE e-mail, ou pire faire
    /// échouer son retrait parce que notre SMTP est indisponible, serait
    /// exactement à l'envers (§42/§57).</para>
    /// </summary>
    public interface IOpsAlertService
    {
        /// <summary>Lève une alerte en tâche de fond. Retourne immédiatement.</summary>
        void Queue(OpsAlertRequest request);

        /// <summary>
        /// Version attendable, pour les appelants qui veulent la garantie que la
        /// ligne est écrite (et pour pouvoir vérifier le dispositif sur un banc
        /// d'essai — <see cref="Queue"/> part en tâche de fond, donc inattendable,
        /// motif §133).
        /// </summary>
        Task SendAsync(OpsAlertRequest request, CancellationToken ct = default);
    }
}
