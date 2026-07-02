using Idara.API.Data;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Extensions
{
    public static class WalletLockExtensions
    {
        /// <summary>
        /// Charge le <see cref="SchoolWallet"/> d'une école avec un verrou
        /// pessimiste PostgreSQL (<c>SELECT ... FOR UPDATE</c>).
        ///
        /// <para><b>DOIT être appelé à l'intérieur d'une transaction ouverte</b>
        /// (<c>BeginTransactionAsync</c>) — le verrou ligne est tenu jusqu'au
        /// commit/rollback. Sérialise TOUS les mouvements concurrents sur le
        /// solde d'une même école : deux retraits simultanés, un retrait vs un
        /// crédit payin, un webhook vs une restitution… ne peuvent plus
        /// s'écraser mutuellement (lost update) ni sur-débiter.</para>
        ///
        /// <para>Comme le wallet peut déjà être tracké par le contexte, on
        /// <c>Reload</c> après la prise de verrou pour garantir des valeurs
        /// fraîches (l'identity-map d'EF ne rafraîchit pas un FromSql sur une
        /// entité déjà suivie).</para>
        /// </summary>
        public static async Task<SchoolWallet?> LockWalletAsync(
            this AppDbContext db, int schoolId, CancellationToken ct = default)
        {
            // ToListAsync (et NON FirstOrDefaultAsync) : on veut que le SQL brut
            // s'exécute VERBATIM. FirstOrDefaultAsync ferait wrapper la requête
            // en sous-requête `SELECT ... FROM (<sql>) LIMIT 1`, or PostgreSQL
            // interdit `FOR UPDATE` dans une sous-requête. Le WHERE porte sur la
            // PK (SchoolId) → au plus 1 ligne, pas de coût.
            var wallets = await db.SchoolWallets
                .FromSqlInterpolated(
                    $"SELECT * FROM \"SchoolWallets\" WHERE \"SchoolId\" = {schoolId} FOR UPDATE")
                .ToListAsync(ct);

            var wallet = wallets.FirstOrDefault();
            if (wallet != null)
                await db.Entry(wallet).ReloadAsync(ct);

            return wallet;
        }

        /// <summary>
        /// Verrou pessimiste pour sérialiser les mouvements de GAINS PLATEFORME
        /// (retraits plateforme concurrents). Comme le solde plateforme P est
        /// recalculé (pas un wallet matérialisé), on n'a pas de ligne de solde à
        /// verrouiller : on prend un <c>FOR UPDATE</c> sur la ligne singleton
        /// <see cref="PlatformSettings"/> (Id = 1), qui existe toujours et n'est
        /// mutée que rarement (édition des réglages). Un simple SELECT ne bloque
        /// PAS sur ce verrou en PostgreSQL — seuls un autre retrait plateforme ou
        /// un UPDATE des réglages attendent. DOIT être appelé dans une transaction.
        /// </summary>
        public static async Task LockPlatformAsync(
            this AppDbContext db, CancellationToken ct = default)
        {
            _ = await db.PlatformSettings
                .FromSqlInterpolated(
                    $"SELECT * FROM \"PlatformSettings\" WHERE \"Id\" = {PlatformSettings.SingletonId} FOR UPDATE")
                .ToListAsync(ct);
        }
    }
}
