using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    public static class PaymentFoundationsExtensions
    {
        /// <summary>
        /// Garantit que l'école possède ses fondations paiement :
        /// <see cref="SchoolPaymentSettings"/> et <see cref="SchoolWallet"/>
        /// (PK = SchoolId, 1-1 chacun).
        ///
        /// <para>Idempotent et tolérant à la concurrence : si deux requêtes
        /// appellent la méthode simultanément, la seconde attrape le conflit de
        /// clé primaire (les lignes existent déjà) et n'échoue pas.</para>
        ///
        /// <para>Pourquoi ce helper : <see cref="DbInitializer"/> ne seede que
        /// les écoles présentes AU DÉMARRAGE. Une école créée entre deux
        /// redémarrages (via submit-kyc) n'avait ni wallet ni settings →
        /// <c>GET /api/fees/wallet</c> renvoyait 404 et un crédit webhook
        /// throwait. On l'appelle donc (a) à la création de l'école et (b) en
        /// filet de secours sur les endpoints qui lisent le wallet.</para>
        /// </summary>
        public static async Task EnsurePaymentFoundationsAsync(
            this AppDbContext db, int schoolId, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var added = false;

            var hasSettings = await db.SchoolPaymentSettings
                .AnyAsync(s => s.SchoolId == schoolId, ct);
            if (!hasSettings)
            {
                db.SchoolPaymentSettings.Add(new SchoolPaymentSettings
                {
                    SchoolId = schoolId,
                    BillingMode = BillingMode.FixedAmount,
                    FeesPayer = FeesPayer.Parent,
                    MonthlyDueDay = 5,
                    BillingPeriod = BillingPeriod.Monthly,
                    CreatedAt = now
                });
                added = true;
            }

            var hasWallet = await db.SchoolWallets
                .AnyAsync(w => w.SchoolId == schoolId, ct);
            if (!hasWallet)
            {
                db.SchoolWallets.Add(new SchoolWallet
                {
                    SchoolId = schoolId,
                    AvailableBalance = 0,
                    PendingBalance = 0,
                    TotalCreditedLifetime = 0,
                    TotalWithdrawnLifetime = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                added = true;
            }

            if (!added) return;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Concurrence : une autre requête a créé les fondations entre
                // nos AnyAsync et le SaveChanges (violation unique 23505). Les
                // lignes existent désormais, on détache nos doublons en attente
                // et on continue sans erreur. Toute autre DbUpdateException
                // (vraie erreur DB) remonte normalement.
                foreach (var entry in db.ChangeTracker.Entries<SchoolPaymentSettings>()
                             .Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                foreach (var entry in db.ChangeTracker.Entries<SchoolWallet>()
                             .Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }
}
