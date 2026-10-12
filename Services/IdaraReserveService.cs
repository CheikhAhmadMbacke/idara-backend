using Idara.API.Common.Utilities;
using Idara.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>Part du portefeuille Wave qui appartient à Idara — le R de <c>R = D + P</c>.</summary>
    /// <param name="IdaraFcfa">Réserve d'Idara : ce qui se compare à D + P.</param>
    /// <param name="WalletTotalFcfa">Solde du portefeuille ENTIER, en direct (tous produits).</param>
    /// <param name="OtherProductsFcfa">Solde net des opérations d'un autre produit (Àppal…).</param>
    /// <param name="UnattributedFcfa">Solde net des lignes que rien ne rattache à Idara, depuis le partage.</param>
    /// <param name="UnattributedCount">Nombre de ces lignes.</param>
    /// <param name="SharedSince">Première opération d'un autre produit — avant, tout était à Idara.</param>
    /// <param name="LedgerMatchesBalance">Le registre recopié retombe-t-il sur le solde en direct ?</param>
    public record IdaraReserve(
        long IdaraFcfa,
        long WalletTotalFcfa,
        long OtherProductsFcfa,
        long UnattributedFcfa,
        int UnattributedCount,
        DateTime? SharedSince,
        bool LedgerMatchesBalance);

    public interface IIdaraReserveService
    {
        /// <summary>
        /// Recopie le registre jusqu'à maintenant, puis calcule la part d'Idara.
        /// Lève si Wave ne répond pas : sans registre à jour, on ne sait PAS ce
        /// qui est à Idara, et le solde entier n'est jamais un repli acceptable.
        /// </summary>
        Task<IdaraReserve> ComputeAsync(CancellationToken ct = default);

        /// <summary>
        /// Identifiants de transaction des lignes d'un AUTRE produit. Une sortie
        /// plateforme enregistrée sur l'une d'elles ne compte pas dans P (§307).
        /// </summary>
        Task<HashSet<string>> ForeignTransactionIdsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// 🔑 <b>Le portefeuille Wave est PARTAGÉ avec Àppal</b> (clés distinctes,
    /// portefeuille commun, §307). Son solde n'est donc pas la réserve d'Idara.
    ///
    /// <para><b>Règle</b> : la part d'Idara est le solde du portefeuille MOINS
    /// tout ce qui n'est pas prouvé être à elle depuis la première opération d'un
    /// autre produit. Avant cette date, le portefeuille n'a servi qu'à Idara —
    /// mesuré : il part de 0 le 2026-09-17 et les 144 700 F rapatriés à la bascule
    /// sont bien à elle.</para>
    ///
    /// <para>Après, une ligne est à Idara seulement si elle se RATTACHE à une
    /// opération qu'Idara connaît, par identifiant et non par la forme de la
    /// référence : session <c>cos-…</c> d'un paiement, <c>pt-…</c> d'un retrait,
    /// sortie plateforme enregistrée — ou une référence <c>idara-…</c>. Une ligne
    /// qui ne se rattache à rien est « non attribuée » : on ne la compte PAS,
    /// parce qu'Idara ne compte que l'argent qu'elle peut prouver être le sien.</para>
    /// </summary>
    public class IdaraReserveService : IIdaraReserveService
    {
        private readonly AppDbContext _db;
        private readonly IWaveClient _wave;
        private readonly WaveLedgerSyncJob _sync;
        private readonly ILogger<IdaraReserveService> _logger;

        public IdaraReserveService(
            AppDbContext db, IWaveClient wave, WaveLedgerSyncJob sync, ILogger<IdaraReserveService> logger)
        {
            _db = db;
            _wave = wave;
            _sync = sync;
            _logger = logger;
        }

        /// <summary>
        /// Référence d'un autre produit : renseignée, ni <c>idara-…</c> ni un
        /// identifiant nu (forme d'Idara avant le 2026-10-12).
        /// </summary>
        public static bool IsForeignReference(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return false;
            var r = reference.Trim();
            if (r.StartsWith(WaveReference.Prefix, StringComparison.OrdinalIgnoreCase)) return false;
            return !r.All(char.IsAsciiDigit);
        }

        public async Task<HashSet<string>> ForeignTransactionIdsAsync(CancellationToken ct = default)
        {
            var lines = await _db.WaveLedgerEntries.AsNoTracking()
                .Where(e => e.ClientReference != null)
                .Select(e => new { e.TransactionId, e.ClientReference })
                .ToListAsync(ct);
            return lines.Where(l => IsForeignReference(l.ClientReference))
                .Select(l => l.TransactionId)
                .ToHashSet();
        }

        public async Task<IdaraReserve> ComputeAsync(CancellationToken ct = default)
        {
            // Le registre d'abord : une opération d'Àppal faite il y a une minute
            // doit déjà être retranchée.
            await _sync.SyncAsync(ct);
            var live = await _wave.GetBalanceFcfaAsync(ct);

            var lines = await _db.WaveLedgerEntries.AsNoTracking()
                .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
                .Select(e => new
                {
                    e.TransactionId, e.Timestamp, e.AmountFcfa, e.BalanceFcfa,
                    e.ClientReference, e.CheckoutSessionId
                })
                .ToListAsync(ct);

            if (lines.Count == 0)
                return new IdaraReserve(live, live, 0, 0, 0, null, live == 0);

            // Solde avant la toute première ligne recopiée, puis contrôle de
            // complétude : le registre doit retomber sur le solde en direct.
            var opening = lines[0].BalanceFcfa - lines[0].AmountFcfa;
            var ledgerTotal = opening + lines.Sum(l => l.AmountFcfa);
            var complete = ledgerTotal == live;

            var sharedSince = lines
                .Where(l => IsForeignReference(l.ClientReference))
                .Select(l => (DateTime?)l.Timestamp)
                .FirstOrDefault();

            if (sharedSince is null)
            {
                // Aucun autre produit n'a jamais touché le portefeuille.
                return new IdaraReserve(live, live, 0, 0, 0, null, complete);
            }

            var after = lines.Where(l => l.Timestamp >= sharedSince).ToList();

            var sessionIds = after.Where(l => l.CheckoutSessionId != null)
                .Select(l => l.CheckoutSessionId!).Distinct().ToList();
            var txIds = after.Select(l => l.TransactionId).Distinct().ToList();

            var idaraSessions = (await _db.Payments.AsNoTracking()
                .Where(p => p.ProviderTransactionId != null && sessionIds.Contains(p.ProviderTransactionId))
                .Select(p => p.ProviderTransactionId!)
                .ToListAsync(ct)).ToHashSet();
            var idaraPayouts = (await _db.Withdrawals.AsNoTracking()
                .Where(w => w.ProviderDisbursementId != null && txIds.Contains(w.ProviderDisbursementId))
                .Select(w => w.ProviderDisbursementId!)
                .ToListAsync(ct)).ToHashSet();
            var idaraOutflows = (await _db.PlatformOutflows.AsNoTracking()
                .Where(o => o.ProviderReference != null && txIds.Contains(o.ProviderReference))
                .Select(o => o.ProviderReference!)
                .ToListAsync(ct)).ToHashSet();

            long other = 0, unattributed = 0;
            var unattributedCount = 0;
            foreach (var l in after)
            {
                // L'autre produit d'abord : sa référence l'emporte même si une
                // sortie avait été enregistrée dessus par erreur (sortie #22).
                if (IsForeignReference(l.ClientReference)) { other += l.AmountFcfa; continue; }

                var isIdara =
                    (l.CheckoutSessionId != null && idaraSessions.Contains(l.CheckoutSessionId))
                    || idaraPayouts.Contains(l.TransactionId)
                    || idaraOutflows.Contains(l.TransactionId)
                    || (l.ClientReference?.Trim().StartsWith(WaveReference.Prefix, StringComparison.OrdinalIgnoreCase) ?? false);
                if (isIdara) continue;

                unattributed += l.AmountFcfa;
                unattributedCount++;
            }

            // Le solde en direct fait foi ; on en retire ce qui n'est pas prouvé
            // être à Idara. Une ligne d'Idara pas encore recopiée reste donc
            // comptée — seule une ligne ÉTRANGÈRE non recopiée pourrait gonfler R,
            // et la recopie vient d'avoir lieu.
            var idara = live - other - unattributed;

            if (!complete)
            {
                _logger.LogWarning(
                    "[reserve] Registre recopié ({Ledger}) ≠ solde en direct ({Live}) — une ligne manque peut-être",
                    ledgerTotal, live);
            }

            return new IdaraReserve(idara, live, other, unattributed, unattributedCount, sharedSince, complete);
        }
    }
}
