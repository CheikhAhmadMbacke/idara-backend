namespace Idara.API.Enums
{
    /// <summary>
    /// Origine d'un incident remonté par l'application.
    ///
    /// ⚠️ Valeurs persistées en <c>integer</c> : ne JAMAIS réordonner, seulement
    /// ajouter à la fin (même discipline que <c>TransferCategory</c>).
    /// </summary>
    public enum IncidentKind
    {
        /// <summary>
        /// Exception non gérée dans l'application (rendu, <c>Future</c> échappé).
        /// C'est le cas qui, jusqu'au 2026-07-29, ne laissait absolument
        /// AUCUNE trace : écran gris chez l'utilisateur, rien côté serveur.
        /// </summary>
        FlutterError = 0,

        /// <summary>
        /// Signalement volontaire via le bouton « Signaler un problème ». Le seul
        /// déclencheur qui ne produit aucun faux positif.
        /// </summary>
        UserReport = 1,

        /// <summary>
        /// Erreur d'API vue depuis le client (5xx, échec réseau répété). Le
        /// serveur en a sa propre trace ; celle-ci dit ce que l'utilisateur, lui,
        /// a réellement vécu.
        /// </summary>
        ApiError = 2,

        /// <summary>
        /// Redémarrage inattendu de l'application (2026-08-27) : au démarrage, le
        /// battement de cœur de la session précédente date de moins de quelques
        /// minutes SANS fermeture normale — l'utilisateur était en pleine
        /// utilisation quand tout est reparti au splash. Un processus tué
        /// n'envoie rien : ce rapport est émis PAR le redémarrage suivant, c'est
        /// le seul moyen de voir ces morts subites (elles n'existaient dans
        /// aucune trace jusqu'ici). Le message dit l'écran quitté, l'ancienneté
        /// du dernier battement, premier plan ou non, et sur le web la cause
        /// quand elle est connue (rechargement service worker).
        /// </summary>
        UnexpectedRestart = 3,
    }
}
