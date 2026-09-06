namespace Idara.API.Models
{
    /// <summary>
    /// Lien de paiement PERMANENT envoyé par l'école à un responsable (WhatsApp).
    ///
    /// <para>Le lien ne porte AUCUN montant et ne crée AUCUNE facture : à chaque
    /// ouverture, la page publique recalcule la dette réelle du responsable dans
    /// cette école (mensualités + inscriptions impayées de TOUS ses enfants) via
    /// le même calcul que « Tout payer » dans l'app. Il n'expire donc jamais, se
    /// réutilise mois après mois, et dit « rien à payer » quand tout est réglé.
    /// Un lien Wave figé, lui, aurait expiré (SenePay passe un Pending abandonné
    /// en Failed) et porté un montant périmé.</para>
    ///
    /// <para>UN lien actif par (école, responsable) : régénérer renvoie le même
    /// (comme les identifiants). Révoquer (<see cref="RevokedAt"/>) le rend mort
    /// et permet d'en créer un nouveau — le jeton précédent ne rouvrira plus.</para>
    ///
    /// <para>Le jeton (128 bits) est la seule authentification : quiconque le
    /// détient peut PAYER pour ce responsable (un tiers payeur, voulu) et lire la
    /// dette de la famille (prénoms + montants — d'où : jamais de photo, et le
    /// lien est révocable).</para>
    /// </summary>
    public class PaymentLink
    {
        public int Id { get; set; }

        /// <summary>Jeton opaque (GUID v4 sans tirets, 32 hexa). Unique.</summary>
        public string Token { get; set; } = string.Empty;

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        /// <summary>Responsable (User rôle Guardian) auquel les paiements seront attribués.</summary>
        public int GuardianId { get; set; }
        public User Guardian { get; set; } = null!;

        /// <summary>Membre de l'école qui a généré le lien (audit).</summary>
        public int CreatedById { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Dernière ouverture de la page publique (null = jamais ouvert).</summary>
        public DateTime? LastOpenedAt { get; set; }
        public int OpenCount { get; set; }

        /// <summary>
        /// PREMIÈRE ouverture de la page publique.
        ///
        /// <para>Distincte de <see cref="LastOpenedAt"/> et elle répond à une
        /// autre question : combien de temps s'écoule entre le SMS et le moment
        /// où la famille clique. C'est la mesure qui dit si le lien fonctionne —
        /// une ouverture dans l'heure, c'est un canal qui marche ; trois jours
        /// après, c'est un message qu'on retrouve en fouillant.</para>
        /// </summary>
        public DateTime? FirstOpenedAt { get; set; }

        /// <summary>Dernière fois que l'école a (re)demandé le lien (renvoi).</summary>
        public DateTime? LastSharedAt { get; set; }

        public DateTime? RevokedAt { get; set; }
        public int? RevokedById { get; set; }

        public bool IsActive => RevokedAt == null;
    }
}
