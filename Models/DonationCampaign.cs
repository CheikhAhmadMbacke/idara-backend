using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Collecte de dons d'une école — l'objet derrière <c>idara.sn/don/{slug}</c>.
    ///
    /// <para><b>Le donateur n'a pas de compte.</b> L'adresse est la seule porte :
    /// quiconque l'ouvre peut donner. C'est voulu, et sans danger — contrairement
    /// au lien de paiement d'un responsable (qui ouvre la dette d'une famille et
    /// garde donc un jeton de 128 bits), une page de collecte n'expose que ce que
    /// l'école a délibérément publié : un titre, un texte, une photo, un total.
    /// D'où une adresse <b>lisible et dictable au téléphone</b> plutôt qu'un
    /// jeton opaque.</para>
    ///
    /// <para><b>Le total collecté n'est pas ici.</b> Il se recalcule des paiements
    /// confirmés à chaque lecture, comme la part plateforme de l'identité
    /// <c>R = D + P</c> (§112). Un compteur stocké finit toujours par mentir : il
    /// suffit d'un remboursement, d'une annulation ou d'un webhook rejoué.</para>
    ///
    /// <para><b>Une collecte qui a reçu de l'argent ne se supprime pas</b> (§55).
    /// Elle se met en pause ou se ferme. Seule une collecte à zéro don part
    /// vraiment — sinon des dons resteraient sans origine et la réconciliation
    /// de l'école ne tomberait plus juste.</para>
    /// </summary>
    public class DonationCampaign
    {
        public int Id { get; set; }

        /// <summary>
        /// Adresse publique lisible (<c>salle-bleue</c>), unique sur toute la
        /// plateforme. Générée du nom, suffixée en cas de collision.
        /// </summary>
        public string Slug { get; set; } = string.Empty;

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Titre lu par le donateur. Seul champ obligatoire.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Pourquoi cette collecte. C'est ce qui décide les gens.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// Photo de couverture (chemin relatif <c>/uploads/donations/…</c>).
        /// ⚠️ Servie par nginx SANS authentification (§122) : elle est publique
        /// dès sa mise en ligne. L'écran d'ajout doit l'écrire à l'école, et
        /// interdire les visages d'élèves reconnaissables sans accord des parents.
        /// </summary>
        public string? CoverImagePath { get; set; }

        /// <summary>Objectif affiché sous forme de barre. Null = pas de barre.</summary>
        public long? GoalAmountFcfa { get; set; }

        public DonationAmountMode AmountMode { get; set; } = DonationAmountMode.Free;

        /// <summary>Montant imposé quand <see cref="AmountMode"/> vaut Fixed.</summary>
        public long? FixedAmountFcfa { get; set; }

        /// <summary>
        /// Montants proposés en boutons sur la page (CSV, ex. <c>5000,10000,25000</c>).
        /// Sur une page de don, la majorité clique un bouton plutôt que de taper.
        /// </summary>
        public string? SuggestedAmountsCsv { get; set; }

        /// <summary>
        /// Qui paie les frais — n'a de sens qu'en montant imposé : majorer une
        /// somme choisie par le donateur lui-même n'en a aucun. Copié du réglage
        /// de l'école à la création (<c>SchoolPaymentSettings.DonationFeesPayer</c>),
        /// puis figé : changer le réglage global ne doit pas modifier ce qu'un
        /// donateur voit sur un lien déjà partagé.
        /// ⚠️ La branche <c>Parent</c> disparaîtra au passage à Wave direct (§145).
        /// </summary>
        public FeesPayer FeesPayer { get; set; } = FeesPayer.School;

        /// <summary>Mur des donateurs sur la page publique (prénom + initiale).</summary>
        public bool ShowDonorWall { get; set; } = true;

        public DonationCampaignStatus Status { get; set; } = DonationCampaignStatus.Active;

        /// <summary>Date limite. Passée, la collecte se lit fermée sans cron.</summary>
        public DateTime? ClosesAt { get; set; }

        /// <summary>
        /// Collecte permanente « Dons au daara », créée d'office à la première
        /// visite de l'écran. Ni supprimable, ni fermable : c'est le lien que
        /// l'école partage quand elle ne veut penser à rien.
        /// </summary>
        public bool IsPermanent { get; set; }

        public int CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }

        public DateTime? ClosedAt { get; set; }
        public int? ClosedById { get; set; }

        /// <summary>Visites de la page publique (pas les rafraîchissements).</summary>
        public int OpenCount { get; set; }
        public DateTime? LastOpenedAt { get; set; }

        /// <summary>Ouverte aux dons : ni pause, ni fermeture, ni date dépassée.</summary>
        public bool IsOpen =>
            Status == DonationCampaignStatus.Active
            && (ClosesAt == null || ClosesAt > DateTime.UtcNow);

        /// <summary>Les montants suggérés, nettoyés et ordonnés.</summary>
        public IReadOnlyList<long> SuggestedAmounts()
        {
            if (string.IsNullOrWhiteSpace(SuggestedAmountsCsv)) return Array.Empty<long>();
            return SuggestedAmountsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var v) ? v : 0)
                .Where(v => v > 0)
                .Distinct()
                .OrderBy(v => v)
                .Take(3)
                .ToList();
        }
    }
}
