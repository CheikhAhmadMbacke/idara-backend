using System.Globalization;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Services.Alerts;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services
{
    /// <summary>
    /// Confronte le registre Wave recopié aux paiements d'Idara, dans les deux
    /// sens. Lancé après chaque recopie horaire (<see cref="WaveLedgerSyncJob"/>).
    ///
    /// <para>🔴 <b>Motif, le 2026-10-21 (paiement 469, §308)</b> : 12 000 F
    /// encaissés chez Wave, « Échoué » chez nous, et personne ne l'a vu avant que
    /// le payeur téléphone. Le webhook et le poll sont deux chemins vers la
    /// MÊME règle : quand la règle est fausse, les deux se trompent ensemble. Le
    /// registre, lui, ne dépend d'aucune règle — c'est l'argent qui a bougé.</para>
    ///
    /// <para>Trois constats :</para>
    /// <list type="number">
    /// <item><b>Encaissé chez Wave, pas réussi chez nous</b> → RATTRAPÉ seul, par
    /// le même règlement que le webhook (crédit, facture, reçu, notifications),
    /// puis signalé. Le registre fait foi : l'argent est entré.</item>
    /// <item><b>Encaissé avec une référence Idara, sans paiement</b> → signalé :
    /// on ne sait pas à quoi l'affecter.</item>
    /// <item><b>Réussi chez nous, absent du registre</b> → signalé : un crédit
    /// sans argent derrière. Seuls les paiements portant une session Wave sont
    /// regardés — un encaissement en espèces n'y passe jamais.</item>
    /// </list>
    ///
    /// <para>Un constat ne sonne qu'UNE fois (clé de regroupement par session ou
    /// par paiement, cherchée sans limite de date) : relancé toutes les heures,
    /// le contrôle ne doit pas réveiller Cheikh chaque heure pour le même fait.</para>
    /// </summary>
    public class PayinLedgerAuditService
    {
        /// <summary>Laisse le webhook arriver avant de conclure qu'il manque.</summary>
        private static readonly TimeSpan SettleGrace = TimeSpan.FromMinutes(15);

        /// <summary>Un paiement réussi doit être recopié au registre dans ce délai (recopie horaire).</summary>
        private static readonly TimeSpan LedgerGrace = TimeSpan.FromHours(3);

        /// <summary>Profondeur regardée : au-delà, un écart a déjà été signalé.</summary>
        private static readonly TimeSpan Window = TimeSpan.FromDays(30);

        private readonly AppDbContext _db;
        private readonly IPayinSettlementService _settlement;
        private readonly IOpsAlertService _alerts;
        private readonly ILogger<PayinLedgerAuditService> _logger;

        public PayinLedgerAuditService(
            AppDbContext db,
            IPayinSettlementService settlement,
            IOpsAlertService alerts,
            ILogger<PayinLedgerAuditService> logger)
        {
            _db = db;
            _settlement = settlement;
            _alerts = alerts;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var since = now - Window;

            var lines = await _db.WaveLedgerEntries.AsNoTracking()
                .Where(e => e.CheckoutSessionId != null
                            && !e.IsReversal
                            && e.AmountFcfa > 0
                            && e.Timestamp >= since
                            && e.Timestamp <= now - SettleGrace)
                .ToListAsync(ct);
            // Àppal partage le portefeuille (§306) : ses sessions ne sont pas à nous.
            lines = lines.Where(l => !IdaraReserveService.IsForeignReference(l.ClientReference)).ToList();

            var sessionIds = lines.Select(l => l.CheckoutSessionId!).Distinct().ToList();
            var bySession = await _db.Payments.AsNoTracking()
                .Where(p => p.ProviderTransactionId != null && sessionIds.Contains(p.ProviderTransactionId))
                .Select(p => new { p.Id, p.ProviderTransactionId, p.Status })
                .ToDictionaryAsync(p => p.ProviderTransactionId!, ct);

            var repaired = 0;
            foreach (var line in lines)
            {
                int paymentId;
                PaymentStatus status;
                if (bySession.TryGetValue(line.CheckoutSessionId!, out var p))
                {
                    (paymentId, status) = (p.Id, p.Status);
                }
                else if (WaveReference.TryParsePayment(line.ClientReference, out var refId)
                         && await _db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) is { } byRef)
                {
                    (paymentId, status) = (byRef.Id, byRef.Status);
                }
                else
                {
                    await AlertOnceAsync(
                        $"payin-ledger-orphan:{line.CheckoutSessionId}",
                        "Encaissement Wave sans paiement Idara",
                        $"{Fcfa(line.AmountFcfa + line.FeeFcfa)} encaisses par Wave sans paiement correspondant",
                        LineFacts(line, null),
                        "Wave a encaissé cette somme mais aucun paiement d'Idara ne lui correspond. "
                        + "Retrouver le payeur (numéro ci-dessus) pour savoir à quoi l'affecter.",
                        null, null, ct);
                    continue;
                }

                if (status == PaymentStatus.Completed) continue;

                var gross = line.AmountFcfa + line.FeeFcfa;
                var result = await _settlement.SettleAsync(
                    paymentId, PaymentStatus.Completed, line.FeeFcfa, line.AmountFcfa,
                    line.CheckoutSessionId, line.Timestamp, null, "ledger-audit", ct);
                if (result.Outcome != PayinSettlementOutcome.Transitioned) continue;

                await _settlement.RunPostCompletionEffectsAsync(paymentId, "ledger-audit", ct);
                repaired++;

                var schoolId = await _db.Payments.AsNoTracking()
                    .Where(x => x.Id == paymentId).Select(x => (int?)x.SchoolId).FirstOrDefaultAsync(ct);
                await AlertOnceAsync(
                    $"payin-ledger-repaired:{paymentId}",
                    $"Paiement {paymentId} rattrapé : encaissé chez Wave, {StatusLabel(status)} chez Idara",
                    $"paiement {paymentId} ({Fcfa(gross)}) rattrape - encaisse chez Wave mais {StatusLabel(status)}",
                    LineFacts(line, paymentId).Append(new AlertFact("Statut avant rattrapage", StatusLabel(status))).ToList(),
                    "Rien à faire pour l'argent : le paiement est maintenant réussi et son effet appliqué "
                    + "(facture, abonnement ou solde). Mais un webhook ou une vérification s'est trompé : "
                    + "chercher pourquoi dans le journal, autour de l'heure indiquée.",
                    schoolId, paymentId, ct);
            }

            // ---- Remboursement fait dans Wave, pas encore enregistré (§308) ----
            // Wave ne rembourse pas par l'API : le geste se fait dans son
            // application, puis s'ENREGISTRE dans Idara. Sans cela, le solde de
            // l'école garde un argent rendu, et R = D + P devient faux.
            var reversals = (await _db.WaveLedgerEntries.AsNoTracking()
                    .Where(e => (e.IsReversal || (e.AmountFcfa < 0 && e.CheckoutSessionId != null))
                                && e.Timestamp >= since
                                && e.Timestamp <= now - SettleGrace)
                    .ToListAsync(ct))
                .Where(l => !IdaraReserveService.IsForeignReference(l.ClientReference))
                .ToList();
            var reversalAlerts = 0;
            foreach (var r in reversals)
            {
                var pid = r.CheckoutSessionId != null
                    ? await _db.Payments.Where(x => x.ProviderTransactionId == r.CheckoutSessionId)
                        .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct)
                    : null;
                if (pid == null && WaveReference.TryParsePayment(r.ClientReference, out var refPid)) pid = refPid;
                if (pid == null) continue; // pas un encaissement d'Idara : la réserve le classe

                var refunded = await _db.Payments.AnyAsync(x => x.Id == pid && x.RefundedAt != null, ct);
                if (refunded) continue;

                reversalAlerts++;
                await AlertOnceAsync(
                    $"payin-ledger-refund:{r.TransactionId}",
                    $"Remboursement Wave du paiement {pid} à enregistrer dans Idara",
                    $"remboursement Wave du paiement {pid} ({Fcfa(Math.Abs(r.AmountFcfa))}) a enregistrer dans Idara",
                    LineFacts(r, pid),
                    "Le remboursement a été fait dans l'application Wave. L'enregistrer : SuperAdmin → Finances → "
                    + "« Rembourser un paiement », avec ce numéro. Sans cela, le solde de l'école et les gains restent faux.",
                    null, pid, ct);
            }

            // ---- Sens inverse : réussi chez nous, absent du registre ----
            var ledgerFloor = await _db.WaveLedgerEntries.MinAsync(e => (DateTime?)e.Timestamp, ct);
            var ghostFrom = ledgerFloor is DateTime f && f > since ? f : since;
            var known = _db.WaveLedgerEntries.Where(e => e.CheckoutSessionId != null).Select(e => e.CheckoutSessionId);
            var ghosts = await _db.Payments.AsNoTracking()
                .Where(x => x.Status == PaymentStatus.Completed
                            && x.ProviderTransactionId != null
                            && x.ProviderTransactionId.StartsWith("cos-")
                            && x.PaidAt >= ghostFrom
                            && x.PaidAt <= now - LedgerGrace
                            && !known.Contains(x.ProviderTransactionId))
                .Select(x => new { x.Id, x.SchoolId, x.AmountFcfa, x.PaidAt, x.ProviderTransactionId })
                .ToListAsync(ct);
            foreach (var g in ghosts)
            {
                await AlertOnceAsync(
                    $"payin-ledger-ghost:{g.Id}",
                    $"Paiement {g.Id} réussi chez Idara mais absent du registre Wave",
                    $"paiement {g.Id} ({Fcfa(g.AmountFcfa)}) reussi chez nous, introuvable chez Wave",
                    new[]
                    {
                        new AlertFact("Paiement", g.Id.ToString(CultureInfo.InvariantCulture)),
                        new AlertFact("Montant", Fcfa(g.AmountFcfa)),
                        new AlertFact("Session Wave", g.ProviderTransactionId ?? "-"),
                        new AlertFact("Reussi le", $"{g.PaidAt:yyyy-MM-dd HH:mm} UTC"),
                    },
                    "Idara a crédité un paiement dont Wave n'a aucune trace : le solde de l'école est "
                    + "peut-être gonflé sans argent derrière. Vérifier la session dans le portail Wave.",
                    g.SchoolId, g.Id, ct);
            }

            _logger.LogInformation(
                "[payin-audit] {Lines} encaissement(s) Wave confrontés · {Repaired} rattrapé(s) · {Ghosts} absent(s) du registre · {Refunds} remboursement(s) à enregistrer",
                lines.Count, repaired, ghosts.Count, reversalAlerts);
        }

        private async Task AlertOnceAsync(
            string key, string subject, string smsHeadline, IReadOnlyList<AlertFact> facts,
            string advice, int? schoolId, int? relatedId, CancellationToken ct)
        {
            if (await _db.OpsAlerts.AnyAsync(a => a.GroupingKey == key, ct)) return;
            await _alerts.SendAsync(new OpsAlertRequest(
                OpsAlertKind.PayinLedgerMismatch, key, subject, facts, advice,
                schoolId, relatedId, smsHeadline), ct);
        }

        private static List<AlertFact> LineFacts(Models.WaveLedgerEntry line, int? paymentId) => new()
        {
            new AlertFact("Paiement", paymentId?.ToString(CultureInfo.InvariantCulture) ?? "aucun"),
            new AlertFact("Encaisse (brut)", Fcfa(line.AmountFcfa + line.FeeFcfa)),
            new AlertFact("Session Wave", line.CheckoutSessionId ?? "-"),
            new AlertFact("Transaction Wave", line.TransactionId),
            new AlertFact("Reference", line.ClientReference ?? "-"),
            new AlertFact("Payeur", line.CounterpartyMobile ?? "-"),
            new AlertFact("Date", $"{line.Timestamp:yyyy-MM-dd HH:mm} UTC"),
        };

        private static string StatusLabel(PaymentStatus s) => s switch
        {
            PaymentStatus.Pending => "en attente",
            PaymentStatus.Failed => "echoue",
            PaymentStatus.Cancelled => "annule",
            PaymentStatus.Expired => "expire",
            _ => s.ToString(),
        };

        private static string Fcfa(long amount) =>
            amount.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") + " F";
    }
}
