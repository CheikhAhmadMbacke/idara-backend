using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Anthropic.Models.Messages;
using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.DTOs.Class;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Observability;
using Idara.API.DTOs.Operations;
using Idara.API.DTOs.Student;
using Idara.API.Enums;
using Idara.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Assistant
{
    /// <summary>
    /// 🛟 <b>L'assistant qui remplace le support</b> (2026-10-08, §304).
    ///
    /// <para>Les demandes de support d'une école sont presque toutes des
    /// questions sur SES données : « pourquoi ce parent n'a pas reçu le SMS ? »,
    /// « pourquoi mon retrait est bloqué ? », « pourquoi je suis en lecture
    /// seule ? », « le parent a oublié son code ». Ces outils lisent les VRAIES
    /// données de production, en temps réel, cloisonnées à l'école et au rôle
    /// du jeton (<see cref="AssistantPolicy"/>) — c'est ce qui permet de
    /// répondre au lieu de dire « je ne sais pas ».</para>
    ///
    /// <para>Mêmes règles que le reste de la boîte : lecture immédiate,
    /// écriture par carte confirmée, exécution par les services des écrans.</para>
    /// </summary>
    public partial class AssistantToolbox
    {
        // =====================================================================
        // Définitions (ordre STABLE : préfixe mis en cache)
        // =====================================================================

        private static IEnumerable<ToolUnion> SupportDefinitions() => new List<ToolUnion>
        {
            Def(AssistantPolicy.ReportUnansweredTool,
                "À appeler quand une question porte bien sur Idara ou sur l'école, mais qu'AUCUN outil ni le guide "
                + "ne permet d'y répondre avec certitude. Ne l'appelle qu'APRÈS avoir cherché avec les outils. "
                + "Ensuite, dis honnêtement que tu n'as pas l'information et propose de transmettre au support "
                + "(propose_contact_support). Ne devine jamais.",
                new()
                {
                    ["question"] = S(new { type = "string", description = "La question, reformulée en une phrase." }),
                    ["why"] = S(new { type = "string", description = "Pourquoi les outils ne suffisent pas." }),
                },
                "question"),

            Def("get_student_details",
                "Fiche complète d'UN élève : classe, régime, inscription, sortie, responsables (avec l'état de leur "
                + "compte : actif, dernière connexion, application installée), présences des 30 derniers jours, "
                + "derniers suivis du Coran, et pour la direction ses factures récentes. À appeler pour toute "
                + "question sur un élève précis, après search_students.",
                new() { ["student_id"] = S(new { type = "integer" }) },
                "student_id"),

            Def("get_attendance",
                "Présences d'un jour (par défaut aujourd'hui) : par classe, combien de présents, absents, retards, "
                + "excusés, non pointés, et les noms des absents. Sans class_id : toutes les classes visibles.",
                new()
                {
                    ["date"] = S(new { type = "string", description = "AAAA-MM-JJ" }),
                    ["class_id"] = S(new { type = "integer" }),
                }),

            Def("find_guardian",
                "Retrouve le compte d'un PARENT (responsable) de l'école, par nom ou par numéro : compte actif ou "
                + "non, peut se connecter, dernière connexion, application installée, langue, enfants. Pour "
                + "« ce parent n'arrive pas à se connecter », « a-t-il un compte ? », « quel est son numéro ? ».",
                new() { ["query"] = S(new { type = "string", description = "Nom ou numéro de téléphone." }) },
                "query"),

            Def("get_sms_history",
                "Historique des SMS envoyés par Idara à un parent de l'école (ou aux responsables d'un élève) : "
                + "date, type de message, parti ou non, et la raison quand il n'est pas parti (plafond, numéro "
                + "invalide, refus de l'opérateur…). Pour « pourquoi ce parent n'a pas reçu le SMS ? ».",
                new()
                {
                    ["guardian_id"] = S(new { type = "integer" }),
                    ["student_id"] = S(new { type = "integer" }),
                    ["phone"] = S(new { type = "string" }),
                    ["days"] = S(new { type = "integer", minimum = 1, maximum = 90 }),
                }),

            Def("get_withdrawals",
                "Derniers retraits et transferts de l'école : montant, frais, bénéficiaire, statut (réussi, échoué, "
                + "en vérification) et la raison d'un échec. Pour « où en est mon retrait ? », « pourquoi a-t-il "
                + "échoué ? ».",
                new() { ["limit"] = S(new { type = "integer", minimum = 1, maximum = 30 }) }),

            Def("get_subscription_status",
                "Abonnement Idara de l'école : plan, statut (essai, actif, en attente de paiement, lecture seule, "
                + "suspendu), fin d'essai, prochaine échéance, délais de grâce, factures Idara impayées, quota SMS. "
                + "Pour « pourquoi je suis en lecture seule ? », « quand dois-je payer ? ».",
                new()),

            Def("get_finance_summary",
                "Résumé financier de l'école pour un mois (par défaut le mois en cours) : solde disponible du "
                + "portefeuille Idara, solde des dons, paiements en ligne reçus, entrées et sorties de caisse, "
                + "retraits effectués.",
                new()
                {
                    ["year"] = S(new { type = "integer" }),
                    ["month"] = S(new { type = "integer", minimum = 1, maximum = 12 }),
                }),

            Def("propose_reset_access_code",
                "PROPOSE de créer un nouveau code de connexion (6 chiffres) pour un parent, un enseignant ou un "
                + "membre du personnel qui a oublié le sien. L'ancien code cessera de marcher. Par défaut le code "
                + "part par SMS au numéro DU COMPTE. Rien n'est fait avant confirmation.",
                new()
                {
                    ["user_id"] = S(new { type = "integer", description = "guardian_id venant de find_guardian, ou identifiant du compte." }),
                    ["send_sms"] = S(new { type = "boolean" }),
                },
                "user_id"),

            Def("propose_update_student",
                "PROPOSE de modifier un élève : nom, prénom, sexe, classe, régime, mensualité personnalisée "
                + "(0 = revenir au tarif normal). Ne renseigne QUE ce qui change.",
                new()
                {
                    ["student_id"] = S(new { type = "integer" }),
                    ["first_name"] = S(new { type = "string" }),
                    ["last_name"] = S(new { type = "string" }),
                    ["gender"] = S(new { type = "string", @enum = new[] { "male", "female" } }),
                    ["class_id"] = S(new { type = "integer" }),
                    ["boarding_status"] = S(new { type = "string", @enum = new[] { "boarding", "half_boarding", "day" } }),
                    ["monthly_fee_fcfa"] = S(new { type = "integer", minimum = 0 }),
                },
                "student_id"),

            Def("propose_student_exit",
                "PROPOSE d'enregistrer la SORTIE d'un élève (il quitte l'école) à une date, avec un motif. La fiche "
                + "reste consultable et sa dette payable. cancel_unpaid_invoices annule les mensualités impayées "
                + "dont la période commence après la sortie (directeur seulement).",
                new()
                {
                    ["student_id"] = S(new { type = "integer" }),
                    ["exit_date"] = S(new { type = "string", description = "AAAA-MM-JJ ; aujourd'hui par défaut." }),
                    ["reason"] = S(new { type = "string", @enum = new[] { "transfer", "graduated", "dropout", "family", "relocation", "health", "deceased", "expelled", "other" } }),
                    ["reason_detail"] = S(new { type = "string" }),
                    ["cancel_unpaid_invoices"] = S(new { type = "boolean" }),
                },
                "student_id", "reason"),

            Def("propose_create_class",
                "PROPOSE de créer une classe, avec sa mensualité si elle est dite.",
                new()
                {
                    ["name"] = S(new { type = "string" }),
                    ["level"] = S(new { type = "string" }),
                    ["monthly_fee_fcfa"] = S(new { type = "integer", minimum = 0 }),
                },
                "name"),

            Def("propose_record_attendance",
                "PROPOSE le pointage d'UNE classe pour un jour : tous les élèves de la classe sont présents, SAUF "
                + "ceux listés absents, en retard ou excusés (« tous présents sauf Moussa et Awa »). Cherche "
                + "d'abord les élèves (search_students avec class_id).",
                new()
                {
                    ["class_id"] = S(new { type = "integer" }),
                    ["date"] = S(new { type = "string", description = "AAAA-MM-JJ ; aujourd'hui par défaut." }),
                    ["absent_student_ids"] = S(new { type = "array", items = new { type = "integer" } }),
                    ["late_student_ids"] = S(new { type = "array", items = new { type = "integer" } }),
                    ["excused_student_ids"] = S(new { type = "array", items = new { type = "integer" } }),
                },
                "class_id"),

            Def("propose_coran_entry",
                "PROPOSE d'enregistrer le suivi du Coran d'UN élève pour un jour : jusqu'à trois portions — "
                + "new_lesson (nouvelle leçon), recent_revision (révision récente), old_revision (révision "
                + "ancienne) — chacune de sourate:verset à sourate:verset, acquise ou non. Les sourates sont "
                + "NUMÉROTÉES (1 à 114) : convertis un nom de sourate en son numéro. Les portions déjà saisies "
                + "ce jour-là pour d'autres types sont conservées.",
                new()
                {
                    ["student_id"] = S(new { type = "integer" }),
                    ["date"] = S(new { type = "string", description = "AAAA-MM-JJ ; aujourd'hui par défaut." }),
                    ["portions"] = S(new
                    {
                        type = "array",
                        maxItems = 3,
                        items = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["kind"] = new { type = "string", @enum = new[] { "new_lesson", "recent_revision", "old_revision" } },
                                ["from_surah"] = new { type = "integer", minimum = 1, maximum = 114 },
                                ["from_ayah"] = new { type = "integer", minimum = 1, maximum = 286 },
                                ["to_surah"] = new { type = "integer", minimum = 1, maximum = 114 },
                                ["to_ayah"] = new { type = "integer", minimum = 1, maximum = 286 },
                                ["status"] = new { type = "string", @enum = new[] { "acquired", "not_acquired", "not_set" } },
                            },
                            required = new[] { "kind" },
                        },
                    }),
                    ["remarks"] = S(new { type = "string" }),
                },
                "student_id", "portions"),

            Def("propose_contact_support",
                "PROPOSE de transmettre la demande au support humain d'Idara, avec la discussion jointe : quand tu "
                + "n'as pas pu résoudre le problème, quand il faut une intervention d'Idara (bug, compte bloqué, "
                + "argent), ou quand l'utilisateur le demande. Le support recontacte l'utilisateur.",
                new() { ["summary"] = S(new { type = "string", description = "Le problème, en deux ou trois phrases, avec ce que tu as déjà vérifié." }) },
                "summary"),
        };

        // =====================================================================
        // Lecture
        // =====================================================================

        private async Task<ToolOutcome> StudentDetailsAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var id = (int)(Long(input, "student_id") ?? throw new ToolInputException("student_id est requis."));
            var s = await ScopedStudents(c).Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id, x.FirstName, x.LastName, x.StudentNumber, x.Gender, x.DateOfBirth,
                    ClassName = x.Class != null ? x.Class.Name : null,
                    x.BoardingStatus, x.EnrollmentDate, x.ExitDate, x.ExitReason, x.ExitReasonDetail,
                    x.FatherFullName, x.FatherPhone, x.MotherFullName, x.MotherPhone,
                })
                .FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Élève introuvable dans votre périmètre.");

            var guardians = await _db.StudentGuardians
                .Where(g => g.StudentId == id && !g.Guardian.IsDeleted)
                .OrderByDescending(g => g.IsPrimaryGuardian)
                .Select(g => new
                {
                    g.GuardianId, g.Guardian.FullName, g.Guardian.PhoneNumber, g.Relationship, g.IsPrimaryGuardian,
                    g.Guardian.AccountStatus, g.Guardian.CanLogin, g.Guardian.LastLoginAt, g.Guardian.PreferredLanguage,
                    Devices = _db.PushDeviceTokens.Count(t => t.UserId == g.GuardianId),
                })
                .ToListAsync(ct);

            var since = DateTime.UtcNow.Date.AddDays(-30);
            var attendance = await _db.Attendances
                .Where(a => a.StudentId == id && a.SchoolId == c.SchoolId && !a.IsDeleted && a.Date >= since)
                .Select(a => new { a.Date, a.Status, a.Reason })
                .ToListAsync(ct);

            var coran = await _db.CoranDailyRecords
                .Where(r => r.StudentId == id && r.SchoolId == c.SchoolId)
                .OrderByDescending(r => r.Date).Take(3)
                .Select(r => new
                {
                    r.Date, r.Remarks,
                    Portions = r.Portions.Select(p => new { p.Kind, p.FromSurah, p.FromAyah, p.ToSurah, p.ToAyah, p.Status }).ToList(),
                })
                .ToListAsync(ct);

            object? invoices = null;
            if (c.IsDirection)
            {
                invoices = await _db.Invoices
                    .Where(i => i.SchoolId == c.SchoolId && i.StudentId == id && i.Status != InvoiceStatus.Cancelled)
                    .OrderByDescending(i => i.PeriodStart).Take(6)
                    .Select(i => new
                    {
                        invoice_id = i.Id,
                        type = i.Type == InvoiceType.Registration ? "registration" : "monthly",
                        period = i.PeriodStart.ToString("yyyy-MM"),
                        amount_due_fcfa = i.AmountDueFcfa,
                        paid_fcfa = i.AmountPaidFcfa,
                        status = i.Status.ToString(),
                    })
                    .ToListAsync(ct);
            }

            return Ok(new
            {
                student_id = s.Id,
                name = $"{s.FirstName} {s.LastName}".Trim(),
                number = s.StudentNumber,
                gender = s.Gender?.ToString(),
                birth_date = s.DateOfBirth?.ToString("yyyy-MM-dd"),
                @class = s.ClassName,
                boarding = s.BoardingStatus?.ToString(),
                enrolled_on = s.EnrollmentDate.ToString("yyyy-MM-dd"),
                left_on = s.ExitDate?.ToString("yyyy-MM-dd"),
                exit_reason = s.ExitReason?.ToString(),
                guardians = guardians.Select(g => new
                {
                    guardian_id = g.GuardianId,
                    name = g.FullName,
                    relationship = g.Relationship,
                    primary = g.IsPrimaryGuardian,
                    // 🔒 Les numéros des familles : la direction seulement.
                    phone = c.IsDirection ? g.PhoneNumber : null,
                    account_active = g.AccountStatus == AccountStatus.Active,
                    can_login = g.CanLogin,
                    last_login = g.LastLoginAt?.ToString("yyyy-MM-dd HH:mm"),
                    app_installed = g.Devices > 0,
                    sms_language = g.PreferredLanguage,
                }),
                father = c.IsDirection ? new { name = s.FatherFullName, phone = s.FatherPhone } : null,
                mother = c.IsDirection ? new { name = s.MotherFullName, phone = s.MotherPhone } : null,
                attendance_last_30_days = new
                {
                    present = attendance.Count(a => a.Status == AttendanceStatus.Present),
                    absent = attendance.Count(a => a.Status == AttendanceStatus.Absent),
                    late = attendance.Count(a => a.Status == AttendanceStatus.Late),
                    excused = attendance.Count(a => a.Status == AttendanceStatus.Excused),
                    last_absences = attendance.Where(a => a.Status == AttendanceStatus.Absent)
                        .OrderByDescending(a => a.Date).Take(5).Select(a => a.Date.ToString("yyyy-MM-dd")),
                },
                last_coran_entries = coran.Select(r => new
                {
                    date = r.Date.ToString("yyyy-MM-dd"),
                    remarks = r.Remarks,
                    portions = r.Portions.Select(p => new
                    {
                        kind = p.Kind.ToString(),
                        from = $"{p.FromSurah}:{p.FromAyah}",
                        to = $"{p.ToSurah}:{p.ToAyah}",
                        status = p.Status.ToString(),
                    }),
                }),
                recent_invoices = invoices,
            });
        }

        private async Task<ToolOutcome> AttendanceAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var date = ParseDate(Str(input, "date"), allowFuture: false) ?? DateTime.UtcNow.Date;
            var classId = (int?)Long(input, "class_id");

            var classes = await ScopedClasses(c)
                .Where(k => classId == null || k.Id == classId)
                .OrderBy(k => k.Name)
                .Select(k => new { k.Id, k.Name })
                .ToListAsync(ct);
            if (classId != null && classes.Count == 0)
                throw new ToolInputException("Classe introuvable dans votre périmètre.");

            var ids = classes.Select(k => k.Id).ToList();
            var students = await ScopedStudents(c).Enrolled(date)
                .Where(s => s.ClassId != null && ids.Contains(s.ClassId.Value))
                .Select(s => new { s.Id, s.FirstName, s.LastName, ClassId = s.ClassId!.Value })
                .ToListAsync(ct);
            var sids = students.Select(s => s.Id).ToList();
            var marks = await _db.Attendances
                .Where(a => a.SchoolId == c.SchoolId && !a.IsDeleted && a.Date == date && sids.Contains(a.StudentId))
                .Select(a => new { a.StudentId, a.Status })
                .ToDictionaryAsync(a => a.StudentId, a => a.Status, ct);

            return Ok(new
            {
                date = date.ToString("yyyy-MM-dd"),
                classes = classes.Select(k =>
                {
                    var mine = students.Where(s => s.ClassId == k.Id).ToList();
                    AttendanceStatus? Of(int sid) => marks.TryGetValue(sid, out var st) ? st : null;
                    return new
                    {
                        class_id = k.Id,
                        name = k.Name,
                        students = mine.Count,
                        present = mine.Count(s => Of(s.Id) == AttendanceStatus.Present),
                        absent = mine.Count(s => Of(s.Id) == AttendanceStatus.Absent),
                        late = mine.Count(s => Of(s.Id) == AttendanceStatus.Late),
                        excused = mine.Count(s => Of(s.Id) == AttendanceStatus.Excused),
                        not_recorded = mine.Count(s => Of(s.Id) == null),
                        absent_names = mine.Where(s => Of(s.Id) == AttendanceStatus.Absent)
                            .Select(s => $"{s.FirstName} {s.LastName}".Trim()),
                    };
                }),
            });
        }

        /// <summary>Les responsables rattachés à au moins un élève (même sorti) de l'école.</summary>
        private IQueryable<User> SchoolGuardians(AssistantCaller c) =>
            _db.Users.Where(u => u.Role == UserRoles.Guardian && !u.IsDeleted
                && _db.StudentGuardians.Any(sg => sg.GuardianId == u.Id
                    && sg.Student.SchoolId == c.SchoolId && !sg.Student.IsDeleted));

        private async Task<ToolOutcome> FindGuardianAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var q = Str(input, "query") ?? throw new ToolInputException("query est requis.");
            var query = SchoolGuardians(c);
            var phone = SenegalPhone.Normalize(q);
            query = phone != null
                ? query.Where(u => u.PhoneNumber == phone)
                : query.OuLeNomCorrespond(q);

            var rows = await query.Take(8)
                .Select(u => new
                {
                    u.Id, u.FullName, u.PhoneNumber, u.AccountStatus, u.CanLogin, u.LastLoginAt, u.PreferredLanguage,
                    Devices = _db.PushDeviceTokens.Count(t => t.UserId == u.Id),
                    Children = _db.StudentGuardians
                        .Where(sg => sg.GuardianId == u.Id && sg.Student.SchoolId == c.SchoolId && !sg.Student.IsDeleted)
                        .Select(sg => new
                        {
                            sg.StudentId, sg.Student.FirstName, sg.Student.LastName,
                            ClassName = sg.Student.Class != null ? sg.Student.Class.Name : null,
                            sg.Student.ExitDate,
                        }).ToList(),
                })
                .ToListAsync(ct);

            return Ok(new
            {
                count = rows.Count,
                guardians = rows.Select(u => new
                {
                    guardian_id = u.Id,
                    name = u.FullName,
                    phone = u.PhoneNumber,
                    account_active = u.AccountStatus == AccountStatus.Active,
                    can_login = u.CanLogin,
                    ever_logged_in = u.LastLoginAt != null,
                    last_login = u.LastLoginAt?.ToString("yyyy-MM-dd HH:mm"),
                    app_installed = u.Devices > 0,
                    sms_language = u.PreferredLanguage,
                    children = u.Children.Select(k => new
                    {
                        student_id = k.StudentId,
                        name = $"{k.FirstName} {k.LastName}".Trim(),
                        @class = k.ClassName,
                        left_school = k.ExitDate != null,
                    }),
                }),
                note = rows.Count == 0
                    ? "Aucun responsable de l'école ne correspond. Il n'a peut-être pas de compte : vérifie la fiche de l'élève (get_student_details)."
                    : "Un code oublié se remplace avec propose_reset_access_code (il ne peut pas être relu : il est chiffré).",
            });
        }

        private async Task<ToolOutcome> SmsHistoryAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var days = (int)Math.Clamp(Long(input, "days") ?? 30, 1, 90);
            var userIds = new List<int>();
            var phones = new List<string>();

            if (Long(input, "guardian_id") is long gid)
            {
                var ok = await SchoolGuardians(c).AnyAsync(u => u.Id == gid, ct);
                if (!ok) throw new ToolInputException("Ce responsable n'appartient pas à votre école.");
                userIds.Add((int)gid);
                var p = await _db.Users.Where(u => u.Id == gid).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
                if (p != null) phones.Add(p);
            }
            if (Long(input, "student_id") is long sid)
            {
                var s = await ScopedStudents(c).Where(x => x.Id == sid)
                    .Select(x => new { x.FatherPhone, x.MotherPhone, Ids = x.StudentGuardians.Select(g => g.GuardianId).ToList() })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new ToolInputException("Élève introuvable dans votre périmètre.");
                userIds.AddRange(s.Ids);
                foreach (var raw in new[] { s.FatherPhone, s.MotherPhone })
                    if (SenegalPhone.Normalize(raw) is string n) phones.Add(n);
                phones.AddRange(await _db.Users.Where(u => s.Ids.Contains(u.Id) && u.PhoneNumber != null)
                    .Select(u => u.PhoneNumber!).ToListAsync(ct));
            }
            if (Str(input, "phone") is string rawPhone)
                phones.Add(SenegalPhone.Normalize(rawPhone) ?? throw new ToolInputException("Numéro sénégalais invalide."));
            if (userIds.Count == 0 && phones.Count == 0)
                throw new ToolInputException("Donne guardian_id, student_id ou phone.");

            // 🔒 Seuls les SMS envoyés AU NOM DE CETTE ÉCOLE : un parent qui a
            // des enfants dans deux écoles ne fait pas voir les messages de
            // l'autre.
            var since = DateTime.UtcNow.AddDays(-days);
            phones = phones.Distinct().ToList();
            var logs = await _db.NotificationLogs
                .Where(l => l.SchoolId == c.SchoolId && l.Channel == "Sms" && l.CreatedAt >= since
                    && ((l.UserId != null && userIds.Contains(l.UserId.Value)) || phones.Contains(l.Recipient)))
                .OrderByDescending(l => l.CreatedAt).Take(30)
                .Select(l => new { l.CreatedAt, l.TemplateCode, l.Recipient, l.Success, l.BlockedReason, l.Error, l.Segments })
                .ToListAsync(ct);

            return Ok(new
            {
                days,
                count = logs.Count,
                sms = logs.Select(l => new
                {
                    at = l.CreatedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                    type = l.TemplateCode,
                    to = l.Recipient,
                    sent = l.Success,
                    blocked_reason = l.BlockedReason,
                    error = l.Error == null ? null : (l.Error.Length > 160 ? l.Error[..160] : l.Error),
                    segments = l.Segments,
                }),
                note = logs.Count == 0
                    ? "Aucun SMS de l'école vers ce destinataire sur la période. Les notifications push et les liens partagés à la main n'apparaissent pas ici."
                    : "sent=false avec blocked_reason = refusé par un garde-fou d'Idara (plafond, numéro non sénégalais…) ; avec error = refusé par l'opérateur.",
            });
        }

        private async Task<ToolOutcome> WithdrawalsAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var limit = (int)Math.Clamp(Long(input, "limit") ?? 10, 1, 30);
            var rows = await _db.Withdrawals
                .Where(w => w.SchoolId == c.SchoolId && !w.IsPlatform && !w.IsHidden)
                .OrderByDescending(w => w.CreatedAt).Take(limit)
                .Select(w => new
                {
                    w.Id, w.CreatedAt, w.AmountFcfa, w.FeesFcfa, w.WalletDebitedFcfa, w.RecipientName, w.RecipientPhone,
                    w.Status, w.FailureReason, w.CompletedAt, w.FailedAt, w.Motif, w.Category,
                })
                .ToListAsync(ct);
            var wallet = await _db.SchoolWallets.AsNoTracking()
                .Where(x => x.SchoolId == c.SchoolId)
                .Select(x => new { x.AvailableBalance, x.PendingBalance, x.DonationBalanceFcfa })
                .FirstOrDefaultAsync(ct);

            return Ok(new
            {
                wallet_available_fcfa = wallet?.AvailableBalance,
                wallet_reserved_fcfa = wallet?.PendingBalance,
                withdrawals = rows.Select(w => new
                {
                    id = w.Id,
                    at = w.CreatedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                    received_by_recipient_fcfa = w.AmountFcfa,
                    fees_fcfa = w.FeesFcfa,
                    debited_from_wallet_fcfa = w.WalletDebitedFcfa,
                    recipient = w.RecipientName,
                    recipient_phone = w.RecipientPhone,
                    category = w.Category.ToString(),
                    motif = w.Motif,
                    status = w.Status switch
                    {
                        WithdrawalStatus.Completed => "completed",
                        WithdrawalStatus.Failed => "failed",
                        WithdrawalStatus.Cancelled => "cancelled",
                        WithdrawalStatus.UnderVerification => "under_verification",
                        _ => "in_progress",
                    },
                    failure_reason = w.FailureReason,
                    completed_at = w.CompletedAt?.ToString("yyyy-MM-dd HH:mm"),
                    failed_at = w.FailedAt?.ToString("yyyy-MM-dd HH:mm"),
                }),
                note = "under_verification = Wave n'a pas encore confirmé : l'argent reste réservé, rien n'est perdu ; "
                     + "Idara revérifie automatiquement auprès de Wave. failed = l'argent est revenu dans le portefeuille.",
            });
        }

        private async Task<ToolOutcome> SubscriptionStatusAsync(AssistantCaller c, CancellationToken ct)
        {
            var sub = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.SchoolId == c.SchoolId)
                .OrderByDescending(s => s.Id)
                .Select(s => new
                {
                    s.Status, PlanName = s.Plan != null ? s.Plan.Name : null, s.AmountFcfa, s.BillingCycle,
                    s.TrialEndsAt, s.NextBillingAt, s.GracePeriodEndsAt, s.ReadOnlyEndsAt, s.ReprieveUntil,
                    s.NotificationQuota, s.NotificationUsedThisCycle,
                })
                .FirstOrDefaultAsync(ct);
            if (sub == null) return Ok(new { status = "none", note = "Aucun abonnement trouvé pour l'école : transmets au support." });

            var unpaid = await _db.SubscriptionInvoices.AsNoTracking()
                .Where(i => i.SchoolId == c.SchoolId
                    && (i.Status == SubscriptionInvoiceStatus.Pending || i.Status == SubscriptionInvoiceStatus.Failed))
                .OrderBy(i => i.PeriodStart)
                .Select(i => new { i.PeriodStart, i.PeriodEnd, i.AmountFcfa, i.Status })
                .ToListAsync(ct);

            return Ok(new
            {
                plan = sub.PlanName,
                status = sub.Status.ToString(),
                price_fcfa = sub.AmountFcfa,
                billing_cycle = sub.BillingCycle.ToString(),
                trial_ends = sub.TrialEndsAt.ToString("yyyy-MM-dd"),
                next_billing = sub.NextBillingAt.ToString("yyyy-MM-dd"),
                grace_period_ends = sub.GracePeriodEndsAt?.ToString("yyyy-MM-dd"),
                read_only_ends = sub.ReadOnlyEndsAt?.ToString("yyyy-MM-dd"),
                reprieve_until = sub.ReprieveUntil?.ToString("yyyy-MM-dd"),
                sms_quota = sub.NotificationQuota,
                sms_used_this_cycle = sub.NotificationUsedThisCycle,
                unpaid_idara_invoices = unpaid.Select(i => new
                {
                    period = $"{i.PeriodStart:yyyy-MM-dd} → {i.PeriodEnd:yyyy-MM-dd}",
                    amount_fcfa = i.AmountFcfa,
                    status = i.Status.ToString(),
                }),
                note = "ReadOnly = une facture Idara est impayée : tout reste consultable, mais rien ne peut être "
                     + "modifié jusqu'au paiement (guide : subscription). Suspended = compte suspendu : support.",
            });
        }

        private async Task<ToolOutcome> FinanceSummaryAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var year = (int?)Long(input, "year") ?? now.Year;
            var month = (int?)Long(input, "month") ?? now.Month;
            if (month < 1 || month > 12 || year < 2020 || year > 2100)
                throw new ToolInputException("Mois ou année invalide.");
            var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddMonths(1);

            var wallet = await _db.SchoolWallets.AsNoTracking()
                .Where(x => x.SchoolId == c.SchoolId)
                .Select(x => new { x.AvailableBalance, x.PendingBalance, x.DonationBalanceFcfa })
                .FirstOrDefaultAsync(ct);
            var online = await _db.Payments
                .Where(p => p.SchoolId == c.SchoolId && p.Status == PaymentStatus.Completed
                    && p.Purpose == PaymentPurpose.SchoolFee && p.PaidAt >= from && p.PaidAt < to)
                .GroupBy(p => 1)
                .Select(g => new { Count = g.Count(), Credited = g.Sum(p => p.WalletCreditedFcfa) })
                .FirstOrDefaultAsync(ct);
            var cash = await _db.CashLedgerEntries
                .Where(e => e.SchoolId == c.SchoolId && !e.IsDeleted && e.OccurredAt >= from && e.OccurredAt < to)
                .GroupBy(e => e.Type)
                .Select(g => new { Type = g.Key, Total = g.Sum(e => e.AmountFcfa), Count = g.Count() })
                .ToListAsync(ct);
            var withdrawn = await _db.Withdrawals
                .Where(w => w.SchoolId == c.SchoolId && !w.IsPlatform && w.Status == WithdrawalStatus.Completed
                    && w.CompletedAt >= from && w.CompletedAt < to)
                .SumAsync(w => (long?)w.AmountFcfa, ct) ?? 0;

            return Ok(new
            {
                month = $"{year:D4}-{month:D2}",
                wallet_available_now_fcfa = wallet?.AvailableBalance,
                wallet_reserved_now_fcfa = wallet?.PendingBalance,
                donations_balance_now_fcfa = wallet?.DonationBalanceFcfa,
                online_school_fee_payments = online?.Count ?? 0,
                online_credited_to_wallet_fcfa = online?.Credited ?? 0,
                cash_income_fcfa = cash.Where(x => x.Type == CashEntryType.Income).Sum(x => x.Total),
                cash_expense_fcfa = cash.Where(x => x.Type == CashEntryType.Expense).Sum(x => x.Total),
                withdrawn_fcfa = withdrawn,
            });
        }

        // =====================================================================
        // Propositions
        // =====================================================================

        internal sealed class ResetCodePayload
        {
            public int UserId { get; set; }
            public bool SendSms { get; set; }
        }

        internal sealed class UpdateStudentPayload
        {
            public int StudentId { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public Gender? Gender { get; set; }
            public int? ClassId { get; set; }
            public BoardingStatus? Boarding { get; set; }
            public long? MonthlyFeeFcfa { get; set; }
        }

        internal sealed class StudentExitPayload
        {
            public int StudentId { get; set; }
            public DateTime ExitDate { get; set; }
            public StudentExitReason Reason { get; set; }
            public string? ReasonDetail { get; set; }
            public bool CancelUnpaid { get; set; }
        }

        internal sealed class CreateClassPayload
        {
            public string Name { get; set; } = "";
            public string? Level { get; set; }
            public long? MonthlyFeeFcfa { get; set; }
        }

        internal sealed class AttendancePayload
        {
            public int ClassId { get; set; }
            public DateTime Date { get; set; }
            public List<int> Present { get; set; } = new();
            public List<int> Absent { get; set; } = new();
            public List<int> Late { get; set; } = new();
            public List<int> Excused { get; set; } = new();
        }

        internal sealed class CoranPortionPayload
        {
            public CoranPortionKind Kind { get; set; }
            public int? FromSurah { get; set; }
            public int? FromAyah { get; set; }
            public int? ToSurah { get; set; }
            public int? ToAyah { get; set; }
            public CoranPortionStatus Status { get; set; }
        }

        internal sealed class CoranEntryPayload
        {
            public int StudentId { get; set; }
            public DateTime Date { get; set; }
            public List<CoranPortionPayload> Portions { get; set; } = new();
            public string? Remarks { get; set; }
        }

        internal sealed class ContactSupportPayload
        {
            public string Summary { get; set; } = "";
        }

        private async Task<ToolOutcome> ProposeResetCodeAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var uid = (int)(Long(input, "user_id") ?? throw new ToolInputException("user_id est requis."));
            var target = await _db.Users.Where(u => u.Id == uid && !u.IsDeleted)
                .Select(u => new { u.Id, u.FullName, u.PhoneNumber, u.Role, u.SchoolId, u.CanLogin })
                .FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Compte introuvable.");
            if (target.Role is not (UserRoles.Guardian or UserRoles.Teacher or UserRoles.SchoolStaff or UserRoles.Surveillant))
                throw new ToolInputException("Seuls les comptes parent, enseignant ou personnel ont un code à 6 chiffres. Le directeur change son mot de passe depuis « Moi ».");
            var belongs = target.Role == UserRoles.Guardian
                ? await SchoolGuardians(c).AnyAsync(u => u.Id == uid, ct)
                : target.SchoolId == c.SchoolId;
            if (!belongs) throw new ToolInputException("Ce compte n'appartient pas à votre école.");
            if (string.IsNullOrWhiteSpace(target.PhoneNumber))
                throw new ToolInputException("Ce compte n'a pas de numéro : aucun code à créer.");

            var p = new ResetCodePayload { UserId = uid, SendSms = Bool(input, "send_sms") ?? true };
            var L = new Lines(c.Lang);
            L.Add("Compte", "الحساب", target.FullName);
            L.Add("Numéro", "الرقم", target.PhoneNumber);
            L.Add("Effet", "الأثر", L.T(
                "Un nouveau code est créé ; l'ancien ne marchera plus et ses téléphones devront se reconnecter.",
                "يُنشأ رمز جديد؛ لن يعمل الرمز القديم وستحتاج هواتفه إلى إعادة تسجيل الدخول."));
            L.Add("Envoi", "الإرسال", p.SendSms
                ? L.T("Par SMS au numéro du compte, et affiché ici", "برسالة إلى رقم الحساب، ويظهر هنا")
                : L.T("Affiché ici seulement", "يظهر هنا فقط"));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.ResetAccessCode,
                L.T("Nouveau code de connexion", "رمز دخول جديد"), p, L, ct);
            return Proposed(action);
        }

        private async Task<ToolOutcome> ProposeUpdateStudentAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var id = (int)(Long(input, "student_id") ?? throw new ToolInputException("student_id est requis."));
            var s = await ScopedStudents(c).Where(x => x.Id == id)
                .Select(x => new { x.FirstName, x.LastName, x.Gender, x.ClassId, ClassName = x.Class != null ? x.Class.Name : null, x.BoardingStatus })
                .FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Élève introuvable dans l'école.");

            var p = new UpdateStudentPayload
            {
                StudentId = id,
                FirstName = Clean(Str(input, "first_name"), 100),
                LastName = Clean(Str(input, "last_name"), 100),
                Gender = Str(input, "gender") switch { "male" => Gender.Male, "female" => Gender.Female, _ => null },
                Boarding = Str(input, "boarding_status") switch
                {
                    "boarding" => BoardingStatus.Boarding,
                    "half_boarding" => BoardingStatus.HalfBoarding,
                    "day" => BoardingStatus.Day,
                    _ => null,
                },
                MonthlyFeeFcfa = Long(input, "monthly_fee_fcfa"),
            };
            if (p.MonthlyFeeFcfa is < 0 or > 100_000_000) throw new ToolInputException("Mensualité invalide.");

            var L = new Lines(c.Lang);
            L.Add("Élève", "التلميذ", $"{s.FirstName} {s.LastName}".Trim());
            var changes = 0;
            if (p.FirstName != null && p.FirstName != s.FirstName) { L.Add("Prénom", "الاسم", $"{s.FirstName} → {p.FirstName}"); changes++; }
            else p.FirstName = null;
            if (p.LastName != null && p.LastName != s.LastName) { L.Add("Nom", "اللقب", $"{s.LastName} → {p.LastName}"); changes++; }
            else p.LastName = null;
            if (p.Gender != null && p.Gender != s.Gender)
            {
                L.Add("Sexe", "الجنس", p.Gender == Gender.Male ? L.T("Garçon", "ذكر") : L.T("Fille", "أنثى"));
                changes++;
            }
            else p.Gender = null;
            if (Long(input, "class_id") is long cid && cid != s.ClassId)
            {
                var name = await _db.Classes.Where(k => k.Id == cid && k.SchoolId == c.SchoolId && !k.IsDeleted)
                    .Select(k => k.Name).FirstOrDefaultAsync(ct)
                    ?? throw new ToolInputException("Cette classe n'existe pas dans l'école. Appelle list_classes.");
                p.ClassId = (int)cid;
                L.Add("Classe", "القسم", $"{s.ClassName ?? L.T("Sans classe", "بدون قسم")} → {name}");
                changes++;
            }
            if (p.Boarding != null && p.Boarding != s.BoardingStatus)
            {
                L.Add("Régime", "النظام", p.Boarding switch
                {
                    BoardingStatus.Boarding => L.T("Interne", "داخلي"),
                    BoardingStatus.HalfBoarding => L.T("Demi-pensionnaire", "نصف داخلي"),
                    _ => L.T("Externe", "خارجي"),
                });
                changes++;
            }
            else p.Boarding = null;
            if (p.MonthlyFeeFcfa is long fee)
            {
                if (fee == 0) L.Add("Mensualité", "القسط الشهري", L.T("Retour au tarif normal", "العودة إلى السعر العادي"));
                else L.Money("Mensualité (personnalisée)", "القسط الشهري (خاص)", fee);
                L.Add("Factures impayées", "الفواتير غير المدفوعة", L.T("Réalignées sur le nouveau tarif", "تُعدّل حسب السعر الجديد"));
                changes++;
            }
            if (changes == 0) throw new ToolInputException("Rien ne change par rapport à la fiche actuelle.");

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.UpdateStudent,
                L.T("Modifier un élève", "تعديل تلميذ"), p, L, ct);
            return Proposed(action);
        }

        private async Task<ToolOutcome> ProposeStudentExitAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var id = (int)(Long(input, "student_id") ?? throw new ToolInputException("student_id est requis."));
            var s = await ScopedStudents(c).Where(x => x.Id == id)
                .Select(x => new { x.FirstName, x.LastName, x.ExitDate })
                .FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Élève introuvable dans l'école.");
            if (s.ExitDate != null)
                throw new ToolInputException($"Cet élève est déjà sorti (le {s.ExitDate:dd/MM/yyyy}).");

            var reason = Str(input, "reason") switch
            {
                "transfer" => StudentExitReason.Transfer,
                "graduated" => StudentExitReason.Graduated,
                "dropout" => StudentExitReason.Dropout,
                "family" => StudentExitReason.Family,
                "relocation" => StudentExitReason.Relocation,
                "health" => StudentExitReason.Health,
                "deceased" => StudentExitReason.Deceased,
                "expelled" => StudentExitReason.Expelled,
                "other" => StudentExitReason.Other,
                _ => throw new ToolInputException("Motif requis : demande pourquoi l'élève part."),
            };
            var detail = Clean(Str(input, "reason_detail"), 300);
            if (reason == StudentExitReason.Other && detail == null)
                throw new ToolInputException("Motif « autre » : demande la précision.");

            var date = ParseDate(Str(input, "exit_date"), allowFuture: true) ?? DateTime.UtcNow.Date;
            var cancel = Bool(input, "cancel_unpaid_invoices") ?? false;
            // §77 : l'annulation de factures est réservée au directeur.
            if (cancel && c.Role != UserRoles.SchoolAdmin)
                throw new ToolInputException("Seul le directeur peut annuler des mensualités. Propose la sortie sans annulation.");
            if (cancel && date > DateTime.UtcNow.Date)
                throw new ToolInputException("Pour une sortie future, les mensualités suivantes ne seront simplement jamais créées : pas d'annulation.");

            var preview = await _students.GetExitPreviewAsync(id, c.SchoolId, date, ct);

            var p = new StudentExitPayload { StudentId = id, ExitDate = date, Reason = reason, ReasonDetail = detail, CancelUnpaid = cancel };
            var L = new Lines(c.Lang);
            L.Add("Élève", "التلميذ", $"{s.FirstName} {s.LastName}".Trim());
            L.Add("Date de sortie", "تاريخ المغادرة", date.ToString("dd/MM/yyyy"));
            L.Add("Motif", "السبب", detail ?? reason.ToString());
            if (preview != null && preview.UnpaidInvoiceCount > 0)
            {
                L.Money("Reste dû", "المتبقي", preview.UnpaidTotalFcfa);
                L.Add("Mensualités impayées", "الأقساط غير المدفوعة", cancel
                    ? L.T($"{preview.CancellableCount} annulée(s) (après la sortie)", $"تُلغى {preview.CancellableCount} (بعد المغادرة)")
                    : L.T("Restent dues", "تبقى مستحقة"));
            }
            L.Add("Fiche", "الملف", L.T("Reste consultable ; plus de facturation", "يبقى متاحا للاطلاع؛ دون فوترة"));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.StudentExit,
                L.T("Sortie d'un élève", "مغادرة تلميذ"), p, L, ct);
            return Proposed(action);
        }

        private async Task<ToolOutcome> ProposeCreateClassAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var name = Clean(Str(input, "name"), 100) ?? throw new ToolInputException("Le nom de la classe est requis.");
            var dup = await _db.Classes.AnyAsync(k => k.SchoolId == c.SchoolId && !k.IsDeleted
                && k.Name.ToLower() == name.ToLower(), ct);
            if (dup) throw new ToolInputException("Une classe avec ce nom existe déjà.");
            var fee = Long(input, "monthly_fee_fcfa");
            if (fee is < 0 or > 100_000_000) throw new ToolInputException("Mensualité invalide.");

            var p = new CreateClassPayload { Name = name, Level = Clean(Str(input, "level"), 50), MonthlyFeeFcfa = fee is > 0 ? fee : null };
            var L = new Lines(c.Lang);
            L.Add("Classe", "القسم", p.Name);
            if (p.Level != null) L.Add("Niveau", "المستوى", p.Level);
            if (p.MonthlyFeeFcfa is long f) L.Money("Mensualité", "القسط الشهري", f);
            else L.Add("Mensualité", "القسط الشهري", L.T("Tarif général de l'école", "السعر العام للمدرسة"));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.CreateClass,
                L.T("Créer une classe", "إنشاء قسم"), p, L, ct);
            return Proposed(action);
        }

        private async Task<ToolOutcome> ProposeAttendanceAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var classId = (int)(Long(input, "class_id") ?? throw new ToolInputException("class_id est requis."));
            var klass = await ScopedClasses(c).Where(k => k.Id == classId).Select(k => k.Name).FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Classe introuvable dans votre périmètre.");
            var date = ParseDate(Str(input, "date"), allowFuture: false) ?? DateTime.UtcNow.Date;
            if (date < DateTime.UtcNow.Date.AddDays(-31))
                throw new ToolInputException("Le pointage par l'assistant est limité au dernier mois.");

            var students = await ScopedStudents(c).Enrolled(date)
                .Where(s => s.ClassId == classId)
                .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                .Select(s => new { s.Id, Name = (s.FirstName + " " + s.LastName).Trim() })
                .ToListAsync(ct);
            if (students.Count == 0) throw new ToolInputException("Aucun élève inscrit dans cette classe à cette date.");

            var known = students.Select(s => s.Id).ToHashSet();
            List<int> Ids(string key)
            {
                var ids = IntArray(input, key);
                var stray = ids.Where(i => !known.Contains(i)).ToList();
                if (stray.Count > 0)
                    throw new ToolInputException($"Élève(s) {string.Join(", ", stray)} absent(s) de cette classe : vérifie avec search_students et class_id.");
                return ids;
            }
            var p = new AttendancePayload { ClassId = classId, Date = date, Absent = Ids("absent_student_ids") };
            p.Late = Ids("late_student_ids").Except(p.Absent).ToList();
            p.Excused = Ids("excused_student_ids").Except(p.Absent).Except(p.Late).ToList();
            var marked = p.Absent.Concat(p.Late).Concat(p.Excused).ToHashSet();
            p.Present = students.Where(s => !marked.Contains(s.Id)).Select(s => s.Id).ToList();

            string Names(IEnumerable<int> ids) => string.Join(", ", students.Where(s => ids.Contains(s.Id)).Select(s => s.Name));
            var L = new Lines(c.Lang);
            L.Add("Classe", "القسم", klass);
            L.Add("Date", "التاريخ", date.ToString("dd/MM/yyyy"));
            L.Add("Présents", "الحاضرون", p.Present.Count.ToString(CultureInfo.InvariantCulture));
            L.Add("Absents", "الغائبون", p.Absent.Count == 0 ? L.T("Aucun", "لا أحد") : Names(p.Absent));
            if (p.Late.Count > 0) L.Add("En retard", "المتأخرون", Names(p.Late));
            if (p.Excused.Count > 0) L.Add("Excusés", "المعذورون", Names(p.Excused));
            if (p.Absent.Count > 0)
                L.Add("Parents", "الأولياء", L.T("Les parents des absents sont prévenus", "يُبلَّغ أولياء الغائبين"));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.RecordAttendance,
                L.T("Pointage de la classe", "تسجيل حضور القسم"), p, L, ct);
            return Proposed(action);
        }

        private async Task<ToolOutcome> ProposeCoranEntryAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var id = (int)(Long(input, "student_id") ?? throw new ToolInputException("student_id est requis."));
            var s = await ScopedStudents(c).Enrolled().Where(x => x.Id == id)
                .Select(x => new { x.FirstName, x.LastName })
                .FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Élève introuvable dans votre périmètre, ou sorti de l'effectif.");
            var date = ParseDate(Str(input, "date"), allowFuture: false) ?? DateTime.UtcNow.Date;

            if (!input.TryGetValue("portions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new ToolInputException("portions est requis.");
            var portions = new List<CoranPortionPayload>();
            foreach (var e in arr.EnumerateArray().Take(3))
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var d = e.EnumerateObject().ToDictionary(x => x.Name, x => x.Value);
                var kind = Str(d, "kind") switch
                {
                    "new_lesson" => CoranPortionKind.NewLesson,
                    "recent_revision" => CoranPortionKind.RecentRevision,
                    "old_revision" => CoranPortionKind.OldRevision,
                    _ => throw new ToolInputException("kind doit être new_lesson, recent_revision ou old_revision."),
                };
                var portion = new CoranPortionPayload
                {
                    Kind = kind,
                    FromSurah = (int?)Long(d, "from_surah"),
                    FromAyah = (int?)Long(d, "from_ayah"),
                    ToSurah = (int?)Long(d, "to_surah") ?? (int?)Long(d, "from_surah"),
                    ToAyah = (int?)Long(d, "to_ayah"),
                    Status = Str(d, "status") switch
                    {
                        "acquired" => CoranPortionStatus.Acquired,
                        "not_acquired" => CoranPortionStatus.NotAcquired,
                        _ => CoranPortionStatus.NotSet,
                    },
                };
                foreach (var v in new[] { portion.FromSurah, portion.ToSurah })
                    if (v is < 1 or > 114) throw new ToolInputException("Une sourate est numérotée de 1 à 114.");
                foreach (var v in new[] { portion.FromAyah, portion.ToAyah })
                    if (v is < 1 or > 286) throw new ToolInputException("Numéro de verset invalide.");
                if (portion.FromSurah == null) throw new ToolInputException("Chaque portion a besoin au moins de sa sourate de départ.");
                portions.Add(portion);
            }
            if (portions.Count == 0) throw new ToolInputException("Aucune portion.");
            if (portions.Select(x => x.Kind).Distinct().Count() != portions.Count)
                throw new ToolInputException("Chaque type de portion ne peut apparaître qu'une fois.");

            var p = new CoranEntryPayload { StudentId = id, Date = date, Portions = portions, Remarks = Clean(Str(input, "remarks"), 500) };
            var L = new Lines(c.Lang);
            L.Add("Élève", "التلميذ", $"{s.FirstName} {s.LastName}".Trim());
            L.Add("Date", "التاريخ", date.ToString("dd/MM/yyyy"));
            foreach (var x in portions)
            {
                var label = x.Kind switch
                {
                    CoranPortionKind.NewLesson => L.T("Nouvelle leçon", "الدرس الجديد"),
                    CoranPortionKind.RecentRevision => L.T("Révision récente", "المراجعة القريبة"),
                    _ => L.T("Révision ancienne", "المراجعة البعيدة"),
                };
                var range = $"{x.FromSurah}:{x.FromAyah?.ToString(CultureInfo.InvariantCulture) ?? "1"} → {x.ToSurah}:{x.ToAyah?.ToString(CultureInfo.InvariantCulture) ?? "…"}";
                var status = x.Status switch
                {
                    CoranPortionStatus.Acquired => L.T(" · acquis", " · محفوظ"),
                    CoranPortionStatus.NotAcquired => L.T(" · non acquis", " · غير محفوظ"),
                    _ => "",
                };
                L.Items.Add(new(label, L.T("Sourate ", "سورة ") + range + status));
            }
            if (p.Remarks != null) L.Add("Remarque", "ملاحظة", p.Remarks);

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.CoranEntry,
                L.T("Suivi du Coran", "متابعة القرآن"), p, L, ct);
            return Proposed(action);
        }

        private async Task<ToolOutcome> ProposeContactSupportAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var summary = Clean(Str(input, "summary"), 600) ?? throw new ToolInputException("summary est requis.");
            var me = await _db.Users.Where(u => u.Id == c.UserId)
                .Select(u => new { u.FullName, u.PhoneNumber, SchoolPhone = u.School != null ? u.School.PhoneNumber : null })
                .FirstOrDefaultAsync(ct);
            var callback = me?.PhoneNumber ?? me?.SchoolPhone;

            var p = new ContactSupportPayload { Summary = summary };
            var L = new Lines(c.Lang);
            L.Add("Demande", "الطلب", summary);
            L.Add("Jointe", "مرفق", L.T("Cette discussion, pour ne rien avoir à réexpliquer", "هذه المحادثة، حتى لا تعيدوا الشرح"));
            L.Add("Rappel", "الاتصال", callback != null
                ? L.T($"Le support vous recontacte au {callback}", $"يتصل بكم الدعم على {callback}")
                : L.T("Le support vous recontacte", "يتصل بكم الدعم"));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.ContactSupport,
                L.T("Transmettre au support Idara", "إرسال إلى دعم «إدارا»"), p, L, ct);
            return Proposed(action);
        }

        private static ToolOutcome Proposed(AssistantAction action) =>
            new(JsonSerializer.Serialize(new { proposal_id = action.Id, status = "awaiting_user_confirmation" }, Json), false, action);

        // =====================================================================
        // Exécution
        // =====================================================================

        private async Task<(bool, string, int?)> ExecResetCodeAsync(
            AssistantCaller c, AssistantAction a, List<UserCredentialDto> credentials, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<ResetCodePayload>(a.PayloadJson, Json)!;
            var r = await _accessCodes.RegenerateAsync(c.SchoolId, c.UserId, p.UserId, ct);
            if (!r.Ok) return (false, r.Error!, null);
            credentials.Add(r.Credential!);
            if (!p.SendSms)
                return (true, T(c.Lang, "Nouveau code créé : il s'affiche ci-dessous, à transmettre.", "تم إنشاء رمز جديد: يظهر أدناه لإرساله."), p.UserId);
            var sent = await _accessCodes.SendCodeSmsAsync(c.SchoolId, c.UserId, p.UserId, r.Credential!.Code, "assistant:reset-access-code", ct);
            return (true, sent
                ? T(c.Lang, "Nouveau code créé et envoyé par SMS.", "تم إنشاء رمز جديد وإرساله برسالة.")
                : T(c.Lang, "Nouveau code créé, mais le SMS n'est pas parti : transmettez le code affiché ci-dessous.",
                    "تم إنشاء رمز جديد لكن الرسالة لم ترسل: أرسلوا الرمز الظاهر أدناه."), p.UserId);
        }

        private async Task<(bool, string, int?)> ExecUpdateStudentAsync(AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<UpdateStudentPayload>(a.PayloadJson, Json)!;
            // PATCH partiel (§12) : null = ne pas toucher.
            var dto = new StudentUpdateDto
            {
                Id = p.StudentId,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Gender = p.Gender,
                ClassId = p.ClassId,
                BoardingStatus = p.Boarding,
                MonthlyFeeFcfa = p.MonthlyFeeFcfa,
                MonthlyFeeReason = p.MonthlyFeeFcfa is > 0 ? "Saisi via l'assistant" : null,
            };
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(dto, new ValidationContext(dto), errors, true))
                return (false, string.Join(" ", errors.Select(e => e.ErrorMessage)), null);
            var updated = await _students.UpdateStudentAsync(c.SchoolId, c.UserId, dto);
            return updated == null
                ? (false, T(c.Lang, "Élève introuvable.", "التلميذ غير موجود."), null)
                : (true, T(c.Lang, "Fiche de l'élève mise à jour.", "تم تحديث ملف التلميذ."), p.StudentId);
        }

        private async Task<(bool, string, int?)> ExecStudentExitAsync(AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<StudentExitPayload>(a.PayloadJson, Json)!;
            var r = await _students.ExitStudentAsync(p.StudentId, c.SchoolId, c.UserId,
                c.Role == UserRoles.SchoolAdmin,
                new StudentExitRequestDto
                {
                    ExitDate = p.ExitDate, Reason = p.Reason, ReasonDetail = p.ReasonDetail, CancelUnpaidInvoices = p.CancelUnpaid,
                }, ct);
            if (!r.Ok) return (false, r.Error!, null);
            return (true, r.CancelledInvoices > 0
                ? T(c.Lang, $"Sortie enregistrée. {r.CancelledInvoices} mensualité(s) annulée(s).", $"تم تسجيل المغادرة. أُلغي {r.CancelledInvoices} قسط.")
                : T(c.Lang, "Sortie enregistrée.", "تم تسجيل المغادرة."), p.StudentId);
        }

        private async Task<(bool, string, int?)> ExecCreateClassAsync(AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<CreateClassPayload>(a.PayloadJson, Json)!;
            var dto = new CreateClassDto { Name = p.Name, Level = p.Level, MonthlyFeeFcfa = p.MonthlyFeeFcfa };
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(dto, new ValidationContext(dto), errors, true))
                return (false, string.Join(" ", errors.Select(e => e.ErrorMessage)), null);
            var r = await _classCreation.CreateAsync(c.SchoolId, c.UserId, dto, ct);
            return r.Ok
                ? (true, T(c.Lang, $"Classe « {p.Name} » créée.", $"تم إنشاء القسم «{p.Name}»."), r.Class!.Id)
                : (false, r.Error!, null);
        }

        private async Task<(bool, string, int?)> ExecAttendanceAsync(AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<AttendancePayload>(a.PayloadJson, Json)!;
            var entries = p.Present.Select(id => new AttendanceEntryInput(id, AttendanceStatus.Present, null))
                .Concat(p.Absent.Select(id => new AttendanceEntryInput(id, AttendanceStatus.Absent, null)))
                .Concat(p.Late.Select(id => new AttendanceEntryInput(id, AttendanceStatus.Late, null)))
                .Concat(p.Excused.Select(id => new AttendanceEntryInput(id, AttendanceStatus.Excused, null)))
                .ToList();
            // Le périmètre est REVÉRIFIÉ à l'exécution par le service (§150).
            var saved = await _attendance.RecordBulkAsync(c.SchoolId, c.UserId, c.Role, p.Date, entries, ct);
            return (saved > 0,
                T(c.Lang, $"{saved} pointage(s) enregistré(s).", $"تم تسجيل {saved} حضور."), p.ClassId);
        }

        private async Task<(bool, string, int?)> ExecCoranEntryAsync(AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<CoranEntryPayload>(a.PayloadJson, Json)!;
            var date = p.Date.ToUtcDay();

            // Le service REMPLACE toutes les portions du jour : on garde celles
            // d'un autre type déjà saisies, sinon « la révision de Moussa »
            // effacerait sa nouvelle leçon du matin.
            var existing = await _db.CoranDailyRecords
                .Where(r => r.StudentId == p.StudentId && r.SchoolId == c.SchoolId && r.Date == date)
                .Select(r => new { r.Remarks, Portions = r.Portions.ToList() })
                .FirstOrDefaultAsync(ct);
            var kinds = p.Portions.Select(x => x.Kind).ToHashSet();
            var dto = new CoranDailyUpsertDto
            {
                StudentId = p.StudentId,
                Date = date,
                Remarks = p.Remarks ?? existing?.Remarks,
                Portions = p.Portions.Select(x => new CoranDailyPortionInputDto
                {
                    Kind = x.Kind, FromSurah = x.FromSurah, FromAyah = x.FromAyah,
                    ToSurah = x.ToSurah, ToAyah = x.ToAyah, Status = x.Status,
                }).ToList(),
            };
            if (existing != null)
                dto.Portions.AddRange(existing.Portions.Where(x => !kinds.Contains(x.Kind)).Select(x => new CoranDailyPortionInputDto
                {
                    Kind = x.Kind, FromSurah = x.FromSurah, FromAyah = x.FromAyah, FromWordIndex = x.FromWordIndex,
                    FromWordText = x.FromWordText, ToSurah = x.ToSurah, ToAyah = x.ToAyah, ToWordIndex = x.ToWordIndex,
                    ToWordText = x.ToWordText, Status = x.Status,
                }));

            var r = await _coranDaily.UpsertAsync(c.SchoolId, c.UserId, c.Role, dto, ct);
            return r.Ok
                ? (true, T(c.Lang, "Suivi du Coran enregistré.", "تم تسجيل متابعة القرآن."), r.RecordId)
                : (false, r.Error!, null);
        }

        private async Task<(bool, string, int?)> ExecContactSupportAsync(AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<ContactSupportPayload>(a.PayloadJson, Json)!;
            var transcript = await TranscriptAsync(a.TurnId, ct);
            var dto = new ReportIncidentDto
            {
                Kind = (int)IncidentKind.UserReport,
                Platform = "assistant",
                Route = "/assistant",
                LocaleCode = c.Lang,
                Message = Clip("Assistant IA — demande transmise au support : " + p.Summary, 400),
                ExceptionType = "AssistantSupportRequest",
                StackTrace = Clip(transcript, 8000),
                UserComment = Clip(p.Summary, 600),
            };
            var r = await _telemetry.RecordAsync(dto, c.UserId, c.SchoolId, c.Role, ct);
            if (r.Stored && r.IncidentId != null) _incidentAlerts.QueueAlert(r.IncidentId.Value);
            return (true, T(c.Lang,
                $"Votre demande est transmise au support d'Idara (référence {r.Code}). Nous vous recontactons au plus vite.",
                $"تم إرسال طلبكم إلى دعم «إدارا» (المرجع {r.Code}). سنتصل بكم في أقرب وقت."), r.IncidentId);
        }

        /// <summary>La discussion de cette carte, en clair, pour que le support n'ait rien à redemander.</summary>
        private async Task<string> TranscriptAsync(int? turnId, CancellationToken ct)
        {
            if (turnId == null) return "";
            var convId = await _db.AssistantTurns.Where(t => t.Id == turnId).Select(t => t.ConversationId).FirstOrDefaultAsync(ct);
            var turns = await _db.AssistantTurns
                .Where(t => convId != null ? t.ConversationId == convId : t.Id == turnId)
                .OrderBy(t => t.Id).Take(40)
                .Select(t => new { t.CreatedAt, t.Prompt, t.Reply })
                .ToListAsync(ct);
            var sb = new StringBuilder();
            foreach (var t in turns)
            {
                sb.Append('[').Append(t.CreatedAt.ToString("yyyy-MM-dd HH:mm")).Append(" UTC] Utilisateur : ").AppendLine(t.Prompt);
                if (t.Reply != null) sb.Append("Assistant : ").AppendLine(t.Reply);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // =====================================================================
        // Utilitaires
        // =====================================================================

        /// <summary>Les classes de l'école, réduites à celles d'un enseignant (§150).</summary>
        private IQueryable<Class> ScopedClasses(AssistantCaller c)
        {
            var q = _db.Classes.Where(k => k.SchoolId == c.SchoolId && !k.IsDeleted);
            if (c.VisibleClassIds is { } ids) q = q.Where(k => ids.Contains(k.Id));
            return q;
        }

        private static DateTime? ParseDate(string? s, bool allowFuture)
        {
            if (s == null) return null;
            if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
                throw new ToolInputException("Date attendue au format AAAA-MM-JJ.");
            d = DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
            if (!allowFuture && d > DateTime.UtcNow.Date) throw new ToolInputException("Cette date est dans le futur.");
            return d;
        }

        private static List<int> IntArray(IReadOnlyDictionary<string, JsonElement> i, string k) =>
            i.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
                    .Select(e => e.GetInt32()).Distinct().Take(300).ToList()
                : new List<int>();

        private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

        /// <summary>L'outil qu'il faut avoir pour confirmer une carte de ce type.</summary>
        internal static string ToolFor(AssistantActionKind k) => k switch
        {
            AssistantActionKind.AddStudent => "propose_add_student",
            AssistantActionKind.RecordPayment => "propose_record_payment",
            AssistantActionKind.SendReminders => "propose_send_reminders",
            AssistantActionKind.ResetAccessCode => "propose_reset_access_code",
            AssistantActionKind.UpdateStudent => "propose_update_student",
            AssistantActionKind.StudentExit => "propose_student_exit",
            AssistantActionKind.CreateClass => "propose_create_class",
            AssistantActionKind.RecordAttendance => "propose_record_attendance",
            AssistantActionKind.CoranEntry => "propose_coran_entry",
            AssistantActionKind.ContactSupport => "propose_contact_support",
            _ => "",
        };
    }
}
