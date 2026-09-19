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

        // ===== Codes d'authentification (anti « SMS pumping ») =====

        /// <summary>
        /// Le canal SMS des codes s'est fermé de lui-même : bourse quotidienne
        /// épuisée, ou taux de vérification effondré. Le second cas est le plus
        /// parlant — des codes partent et personne ne les saisit, ce qui est la
        /// signature d'un robot qui tire des numéros au hasard.
        /// </summary>
        AuthCodeChannelClosed = 20,

        // ===== Encaissements =====

        /// <summary>
        /// Le prestataire refuse d'ouvrir une session de paiement : le payeur a
        /// appuyé sur « Payer » et rien ne s'est passé. Rien à corriger côté
        /// école ni côté famille — c'est nous ou Wave.
        ///
        /// <para>Distinct d'un paiement abandonné, qui est le cas le plus
        /// fréquent et parfaitement normal : la famille a changé d'avis. Seul le
        /// refus TECHNIQUE remonte ici.</para>
        /// </summary>
        PayinProviderRejected = 30,

        // ===== Arrivées sur la plateforme =====
        // Ni une panne ni un coût : une nouvelle qui se périme. Un directeur qui
        // vient d'ouvrir un compte se rappelle le jour même ; une semaine plus
        // tard, il est passé à autre chose. Mesuré en prod le 2026-09-19 :
        // 12 comptes créés pour 5 dossiers déposés en 90 jours — les sept qui se
        // sont arrêtés en route ne se voyaient qu'après coup.

        /// <summary>Un compte de directeur vient d'être créé. L'école n'existe
        /// pas encore : à ce stade il n'y a qu'un numéro ou une adresse.</summary>
        SchoolAccountCreated = 40,

        /// <summary>Un dossier d'école vient d'être déposé et attend TA
        /// validation. C'est ici, et seulement ici, qu'un nom d'école existe.</summary>
        SchoolKycSubmitted = 41,
    }
}
