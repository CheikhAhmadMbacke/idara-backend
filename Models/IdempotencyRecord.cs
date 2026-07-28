namespace Idara.API.Models
{
    /// <summary>
    /// Trace d'une écriture déjà traitée, identifiée par une clé fournie par le
    /// client (en-tête <c>Idempotency-Key</c>).
    ///
    /// <para><b>À quoi ça sert.</b> Quand le réseau coupe pendant un envoi,
    /// l'application ne sait PAS si le serveur a traité la demande ou non : la
    /// requête est peut-être arrivée et c'est la réponse qui s'est perdue. La
    /// file d'attente locale (Outbox) rejoue donc l'envoi au retour du réseau —
    /// et sans précaution, une écriture de caisse ou une note serait enregistrée
    /// DEUX fois.</para>
    ///
    /// <para>Le client génère un identifiant unique par saisie et le renvoie à
    /// l'identique à chaque tentative. À la première, on exécute et on mémorise
    /// la réponse ; aux suivantes, on renvoie cette même réponse sans rien
    /// réexécuter. C'est exactement le raisonnement de l'idempotence des webhooks
    /// (CLAUDE.md §50), appliqué cette fois au client.</para>
    ///
    /// <para>Les endpoints en <i>upsert par clé naturelle</i> (présences, journal
    /// quotidien, suivi coranique du jour) sont rejouables sans ce mécanisme :
    /// ré-enregistrer la même journée écrase la même ligne. Il reste utile pour
    /// eux car il évite un aller-retour inutile, mais il est INDISPENSABLE pour
    /// les écritures qui insèrent (caisse, notes).</para>
    /// </summary>
    public class IdempotencyRecord
    {
        public int Id { get; set; }

        /// <summary>Clé fournie par le client. Unique PAR utilisateur.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Propriétaire de la clé. La clé seule ne suffit pas : deux appareils
        /// pourraient tirer le même identifiant, et surtout un utilisateur ne
        /// doit jamais pouvoir récupérer la réponse mémorisée d'un autre.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Méthode + chemin de la requête d'origine. Sert de garde-fou : rejouer
        /// la même clé sur un AUTRE endpoint est une erreur du client, pas une
        /// reprise — on refuse plutôt que de renvoyer une réponse sans rapport.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        public int StatusCode { get; set; }

        /// <summary>Corps de la réponse d'origine, réémis tel quel au rejeu.</summary>
        public string ResponseBody { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
