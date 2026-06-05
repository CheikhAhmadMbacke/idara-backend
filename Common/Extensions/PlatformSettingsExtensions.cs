using Idara.API.Data;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    public static class PlatformSettingsExtensions
    {
        /// <summary>
        /// Renvoie la ligne unique de <see cref="PlatformSettings"/>, en la
        /// créant avec les valeurs par défaut si elle n'existe pas encore
        /// (idempotent, tolérant à la concurrence). Une seule ligne, lookup PK :
        /// coût négligeable même appelé à chaque paiement / retrait.
        /// </summary>
        public static async Task<PlatformSettings> GetPlatformSettingsAsync(
            this AppDbContext db, CancellationToken ct = default)
        {
            var settings = await db.PlatformSettings
                .FirstOrDefaultAsync(x => x.Id == Models.PlatformSettings.SingletonId, ct);
            if (settings != null) return settings;

            settings = new PlatformSettings { Id = Models.PlatformSettings.SingletonId };
            db.PlatformSettings.Add(settings);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrence : créé entre-temps par une autre requête.
                db.Entry(settings).State = EntityState.Detached;
                settings = await db.PlatformSettings
                    .FirstAsync(x => x.Id == Models.PlatformSettings.SingletonId, ct);
            }
            return settings;
        }
    }
}
