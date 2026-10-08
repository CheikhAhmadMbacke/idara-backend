using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Common;
using Idara.API.DTOs.Payment;
using Idara.API.DTOs.Student;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Idara.API.Services.Assistant
{
    /// <summary>Qui parle à l'assistant, et dans quelle langue l'application est réglée.</summary>
    public record AssistantCaller(int SchoolId, int UserId, string Role, string Lang);

    /// <summary>Une ligne de carte de confirmation. <c>AmountFcfa</c> renseigné = l'application formate l'argent (§239).</summary>
    public record AssistantCardLine(string Label, string? Value, long? AmountFcfa = null);

    public record AssistantCardDto(
        int Id, string Kind, string Title, List<AssistantCardLine> Lines,
        string Status, DateTime ExpiresAt, string? ResultMessage);

    /// <summary>Résultat d'un outil : le texte rendu au modèle, et l'éventuelle proposition créée.</summary>
    public record ToolOutcome(string Content, bool IsError, AssistantAction? Proposal = null);

    public record AssistantConfirmResult(
        bool Ok, string Message, AssistantCardDto Card, List<UserCredentialDto> Credentials);

    /// <summary>
    /// Les outils de l'assistant, et l'exécution de ce qu'il propose.
    ///
    /// <para>🔴 <b>Deux familles, et jamais de mélange.</b> Les outils de
    /// LECTURE s'exécutent aussitôt. Les outils d'ÉCRITURE ne font que
    /// <b>déposer une proposition</b> (<see cref="AssistantAction"/>) : rien
    /// n'est écrit, aucun SMS ne part, aucun franc ne bouge tant que l'école
    /// n'a pas appuyé sur « Confirmer ». L'exécution passe alors par les
    /// MÊMES services que les écrans (§199) — l'assistant n'a aucun chemin
    /// d'écriture qui lui soit propre.</para>
    ///
    /// <para>Tout est cloisonné à l'école du jeton : l'identifiant d'école ne
    /// vient JAMAIS du modèle.</para>
    /// </summary>
    public class AssistantToolbox
    {
        private static readonly TimeSpan ProposalLifetime = TimeSpan.FromMinutes(30);

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly AppDbContext _db;
        private readonly IStudentService _students;
        private readonly ICashPaymentService _cash;
        private readonly ICashReceiptNotifier _cashNotifier;
        private readonly IPaymentRosterService _roster;
        private readonly IGuardianPaymentService _guardianPayments;
        private readonly IPaymentLinkService _paymentLinks;
        private readonly INotificationService _notif;
        private readonly ILogger<AssistantToolbox> _logger;

        public AssistantToolbox(
            AppDbContext db,
            IStudentService students,
            ICashPaymentService cash,
            ICashReceiptNotifier cashNotifier,
            IPaymentRosterService roster,
            IGuardianPaymentService guardianPayments,
            IPaymentLinkService paymentLinks,
            INotificationService notif,
            ILogger<AssistantToolbox> logger)
        {
            _db = db;
            _students = students;
            _cash = cash;
            _cashNotifier = cashNotifier;
            _roster = roster;
            _guardianPayments = guardianPayments;
            _paymentLinks = paymentLinks;
            _notif = notif;
            _logger = logger;
        }

        // =====================================================================
        // Définitions
        // =====================================================================

        private static JsonElement S(object o) => JsonSerializer.SerializeToElement(o);

        private static Tool Def(string name, string description,
            Dictionary<string, JsonElement> props, params string[] required) => new()
        {
            Name = name,
            Description = description,
            InputSchema = new() { Properties = props, Required = required },
        };

        /// <summary>Le refus : rendu par le SERVEUR, jamais rédigé par l'IA, et jamais facturé.</summary>
        public const string DeclineTool = "decline_request";

        /// <summary>Ordre et contenu STABLES : ils forment le préfixe mis en cache.</summary>
        public static IReadOnlyList<ToolUnion> Definitions { get; } = new List<ToolUnion>
        {
            Def(DeclineTool,
                "À appeler SEUL, sans rien écrire d'autre, quand la demande sort du périmètre d'Idara "
                + "(culture générale, rédaction, conseils personnels, santé, politique, autre logiciel, "
                + "données d'une autre école ou de toute la plateforme…), quand elle est dans une autre "
                + "langue que le français ou l'arabe, ou quand elle est incompréhensible (dictée ratée). "
                + "Le serveur affiche lui-même un message fixe et la demande n'est pas décomptée.",
                new()
                {
                    ["reason"] = S(new { type = "string", @enum = new[] { "off_topic", "unsupported_language", "unintelligible" } }),
                },
                "reason"),

            Def("get_app_guide",
                "Guide pas à pas d'un écran d'Idara, avec les libellés EXACTS affichés dans la langue de "
                + "l'application de l'utilisateur. À appeler dès qu'on demande COMMENT faire quelque chose "
                + "dans l'application. Ne décris jamais un écran de mémoire : cite le guide.",
                new()
                {
                    ["topic"] = S(new { type = "string", @enum = AssistantGuide.Topics.ToArray() }),
                },
                "topic"),

            Def("search_students",
                "Cherche des élèves de l'école par nom, prénom, matricule ou nom d'un responsable, et/ou "
                + "liste les élèves d'une classe (class_id). Accepte l'arabe comme le latin et ignore les "
                + "accents. Rend au plus 10 élèves par nom, 60 par classe, avec leur classe, leur "
                + "responsable et leur reste dû. À appeler AVANT toute action sur un élève.",
                new()
                {
                    ["query"] = S(new { type = "string", description = "Nom, prénom ou matricule. Facultatif si class_id est donné." }),
                    ["class_id"] = S(new { type = "integer", description = "Pour lister une classe (identifiant venant de list_classes)." }),
                }),

            Def("list_classes",
                "Liste les classes de l'école avec leur mensualité courante, et les tarifs par régime "
                + "(interne, demi-pensionnaire, externe) s'ils existent.",
                new()),

            Def("get_payment_roster",
                "Cahier des mensualités d'un mois : qui a payé, qui est en attente, qui est en retard. "
                + "Sans année ni mois, c'est le mois en cours. Rend les compteurs et la liste des élèves "
                + "qui n'ont pas soldé (avec l'identifiant de facture).",
                new()
                {
                    ["year"] = S(new { type = "integer" }),
                    ["month"] = S(new { type = "integer", minimum = 1, maximum = 12 }),
                }),

            Def("get_student_invoices",
                "Factures NON soldées d'un élève (mensualités et inscription) avec le reste dû de chacune. "
                + "Indispensable avant d'enregistrer un paiement : il faut l'identifiant de la facture.",
                new() { ["student_id"] = S(new { type = "integer" }) },
                "student_id"),

            Def("propose_add_student",
                "PROPOSE l'inscription d'un élève. Rien n'est écrit : l'utilisateur verra une carte et "
                + "devra confirmer. Ne renseigne que ce que l'utilisateur a dit ; n'invente aucune valeur. "
                + "Si la classe dite ne correspond à aucune classe de list_classes, demande-lui laquelle.",
                new()
                {
                    ["first_name"] = S(new { type = "string" }),
                    ["last_name"] = S(new { type = "string" }),
                    ["gender"] = S(new { type = "string", @enum = new[] { "male", "female" } }),
                    ["class_id"] = S(new { type = "integer", description = "Identifiant venant de list_classes." }),
                    ["boarding_status"] = S(new { type = "string", @enum = new[] { "boarding", "half_boarding", "day" },
                        description = "interne / demi-pensionnaire / externe, seulement si dit." }),
                    ["registration_fee_fcfa"] = S(new { type = "integer", minimum = 0,
                        description = "Frais d'inscription. 0 = exonéré. Omettre si non dit." }),
                    ["registration_paid_now"] = S(new { type = "boolean",
                        description = "Vrai seulement si l'utilisateur dit que l'inscription est déjà payée (espèces ou Wave sur le numéro du daara)." }),
                    ["monthly_fee_fcfa"] = S(new { type = "integer", minimum = 0,
                        description = "Mensualité annoncée par l'utilisateur, si elle l'a été." }),
                    ["guardian_first_name"] = S(new { type = "string" }),
                    ["guardian_last_name"] = S(new { type = "string" }),
                    ["guardian_phone"] = S(new { type = "string", description = "Numéro sénégalais du responsable." }),
                    ["guardian_relationship"] = S(new { type = "string", description = "père, mère, oncle…" }),
                },
                "first_name", "last_name"),

            Def("propose_record_payment",
                "PROPOSE d'enregistrer un paiement reçu HORS d'Idara (espèces, ou Wave/Orange Money envoyé "
                + "directement sur le numéro du daara) contre une facture. Une fois confirmé, la facture est "
                + "soldée d'autant, l'entrée de caisse est écrite et le reçu part par SMS au responsable. "
                + "Les paiements faits DANS Idara s'enregistrent tout seuls : ne jamais les ressaisir.",
                new()
                {
                    ["invoice_id"] = S(new { type = "integer", description = "Venant de get_student_invoices ou get_payment_roster." }),
                    ["amount_fcfa"] = S(new { type = "integer", minimum = 1 }),
                    ["method"] = S(new { type = "string", @enum = new[] { "cash", "wave", "orange_money", "other" } }),
                    ["paid_on"] = S(new { type = "string", description = "Date AAAA-MM-JJ si ce n'est pas aujourd'hui." }),
                },
                "invoice_id", "amount_fcfa", "method"),

            Def("propose_send_reminders",
                "PROPOSE d'envoyer un SMS de relance, avec le lien de paiement, aux responsables d'élèves "
                + "qui n'ont pas soldé. Un SMS par famille, même pour plusieurs enfants. Rien ne part avant "
                + "confirmation.",
                new()
                {
                    ["student_ids"] = S(new { type = "array", items = new { type = "integer" }, minItems = 1, maxItems = 300 }),
                },
                "student_ids"),
        };

        // =====================================================================
        // Exécution d'un outil
        // =====================================================================

        public async Task<ToolOutcome> RunAsync(
            AssistantCaller c, string name, IReadOnlyDictionary<string, JsonElement> input,
            int? turnId, CancellationToken ct)
        {
            try
            {
                return name switch
                {
                    "get_app_guide" => AppGuide(c, input),
                    "search_students" => await SearchStudentsAsync(c, input, ct),
                    "list_classes" => await ListClassesAsync(c, ct),
                    "get_payment_roster" => await RosterAsync(c, input, ct),
                    "get_student_invoices" => await StudentInvoicesAsync(c, input, ct),
                    "propose_add_student" => await ProposeAddStudentAsync(c, input, turnId, ct),
                    "propose_record_payment" => await ProposeRecordPaymentAsync(c, input, turnId, ct),
                    "propose_send_reminders" => await ProposeRemindersAsync(c, input, turnId, ct),
                    _ => Error($"Outil inconnu : {name}"),
                };
            }
            catch (ToolInputException ex)
            {
                return Error(ex.Message);
            }
        }

        private static ToolOutcome Ok(object payload) => new(JsonSerializer.Serialize(payload, Json), false);
        private static ToolOutcome Error(string message) => new(JsonSerializer.Serialize(new { error = message }, Json), true);

        private static ToolOutcome AppGuide(AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input)
        {
            var topic = Str(input, "topic") ?? throw new ToolInputException("topic est requis.");
            var text = AssistantGuide.For(topic, c.Lang)
                ?? throw new ToolInputException($"Sujet inconnu. Sujets : {string.Join(", ", AssistantGuide.Topics)}.");
            return new ToolOutcome(text, false);
        }

        private async Task<ToolOutcome> SearchStudentsAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var q = Str(input, "query");
            var classId = Long(input, "class_id");
            if (q == null && classId == null)
                throw new ToolInputException("Donne un nom (query) ou une classe (class_id).");

            // 🔑 Règle d'or §267 : même chemin que tous les écrans — accents
            // ignorés, ajami trouvé en latin. Jamais de règle locale.
            var query = ScopedStudents(c).OuLeNomCorrespond(q);
            if (classId is long k) query = query.Where(s => s.ClassId == k && s.ExitDate == null);
            var rows = await query
                .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                .Take(q == null ? 60 : 10)
                .Select(s => new
                {
                    s.Id,
                    s.FirstName,
                    s.LastName,
                    s.StudentNumber,
                    ClassName = s.Class != null ? s.Class.Name : null,
                    s.ExitDate,
                    Guardian = s.StudentGuardians
                        .Where(g => !g.Guardian.IsDeleted)
                        .OrderByDescending(g => g.IsPrimaryGuardian)
                        .Select(g => new { g.Guardian.FullName, g.Guardian.PhoneNumber })
                        .FirstOrDefault(),
                    Due = _db.Invoices
                        .Where(i => i.StudentId == s.Id && i.Status != InvoiceStatus.Cancelled
                                    && i.AmountPaidFcfa < i.AmountDueFcfa)
                        .Sum(i => (long?)(i.AmountDueFcfa - i.AmountPaidFcfa)) ?? 0,
                })
                .ToListAsync(ct);

            return Ok(new
            {
                count = rows.Count,
                students = rows.Select(s => new
                {
                    student_id = s.Id,
                    name = $"{s.FirstName} {s.LastName}".Trim(),
                    number = s.StudentNumber,
                    @class = s.ClassName,
                    left_school = s.ExitDate != null,
                    guardian = s.Guardian?.FullName,
                    guardian_has_phone = !string.IsNullOrWhiteSpace(s.Guardian?.PhoneNumber),
                    outstanding_fcfa = s.Due,
                }),
            });
        }

        private async Task<ToolOutcome> ListClassesAsync(AssistantCaller c, CancellationToken ct)
        {
            var today = DateTime.UtcNow.Date;
            var classes = await _db.Classes
                .Where(k => k.SchoolId == c.SchoolId && !k.IsDeleted)
                .OrderBy(k => k.Name)
                .Select(k => new
                {
                    k.Id,
                    k.Name,
                    k.Level,
                    // L'effectif : sans lui, l'assistant répondait ne pas savoir
                    // combien d'élèves compte une classe (vu à l'essai réel).
                    Students = _db.Students.Count(s => s.ClassId == k.Id && s.SchoolId == c.SchoolId
                                                       && !s.IsDeleted && s.ExitDate == null),
                    Fee = _db.ClassFees
                        .Where(f => f.ClassId == k.Id && f.SchoolId == c.SchoolId && f.EffectiveFrom <= today)
                        .OrderByDescending(f => f.EffectiveFrom).ThenByDescending(f => f.Id)
                        .Select(f => (long?)f.AmountFcfa)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var settings = await _db.SchoolPaymentSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == c.SchoolId, ct);

            return Ok(new
            {
                classes = classes.Select(k => new
                {
                    class_id = k.Id, name = k.Name, level = k.Level, students = k.Students,
                    monthly_fee_fcfa = k.Fee,
                }),
                students_total = await ScopedStudents(c).CountAsync(s => s.ExitDate == null, ct),
                students_without_class = await ScopedStudents(c).CountAsync(s => s.ExitDate == null && s.ClassId == null, ct),
                default_monthly_fee_fcfa = settings?.GeneralMonthlyFeeFcfa,
                default_registration_fee_fcfa = settings?.RegistrationFeeFcfa,
                boarding_fees_fcfa = settings == null ? null : new
                {
                    boarding = settings.BoardingMonthlyFeeFcfa,
                    half_boarding = settings.HalfBoardingMonthlyFeeFcfa,
                    day = settings.DayMonthlyFeeFcfa,
                },
            });
        }

        private async Task<ToolOutcome> RosterAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var year = (int?)Long(input, "year") ?? now.Year;
            var month = (int?)Long(input, "month") ?? now.Month;
            if (month < 1 || month > 12 || year < 2020 || year > 2100)
                throw new ToolInputException("Mois ou année invalide.");

            var r = await _roster.BuildAsync(c.SchoolId, year, month, ct);

            // 🔴 Le MODE de facturation change le sens de la question. En
            // montant libre, l'école n'a AUCUNE mensualité : « qui n'a pas
            // payé » ne se lit pas dans des factures, et conseiller de « créer
            // les mensualités » est faux (vu en production le 2026-10-07 : 40
            // élèves « sans mensualité » dans une école en montant libre).
            var mode = await _db.SchoolPaymentSettings.AsNoTracking()
                .Where(s => s.SchoolId == c.SchoolId)
                .Select(s => (BillingMode?)s.BillingMode).FirstOrDefaultAsync(ct);
            if (mode == BillingMode.FreeAmount)
            {
                var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                var to = from.AddMonths(1);
                var payers = await _db.Payments
                    .Where(p => p.SchoolId == c.SchoolId && p.Status == PaymentStatus.Completed
                                && p.Purpose == PaymentPurpose.SchoolFee && p.StudentId != null
                                && p.PaidAt >= from && p.PaidAt < to)
                    .Select(p => p.StudentId!.Value).Distinct().ToListAsync(ct);
                var active = await ScopedStudents(c).Where(s => s.ExitDate == null)
                    .Select(s => new { s.Id, s.FirstName, s.LastName, ClassName = s.Class != null ? s.Class.Name : null })
                    .ToListAsync(ct);
                return Ok(new
                {
                    year, month,
                    billing_mode = "free_amount",
                    note = "L'école est en MONTANT LIBRE : aucune mensualité n'est générée, chaque famille paie ce "
                         + "qu'elle veut, et un rappel mensuel part automatiquement aux familles. On ne peut donc "
                         + "pas dire qui « doit » : seulement qui a payé quelque chose ce mois-ci. Pour avoir des "
                         + "mensualités et des retards, il faut passer en montant fixe (guide : payment_settings).",
                    students_total = active.Count,
                    paid_something_this_month = payers.Count,
                    paid_nothing_this_month = active.Where(s => !payers.Contains(s.Id)).Take(200)
                        .Select(s => new { student_id = s.Id, name = $"{s.FirstName} {s.LastName}".Trim(), @class = s.ClassName }),
                });
            }
            var unpaid = r.Entries
                .Where(e => e.Status is RosterPaymentStatus.Overdue or RosterPaymentStatus.Pending)
                .Take(200)
                .Select(e => new
                {
                    student_id = e.StudentId,
                    name = $"{e.StudentFirstName} {e.StudentLastName}".Trim(),
                    @class = e.ClassName,
                    status = e.Status == RosterPaymentStatus.Overdue ? "overdue" : "pending",
                    invoice_id = e.InvoiceId,
                    remaining_fcfa = e.AmountDueFcfa - e.AmountPaidFcfa,
                    guardian = e.GuardianFullName,
                    guardian_has_phone = !string.IsNullOrWhiteSpace(e.GuardianPhone),
                });

            return Ok(new
            {
                year, month,
                billing_mode = "fixed_amount",
                paid = r.PaidCount, pending = r.PendingCount, overdue = r.OverdueCount,
                without_invoice = r.NoInvoiceCount,
                deadline = r.DeadlineDate?.ToString("yyyy-MM-dd"),
                unpaid,
            });
        }

        private async Task<ToolOutcome> StudentInvoicesAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var studentId = (int)(Long(input, "student_id") ?? throw new ToolInputException("student_id est requis."));
            var student = await ScopedStudents(c).Where(s => s.Id == studentId)
                .Select(s => new { s.FirstName, s.LastName }).FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Élève introuvable dans cette école.");

            var invoices = await _db.Invoices
                .Where(i => i.SchoolId == c.SchoolId && i.StudentId == studentId
                            && i.Status != InvoiceStatus.Cancelled && i.AmountPaidFcfa < i.AmountDueFcfa)
                .OrderBy(i => i.PeriodStart)
                .Select(i => new
                {
                    invoice_id = i.Id,
                    type = i.Type == InvoiceType.Registration ? "registration" : "monthly",
                    period = i.PeriodStart,
                    due_date = i.DueDate,
                    amount_due_fcfa = i.AmountDueFcfa,
                    already_paid_fcfa = i.AmountPaidFcfa,
                    remaining_fcfa = i.AmountDueFcfa - i.AmountPaidFcfa,
                })
                .ToListAsync(ct);

            return Ok(new
            {
                student = $"{student.FirstName} {student.LastName}".Trim(),
                invoices = invoices.Select(i => new
                {
                    i.invoice_id, i.type,
                    period = i.period.ToString("yyyy-MM"),
                    due_date = i.due_date.ToString("yyyy-MM-dd"),
                    i.amount_due_fcfa, i.already_paid_fcfa, i.remaining_fcfa,
                }),
            });
        }

        // =====================================================================
        // Propositions
        // =====================================================================

        internal sealed class AddStudentPayload
        {
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public Gender? Gender { get; set; }
            public int? ClassId { get; set; }
            public BoardingStatus? Boarding { get; set; }
            public long? RegistrationFeeFcfa { get; set; }
            public bool RegistrationPaidNow { get; set; }
            /// <summary>Tarif personnalisé, posé SEULEMENT s'il diffère du tarif résolu.</summary>
            public long? MonthlyFeeOverrideFcfa { get; set; }
            public string? GuardianFirstName { get; set; }
            public string? GuardianLastName { get; set; }
            public string? GuardianPhoneE164 { get; set; }
            public string? GuardianRelationship { get; set; }
        }

        internal sealed class RecordPaymentPayload
        {
            public int InvoiceId { get; set; }
            public long AmountFcfa { get; set; }
            public string Method { get; set; } = "cash";
            public DateTime? PaidOn { get; set; }
        }

        internal sealed class ReminderRecipient
        {
            public int? GuardianId { get; set; }
            public string PhoneE164 { get; set; } = "";
            public string? GuardianName { get; set; }
            public string StudentNames { get; set; } = "";
            public long AmountFcfa { get; set; }
            public int FirstStudentId { get; set; }
        }

        internal sealed class RemindersPayload
        {
            public List<ReminderRecipient> Recipients { get; set; } = new();
            public List<string> Skipped { get; set; } = new();
        }

        private async Task<ToolOutcome> ProposeAddStudentAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var p = new AddStudentPayload
            {
                FirstName = Clean(Str(input, "first_name"), 100) ?? throw new ToolInputException("Le prénom est requis."),
                LastName = Clean(Str(input, "last_name"), 100) ?? throw new ToolInputException("Le nom est requis."),
                Gender = Str(input, "gender") switch
                {
                    "male" => Enums.Gender.Male, "female" => Enums.Gender.Female, _ => null,
                },
                Boarding = Str(input, "boarding_status") switch
                {
                    "boarding" => BoardingStatus.Boarding,
                    "half_boarding" => BoardingStatus.HalfBoarding,
                    "day" => BoardingStatus.Day,
                    _ => null,
                },
                RegistrationFeeFcfa = Long(input, "registration_fee_fcfa"),
                RegistrationPaidNow = Bool(input, "registration_paid_now") ?? false,
            };
            if (p.RegistrationFeeFcfa is < 0 or > 100_000_000)
                throw new ToolInputException("Frais d'inscription invalides.");

            string? className = null;
            if (Long(input, "class_id") is long cid)
            {
                className = await _db.Classes
                    .Where(k => k.Id == cid && k.SchoolId == c.SchoolId && !k.IsDeleted)
                    .Select(k => k.Name).FirstOrDefaultAsync(ct)
                    ?? throw new ToolInputException("Cette classe n'existe pas dans l'école. Appelle list_classes.");
                p.ClassId = (int)cid;
            }

            // Mensualité : on ne pose un tarif PERSONNALISÉ que si elle diffère de
            // ce que la hiérarchie tarifaire donnerait déjà (§138). Sinon on
            // créerait une exception permanente là où la classe suffisait, et la
            // prochaine hausse du tarif de classe ne toucherait pas cet élève.
            var settings = await _db.SchoolPaymentSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == c.SchoolId, ct);
            long? resolved = null;
            if (settings != null)
            {
                var r = await FeeResolver.ResolveAsync(_db, c.SchoolId, settings,
                    new[] { new FeeTarget(0, p.ClassId, p.Boarding) }, DateTime.UtcNow.Date, ct);
                resolved = r.TryGetValue(0, out var v) ? v : null;
            }
            if (Long(input, "monthly_fee_fcfa") is long monthly)
            {
                if (monthly is <= 0 or > 100_000_000) throw new ToolInputException("Mensualité invalide.");
                if (monthly != resolved) p.MonthlyFeeOverrideFcfa = monthly;
            }

            var gFirst = Clean(Str(input, "guardian_first_name"), 100);
            var gLast = Clean(Str(input, "guardian_last_name"), 100);
            var gPhoneRaw = Str(input, "guardian_phone");
            if (gPhoneRaw != null)
            {
                // 🔑 Règle d'or §246 : un compte reçoit des SMS, donc son numéro
                // est sénégalais.
                p.GuardianPhoneE164 = SenegalPhone.Normalize(gPhoneRaw)
                    ?? throw new ToolInputException(
                        $"« {gPhoneRaw} » n'est pas un numéro mobile sénégalais. Demande le bon numéro.");
                if (gFirst == null && gLast == null)
                    throw new ToolInputException("Il faut le nom du responsable pour lui créer un compte.");
                p.GuardianFirstName = gFirst;
                p.GuardianLastName = gLast;
                p.GuardianRelationship = Clean(Str(input, "guardian_relationship"), 50);
            }

            // Doublon probable : on le DIT sur la carte, on ne bloque pas.
            var homonyms = await ScopedStudents(c)
                .OuLeNomCorrespond($"{p.FirstName} {p.LastName}")
                .CountAsync(ct);

            var L = new Lines(c.Lang);
            L.Add("Élève", "التلميذ", $"{p.FirstName} {p.LastName}");
            if (p.Gender != null) L.Add("Sexe", "الجنس", p.Gender == Enums.Gender.Male
                ? L.T("Garçon", "ذكر") : L.T("Fille", "أنثى"));
            L.Add("Classe", "القسم", className ?? L.T("Sans classe", "بدون قسم"));
            if (p.Boarding != null) L.Add("Régime", "النظام", p.Boarding switch
            {
                BoardingStatus.Boarding => L.T("Interne", "داخلي"),
                BoardingStatus.HalfBoarding => L.T("Demi-pensionnaire", "نصف داخلي"),
                _ => L.T("Externe", "خارجي"),
            });
            var regFee = p.RegistrationFeeFcfa ?? settings?.RegistrationFeeFcfa ?? 0;
            if (regFee > 0)
            {
                L.Money("Inscription", "التسجيل", regFee);
                L.Add("Règlement de l'inscription", "دفع التسجيل", p.RegistrationPaidNow
                    ? L.T("Déjà payée (encaissée en caisse)", "مدفوع (يسجل في الصندوق)")
                    : L.T("À payer par la famille", "على الأسرة دفعه"));
            }
            else L.Add("Inscription", "التسجيل", L.T("Aucuns frais", "بدون رسوم"));
            var monthlyShown = p.MonthlyFeeOverrideFcfa ?? resolved;
            if (monthlyShown is long m) L.Money(
                p.MonthlyFeeOverrideFcfa != null ? "Mensualité (personnalisée)" : "Mensualité",
                p.MonthlyFeeOverrideFcfa != null ? "القسط الشهري (خاص)" : "القسط الشهري", m);
            else L.Add("Mensualité", "القسط الشهري", L.T("Aucun tarif défini", "لا يوجد سعر محدد"));
            if (p.GuardianPhoneE164 != null)
                L.Add("Responsable", "ولي الأمر",
                    $"{$"{p.GuardianFirstName} {p.GuardianLastName}".Trim()} · {p.GuardianPhoneE164}");
            else L.Add("Responsable", "ولي الأمر", L.T("Aucun (à ajouter plus tard)", "لا يوجد (يضاف لاحقا)"));
            if (homonyms > 0)
                L.Add("Attention", "تنبيه", L.T(
                    $"{homonyms} élève(s) portent déjà un nom proche",
                    $"يوجد {homonyms} تلميذ باسم مشابه"));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.AddStudent,
                L.T("Ajouter un élève", "إضافة تلميذ"), p, L, ct);
            return new ToolOutcome(JsonSerializer.Serialize(new
            {
                proposal_id = action.Id,
                status = "awaiting_user_confirmation",
                resolved_monthly_fee_fcfa = monthlyShown,
                registration_fee_fcfa = regFee,
                similar_names_already_enrolled = homonyms,
            }, Json), false, action);
        }

        private async Task<ToolOutcome> ProposeRecordPaymentAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            var invoiceId = (int)(Long(input, "invoice_id") ?? throw new ToolInputException("invoice_id est requis."));
            var amount = Long(input, "amount_fcfa") ?? throw new ToolInputException("amount_fcfa est requis.");
            var method = Str(input, "method") ?? "cash";
            if (method is not ("cash" or "wave" or "orange_money" or "other")) method = "other";

            DateTime? paidOn = null;
            if (Str(input, "paid_on") is string d)
            {
                if (!DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                    throw new ToolInputException("paid_on doit être au format AAAA-MM-JJ.");
                if (parsed.Date > DateTime.UtcNow.Date)
                    throw new ToolInputException("Un paiement ne peut pas être daté dans le futur.");
                paidOn = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            }

            var inv = await _db.Invoices
                .Where(i => i.Id == invoiceId && i.SchoolId == c.SchoolId)
                .Select(i => new
                {
                    i.Id, i.Type, i.PeriodStart, i.AmountDueFcfa, i.AmountPaidFcfa, i.Status,
                    i.Student.FirstName, i.Student.LastName,
                    Guardian = i.Student.StudentGuardians
                        .Where(g => !g.Guardian.IsDeleted)
                        .OrderByDescending(g => g.IsPrimaryGuardian)
                        .Select(g => new { g.Guardian.FullName, g.Guardian.PhoneNumber })
                        .FirstOrDefault(),
                })
                .FirstOrDefaultAsync(ct)
                ?? throw new ToolInputException("Facture introuvable dans cette école.");

            if (inv.Status == InvoiceStatus.Cancelled) throw new ToolInputException("Cette facture est annulée.");
            var remaining = inv.AmountDueFcfa - inv.AmountPaidFcfa;
            if (remaining <= 0) throw new ToolInputException("Cette facture est déjà soldée.");
            if (amount <= 0) throw new ToolInputException("Le montant doit être supérieur à zéro.");
            // Même règle que l'écran de caisse : on refuse plutôt que de
            // tronquer en silence (CashPaymentService).
            if (amount > remaining)
                throw new ToolInputException($"Le montant dépasse le reste dû ({remaining} FCFA). Demande confirmation du montant.");

            var p = new RecordPaymentPayload { InvoiceId = inv.Id, AmountFcfa = amount, Method = method, PaidOn = paidOn };

            var L = new Lines(c.Lang);
            L.Add("Élève", "التلميذ", $"{inv.FirstName} {inv.LastName}".Trim());
            L.Add("Facture", "الفاتورة", inv.Type == InvoiceType.Registration
                ? L.T("Inscription", "التسجيل")
                : L.T($"Mensualité {inv.PeriodStart:MM/yyyy}", $"قسط {inv.PeriodStart:MM/yyyy}"));
            L.Money("Montant reçu", "المبلغ المستلم", amount);
            L.Money("Reste dû après", "المتبقي بعد الدفع", remaining - amount);
            L.Add("Moyen", "وسيلة الدفع", method switch
            {
                "cash" => L.T("Espèces", "نقدا"),
                "wave" => L.T("Wave reçu sur le numéro du daara", "Wave على رقم المدرسة"),
                "orange_money" => L.T("Orange Money reçu sur le numéro du daara", "Orange Money على رقم المدرسة"),
                _ => L.T("Autre (hors Idara)", "أخرى (خارج «إدارا»)"),
            });
            L.Add("Date", "التاريخ", (paidOn ?? DateTime.UtcNow).ToString("dd/MM/yyyy"));
            L.Add("Reçu", "الإيصال", !string.IsNullOrWhiteSpace(inv.Guardian?.PhoneNumber)
                ? L.T($"Envoyé par SMS à {inv.Guardian!.FullName}", $"يرسل برسالة إلى {inv.Guardian!.FullName}")
                : L.T("Aucun responsable joignable : pas de SMS", "لا يوجد ولي أمر برقم هاتف: لا رسالة"));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.RecordPayment,
                L.T("Enregistrer un paiement", "تسجيل دفعة"), p, L, ct);
            return new ToolOutcome(JsonSerializer.Serialize(new
            {
                proposal_id = action.Id,
                status = "awaiting_user_confirmation",
                remaining_after_fcfa = remaining - amount,
            }, Json), false, action);
        }

        private async Task<ToolOutcome> ProposeRemindersAsync(
            AssistantCaller c, IReadOnlyDictionary<string, JsonElement> input, int? turnId, CancellationToken ct)
        {
            if (!input.TryGetValue("student_ids", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new ToolInputException("student_ids est requis.");
            var ids = arr.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : 0)
                .Where(v => v > 0).Distinct().Take(300).ToList();
            if (ids.Count == 0) throw new ToolInputException("Aucun élève désigné.");

            var students = await ScopedStudents(c)
                .Where(s => ids.Contains(s.Id))
                .Select(s => new
                {
                    s.Id, s.FirstName, s.LastName, s.FatherPhone, s.MotherPhone,
                    Linked = s.StudentGuardians
                        .Where(g => !g.Guardian.IsDeleted)
                        .OrderByDescending(g => g.IsPrimaryGuardian)
                        .Select(g => new { g.GuardianId, g.Guardian.FullName, g.Guardian.PhoneNumber })
                        .FirstOrDefault(),
                    Due = _db.Invoices
                        .Where(i => i.StudentId == s.Id && i.Status != InvoiceStatus.Cancelled
                                    && i.AmountPaidFcfa < i.AmountDueFcfa)
                        .Sum(i => (long?)(i.AmountDueFcfa - i.AmountPaidFcfa)) ?? 0,
                })
                .ToListAsync(ct);

            var payload = new RemindersPayload();
            var byGuardian = new Dictionary<int, ReminderRecipient>();
            foreach (var s in students)
            {
                var name = $"{s.FirstName} {s.LastName}".Trim();
                if (s.Due <= 0) { payload.Skipped.Add($"{name} : à jour"); continue; }

                if (s.Linked != null && SenegalPhone.Normalize(s.Linked.PhoneNumber) is string gp)
                {
                    if (byGuardian.TryGetValue(s.Linked.GuardianId, out var r))
                    {
                        r.StudentNames += $", {s.FirstName}";
                        continue;
                    }
                    // 🔴 §249 : le lien réclame TOUTE la dette du responsable,
                    // tous enfants confondus. Le SMS annonce donc ce que le lien
                    // demandera — jamais la seule mensualité.
                    var outstanding = await _guardianPayments.GetOutstandingAsync(s.Linked.GuardianId, c.SchoolId, ct);
                    r = new ReminderRecipient
                    {
                        GuardianId = s.Linked.GuardianId,
                        PhoneE164 = gp,
                        GuardianName = s.Linked.FullName,
                        StudentNames = name,
                        AmountFcfa = outstanding?.TotalDueFcfa ?? s.Due,
                        FirstStudentId = s.Id,
                    };
                    byGuardian[s.Linked.GuardianId] = r;
                    payload.Recipients.Add(r);
                    continue;
                }

                // Pas de compte responsable : le numéro du dossier, sans lien de
                // paiement (un lien se rattache à un compte, §161).
                var raw = SenegalPhone.Normalize(s.FatherPhone) ?? SenegalPhone.Normalize(s.MotherPhone);
                if (raw == null) { payload.Skipped.Add($"{name} : aucun numéro sénégalais"); continue; }
                var same = payload.Recipients.FirstOrDefault(x => x.GuardianId == null && x.PhoneE164 == raw);
                if (same != null) { same.StudentNames += $", {s.FirstName}"; same.AmountFcfa += s.Due; continue; }
                payload.Recipients.Add(new ReminderRecipient
                {
                    PhoneE164 = raw, StudentNames = name, AmountFcfa = s.Due, FirstStudentId = s.Id,
                });
            }

            var missing = ids.Count - students.Count;
            if (payload.Recipients.Count == 0)
                return Error("Aucune famille à relancer : " + (payload.Skipped.Count > 0
                    ? string.Join(" ; ", payload.Skipped)
                    : "aucun de ces élèves n'appartient à l'école."));

            var L = new Lines(c.Lang);
            L.Add("Familles relancées", "الأسر المعنية", payload.Recipients.Count.ToString(CultureInfo.InvariantCulture));
            L.Add("SMS", "الرسائل", L.T(
                $"{payload.Recipients.Count} SMS, avec le lien de paiement quand la famille a un compte",
                $"{payload.Recipients.Count} رسالة، مع رابط الدفع إذا كان للأسرة حساب"));
            foreach (var r in payload.Recipients.Take(15))
                L.Line(r.StudentNames, r.AmountFcfa);
            if (payload.Recipients.Count > 15)
                L.Add("…", "…", L.T($"et {payload.Recipients.Count - 15} autre(s)", $"و {payload.Recipients.Count - 15} أخرى"));
            if (payload.Skipped.Count > 0)
                L.Add("Non relancés", "غير معنيين", string.Join(" ; ", payload.Skipped.Take(5)));

            var action = await SaveProposalAsync(c, turnId, AssistantActionKind.SendReminders,
                L.T("Envoyer des relances", "إرسال تذكيرات"), payload, L, ct);
            return new ToolOutcome(JsonSerializer.Serialize(new
            {
                proposal_id = action.Id,
                status = "awaiting_user_confirmation",
                families = payload.Recipients.Count,
                skipped = payload.Skipped,
                unknown_student_ids = missing,
            }, Json), false, action);
        }

        private async Task<AssistantAction> SaveProposalAsync<T>(
            AssistantCaller c, int? turnId, AssistantActionKind kind, string title, T payload,
            Lines lines, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var a = new AssistantAction
            {
                SchoolId = c.SchoolId,
                UserId = c.UserId,
                TurnId = turnId,
                Kind = kind,
                Title = title,
                PayloadJson = JsonSerializer.Serialize(payload, Json),
                SummaryJson = JsonSerializer.Serialize(lines.Items, Json),
                Status = AssistantActionStatus.Pending,
                CreatedAt = now,
                ExpiresAt = now + ProposalLifetime,
            };
            _db.AssistantActions.Add(a);
            await _db.SaveChangesAsync(ct);
            return a;
        }

        // =====================================================================
        // Confirmation
        // =====================================================================

        public static AssistantCardDto ToCard(AssistantAction a) => new(
            a.Id, a.Kind.ToString(), a.Title,
            JsonSerializer.Deserialize<List<AssistantCardLine>>(a.SummaryJson, Json) ?? new(),
            a.Status.ToString(), a.ExpiresAt, a.ResultMessage);

        public async Task<AssistantConfirmResult?> ConfirmAsync(AssistantCaller c, int actionId, CancellationToken ct)
        {
            // Verrou de ligne : deux appuis rapides sur « Confirmer » ne doivent
            // pas inscrire l'élève deux fois ni encaisser deux fois.
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var action = await _db.AssistantActions
                .FromSqlInterpolated($@"SELECT * FROM ""AssistantActions"" WHERE ""Id"" = {actionId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (action == null || action.SchoolId != c.SchoolId || action.UserId != c.UserId) return null;

            if (action.Status != AssistantActionStatus.Pending)
                return new(false, AlreadyMessage(c.Lang, action.Status), ToCard(action), new());

            if (action.ExpiresAt < DateTime.UtcNow)
            {
                action.Status = AssistantActionStatus.Expired;
                action.ResolvedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return new(false, T(c.Lang,
                    "Cette proposition a expiré. Redemandez-la à l'assistant.",
                    "انتهت صلاحية هذا الاقتراح. اطلبوه من جديد من المساعد."), ToCard(action), new());
            }

            // On marque AVANT d'exécuter, sous le verrou : si l'exécution plante
            // à mi-chemin, la carte ne peut pas être rejouée en aveugle.
            action.Status = AssistantActionStatus.Done;
            action.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var credentials = new List<UserCredentialDto>();
            string message;
            bool ok;
            try
            {
                (ok, message, var entityId) = action.Kind switch
                {
                    AssistantActionKind.AddStudent => await ExecAddStudentAsync(c, action, credentials, ct),
                    AssistantActionKind.RecordPayment => await ExecRecordPaymentAsync(c, action, ct),
                    AssistantActionKind.SendReminders => await ExecRemindersAsync(c, action, ct),
                    _ => (false, "Action inconnue.", (int?)null),
                };
                action.ResultEntityId = entityId;
            }
            catch (InvalidOperationException ex)
            {
                ok = false;
                message = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[assistant] Exécution de l'action {Id} ({Kind}) en échec", action.Id, action.Kind);
                ok = false;
                message = T(c.Lang,
                    "L'opération n'a pas pu être faite. Réessayez depuis l'écran habituel.",
                    "تعذر تنفيذ العملية. أعيدوا المحاولة من الشاشة المعتادة.");
            }

            if (!ok) action.Status = AssistantActionStatus.Failed;
            action.ResultMessage = message;
            await _db.SaveChangesAsync(ct);
            return new(ok, message, ToCard(action), credentials);
        }

        public async Task<AssistantCardDto?> CancelAsync(AssistantCaller c, int actionId, CancellationToken ct)
        {
            var a = await _db.AssistantActions.FirstOrDefaultAsync(
                x => x.Id == actionId && x.SchoolId == c.SchoolId && x.UserId == c.UserId, ct);
            if (a == null) return null;
            if (a.Status == AssistantActionStatus.Pending)
            {
                a.Status = AssistantActionStatus.Cancelled;
                a.ResolvedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            return ToCard(a);
        }

        private async Task<(bool, string, int?)> ExecAddStudentAsync(
            AssistantCaller c, AssistantAction a, List<UserCredentialDto> credentials, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<AddStudentPayload>(a.PayloadJson, Json)!;
            var dto = new StudentCreateDto
            {
                FirstName = p.FirstName,
                LastName = p.LastName,
                Gender = p.Gender,
                ClassId = p.ClassId,
                BoardingStatus = p.Boarding,
                EnrollmentDate = DateTime.UtcNow.Date,
                MonthlyFeeFcfa = p.MonthlyFeeOverrideFcfa,
                MonthlyFeeReason = p.MonthlyFeeOverrideFcfa != null ? "Saisi via l'assistant" : null,
                RegistrationFeeFcfa = p.RegistrationFeeFcfa,
                RegistrationPaymentMode = p.RegistrationPaidNow
                    ? RegistrationPaymentMode.Cash : RegistrationPaymentMode.Online,
            };
            if (p.GuardianPhoneE164 != null)
                dto.Guardians.Add(new GuardianInputDto
                {
                    FirstName = p.GuardianFirstName ?? "",
                    LastName = p.GuardianLastName ?? "",
                    PhoneNumber = p.GuardianPhoneE164,
                    Relationship = p.GuardianRelationship,
                    IsPrimaryGuardian = true,
                });

            // L'appel ne passe pas par un contrôleur : la validation des
            // annotations n'a donc PAS eu lieu. On la fait ici, sur le même DTO.
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(dto, new ValidationContext(dto), errors, true)
                || dto.Guardians.Any(g => !Validator.TryValidateObject(g, new ValidationContext(g), errors, true)))
                return (false, string.Join(" ", errors.Select(e => e.ErrorMessage)), null);

            // Le numéro est la clé d'unicité des comptes, et elle est GLOBALE
            // (§198) : on le dit clairement au lieu de laisser remonter un 500.
            if (p.GuardianPhoneE164 != null)
            {
                var holder = await _db.Users.Where(u => u.PhoneNumber == p.GuardianPhoneE164 && !u.IsDeleted)
                    .Select(u => new { u.Role, u.SchoolId }).FirstOrDefaultAsync(ct);
                if (holder != null && !(holder.Role == UserRoles.Guardian))
                    return (false, T(c.Lang,
                        $"Le numéro {p.GuardianPhoneE164} appartient déjà à un compte qui n'est pas un responsable. Ajoutez l'élève depuis l'écran Élèves.",
                        $"الرقم {p.GuardianPhoneE164} مرتبط بحساب ليس لولي أمر. أضيفوا التلميذ من شاشة التلاميذ."), null);
            }

            var created = await _students.CreateStudentAsync(c.SchoolId, c.UserId, dto);
            credentials.AddRange(created.NewGuardianCredentials);
            return (true, T(c.Lang,
                $"{p.FirstName} {p.LastName} est inscrit(e).",
                $"تم تسجيل {p.FirstName} {p.LastName}."), created.Id);
        }

        private async Task<(bool, string, int?)> ExecRecordPaymentAsync(
            AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<RecordPaymentPayload>(a.PayloadJson, Json)!;
            var note = p.Method switch
            {
                "wave" => "Wave reçu sur le numéro du daara (via l'assistant)",
                "orange_money" => "Orange Money reçu sur le numéro du daara (via l'assistant)",
                "cash" => "Espèces (via l'assistant)",
                _ => "Paiement hors Idara (via l'assistant)",
            };

            // 🔴 Même chemin que l'écran de caisse : la facture est créditée,
            // l'argent va en CAISSE et jamais au wallet (§182) — il n'est
            // jamais passé par la réserve.
            var result = await _cash.CollectAsync(
                c.SchoolId, p.InvoiceId, p.AmountFcfa, note, p.PaidOn, c.UserId, ct);
            if (!result.Ok || result.Payment == null)
                return (false, result.Error ?? T(c.Lang, "Encaissement impossible.", "تعذر التسجيل."), null);

            var sms = await _cashNotifier.NotifyAsync(result.Payment, "assistant:record-payment", ct);
            if (sms)
                return (true, T(c.Lang, "Paiement enregistré, reçu envoyé par SMS.",
                    "تم تسجيل الدفعة وإرسال الإيصال برسالة."), result.Payment.Id);

            // Deux causes, et on ne les confond pas : « personne à prévenir »
            // et « le SMS n'est pas parti » (plafond, service indisponible)
            // n'appellent pas le même geste. Vu à l'essai réel.
            var reachable = result.Payment.GuardianId is int gid && await _db.Users
                .AnyAsync(u => u.Id == gid && !u.IsDeleted && u.PhoneNumber != null, ct);
            return (true, reachable
                ? T(c.Lang, "Paiement enregistré. Le SMS du reçu n'a pas pu partir : le reçu reste disponible dans l'historique des paiements.",
                    "تم تسجيل الدفعة. تعذر إرسال رسالة الإيصال: الإيصال متاح في سجل المدفوعات.")
                : T(c.Lang, "Paiement enregistré. Aucun SMS : l'élève n'a pas de responsable avec un numéro.",
                    "تم تسجيل الدفعة. لم ترسل رسالة: ليس للتلميذ ولي أمر برقم هاتف."), result.Payment.Id);
        }

        private async Task<(bool, string, int?)> ExecRemindersAsync(
            AssistantCaller c, AssistantAction a, CancellationToken ct)
        {
            var p = JsonSerializer.Deserialize<RemindersPayload>(a.PayloadJson, Json)!;
            var school = await _db.Schools.Where(s => s.Id == c.SchoolId)
                .Select(s => s.Name).FirstOrDefaultAsync(ct);
            var platform = await _db.GetPlatformSettingsAsync(ct);

            int sent = 0, failed = 0;
            foreach (var r in p.Recipients)
            {
                string? url = null;
                string lang = "fr";
                if (r.GuardianId is int gid)
                {
                    var (link, _) = await _paymentLinks.EnsureAsync(c.SchoolId, gid, c.UserId, ct);
                    url = _paymentLinks.BuildUrl(link.Token);
                    lang = await _db.Users.Where(u => u.Id == gid)
                        .Select(u => u.PreferredLanguage).FirstOrDefaultAsync(ct) ?? "fr";
                }

                // Par le POINT UNIQUE : langue, garde-fou de dépense, plafonds de
                // l'école et assainissement GSM-7 s'appliquent (CLAUDE.md, SMS).
                var ok = await _notif.SendSmsAsync(new NotificationSmsRequest(
                    UserId: r.GuardianId,
                    RawPhone: r.PhoneE164,
                    PreferredLanguage: lang,
                    Message: NotificationTemplates.SchoolPaymentReminder(school, r.StudentNames, r.AmountFcfa, url),
                    Bilingual: platform.SmsBilingual,
                    TemplateCode: NotificationTemplates.SchoolPaymentReminderCode,
                    RelatedEntityId: r.FirstStudentId,
                    PushRoute: "/guardian/invoices",
                    SchoolId: c.SchoolId,
                    TriggerSource: "assistant:send-reminders",
                    TriggerUserId: c.UserId), ct);
                if (ok) sent++; else failed++;
            }

            var msg = T(c.Lang,
                failed == 0 ? $"{sent} relance(s) envoyée(s)." : $"{sent} relance(s) envoyée(s), {failed} non partie(s) (plafond SMS ou numéro refusé).",
                failed == 0 ? $"تم إرسال {sent} تذكير." : $"تم إرسال {sent} تذكير، و{failed} لم ترسل (سقف الرسائل أو رقم مرفوض).");
            return (sent > 0, msg, null);
        }

        // =====================================================================
        // Utilitaires
        // =====================================================================

        private IQueryable<Student> ScopedStudents(AssistantCaller c) =>
            _db.Students.Where(s => s.SchoolId == c.SchoolId && !s.IsDeleted);

        private static string AlreadyMessage(string lang, AssistantActionStatus s) => s switch
        {
            AssistantActionStatus.Done => T(lang, "Déjà confirmé.", "تم التأكيد مسبقا."),
            AssistantActionStatus.Cancelled => T(lang, "Cette proposition a été annulée.", "تم إلغاء هذا الاقتراح."),
            AssistantActionStatus.Expired => T(lang, "Cette proposition a expiré.", "انتهت صلاحية هذا الاقتراح."),
            _ => T(lang, "Cette proposition n'est plus disponible.", "هذا الاقتراح لم يعد متاحا."),
        };

        internal static string T(string lang, string fr, string ar) => lang == "ar" ? ar : fr;

        private static string? Str(IReadOnlyDictionary<string, JsonElement> i, string k) =>
            i.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim() : null;

        /// <summary>Entier, qu'il arrive en nombre ou en chaîne (« 15 000 »).</summary>
        private static long? Long(IReadOnlyDictionary<string, JsonElement> i, string k)
        {
            if (!i.TryGetValue(k, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (long)Math.Round(d);
            if (v.ValueKind == JsonValueKind.String)
            {
                var digits = new string((v.GetString() ?? "").Where(char.IsDigit).ToArray());
                if (long.TryParse(digits, out var s)) return s;
            }
            return null;
        }

        private static bool? Bool(IReadOnlyDictionary<string, JsonElement> i, string k) =>
            i.TryGetValue(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean() : null;

        private static string? Clean(string? s, int max) =>
            string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

        /// <summary>Lignes de carte dans la langue de l'application.</summary>
        internal sealed class Lines
        {
            private readonly string _lang;
            public List<AssistantCardLine> Items { get; } = new();
            public Lines(string lang) => _lang = lang;
            public string T(string fr, string ar) => AssistantToolbox.T(_lang, fr, ar);
            public void Add(string fr, string ar, string? value) => Items.Add(new(T(fr, ar), value));
            public void Money(string fr, string ar, long amount) => Items.Add(new(T(fr, ar), null, amount));
            public void Line(string label, long amount) => Items.Add(new(label, null, amount));
        }

        private sealed class ToolInputException : Exception
        {
            public ToolInputException(string m) : base(m) { }
        }
    }
}
