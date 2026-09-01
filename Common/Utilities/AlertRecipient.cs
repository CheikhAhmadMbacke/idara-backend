using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.Options;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Common.Utilities
{
    /// <summary>
    /// À qui adresser une alerte interne. <b>Source unique</b> : la même règle
    /// sert aux incidents clients et aux alertes d'exploitation. Deux copies de
    /// ce repli finiraient par diverger, et on découvrirait la divergence le jour
    /// où l'une des deux familles d'alerte n'arriverait plus.
    ///
    /// <para>Repli en trois temps — réglage explicite, puis comptes SuperAdmin de
    /// la base, puis configuration de démarrage : <b>aucune variable
    /// d'environnement n'est donc nécessaire</b> pour que les alertes
    /// fonctionnent en production.</para>
    /// </summary>
    public static class AlertRecipient
    {
        public static async Task<string?> ResolveAsync(
            AppDbContext db,
            string? configured,
            SuperAdminSettings superAdmin,
            CancellationToken ct = default)
        {
            var explicitEmail = configured?.Trim();
            if (!string.IsNullOrEmpty(explicitEmail)) return explicitEmail;

            var fromDb = await db.Users
                .Where(u => u.Role == UserRoles.SuperAdmin && !u.IsDeleted && u.Email != null)
                .OrderBy(u => u.Id)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(fromDb)) return fromDb;

            return string.IsNullOrWhiteSpace(superAdmin.Email) ? null : superAdmin.Email;
        }
    }
}
