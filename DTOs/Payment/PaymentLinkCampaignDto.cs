namespace Idara.API.DTOs.Payment
{
    /// <summary>
    /// Ce que coûterait — ou ce qu'a coûté — la diffusion du lien de paiement à
    /// tous les responsables d'une école.
    /// </summary>
    public class PaymentLinkCampaignSchoolDto
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;

        /// <summary>Responsables joignables (élève inscrit + numéro sénégalais valide).</summary>
        public int Recipients { get; set; }

        /// <summary>Déjà servis lors d'un envoi précédent — ils ne repaieront pas.</summary>
        public int AlreadySent { get; set; }

        /// <summary>Reste à envoyer = <see cref="Recipients"/> − <see cref="AlreadySent"/>.</summary>
        public int Pending { get; set; }

        /// <summary>Responsables écartés faute de numéro exploitable, comptés pour
        /// qu'ils ne disparaissent pas silencieusement du recensement.</summary>
        public int SkippedNoPhone { get; set; }

        /// <summary>Segments facturés pour ce qui reste à envoyer.</summary>
        public int PendingSegments { get; set; }

        /// <summary>Coût de ce qui reste à envoyer, arrondi au franc supérieur.</summary>
        public long PendingCostFcfa { get; set; }

        /// <summary>Destinataires sur Orange (77/78) — tarif on-net.</summary>
        public int OnNetRecipients { get; set; }

        /// <summary>Destinataires sur les autres réseaux (70/75/76) — tarif off-net.</summary>
        public int OffNetRecipients { get; set; }

        /// <summary>
        /// Destinataires dont le message bascule en UCS-2 — un caractère hors
        /// alphabet GSM-7 dans leur nom (ï de Maïmouna, ë, ç…). Leur SMS coûte
        /// ~2,5× celui des autres. Compté à part pour que ce surcoût se VOIE :
        /// il est autrement parfaitement invisible.
        /// </summary>
        public int Ucs2Recipients { get; set; }
    }

    /// <summary>Recensement complet, école par école, AVANT le moindre envoi.</summary>
    public class PaymentLinkCampaignPreviewDto
    {
        public int Schools { get; set; }
        public int Recipients { get; set; }
        public int AlreadySent { get; set; }
        public int Pending { get; set; }
        public int SkippedNoPhone { get; set; }
        public int PendingSegments { get; set; }

        /// <summary>Le chiffre qui décide : ce que coûtera l'envoi restant.</summary>
        public long PendingCostFcfa { get; set; }

        public int OnNetRecipients { get; set; }
        public int OffNetRecipients { get; set; }

        /// <summary>Destinataires dont le nom fait basculer le message en UCS-2
        /// (~2,5× le prix). Zéro est la valeur attendue.</summary>
        public int Ucs2Recipients { get; set; }

        /// <summary>Prix unitaires en vigueur, pour que le total soit vérifiable
        /// à la main et non à croire sur parole.</summary>
        public long OnNetPriceCentimes { get; set; }
        public long OffNetPriceCentimes { get; set; }

        /// <summary>Un exemple du message exact qui partira, avec son chiffrage.</summary>
        public string SampleMessage { get; set; } = string.Empty;
        public int SampleSegments { get; set; }
        public int SampleCharCount { get; set; }

        public List<PaymentLinkCampaignSchoolDto> BySchool { get; set; } = new();
    }

    /// <summary>Ordre d'envoi. Le coût maximal est OBLIGATOIRE et sert de frein.</summary>
    public class PaymentLinkCampaignRunRequestDto
    {
        /// <summary>Écoles visées. Vide ou null = toutes les écoles éligibles.</summary>
        public List<int>? SchoolIds { get; set; }

        /// <summary>
        /// Plafond de dépense de CET appel, en FCFA. L'envoi s'arrête net dès que
        /// le suivant le dépasserait. C'est le seul chiffre que le SuperAdmin
        /// valide vraiment : tout le reste en découle.
        /// </summary>
        public long MaxCostFcfa { get; set; }

        /// <summary>
        /// Destinataires traités dans CET appel (borné à 100). L'envoi se fait par
        /// tranches pour qu'une requête HTTP n'expire jamais en plein milieu — la
        /// reprise est gratuite, l'opération étant idempotente.
        /// </summary>
        public int MaxRecipients { get; set; } = 40;
    }

    /// <summary>Ce qui s'est réellement passé.</summary>
    public class PaymentLinkCampaignRunResultDto
    {
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int LinksCreated { get; set; }

        /// <summary>Restants après cette tranche — 0 = campagne terminée.</summary>
        public int RemainingPending { get; set; }

        /// <summary>Dépense réelle de cette tranche, relue du registre des envois
        /// (jamais estimée : c'est le registre qui fait foi, §191).</summary>
        public long SpentFcfa { get; set; }

        /// <summary>Pourquoi l'envoi s'est arrêté : "done", "max_recipients",
        /// "budget", "too_many_failures".</summary>
        public string StoppedReason { get; set; } = string.Empty;
    }

    /// <summary>Ce qu'une école a fait du lien qu'on lui a envoyé.</summary>
    public class PaymentLinkFunnelSchoolDto
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;

        /// <summary>SMS de campagne réellement partis.</summary>
        public int SmsSent { get; set; }

        /// <summary>Liens ouverts au moins une fois DEPUIS leur SMS.</summary>
        public int Opened { get; set; }

        /// <summary>Paiements terminés passés par un lien depuis son SMS.</summary>
        public int Payments { get; set; }

        /// <summary>Montant encaissé par ces paiements.</summary>
        public long PaidFcfa { get; set; }
    }

    /// <summary>
    /// Résultats de la campagne, et la réponse à la question produit : le lien
    /// sert-il plus que l'application ?
    /// </summary>
    public class PaymentLinkCampaignStatsDto
    {
        // ----- Entonnoir de la campagne -----

        /// <summary>SMS partis (registre des envois, jamais une estimation).</summary>
        public int SmsSent { get; set; }

        /// <summary>Liens ouverts au moins une fois depuis leur SMS.</summary>
        public int Opened { get; set; }

        /// <summary>Liens jamais ouverts depuis leur SMS.</summary>
        public int NeverOpened { get; set; }

        /// <summary>Ouvertures totales (un même responsable peut revenir).</summary>
        public int TotalOpens { get; set; }

        /// <summary>Paiements terminés passés par un lien depuis son SMS.</summary>
        public int Payments { get; set; }
        public long PaidFcfa { get; set; }

        /// <summary>
        /// Délai moyen entre le SMS et la PREMIÈRE ouverture, en heures, calculé
        /// sur les seuls liens qui n'avaient jamais été ouverts avant. C'est lui
        /// qui dit si le canal fonctionne : une ouverture dans l'heure, c'est un
        /// message lu ; trois jours après, c'est un message retrouvé.
        /// </summary>
        public double? AvgHoursToFirstOpen { get; set; }

        /// <summary>Nombre de liens sur lesquels ce délai a pu être mesuré.</summary>
        public int DelaySampleSize { get; set; }

        // ----- Lien contre application, sur la même période -----

        /// <summary>Jours observés pour la comparaison ci-dessous.</summary>
        public int ComparisonDays { get; set; }

        /// <summary>Paiements de scolarité passés par un LIEN.</summary>
        public int ViaLinkCount { get; set; }
        public long ViaLinkFcfa { get; set; }

        /// <summary>Paiements de scolarité faits depuis l'APPLICATION (parent connecté).</summary>
        public int ViaAppCount { get; set; }
        public long ViaAppFcfa { get; set; }

        /// <summary>Encaissements en ESPÈCES au guichet de l'école (§182).</summary>
        public int ViaCashCount { get; set; }
        public long ViaCashFcfa { get; set; }

        public List<PaymentLinkFunnelSchoolDto> BySchool { get; set; } = new();
    }
}
