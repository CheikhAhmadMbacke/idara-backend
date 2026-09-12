using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Alerts;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Notifications
{
    /// <inheritdoc cref="ISmsBudgetGuard"/>
    public class SmsBudgetGuard : ISmsBudgetGuard
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOpsAlertService _alerts;
        private readonly ILogger<SmsBudgetGuard> _logger;

        public SmsBudgetGuard(
            IServiceScopeFactory scopeFactory,
            IOpsAlertService alerts,
            ILogger<SmsBudgetGuard> logger)
        {
            _scopeFactory = scopeFactory;
            _alerts = alerts;
            _logger = logger;
        }

        // Motifs de blocage : littéraux stables, écrits au registre et lus par
        // les écrans. Ne pas les reformuler à la légère — une capture d'écran
        // vieille d'un mois doit rester compréhensible.
        public const string BlockedForeign = "foreign_recipient";
        public const string BlockedKillSwitch = "kill_switch";
        public const string BlockedHardCap = "platform_hard_cap";
        public const string BlockedSoftCap = "platform_soft_cap";
        public const string BlockedSchoolSuspended = "school_suspended";
        public const string BlockedSchoolHourly = "school_hourly_cap";
        public const string BlockedSchoolDaily = "school_daily_cap";
        public const string BlockedSchoolMonthly = "school_monthly_cap";
        public const string BlockedSchoolRatio = "school_repeat_ratio";
        public const string BlockedRecipientDaily = "recipient_daily_cap";
        public const string BlockedRecipientMonthly = "recipient_monthly_cap";

        public async Task<SmsGuardDecision> EvaluateAsync(
            SmsGuardContext ctx, CancellationToken ct = default)
        {
            var seg = SmsSegmentCalculator.Measure(ctx.Message);
            var network = SmsSegmentCalculator.NetworkOf(ctx.RecipientE164);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var p = await db.GetPlatformSettingsAsync(ct);

                var unitPrice = p.SmsUnitPriceCentimes(network);
                var cost = SmsSegmentCalculator.CostCentimes(
                    seg.Segments, network,
                    p.SmsOnNetPriceCentimes, p.SmsOffNetPriceCentimes, p.SmsInternationalPriceCentimes);

                SmsGuardDecision Block(string reason) =>
                    new(false, reason, seg, network, unitPrice, 0);
                SmsGuardDecision Allow() =>
                    new(true, null, seg, network, unitPrice, cost);

                // ============================================================
                // 1. La destination. Vérifiée EN PREMIER et sans condition.
                // Un SMS international coûte 40,35 F contre 3,50 : onze fois le
                // prix, et c'est précisément le carburant de la fraude au SMS
                // pumping. Rien, dans Idara, n'a de raison d'écrire hors du
                // Sénégal — un tel envoi est donc soit un bug, soit une attaque,
                // et dans les deux cas il faut le voir.
                // ============================================================
                if (!SmsSegmentCalculator.IsSenegalMobileE164(ctx.RecipientE164))
                {
                    _alerts.Queue(new OpsAlertRequest(
                        OpsAlertKind.SmsForeignRecipientBlocked,
                        GroupingKey: "sms-foreign",
                        Subject: "Tentative d'envoi SMS hors Senegal bloquee",
                        Facts: new[]
                        {
                            new AlertFact("Numero vise", Mask(ctx.RecipientE164)),
                            new AlertFact("Ecole", ctx.SchoolId?.ToString() ?? "hors ecole"),
                            new AlertFact("Cout evite", FormatFcfa(
                                SmsSegmentCalculator.CostCentimes(seg.Segments, SmsNetwork.International,
                                    p.SmsOnNetPriceCentimes, p.SmsOffNetPriceCentimes,
                                    p.SmsInternationalPriceCentimes))),
                        },
                        Advice: "Aucun envoi d'Idara ne doit sortir du Senegal. Verifier d'ou vient "
                              + "ce numero (fiche eleve, beneficiaire de transfert) et si un compte a ete detourne.",
                        SchoolId: ctx.SchoolId));
                    return Block(BlockedForeign);
                }

                // ============================================================
                // 2. Le coupe-circuit manuel. Avant tout comptage : c'est le
                // geste qu'on fait quand on a vu la facture déraper, il doit
                // agir même si la base de comptage est en peine.
                // ============================================================
                if (p.SmsKillSwitch) return Block(BlockedKillSwitch);

                var now = DateTime.UtcNow;
                var dayStart = now.Date;
                var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var hourAgo = now.AddHours(-1);
                var thirtyDaysAgo = now.AddDays(-30);

                // Seules les lignes RÉELLEMENT parties comptent : une ligne
                // bloquée n'a rien coûté, et la faire peser dans le budget
                // ferait s'auto-alimenter le blocage.
                var billed = db.NotificationLogs.Where(n => n.Channel == "Sms" && n.BlockedReason == null);

                // ============================================================
                // 3. Plafonds PLATEFORME, en FCFA — la seule unité qui parle
                // quand il s'agit de ne pas recevoir une facture d'un million.
                // Deux paliers (décision 2026-09-01) : le palier absolu ferme
                // aussi les codes de connexion, parce qu'une attaque qui vise
                // justement l'OTP ne doit pas trouver de porte laissée ouverte.
                // ============================================================
                var spentDayCentimes = await billed
                    .Where(n => n.CreatedAt >= dayStart).SumAsync(n => (long?)n.CostCentimes, ct) ?? 0;
                var spentMonthCentimes = await billed
                    .Where(n => n.CreatedAt >= monthStart).SumAsync(n => (long?)n.CostCentimes, ct) ?? 0;

                var afterDay = spentDayCentimes + cost;
                var afterMonth = spentMonthCentimes + cost;

                if (afterDay > p.SmsHardDailyCapFcfa * 100 || afterMonth > p.SmsHardMonthlyCapFcfa * 100)
                {
                    await RaiseCapAsync(OpsAlertKind.SmsHardCapReached, "sms-hard-cap",
                        "Envoi de SMS TOTALEMENT suspendu (palier absolu atteint)",
                        spentDayCentimes, spentMonthCentimes, p, db, ct);
                    return Block(BlockedHardCap);
                }

                if (ctx.Priority != SmsPriority.Critical
                    && !ctx.AuthorizedCampaign
                    && (afterDay > p.SmsSoftDailyCapFcfa * 100 || afterMonth > p.SmsSoftMonthlyCapFcfa * 100))
                {
                    await RaiseCapAsync(OpsAlertKind.SmsSoftCapReached, "sms-soft-cap",
                        "Depense SMS : palier d'alerte atteint, rappels suspendus",
                        spentDayCentimes, spentMonthCentimes, p, db, ct);
                    return Block(BlockedSoftCap);
                }

                // ============================================================
                // 4. Plafond par DESTINATAIRE. Il s'applique à TOUTES les
                // priorités, codes de connexion compris : recevoir huit messages
                // en un jour n'arrive jamais normalement (un parent en reçoit
                // environ quatre par MOIS), et c'est la signature d'une boucle
                // qui viserait justement l'endpoint anonyme des codes.
                // Persisté en base, donc insensible aux redéploiements — les
                // compteurs mémoire, eux, repartaient à zéro (§92).
                // ============================================================
                var toRecipient = billed.Where(n => n.Recipient == ctx.RecipientE164);
                if (await toRecipient.CountAsync(n => n.CreatedAt >= dayStart, ct) >= p.SmsMaxPerRecipientPerDay)
                    return Block(BlockedRecipientDaily);
                if (await toRecipient.CountAsync(n => n.CreatedAt >= thirtyDaysAgo, ct) >= p.SmsMaxPerRecipientPerMonth)
                    return Block(BlockedRecipientMonthly);

                // ============================================================
                // 5. Plafonds par ÉCOLE. Calés sur l'EFFECTIF et non sur le plan
                // d'abonnement : les SMS sont inclus dans le produit et ne se
                // vendent pas au détail (décision 2026-09-01), donc le garde-fou
                // doit se régler sur la réalité de l'école, pas sur ce qu'elle
                // paie. Les envois critiques échappent à ces plafonds : on isole
                // une école qui s'emballe, on ne l'enferme pas dehors.
                // ============================================================
                // Une campagne autorisee y echappe : ses plafonds a elle sont le
                // budget confirme par le SuperAdmin et le palier ABSOLU ci-dessus.
                if (ctx.SchoolId is int schoolId
                    && ctx.Priority != SmsPriority.Critical
                    && !ctx.AuthorizedCampaign)
                {
                    var school = await db.Schools.AsNoTracking()
                        .Where(s => s.Id == schoolId)
                        .Select(s => new { s.Name, s.SmsSuspended, s.SmsMonthlyCapOverrideSegments })
                        .FirstOrDefaultAsync(ct);

                    if (school?.SmsSuspended == true) return Block(BlockedSchoolSuspended);

                    var students = await db.Students
                        .Where(s => s.SchoolId == schoolId).Enrolled().CountAsync(ct);

                    var ofSchool = billed.Where(n => n.SchoolId == schoolId);
                    var hour = await ofSchool.Where(n => n.CreatedAt >= hourAgo)
                        .SumAsync(n => (int?)n.Segments, ct) ?? 0;
                    var day = await ofSchool.Where(n => n.CreatedAt >= dayStart)
                        .SumAsync(n => (int?)n.Segments, ct) ?? 0;
                    var month = await ofSchool.Where(n => n.CreatedAt >= monthStart)
                        .SumAsync(n => (int?)n.Segments, ct) ?? 0;

                    var monthlyCap = school?.SmsMonthlyCapOverrideSegments ?? p.SmsSchoolMonthlyCap(students);
                    var dailyCap = p.SmsSchoolDailyCap(students);
                    var hourlyCap = p.SmsSchoolHourlyCap(students);

                    string? capBreached =
                        month + seg.Segments > monthlyCap ? BlockedSchoolMonthly
                        : day + seg.Segments > dailyCap ? BlockedSchoolDaily
                        : hour + seg.Segments > hourlyCap ? BlockedSchoolHourly
                        : null;

                    // --------------------------------------------------------
                    // Le signal de FORME, et c'est le meilleur du dispositif :
                    // un envoi légitime touche des numéros TOUS distincts (un
                    // par famille), une boucle retape le même petit ensemble.
                    // Le ratio « messages ÷ numéros distincts » attrape ça quelle
                    // que soit la taille de l'école et sans aucun réglage de
                    // volume — un plafond, lui, laisse toujours passer l'abus
                    // qui reste sous le seuil.
                    // --------------------------------------------------------
                    if (capBreached == null)
                    {
                        var lastHour = await ofSchool.Where(n => n.CreatedAt >= hourAgo)
                            .Select(n => n.Recipient).ToListAsync(ct);
                        if (lastHour.Count >= p.SmsRatioMinMessages)
                        {
                            var distinct = Math.Max(1, lastHour.Distinct().Count());
                            if (lastHour.Count / (double)distinct > p.SmsMaxMessagesPerDistinctRecipient)
                                capBreached = BlockedSchoolRatio;
                        }
                    }

                    if (capBreached != null)
                    {
                        await RaiseSchoolRunawayAsync(
                            db, schoolId, school?.Name, capBreached,
                            students, hour, day, month, hourlyCap, dailyCap, monthlyCap, hourAgo, ct);
                        return Block(capBreached);
                    }
                }

                return Allow();
            }
            catch (Exception ex)
            {
                // Le garde-fou en panne ne doit pas devenir la panne. Couper
                // toutes les notifications d'un daara parce qu'une requête de
                // comptage a échoué serait un dégât plus fréquent et plus certain
                // que celui qu'on prévient. Le coupe-circuit du fournisseur reste
                // en dernière ligne.
                _logger.LogError(ex,
                    "[sms-guard] Evaluation impossible — envoi AUTORISE par defaut (school={School})",
                    ctx.SchoolId);
                return new SmsGuardDecision(true, null, seg, network, 0, 0);
            }
        }

        /// <summary>Alerte de plafond plateforme, avec la dépense réelle du jour
        /// et du mois — un « plafond atteint » sans le montant n'aide pas à
        /// décider s'il faut relever le seuil ou couper une école.</summary>
        private async Task RaiseCapAsync(
            OpsAlertKind kind, string key, string subject,
            long spentDayCentimes, long spentMonthCentimes,
            PlatformSettings p, AppDbContext db, CancellationToken ct)
        {
            // Les trois écoles qui pèsent le plus dans le mois : c'est là qu'on
            // regarde en premier, et souvent la réponse y est déjà.
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var top = await db.NotificationLogs
                .Where(n => n.Channel == "Sms" && n.BlockedReason == null
                            && n.CreatedAt >= monthStart && n.SchoolId != null)
                .GroupBy(n => new { n.SchoolId, n.SchoolNameSnapshot })
                .Select(g => new { g.Key.SchoolId, g.Key.SchoolNameSnapshot, Cost = g.Sum(x => x.CostCentimes) })
                .OrderByDescending(x => x.Cost)
                .Take(3)
                .ToListAsync(ct);

            var facts = new List<AlertFact>
            {
                new("Depense du jour", FormatFcfa(spentDayCentimes)),
                new("Depense du mois", FormatFcfa(spentMonthCentimes)),
                new("Palier souple", $"{p.SmsSoftDailyCapFcfa} FCFA/jour · {p.SmsSoftMonthlyCapFcfa} FCFA/mois"),
                new("Palier absolu", $"{p.SmsHardDailyCapFcfa} FCFA/jour · {p.SmsHardMonthlyCapFcfa} FCFA/mois"),
            };
            for (var i = 0; i < top.Count; i++)
                facts.Add(new AlertFact($"Ecole n°{i + 1} du mois",
                    $"{top[i].SchoolNameSnapshot ?? $"#{top[i].SchoolId}"} — {FormatFcfa(top[i].Cost)}"));

            _alerts.Queue(new OpsAlertRequest(
                kind, key, subject, facts,
                Advice: kind == OpsAlertKind.SmsHardCapReached
                    ? "Plus AUCUN SMS ne part, codes de connexion compris. Verifier l'ecole en tete de "
                    + "liste, puis relever le palier depuis Reglages plateforme une fois la cause comprise."
                    : "Les rappels et envois en masse sont suspendus ; les codes de connexion passent "
                    + "encore. Verifier l'ecole en tete de liste avant de relever le palier."));
        }

        /// <summary>Alerte d'emballement sur une école. Nomme l'école, le seuil
        /// franchi et surtout les DÉCLENCHEURS : « 400 SMS via
        /// api:auth/credentials-sms » se referme, « beaucoup de SMS » non.</summary>
        private async Task RaiseSchoolRunawayAsync(
            AppDbContext db, int schoolId, string? schoolName, string reason,
            int students, int hour, int day, int month,
            int hourlyCap, int dailyCap, int monthlyCap, DateTime hourAgo, CancellationToken ct)
        {
            var triggers = await db.NotificationLogs
                .Where(n => n.Channel == "Sms" && n.SchoolId == schoolId && n.CreatedAt >= hourAgo)
                .GroupBy(n => new { n.TriggerSource, n.TemplateCode, n.TriggerUserId })
                .Select(g => new
                {
                    g.Key.TriggerSource,
                    g.Key.TemplateCode,
                    g.Key.TriggerUserId,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync(ct);

            var facts = new List<AlertFact>
            {
                new("Ecole", schoolName ?? $"#{schoolId}"),
                new("Motif", ReasonLabel(reason)),
                new("Effectif", $"{students} eleve(s)"),
                new("Derniere heure", $"{hour} / {hourlyCap} segments"),
                new("Aujourd'hui", $"{day} / {dailyCap} segments"),
                new("Ce mois", $"{month} / {monthlyCap} segments"),
            };
            foreach (var t in triggers)
                facts.Add(new AlertFact(
                    "Declencheur",
                    $"{t.Count} × {t.TemplateCode} via {t.TriggerSource ?? "inconnu"}"
                    + (t.TriggerUserId != null ? $" (compte #{t.TriggerUserId})" : "")));

            _alerts.Queue(new OpsAlertRequest(
                OpsAlertKind.SmsSchoolRunaway,
                GroupingKey: $"sms-school-{schoolId}",
                Subject: $"Emballement SMS — {schoolName ?? $"ecole #{schoolId}"}",
                Facts: facts,
                Advice: "Les rappels de cette ecole sont suspendus ; ses codes de connexion passent "
                      + "encore. Regarder le declencheur en tete : s'il vient d'un compte precis, ce "
                      + "compte est probablement detourne. Sinon, relever son plafond depuis sa fiche.",
                SchoolId: schoolId));
        }

        /// <summary>Motif en clair. Publique et pure exprès : c'est ce texte que
        /// lisent l'e-mail et l'écran, il doit se vérifier sans base.</summary>
        public static string ReasonLabel(string reason) => reason switch
        {
            BlockedForeign => "destinataire hors Senegal",
            BlockedKillSwitch => "envoi de SMS coupe manuellement",
            BlockedHardCap => "palier absolu de depense atteint",
            BlockedSoftCap => "palier d'alerte de depense atteint",
            BlockedSchoolSuspended => "ecole suspendue",
            BlockedSchoolHourly => "plafond horaire de l'ecole",
            BlockedSchoolDaily => "plafond journalier de l'ecole",
            BlockedSchoolMonthly => "plafond mensuel de l'ecole",
            BlockedSchoolRatio => "meme numero retape en boucle",
            BlockedRecipientDaily => "trop de messages vers ce numero aujourd'hui",
            BlockedRecipientMonthly => "trop de messages vers ce numero ce mois",
            _ => reason,
        };

        /// <summary>Centimes → « 12 345 FCFA ». Une alerte se lit sur un
        /// téléphone : les centimes n'y apprennent rien.</summary>
        public static string FormatFcfa(long centimes) =>
            Math.Round(centimes / 100.0)
                .ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                .Replace(",", " ") + " FCFA";

        private static string Mask(string phone) =>
            string.IsNullOrEmpty(phone) || phone.Length < 4 ? "***" : phone[..^4] + "****";
    }
}
