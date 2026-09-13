namespace Idara.API.DTOs.Auth
{
    public class SendOtpRequest
    {
        /// <summary>
        /// Adresse email <b>ou</b> numéro de téléphone. La présence d'un « @ »
        /// tranche — exactement comme à la connexion.
        /// </summary>
        /// <remarks>
        /// 🔴 <b>Pas de <c>[Required]</c> ni de <c>[EmailAddress]</c> ici</b>, et
        /// ce n'est pas un oubli : un numéro n'est pas une adresse, l'attribut
        /// rejetterait toutes les inscriptions par téléphone avant même
        /// d'atteindre le contrôleur. La validation se fait dans
        /// <c>AuthIdentifier.Parse</c>, qui sait dire LEQUEL des deux était
        /// attendu et pourquoi il n'est pas bon (§185 : un
        /// <c>ValidationProblemDetails</c> porte toujours un titre passe-partout,
        /// inutilisable dans un écran).
        /// </remarks>
        public string? Identifier { get; set; }

        /// <summary>
        /// Ancien nom du champ, toléré tant que des applications antérieures au
        /// 2026-09-13 tournent encore sur les téléphones.
        ///
        /// <para>🔴 §220 — le contenu d'un DTO est un contrat FIGÉ dès la première
        /// version publiée. Retirer ce champ casserait l'inscription de tous ceux
        /// qui n'ont pas encore mis à jour, et la livraison mobile a déjà montré
        /// qu'elle pouvait prendre des jours (§250). Il disparaîtra quand le parc
        /// aura tourné, pas avant.</para>
        /// </summary>
        public string? Email { get; set; }

        /// <summary>Langue du code ("fr" ou "ar").</summary>
        public string? PreferredLanguage { get; set; }

        /// <summary>Ce que l'appelant a fourni, quel que soit le nom du champ.</summary>
        public string RawIdentifier =>
            !string.IsNullOrWhiteSpace(Identifier) ? Identifier! : (Email ?? string.Empty);
    }
}
