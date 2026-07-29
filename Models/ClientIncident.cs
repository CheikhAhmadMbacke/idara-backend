using Idara.API.Enums;

namespace Idara.API.Models
{
    /// <summary>
    /// Incident remonté par l'application, retrouvable par son code.
    ///
    /// <para><b>Le problème que cette table résout.</b> Avant le 2026-07-29,
    /// une exception dans le rendu d'un écran donnait un écran gris chez
    /// l'utilisateur et <b>ne laissait rien, nulle part</b> : rien sur le
    /// téléphone, rien sur le serveur (le plantage est purement local). Même en
    /// récupérant l'appareil, il n'y avait rien à lire. Il n'existait donc aucun
    /// moyen, même théorique, de diagnostiquer un plantage d'interface.</para>
    ///
    /// <para><b>Ajout seul, purgé à 30 jours</b> au démarrage (motif
    /// <see cref="IdempotencyRecord"/>). C'est de la donnée de diagnostic, pas de
    /// la donnée métier : elle n'a aucune valeur passé un mois.</para>
    ///
    /// <para><b>Ce qui n'y entre JAMAIS</b> (§4.7 du plan) : aucun corps de
    /// requête, aucun contenu de champ de saisie, aucun mot de passe, aucun jeton,
    /// aucun nom propre. Un écran est enregistré comme <c>/students/427</c>,
    /// jamais « Fiche de Fatou Diop ». C'est ce qui permet de collecter sans
    /// consentement explicite et sans sous-traitant à déclarer.</para>
    /// </summary>
    public class ClientIncident
    {
        public int Id { get; set; }

        /// <summary>
        /// Code affiché à l'utilisateur (<c>IDR-7K2MQ4</c>). C'est la clé de
        /// recherche : le directeur le dicte au téléphone, on le colle dans la
        /// page SuperAdmin.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        public IncidentKind Kind { get; set; }

        /// <summary>
        /// Auteur du signalement. Nullable : un compte peut être supprimé
        /// (anonymisé, §68) sans qu'on veuille perdre l'incident, et l'endpoint de
        /// réception exige une authentification mais la trace peut survivre.
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>École concernée, telle que vue dans le jeton de l'appelant.</summary>
        public int? SchoolId { get; set; }

        public string Role { get; set; } = string.Empty;

        /// <summary>android / ios / web.</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>
        /// Version de l'application. Indispensable : la moitié des « ça ne marche
        /// pas » se résolvent en « cette version-là est ancienne ».
        /// </summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>Modèle d'appareil et version d'OS, sans identifiant unique.</summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>Langue affichée au moment de l'incident (fr / ar).</summary>
        public string LocaleCode { get; set; } = string.Empty;

        /// <summary>Écran courant, sous forme de route (jamais un libellé de données).</summary>
        public string Route { get; set; } = string.Empty;

        /// <summary>
        /// Message d'erreur, tronqué. ⚠️ Un message serveur peut contenir des
        /// données personnelles (« Le parent X est déjà lié ») : le renvoyer au
        /// client est normal, le <b>stocker</b> demande de le borner. Tronqué à la
        /// réception, jamais au-delà.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        public string ExceptionType { get; set; } = string.Empty;

        /// <summary>
        /// Pile d'appels. Lisible telle quelle : la chaîne de compilation
        /// n'utilise pas <c>--obfuscate</c> (et ne doit jamais l'utiliser, sans
        /// quoi il faudrait conserver les symboles de chaque version publiée).
        /// </summary>
        public string StackTrace { get; set; } = string.Empty;

        /// <summary>
        /// Code de corrélation de la requête HTTP fautive, quand l'incident en
        /// découle. C'est lui qui relie la trace du téléphone aux lignes du
        /// journal serveur.
        /// </summary>
        public string? RequestTrace { get; set; }

        /// <summary>Ce que l'utilisateur a écrit dans « Signaler un problème ».</summary>
        public string? UserComment { get; set; }

        /// <summary>
        /// Chronologie des événements précédant l'incident, en <c>jsonb</c>.
        ///
        /// <para><b>Réservée au lot 2</b> et volontairement posée dès maintenant :
        /// une colonne nullable non alimentée ne coûte rien, alors qu'une seconde
        /// migration sur une table de diagnostic en coûterait.</para>
        /// </summary>
        public string? Timeline { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Marqué comme traité par le SuperAdmin (simple confort de tri).</summary>
        public bool IsResolved { get; set; }

        public User? User { get; set; }
        public School? School { get; set; }
    }
}
