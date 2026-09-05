using System.ComponentModel.DataAnnotations;
using Idara.API.DTOs.Common;
using Idara.API.Enums;

namespace Idara.API.DTOs.Donation
{
    /// <summary>Une collecte, telle que l'école la lit.</summary>
    public class DonationCampaignDto
    {
        public int Id { get; set; }
        public string Slug { get; set; } = string.Empty;

        /// <summary>Adresse complète à partager (https://idara.sn/don/…).</summary>
        public string PublicUrl { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? CoverImageUrl { get; set; }

        public long? GoalAmountFcfa { get; set; }
        public DonationAmountMode AmountMode { get; set; }
        public long? FixedAmountFcfa { get; set; }
        public List<long> SuggestedAmounts { get; set; } = new();
        public FeesPayer FeesPayer { get; set; }
        public bool ShowDonorWall { get; set; }

        public DonationCampaignStatus Status { get; set; }
        public DateTime? ClosesAt { get; set; }
        public bool IsPermanent { get; set; }

        /// <summary>Accepte les dons : ni pause, ni fermeture, ni date dépassée.</summary>
        public bool IsOpen { get; set; }

        /// <summary>Recalculé des paiements confirmés, jamais stocké (§112).</summary>
        public long CollectedFcfa { get; set; }
        public int DonationCount { get; set; }
        public int PendingCount { get; set; }
        public int OpenCount { get; set; }

        /// <summary>
        /// Supprimable UNIQUEMENT si la collecte n'a jamais rien reçu. Calculé par
        /// le serveur pour que l'écran n'ait pas à redevimer la règle (§55).
        /// </summary>
        public bool CanDelete { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Ce que l'utilisateur courant a le droit de faire sur les collectes.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>L'autorité, c'est le SERVEUR.</b> L'écran testait le rôle de son
    /// côté (<c>role == 'SchoolAdmin'</c>) pour décider d'afficher le bouton
    /// « Nouvelle collecte ». Quand ce test échouait — pour quelque raison que
    /// ce soit — le bouton disparaissait <b>sans un mot</b>, et l'utilisateur
    /// n'avait aucun moyen de comprendre : ni message, ni bouton grisé, rien.
    /// C'est exactement la porte murée du §212, en pire, parce qu'ici la porte
    /// existe et se ferme sur un test dupliqué.
    /// Désormais le serveur DIT ce qui est permis, et l'écran l'explique quand
    /// ça ne l'est pas.
    /// </remarks>
    public class DonationCampaignsResponse
    {
        public List<DonationCampaignDto> Campaigns { get; set; } = new();

        /// <summary>Créer, modifier, fermer : réservé à la direction.</summary>
        public bool CanManage { get; set; }
    }

    /// <summary>
    /// L'enveloppe RÉELLEMENT servie par <c>GET /api/donation-campaigns</c> :
    /// <c>data</c> reste la LISTE nue, et le droit de gérer voyage à côté d'elle.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Pourquoi cette forme et pas <see cref="DonationCampaignsResponse"/>.</b>
    /// Le 5 septembre 2026, <c>data</c> est passé de la liste nue à un objet
    /// <c>{ campaigns, canManage }</c> pour porter ce droit. Toutes les applications
    /// déjà installées lisaient <c>data</c> par un <c>as List</c> : le cast a échoué
    /// chez elles, et l'écran des collectes est mort d'un coup sur tous les
    /// téléphones du parc — « Impossible de charger vos collectes », sans qu'aucune
    /// d'elles ait changé.
    ///
    /// Une réponse ne peut pas être à la fois une liste et un objet ; en revanche
    /// l'ENVELOPPE, elle, peut porter un champ de plus. Un ancien client lit sa
    /// liste et ignore <c>canManage</c> (il retrouve son test de rôle local, qui
    /// marchait) ; un client à jour lit le champ et obéit au serveur. Personne n'a
    /// à installer quoi que ce soit — la règle de conduite du projet : chercher
    /// d'abord côté serveur.
    ///
    /// ⚠️ <b>Règle générale.</b> Le contenu de <c>data</c> est un contrat public
    /// figé dès la première application publiée. On y AJOUTE des champs, on n'en
    /// change jamais la nature.
    /// </remarks>
    public class DonationCampaignsEnvelope : ApiResponse<List<DonationCampaignDto>>
    {
        /// <summary>Créer, modifier, fermer : réservé à la direction.</summary>
        public bool CanManage { get; set; }
    }

    /// <summary>Un don reçu, tel que l'école le lit.</summary>
    public class CampaignDonationDto
    {
        public int Id { get; set; }

        /// <summary>Nom déclaré par le donateur. L'école le voit toujours, même anonymisé publiquement.</summary>
        public string? DonorName { get; set; }
        public string? DonorPhone { get; set; }
        public string? DonorOrganization { get; set; }
        public bool DonorAnonymous { get; set; }

        public long AmountFcfa { get; set; }
        public PaymentStatus Status { get; set; }
        public DateTime InitiatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public bool HasReceipt { get; set; }
    }

    public class CreateDonationCampaignRequest
    {
        [Required(ErrorMessage = "Le nom de la collecte est obligatoire.")]
        [StringLength(120, ErrorMessage = "Le nom ne peut pas dépasser 120 caractères.")]
        public string Name { get; set; } = string.Empty;

        // ⚠️ Jamais de MinimumLength sur un champ facultatif : StringLength le
        // rendrait obligatoire en silence (§184).
        [StringLength(2000, ErrorMessage = "Le texte ne peut pas dépasser 2000 caractères.")]
        public string? Description { get; set; }

        [Range(1, 1_000_000_000, ErrorMessage = "L'objectif doit être un montant positif.")]
        public long? GoalAmountFcfa { get; set; }

        public DonationAmountMode AmountMode { get; set; } = DonationAmountMode.Free;

        [Range(1, 100_000_000, ErrorMessage = "Le montant imposé doit être positif.")]
        public long? FixedAmountFcfa { get; set; }

        /// <summary>Jusqu'à trois montants proposés en boutons sur la page.</summary>
        public List<long>? SuggestedAmounts { get; set; }

        /// <summary>
        /// Qui paie les frais. Null = on reprend le réglage de l'école. N'a de
        /// sens qu'en montant imposé.
        /// </summary>
        public FeesPayer? FeesPayer { get; set; }

        public bool ShowDonorWall { get; set; } = true;

        public DateTime? ClosesAt { get; set; }
    }

    /// <summary>
    /// Modification d'une collecte. DTO SÉPARÉ de la création : un DTO d'écriture
    /// partagé entre deux formulaires est exactement l'endroit où un champ ajouté
    /// d'un côté se perd de l'autre, en silence (§140).
    /// </summary>
    public class UpdateDonationCampaignRequest
    {
        [StringLength(120)]
        public string? Name { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        [Range(0, 1_000_000_000)]
        public long? GoalAmountFcfa { get; set; }

        public DonationAmountMode? AmountMode { get; set; }

        [Range(0, 100_000_000)]
        public long? FixedAmountFcfa { get; set; }

        public List<long>? SuggestedAmounts { get; set; }
        public FeesPayer? FeesPayer { get; set; }
        public bool? ShowDonorWall { get; set; }

        /// <summary>Date limite. `true` sur <see cref="ClearClosesAt"/> pour la retirer.</summary>
        public DateTime? ClosesAt { get; set; }
        public bool ClearClosesAt { get; set; }

        /// <summary>Retirer la photo de couverture.</summary>
        public bool ClearCoverImage { get; set; }
    }
}
