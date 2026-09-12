using Idara.API.Common.Utilities;
using Idara.API.Data;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Push;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Notifications
{
    /// <summary>
    /// Orchestration des notifications. Deux familles d'envoi :
    ///   - <see cref="SendSmsAsync"/> : SMS + push en fan-out (parents, invitations…).
    ///   - <see cref="SendPushOnlyAsync"/> / <see cref="SendBroadcastAsync"/> :
    ///     push UNIQUEMENT (notifs école, suivi enfant, broadcast SuperAdmin) —
    ///     le SMS n'y est pas voulu (coût, et c'est de l'info temps-réel gratuite).
    ///
    /// Best-effort total : ne lève JAMAIS (un échec notif ne casse jamais une
    /// transaction métier — §42/§57). Le push n'atteint que les comptes ayant
    /// l'app + permission (sans token = no-op). Toutes les écritures DB passent
    /// par un scope DÉDIÉ (<see cref="IServiceScopeFactory"/>) pour ne pas
    /// interférer avec le change tracker de l'appelant (webhook, cron…).
    /// </summary>
    public class NotificationService : INotificationService
    {
        private const string BaseUrl = "https://idara.sn";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISmsService _sms;
        private readonly IPushService _push;
        private readonly ISmsBudgetGuard _guard;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            IServiceScopeFactory scopeFactory,
            ISmsService sms,
            IPushService push,
            ISmsBudgetGuard guard,
            ILogger<NotificationService> logger)
        {
            _scopeFactory = scopeFactory;
            _sms = sms;
            _push = push;
            _guard = guard;
            _logger = logger;
        }

        /// <summary>
        /// Ce qu'un envoi vaut la peine de coûter, DÉDUIT du code de gabarit.
        ///
        /// <para><b>Déduit ici et pas demandé à l'appelant, volontairement.</b>
        /// Un classement laissé au bon vouloir de douze points d'appel finit par
        /// diverger, et la divergence ne se voit que le jour où un plafond est
        /// atteint — c'est-à-dire au pire moment. La règle est simple : est
        /// critique ce dont l'absence <b>met quelqu'un à la porte de
        /// l'application</b> ; est « masse » ce qu'un automate envoie en lot et
        /// que le tour suivant rattrapera.</para>
        ///
        /// <para>Publique et pure exprès (§133) : un classement qui décide de ce
        /// qu'on coupe doit se vérifier sans base ni fournisseur SMS.</para>
        /// </summary>
        public static SmsPriority PriorityOf(string templateCode) => templateCode switch
        {
            "OTP" => SmsPriority.Critical,
            "CREDENTIALS_SMS" => SmsPriority.Critical,

            "INVOICE_DUE" => SmsPriority.Bulk,
            "INVOICE_DUE_SOON" => SmsPriority.Bulk,
            "INVOICE_OVERDUE" => SmsPriority.Bulk,
            "REGISTRATION_DUE" => SmsPriority.Bulk,
            "REGISTRATION_OVERDUE" => SmsPriority.Bulk,
            "FREE_PAYMENT_DUE" => SmsPriority.Bulk,
            "SUBSCRIPTION_DUE_SOON" => SmsPriority.Bulk,

            _ => SmsPriority.Normal,
        };

        /// <summary>
        /// Vrai si l'envoi a probablement été FACTURÉ malgré son échec.
        ///
        /// <para>Un refus explicite du fournisseur (code 102, 110, 115…) n'a rien
        /// envoyé : ne rien compter. Mais un <b>timeout</b> ou une coupure réseau
        /// ne dit PAS que le message n'est pas parti — il peut être sorti et
        /// figurer sur la facture. On compte alors la dépense : c'est la même
        /// doctrine qu'au décaissement (§78), où un état ambigu se traite comme
        /// une sortie de fonds et jamais comme un échec. Un budget qui sous-estime
        /// est un budget qui ne protège pas.</para>
        /// </summary>
        public static bool WasProbablyBilled(SmsSendResult result) =>
            result.Success
            || result.Error is "Timeout" or "Network error"
            || (result.Error?.StartsWith("HTTP 5", StringComparison.Ordinal) ?? false);

        // ===================== SMS + push (fan-out) =====================

        public async Task<bool> SendSmsAsync(NotificationSmsRequest req, CancellationToken ct = default)
        {
            // ===== RÈGLE D'OR de la langue (décision utilisateur 2026-08-18) =====
            // Appliquée ICI, au point de passage unique de TOUS les SMS, pour
            // qu'aucun appelant présent ou futur ne puisse l'oublier. La langue
            // se détermine depuis le compte du RÉCEPTEUR, jamais depuis le
            // contexte de l'envoyeur (daara) :
            //  (1) destinataire JAMAIS connecté → BILINGUE — sa « préférence »
            //      stockée n'est que l'héritage de la langue de l'admin qui a
            //      créé le compte, pas la sienne ;
            //  (2) sinon → SA langue ACTUELLE, relue FRAÎCHE en base (mise à
            //      jour à chaque connexion depuis l'Accept-Language réel de son
            //      app), et non la valeur passée par l'appelant, qui peut être
            //      périmée (chargée avant une connexion récente).
            var bilingual = req.Bilingual;
            var lang = req.PreferredLanguage;
            var schoolId = req.SchoolId;
            string? schoolName = null;

            if (req.UserId != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var target = await db.Users.AsNoTracking()
                        .Where(u => u.Id == req.UserId.Value)
                        .Select(u => new { u.LastLoginAt, u.PreferredLanguage, u.SchoolId })
                        .FirstOrDefaultAsync(ct);
                    if (target != null)
                    {
                        if (target.LastLoginAt == null) bilingual = true;
                        if (!string.IsNullOrWhiteSpace(target.PreferredLanguage))
                            lang = target.PreferredLanguage;
                        // Attribution de la dépense, déduite au centre : un
                        // appelant qui oublie SchoolId rendrait le coût
                        // inimputable, donc invisible du plafond par école.
                        schoolId ??= target.SchoolId;
                    }

                    // Un responsable n'a PAS de SchoolId (§15) : son école est
                    // celle de ses enfants. Sans ce repli, la totalité des SMS
                    // aux parents — c'est-à-dire l'essentiel de la dépense —
                    // n'aurait été rattachée à aucun daara.
                    schoolId ??= await db.StudentGuardians
                        .Where(sg => sg.GuardianId == req.UserId.Value && !sg.Student.IsDeleted)
                        .OrderBy(sg => sg.StudentId)
                        .Select(sg => (int?)sg.Student.SchoolId)
                        .FirstOrDefaultAsync(ct);

                    if (schoolId != null)
                        schoolName = await db.Schools.AsNoTracking()
                            .Where(s => s.Id == schoolId.Value)
                            .Select(s => s.Name)
                            .FirstOrDefaultAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[notif] Échec relecture du destinataire {UserId} — envoi avec les valeurs fournies",
                        req.UserId);
                }
            }

            // Push d'abord (indépendant du numéro), best-effort isolé — dans la
            // langue ACTUELLE du destinataire, comme le SMS.
            if (req.UserId != null)
            {
                await DispatchPushAsync(
                    req.UserId.Value, lang, req.Message,
                    req.TemplateCode, req.RelatedEntityId, req.PushRoute, url: null, ct);
            }

            var priority = req.Priority ?? PriorityOf(req.TemplateCode);

            try
            {
                var phone = SenegalPhone.Normalize(req.RawPhone);
                if (phone == null)
                {
                    _logger.LogWarning(
                        "[notif] {Template} SMS non envoyé : numéro invalide/absent (userId={UserId})",
                        req.TemplateCode, req.UserId);
                    await WriteLogAsync(new NotificationLog
                    {
                        UserId = req.UserId,
                        Channel = "Sms",
                        Recipient = req.RawPhone ?? string.Empty,
                        TemplateCode = req.TemplateCode,
                        RelatedEntityId = req.RelatedEntityId,
                        Success = false,
                        Error = "invalid_phone",
                        SchoolId = schoolId,
                        SchoolNameSnapshot = schoolName,
                        TriggerSource = req.TriggerSource,
                        TriggerUserId = req.TriggerUserId,
                        Priority = priority,
                    }, ct);
                    return false;
                }

                var text = req.Message.Compose(bilingual, lang);

                // ===== §225 — l'alphabet GSM-7 est une question d'ARGENT =====
                // Un SEUL caractère hors alphabet bascule le message ENTIER en
                // UCS-2 : le segment tombe de 160 à 70 caractères et la facture
                // DOUBLE. Or « ë », « ï », « ó », « ŋ » et « ç » minuscule n'en
                // font pas partie — autrement dit Maïmouna, Aïssatou, Ndoyë et
                // Françoise, qui sont des noms courants ici. Mesuré en prod :
                // 8 élèves sur 186 et 4 comptes sur 145 sont concernés, et ces
                // familles-là payaient 2× sur CHAQUE rappel, depuis toujours.
                // Rien ne le signalait : le SMS s'affiche normalement, seule la
                // facture le dit, un mois plus tard.
                //
                // 🔴 On n'assainit QUE un corps sans arabe, et la garde n'est pas
                // cosmétique : passer Gsm7Text.Sanitize sur de l'arabe VIDERAIT
                // le message. Aucun caractère arabe n'appartient à l'alphabet
                // GSM-7 et aucun ne se décompose en lettre latine — ils seraient
                // tous supprimés. Un corps arabe ou bilingue est de toute façon
                // déjà en UCS-2 : il n'y a rien à y gagner. Le test porte sur le
                // CONTENU et non sur la langue demandée, pour couvrir aussi les
                // corps PreComposed, que Compose rend sans regarder `lang`.
                //
                // Placé ici plutôt que dans les gabarits : les mêmes
                // BilingualMessage servent aussi les notifications PUSH, où
                // l'écran affiche « Maïmouna » sans le moindre surcoût. La
                // substitution est une propriété du CANAL SMS, pas du message.
                text = BodyForSms(text);

                // ===== Garde-fou de dépense — LE point de passage unique =====
                // Placé ici et nulle part ailleurs : c'est par cette méthode que
                // passent TOUS les SMS d'Idara, donc c'est le seul endroit où un
                // plafond ne peut pas être contourné par un appelant futur.
                var verdict = await _guard.EvaluateAsync(
                    new SmsGuardContext(schoolId, phone, text, priority,
                        req.AuthorizedCampaign), ct);

                if (!verdict.Allowed)
                {
                    // La ligne est écrite QUAND MÊME. Un envoi bloqué est
                    // exactement ce qu'on cherchait à voir : l'effacer
                    // reviendrait à masquer l'attaque que le dispositif vient de
                    // détecter. Coût zéro — rien n'est parti.
                    _logger.LogWarning(
                        "[notif] {Template} BLOQUÉ ({Reason}) vers {Phone} (école {School}, {Seg} segment(s))",
                        req.TemplateCode, verdict.BlockedReason, Mask(phone), schoolId, verdict.Segmentation.Segments);
                    await WriteLogAsync(BuildLog(req, phone, schoolId, schoolName, priority, verdict,
                        success: false, billed: false, error: null, providerMessageId: null,
                        blockedReason: verdict.BlockedReason), ct);
                    return false;
                }

                var result = await _sms.SendAsync(phone, text, ct);

                await WriteLogAsync(BuildLog(req, phone, schoolId, schoolName, priority, verdict,
                    success: result.Success, billed: WasProbablyBilled(result),
                    error: result.Error, providerMessageId: result.MessageId,
                    blockedReason: null), ct);
                await WriteGroupedCompanionsAsync(req, phone, schoolId, schoolName, priority,
                    result.Success, ct);
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Exception inattendue sur SMS {Template} (userId={UserId})",
                    req.TemplateCode, req.UserId);
                return false;
            }
        }

        /// <summary>
        /// Trace les entités couvertes par un message GROUPÉ (les factures des
        /// frères et sœurs réunies en un seul envoi).
        ///
        /// <para>Une ligne par entité, sur le canal <c>SmsGrouped</c> et à coût
        /// nul. Le canal distinct n'est pas cosmétique : le garde-fou et le total
        /// facturé ne comptent que <c>Channel == "Sms"</c>, donc ces lignes ne
        /// peuvent pas gonfler la dépense d'un message qui n'est parti qu'une
        /// fois. La déduplication, elle, ne filtre pas sur le canal et les voit —
        /// c'est exactement ce qu'il faut pour qu'une facture déjà rappelée à
        /// l'intérieur d'un groupe ne soit pas re-rappelée demain.</para>
        /// </summary>
        private async Task WriteGroupedCompanionsAsync(
            NotificationSmsRequest req, string phone, int? schoolId, string? schoolName,
            SmsPriority priority, bool success, CancellationToken ct)
        {
            if (req.GroupedEntityIds is not { Count: > 0 }) return;

            foreach (var id in req.GroupedEntityIds.Distinct())
            {
                if (id == req.RelatedEntityId) continue; // déjà porté par la ligne principale
                await WriteLogAsync(new NotificationLog
                {
                    UserId = req.UserId,
                    Channel = "SmsGrouped",
                    Recipient = phone,
                    TemplateCode = req.TemplateCode,
                    RelatedEntityId = id,
                    Success = success,
                    SchoolId = schoolId,
                    SchoolNameSnapshot = schoolName,
                    TriggerSource = req.TriggerSource,
                    TriggerUserId = req.TriggerUserId,
                    Priority = priority,
                    CreatedAt = DateTime.UtcNow,
                }, ct);
            }
        }

        /// <summary>Compose la ligne de registre d'un SMS, chiffrage compris.
        /// Le coût vient du verdict du garde-fou et n'est pas recalculé : le
        /// montant contrôlé et le montant enregistré doivent être le même, sinon
        /// le plafond et la facture racontent deux histoires.</summary>
        private static NotificationLog BuildLog(
            NotificationSmsRequest req, string phone, int? schoolId, string? schoolName,
            SmsPriority priority, SmsGuardDecision verdict,
            bool success, bool billed, string? error, string? providerMessageId,
            string? blockedReason) => new()
            {
                UserId = req.UserId,
                Channel = "Sms",
                Recipient = phone,
                TemplateCode = req.TemplateCode,
                RelatedEntityId = req.RelatedEntityId,
                Success = success,
                ProviderMessageId = providerMessageId,
                Error = error,
                SchoolId = schoolId,
                SchoolNameSnapshot = schoolName,
                TriggerSource = req.TriggerSource,
                TriggerUserId = req.TriggerUserId,
                Priority = priority,
                Encoding = verdict.Segmentation.Encoding,
                CharCount = verdict.Segmentation.CharCount,
                Segments = verdict.Segmentation.Segments,
                SegmentsFixed160 = verdict.Segmentation.SegmentsFixed160,
                Network = verdict.Network,
                UnitPriceCentimes = verdict.UnitPriceCentimes,
                CostCentimes = billed ? verdict.CostCentimes : 0,
                BlockedReason = blockedReason,
                CreatedAt = DateTime.UtcNow,
            };

        private static string Mask(string phone) =>
            string.IsNullOrEmpty(phone) || phone.Length < 4 ? "***" : phone[..^4] + "****";

        /// <summary>
        /// Le corps tel qu'il partira réellement par SMS.
        ///
        /// <para>Publique et pure exprès, pour la même raison que
        /// <see cref="PriorityOf"/> (§133) : ce qui décide du PRIX d'un envoi
        /// doit pouvoir se vérifier sans base de données ni fournisseur SMS.
        /// Une règle de facturation qu'on ne peut éprouver qu'en production
        /// n'est pas éprouvée.</para>
        /// </summary>
        public static string BodyForSms(string body) =>
            string.IsNullOrEmpty(body) || ContainsArabic(body)
                ? body
                : Gsm7Text.Sanitize(body);

        /// <summary>
        /// Le corps contient-il de l'écriture arabe ? Garde-fou du §225 : c'est
        /// ce test qui empêche <see cref="Gsm7Text.Sanitize"/> de VIDER un
        /// message arabe — aucun caractère arabe n'est dans l'alphabet GSM-7 et
        /// aucun ne se décompose en lettre latine, ils seraient donc tous
        /// supprimés un à un.
        /// </summary>
        /// <remarks>
        /// Couvre le bloc arabe (U+0600–U+06FF) et le supplément (U+0750–U+077F),
        /// ainsi que les formes de présentation (U+FB50–U+FDFF, U+FE70–U+FEFF)
        /// que produisent certains claviers et copier-coller.
        /// </remarks>
        private static bool ContainsArabic(string text)
        {
            foreach (var c in text)
            {
                // Points de code écrits en clair plutôt qu'en littéraux arabes :
                // les bornes resteraient invisibles à la relecture, et un éditeur
                // en RTL peut les réordonner à l'affichage. U+FEFF (BOM) est
                // volontairement EXCLU — ce n'est pas de l'arabe, et le prendre
                // pour tel désactiverait la parade sur un fichier à BOM.
                if ((c >= '؀' && c <= 'ۿ')   // arabe
                    || (c >= 'ݐ' && c <= 'ݿ') // supplement
                    || (c >= 'ࢠ' && c <= 'ࣿ') // arabe etendu-A
                    || (c >= 'ﭐ' && c <= '﷿') // formes de presentation A
                    || (c >= 'ﹰ' && c <= 'ﻼ')) // formes de presentation B
                {
                    return true;
                }
            }
            return false;
        }

        // ===================== Push uniquement =====================

        public async Task SendPushOnlyAsync(PushOnlyRequest req, CancellationToken ct = default)
        {
            await DispatchPushAsync(
                req.UserId, req.PreferredLanguage, req.Message,
                req.TemplateCode, req.RelatedEntityId, req.PushRoute, url: null, ct);
        }

        /// <summary>Cœur d'envoi push à un utilisateur (compose une seule langue,
        /// envoie à tous ses appareils, purge les tokens morts, trace). Isolé.</summary>
        private async Task DispatchPushAsync(
            int userId, string lang, BilingualMessage message,
            string templateCode, int? relatedEntityId, string? route, string? url,
            CancellationToken ct)
        {
            if (!_push.IsConfigured) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var tokens = await db.PushDeviceTokens.Where(t => t.UserId == userId).ToListAsync(ct);
                if (tokens.Count == 0) return;

                // Push = une seule langue (gratuit, pas d'UCS-2 à optimiser).
                var body = message.Compose(bilingual: false, preferredLanguage: lang);
                var data = BuildData(templateCode, relatedEntityId, route, url);
                var ok = await PushToTokensAsync(db, tokens, "Idara", body, data, LinkFor(route, url), ct);

                await db.SaveChangesAsync(ct);
                await WritePushLogAsync(userId, $"{tokens.Count} device(s)", templateCode,
                    relatedEntityId, success: ok > 0,
                    error: ok > 0 ? null : "no_delivery", schoolId: null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Exception inattendue sur push {Template} (userId={UserId})",
                    templateCode, userId);
            }
        }

        // ===================== Broadcast =====================

        public async Task<int> SendBroadcastAsync(
            IReadOnlyCollection<int> userIds, BroadcastContent content, CancellationToken ct = default)
        {
            if (!_push.IsConfigured || userIds.Count == 0) return 0;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var tokens = await db.PushDeviceTokens
                    .Where(t => userIds.Contains(t.UserId))
                    .ToListAsync(ct);
                if (tokens.Count == 0) return 0;

                var title = string.IsNullOrWhiteSpace(content.Title) ? "Idara" : content.Title;
                var data = BuildData(content.TemplateCode, relatedEntityId: null, route: null, url: content.Url);
                var ok = await PushToTokensAsync(db, tokens, title, content.Body, data, LinkFor(null, content.Url), ct);

                await db.SaveChangesAsync(ct);
                await WritePushLogAsync(userId: null, $"broadcast {tokens.Count} device(s)",
                    content.TemplateCode, relatedEntityId: null, success: ok > 0,
                    error: null, schoolId: null, ct);

                _logger.LogInformation("[notif] broadcast {Template} : {Ok}/{Total} appareil(s)",
                    content.TemplateCode, ok, tokens.Count);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Exception inattendue sur broadcast {Template}", content.TemplateCode);
                return 0;
            }
        }

        // ===================== Helpers =====================

        /// <summary>Envoie à chaque token, met à jour LastSeenAt sur succès, marque
        /// les tokens morts pour purge (RemoveRange). Renvoie le nb de succès.
        /// Le SaveChanges est laissé à l'appelant (un seul commit).</summary>
        private async Task<int> PushToTokensAsync(
            AppDbContext db, List<PushDeviceToken> tokens, string title, string body,
            IReadOnlyDictionary<string, string> data, string link, CancellationToken ct)
        {
            var ok = 0;
            var stale = new List<PushDeviceToken>();
            foreach (var t in tokens)
            {
                var r = await _push.SendAsync(t.Token, title, body, data, link, ct);
                if (r.Success) { ok++; t.LastSeenAt = DateTime.UtcNow; }
                else if (r.TokenInvalid) stale.Add(t);
            }
            if (stale.Count > 0) db.PushDeviceTokens.RemoveRange(stale);
            return ok;
        }

        private static Dictionary<string, string> BuildData(
            string templateCode, int? relatedEntityId, string? route, string? url)
        {
            var data = new Dictionary<string, string> { ["templateCode"] = templateCode };
            if (relatedEntityId != null) data["relatedEntityId"] = relatedEntityId.Value.ToString();
            if (!string.IsNullOrWhiteSpace(route)) data["route"] = route!;
            if (!string.IsNullOrWhiteSpace(url)) data["url"] = url!;
            return data;
        }

        private static string LinkFor(string? route, string? url) =>
            !string.IsNullOrWhiteSpace(url) ? url!
            : !string.IsNullOrWhiteSpace(route) ? BaseUrl + route
            : BaseUrl;

        public async Task<bool> HasAttemptedAsync(
            string templateCode, int relatedEntityId, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.NotificationLogs.AnyAsync(
                    n => n.TemplateCode == templateCode && n.RelatedEntityId == relatedEntityId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Échec lecture dédup {Template}/{Id}", templateCode, relatedEntityId);
                return false;
            }
        }

        public async Task NotifyGuardiansOfStudentAsync(
            int studentId, BilingualMessage message, string templateCode,
            string? pushRoute, bool oncePerDay, CancellationToken ct = default)
        {
            if (!_push.IsConfigured) return;
            try
            {
                // Plafond 1/élève/jour : si déjà tenté aujourd'hui pour ce template
                // et cet élève, on ne renotifie pas (plusieurs maîtres, re-saisies).
                if (oncePerDay
                    && await HasAttemptedSinceAsync(templateCode, studentId, DateTime.UtcNow.Date, ct))
                    return;

                List<GuardianRef> guardians;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    // Point de coupure CENTRAL des notifs élève : un élève
                    // supprimé (oubli préexistant corrigé le 2026-08-17) ou SORTI
                    // ne déclenche plus rien vers ses parents — absence, journal,
                    // cycle Coran, bulletin, d'un coup. Périmètre Enrolled écrit
                    // en ligne (l'extension porte sur IQueryable<Student>).
                    var today = DateTime.UtcNow.Date;
                    guardians = await db.StudentGuardians
                        .Where(sg => sg.StudentId == studentId
                                     && !sg.Guardian.IsDeleted
                                     && !sg.Student.IsDeleted
                                     && (sg.Student.ExitDate == null || sg.Student.ExitDate > today))
                        .Select(sg => new GuardianRef(sg.GuardianId, sg.Guardian.PreferredLanguage))
                        .ToListAsync(ct);
                }

                foreach (var g in guardians)
                {
                    await DispatchPushAsync(
                        g.Id, g.Lang ?? "fr", message, templateCode,
                        relatedEntityId: studentId, route: pushRoute, url: null, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[notif] Exception inattendue NotifyGuardiansOfStudent {Template} student={Student}",
                    templateCode, studentId);
            }
        }

        private sealed record GuardianRef(int Id, string? Lang);

        public async Task<bool> HasAttemptedSinceAsync(
            string templateCode, int relatedEntityId, DateTime sinceUtc, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.NotificationLogs.AnyAsync(
                    n => n.TemplateCode == templateCode
                         && n.RelatedEntityId == relatedEntityId
                         && n.CreatedAt >= sinceUtc, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Échec lecture dédup-since {Template}/{Id}", templateCode, relatedEntityId);
                return false;
            }
        }

        /// <summary>
        /// Écrit une ligne de registre dans un scope DÉDIÉ. Ne lève jamais : une
        /// trace ratée ne doit pas casser l'envoi qu'elle décrit.
        ///
        /// <para>⚠️ Le registre est désormais aussi le <b>compteur du garde-fou</b>
        /// et la <b>pièce comptable</b> confrontée à la facture Sonatel. Toute
        /// ligne manquante est donc un SMS qui ne sera ni plafonné ni facturé —
        /// d'où le scope dédié, insensible à l'état du <c>DbContext</c> de
        /// l'appelant (webhook, cron), et le <c>catch</c> qui journalise au lieu
        /// d'avaler en silence.</para>
        /// </summary>
        private async Task WriteLogAsync(NotificationLog log, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.NotificationLogs.Add(log);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[notif] Échec écriture NotificationLog {Template}", log.TemplateCode);
            }
        }

        /// <summary>Trace d'un envoi PUSH : gratuit, donc aucun chiffrage ni
        /// segment — les colonnes de coût restent à zéro et n'entrent jamais dans
        /// les compteurs du garde-fou (qui filtre sur <c>Channel == "Sms"</c>).</summary>
        private Task WritePushLogAsync(
            int? userId, string recipient, string templateCode, int? relatedEntityId,
            bool success, string? error, int? schoolId, CancellationToken ct) =>
            WriteLogAsync(new NotificationLog
            {
                UserId = userId,
                Channel = "Push",
                Recipient = recipient,
                TemplateCode = templateCode,
                RelatedEntityId = relatedEntityId,
                Success = success,
                Error = error,
                SchoolId = schoolId,
                Priority = PriorityOf(templateCode),
                CreatedAt = DateTime.UtcNow,
            }, ct);
    }
}
