namespace Idara.API.Common.Extensions
{
    /// <summary>
    /// Helpers de normalisation DateTime pour PostgreSQL via Npgsql.
    ///
    /// Contexte : depuis Npgsql 6+ (et donc EF Core PG 8 utilisé ici), une
    /// colonne <c>timestamp with time zone</c> (<c>timestamptz</c>) refuse
    /// tout <see cref="DateTime"/> dont <see cref="DateTime.Kind"/> n'est pas
    /// <see cref="DateTimeKind.Utc"/>. C'est strict : passer un
    /// <c>Kind = Unspecified</c> ou <c>Local</c> dans un <c>Where</c> EF Core
    /// déclenche un <c>500 — Cannot write DateTime with Kind=Unspecified to
    /// PostgreSQL type 'timestamp with time zone'</c>.
    ///
    /// Les paramètres <see cref="DateTime"/> qu'ASP.NET binde depuis une query
    /// string (ex: <c>?date=2026-05-22T23:51:23</c>) ou un body JSON sont
    /// toujours <c>Kind = Unspecified</c> sauf si le client a écrit
    /// explicitement un <c>Z</c> ou un offset. Comme on ne peut pas faire
    /// confiance au client, on normalise systématiquement avant la requête.
    ///
    /// Convention de normalisation : <c>Unspecified</c> est traité comme
    /// "déjà en UTC, on a juste oublié le tag" (et non comme heure locale
    /// serveur). C'est cohérent avec le fait que toutes les écritures se font
    /// via <see cref="DateTime.UtcNow"/> côté backend.
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Normalise un <see cref="DateTime"/> pour qu'il soit accepté par
        /// une colonne <c>timestamptz</c>. Idempotent : si déjà UTC, retourne
        /// tel quel.
        /// </summary>
        public static DateTime ToUtcSafe(this DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }

        /// <summary>
        /// Variante null-safe de <see cref="ToUtcSafe(DateTime)"/>.
        /// </summary>
        public static DateTime? ToUtcSafe(this DateTime? value)
        {
            return value.HasValue ? value.Value.ToUtcSafe() : null;
        }

        /// <summary>
        /// Tronque à minuit UTC. Pratique pour filtrer "tous les
        /// enregistrements de la journée X" sans dépendre de l'heure.
        /// Combine avec <c>AddDays(1)</c> pour la borne haute exclusive.
        /// </summary>
        public static DateTime ToUtcDay(this DateTime value)
        {
            var d = value.ToUtcSafe();
            return new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
        }

        /// <summary>
        /// Variante null-safe de <see cref="ToUtcDay(DateTime)"/>.
        /// </summary>
        public static DateTime? ToUtcDay(this DateTime? value)
        {
            return value.HasValue ? value.Value.ToUtcDay() : null;
        }
    }
}
