using Idara.API.Enums;

namespace Idara.API.DTOs.Admin
{
    /// <summary>
    /// Un retrait vu du back-office plateforme. Distinct de
    /// <see cref="Payment.WithdrawalDto"/> (vue école) sur trois points, et
    /// chacun compte :
    /// <list type="bullet">
    /// <item>l'ÉCOLE est nommée — la vue école n'en a pas besoin, le back-office
    /// ne sert qu'à ça ;</item>
    /// <item>le suivi de vérification est exposé (tentatives, prochaine, dernier
    /// contrôle) : c'est ce qui dit si un retrait « en cours » avance ou est
    /// coincé ;</item>
    /// <item>les retraits MASQUÉS par l'école y figurent quand même. Le masquage
    /// est cosmétique et propre à son écran ; la plateforme, elle, doit voir tout
    /// l'argent sorti, sans quoi une réconciliation serait fausse sans qu'on
    /// puisse le voir.</item>
    /// </list>
    /// </summary>
    public class AdminWithdrawalDto
    {
        public int Id { get; set; }
        public string Reference { get; set; } = string.Empty;

        public int? SchoolId { get; set; }
        /// <summary>Nom de l'école, ou « Plateforme (gains) » pour un retrait de gains.</summary>
        public string SchoolName { get; set; } = string.Empty;
        public bool IsPlatform { get; set; }

        public long AmountFcfa { get; set; }
        public long FeesFcfa { get; set; }
        public long NetReceivedFcfa { get; set; }

        public PaymentOperator Operator { get; set; }
        public TransferCategory Category { get; set; }
        public string? CategoryLabel { get; set; }
        public string? Motif { get; set; }
        public WithdrawalSource Source { get; set; }
        public long DonationAmountFcfa { get; set; }

        public string RecipientName { get; set; } = string.Empty;
        /// <summary>Numéro complet : c'est celui qu'on rappelle quand un
        /// versement s'est perdu. Réservé au SuperAdmin.</summary>
        public string RecipientPhone { get; set; } = string.Empty;

        public WithdrawalStatus Status { get; set; }
        /// <summary>Statut en clair, pour que l'écran et l'export disent la même chose.</summary>
        public string StatusLabel { get; set; } = string.Empty;

        public string? SenePayDisbursementId { get; set; }
        public string? FailureReason { get; set; }
        /// <summary>Cause CLASSÉE de l'échec (« Le prestataire ne peut pas
        /// decaisser »), null si le retrait n'a pas échoué.</summary>
        public string? FailureCause { get; set; }

        public int InitiatedById { get; set; }
        public string? InitiatedByName { get; set; }

        // --- Suivi de vérification : dit si un retrait « en cours » avance ---
        public int VerificationAttempts { get; set; }
        public DateTime? VerificationStartedAt { get; set; }
        public DateTime? NextVerificationAt { get; set; }
        public DateTime? LastCheckedAt { get; set; }
        /// <summary>Horodatage d'un re-débit correcteur (double dépense évitée).</summary>
        public DateTime? ReversedAt { get; set; }

        /// <summary>Masqué de l'écran de l'école — mais jamais du back-office.</summary>
        public bool IsHiddenFromSchool { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }
    }

    /// <summary>Compteurs d'en-tête de la page des retraits.</summary>
    public class AdminWithdrawalSummaryDto
    {
        public int Count { get; set; }

        public int CompletedCount { get; set; }
        public long CompletedAmountFcfa { get; set; }

        public int FailedCount { get; set; }
        public long FailedAmountFcfa { get; set; }

        /// <summary>Initiés + en vérification : de l'argent réservé qui n'est ni
        /// sorti ni rendu. C'est le chiffre à surveiller.</summary>
        public int PendingCount { get; set; }
        public long PendingAmountFcfa { get; set; }

        /// <summary>Retraits de gains plateforme, isolés : ils ne débitent aucun
        /// wallet école et les additionner au reste ferait croire à une sortie
        /// d'argent des daara.</summary>
        public int PlatformCount { get; set; }
        public long PlatformAmountFcfa { get; set; }

        /// <summary>Bloqués en vérification au-delà de 48 h — chacun demande une
        /// intervention manuelle.</summary>
        public int StuckCount { get; set; }
    }

    /// <summary>Une alerte d'exploitation, telle que listée au back-office.</summary>
    public class OpsAlertDto
    {
        public int Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string KindLabel { get; set; } = string.Empty;
        public bool Urgent { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? Advice { get; set; }
        public int? SchoolId { get; set; }
        public string? SchoolName { get; set; }
        public int? RelatedId { get; set; }
        /// <summary>Null = enregistrée mais JAMAIS envoyée par e-mail (regroupée,
        /// plafond atteint, ou envoi en échec). Distinction affichée telle
        /// quelle : elle dit si tu as réellement été prévenu.</summary>
        public DateTime? EmailedAt { get; set; }
        public bool Resolved { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
