using Idara.API.DTOs.Observability;

namespace Idara.API.Services.Observability
{
    /// <summary>
    /// Destination des incidents remontés par l'application.
    ///
    /// <para><b>Pourquoi une interface pour une seule implémentation.</b> C'est la
    /// porte de sortie du chantier, exactement comme <c>ISmsService</c> (§89) et
    /// <c>IPushService</c> (§96) : le jour où le volume justifierait une
    /// plateforme dédiée (GlitchTip ou autre), on écrit <b>un seul fichier</b> et
    /// rien d'autre ne bouge — ni les points de collecte côté application, ni la
    /// rédaction, ni les déclencheurs, ni la page de consultation. Cette
    /// discipline a déjà servi deux fois sur ce projet.</para>
    /// </summary>
    public interface ITelemetrySink
    {
        /// <summary>
        /// Enregistre un incident. Applique les plafonds et les troncatures ;
        /// <b>ne lève jamais</b> pour un motif de politique (plafond atteint) mais
        /// renvoie <c>Stored = false</c>.
        /// </summary>
        Task<IncidentAcceptedDto> RecordAsync(
            ReportIncidentDto dto,
            int? userId,
            int? schoolId,
            string role,
            CancellationToken ct = default);
    }
}
