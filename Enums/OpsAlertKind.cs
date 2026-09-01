namespace Idara.API.Enums
{
    /// <summary>
    /// Nature d'une alerte d'exploitation — un événement dont TU dois être
    /// prévenu par e-mail parce qu'il coûte de l'argent ou bloque une école, et
    /// que personne ne te le dira autrement.
    ///
    /// <para>Distinct des incidents clients (<see cref="IncidentKind"/>, qui
    /// racontent qu'un utilisateur a rencontré un problème) et des anomalies
    /// comptables de décaissement (<see cref="PayoutAlertType"/>, qui restent la
    /// source de vérité financière). Une anomalie de décaissement produit une
    /// alerte d'exploitation EN PLUS, pour qu'elle sorte de la base et arrive
    /// dans ta boîte : jusqu'ici elle n'était qu'une ligne que personne ne
    /// lisait.</para>
    /// </summary>
    public enum OpsAlertKind
    {
        // ===== SMS =====

        /// <summary>Palier SOUPLE de dépense SMS atteint : rappels et envois de
        /// masse suspendus, codes de connexion encore délivrés.</summary>
        SmsSoftCapReached = 0,

        /// <summary>Palier ABSOLU atteint : plus aucun SMS ne part.</summary>
        SmsHardCapReached = 1,

        /// <summary>Une école dépasse son plafond ou présente un profil
        /// d'emballement (même numéro retapé en boucle).</summary>
        SmsSchoolRunaway = 2,

        /// <summary>Tentative d'envoi vers un numéro hors Sénégal — ne devrait
        /// JAMAIS arriver, et coûterait onze fois le tarif local.</summary>
        SmsForeignRecipientBlocked = 3,

        // ===== Retraits / décaissements =====

        /// <summary>Un retrait a échoué (école ou plateforme), avec son motif.</summary>
        WithdrawalFailed = 10,

        /// <summary>Le prestataire ne peut pas décaisser (réserve à sec,
        /// indisponibilité opérateur) — c'est TOI qui dois agir.</summary>
        WithdrawalProviderOutage = 11,

        /// <summary>Retrait bloqué en vérification au-delà du seuil : issue
        /// toujours indéterminée, réconciliation manuelle requise.</summary>
        WithdrawalStuck = 12,

        /// <summary>Anomalie comptable de décaissement (double dépense corrigée,
        /// réconciliation rompue, correction impossible).</summary>
        PayoutAnomaly = 13,
    }
}
