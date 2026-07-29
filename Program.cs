using System.Text;
using System.Text.Json.Serialization;
using Idara.API.Common.Middleware;
using Idara.API.Common.Observability;
using Idara.API.Data;
using Idara.API.Options;
using Idara.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using QuestPDF.Infrastructure;
using Serilog;

// QuestPDF Community license : gratuit jusqu'à 1 M$ de revenus annuels.
// Doit être appelé avant toute génération de document.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ---------- Journal structuré (Serilog) ----------
// À partir d'ici, TOUTE ligne de journal part en JSON dans un fichier quotidien,
// avec son code de corrélation, son utilisateur et son école (cf.
// IdaraLogEnricher). Avant le 2026-07-29, ces lignes étaient du texte libre dans
// journalctl : non requêtable, non corrélable, et mêlant toutes les écoles.
//
// Deux temps, c'est le motif recommandé par Serilog : un journal « d'amorçage »
// tout de suite (sans quoi une erreur de configuration au démarrage — clé JWT
// trop courte, chaîne de connexion absente — partirait dans le vide), puis le
// journal définitif une fois le conteneur d'injection prêt, car l'enrichisseur a
// besoin d'accéder au contexte HTTP.
Log.Logger = SerilogSetup.CreateBootstrapLogger();
builder.Services.AddHttpContextAccessor();
builder.Host.UseSerilog((context, services, config) =>
    SerilogSetup.Configure(
        config,
        services.GetRequiredService<IOptions<ObservabilitySettings>>().Value,
        context.HostingEnvironment.ContentRootPath,
        context.HostingEnvironment.IsDevelopment(),
        services.GetRequiredService<IHttpContextAccessor>()));

// ---------- Options (strongly-typed configuration) ----------
builder.Services.Configure<ObservabilitySettings>(builder.Configuration.GetSection(ObservabilitySettings.SectionName));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.Configure<SuperAdminSettings>(builder.Configuration.GetSection(SuperAdminSettings.SectionName));
builder.Services.Configure<OtpSettings>(builder.Configuration.GetSection(OtpSettings.SectionName));
builder.Services.Configure<UploadSettings>(builder.Configuration.GetSection(UploadSettings.SectionName));
builder.Services.Configure<SenePaySettings>(builder.Configuration.GetSection(SenePaySettings.SectionName));
builder.Services.Configure<AfricasTalkingSettings>(builder.Configuration.GetSection(AfricasTalkingSettings.SectionName));
builder.Services.Configure<FcmSettings>(builder.Configuration.GetSection(FcmSettings.SectionName));
builder.Services.Configure<AppDistributionSettings>(builder.Configuration.GetSection(AppDistributionSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Section 'Jwt' manquante dans la configuration.");
if (string.IsNullOrWhiteSpace(jwtSettings.Key) || jwtSettings.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key doit faire au moins 32 caractères (configurez via User Secrets ou variables d'environnement).");

// ---------- MVC + JSON ----------
builder.Services.AddControllers(options =>
    {
        // Rend rejouables sans doublon les écritures mises en file d'attente par
        // l'application quand le réseau manque. Totalement inerte tant que le
        // client n'envoie pas l'en-tête `Idempotency-Key` : aucun changement de
        // comportement pour l'existant.
        options.Filters.Add<Idara.API.Common.Filters.IdempotencyFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10 Mo
});

builder.Services.AddEndpointsApiExplorer();

// ---------- Swagger ----------
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Idara API - Gestion des écoles coraniques",
        Version = "v1",
        Description = "API pour la gestion des daaras : inscriptions, paiements et suivi pédagogique."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Collez le token JWT reçu lors du login (sans le préfixe 'Bearer ')."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ---------- DbContext ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection manquant. Configurez-le via appsettings.Development.json (dev), " +
        "user-secrets, ou variable d'environnement ConnectionStrings__DefaultConnection (prod).");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ---------- Services applicatifs ----------
// Nommage des PDF persistés dans wwwroot/uploads (reçus, bulletins, factures
// d'abonnement) : suffixe HMAC indevinable, cf. Common/Utilities/PdfFileNamer.
// Singleton : dérive sa clé une seule fois au démarrage, puis sans état.
builder.Services.AddSingleton<Idara.API.Common.Utilities.IPdfFileNamer,
    Idara.API.Common.Utilities.PdfFileNamer>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IReportCardPdfService, ReportCardPdfService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IReceiptPdfService, ReceiptPdfService>();
builder.Services.AddSingleton<IExportPdfService, ExportPdfService>();
builder.Services.AddScoped<IInvoiceRepricingService, InvoiceRepricingService>();
builder.Services.AddScoped<IUserDeletionService, UserDeletionService>();
builder.Services.AddScoped<Idara.API.Services.Notifications.INotificationService, Idara.API.Services.Notifications.NotificationService>();

// ---------- Observabilité (lot 1) ----------
// Destination des incidents remontés par l'application. Derrière une interface
// (motif ISmsService / IPushService) : basculer un jour vers une plateforme
// dédiée ne demanderait de réécrire qu'un seul fichier.
builder.Services.AddScoped<Idara.API.Services.Observability.ITelemetrySink,
    Idara.API.Services.Observability.DatabaseTelemetrySink>();
// Recherche dans les fichiers de journal (sans état, sans DbContext) : singleton.
builder.Services.AddSingleton<Idara.API.Services.Observability.IServerLogSearchService,
    Idara.API.Services.Observability.ServerLogSearchService>();

// Cache mémoire : sert au rate-limiting applicatif (anti brute-force login /
// code à 6 chiffres / abus SMS). Mono-instance → en mémoire suffit.
builder.Services.AddMemoryCache();
// Source unique des transitions de solde wallet liées aux payouts (verrou
// pessimiste, idempotence, webhook correcteur anti double dépense).
builder.Services.AddScoped<IPayoutSettlementService, PayoutSettlementService>();
// Source unique des règlements payin (transition statut + crédit wallet/invoice),
// partagée par le webhook ET le PayinVerificationJob (verrou pessimiste + garde
// Status==Pending → idempotent, jamais de double crédit ni de perte).
builder.Services.AddScoped<IPayinSettlementService, PayinSettlementService>();
// Réconciliation financière plateforme (SuperAdmin) : R = D + P recalculé depuis
// les tables sources, sans hook ni ledger de revenu (aucune dérive).
builder.Services.AddScoped<IPlatformFinanceService, PlatformFinanceService>();
builder.Services.AddScoped<DbInitializer>();

// ---------- Cron / background jobs ----------
// Génère les Invoices mensuelles à 02:00 UTC chaque jour pour les écoles
// dont MonthlyDueDay tombe aujourd'hui. Idempotent en DB.
// Singleton + HostedService pointant sur la même instance : permet à un
// endpoint admin de déclencher RunOnceAsync manuellement pour rejouer un jour.
builder.Services.AddSingleton<MonthlyInvoiceGenerationJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonthlyInvoiceGenerationJob>());

// Rappel SMS quotidien (09:00 UTC) des factures en retard, 1 seul par facture.
builder.Services.AddSingleton<OverdueInvoiceReminderJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OverdueInvoiceReminderJob>());

// Rappel push quotidien (08:30 UTC) des abonnements à échéance proche (J-3),
// pour que l'école recharge AVANT le passage en lecture seule. 1 seul par cycle.
builder.Services.AddSingleton<SubscriptionRenewalReminderJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SubscriptionRenewalReminderJob>());

// Vérifie en continu (~60s) les retraits restés en UnderVerification (issue
// indéterminée) via GET /payouts/{id} autoritatif, avec back-off. Anti double
// dépense : ne restitue jamais sur un état ambigu.
builder.Services.AddSingleton<PayoutVerificationJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PayoutVerificationJob>());

// Réconciliation quotidienne (02:30 UTC) : invariant réserve marchand ≈
// Σ(wallets) + backstop de re-poll. Dépend du PayoutVerificationJob (singleton).
builder.Services.AddSingleton<PayoutReconciliationJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PayoutReconciliationJob>());

// Vérifie en continu (~3min) les payins restés Pending via GET /{token}/status
// autoritatif : résout les paiements abandonnés (parent revenu sans confirmer)
// ET récupère un webhook manqué (Completed). Ne marque terminal QUE sur statut
// SenePay explicite — jamais sur l'horloge/une erreur réseau (anti-perte).
builder.Services.AddSingleton<PayinVerificationJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PayinVerificationJob>());

// Facturation des abonnements plateforme (Phase 4) : moteur scoped (réutilisé
// par le cron ET le retry post-crédit-wallet) + cron quotidien 02:15 UTC.
builder.Services.AddScoped<ISubscriptionInvoicePdfService, SubscriptionInvoicePdfService>();
builder.Services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
builder.Services.AddSingleton<SubscriptionBillingJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SubscriptionBillingJob>());

// ---------- SenePay (HttpClient typé) ----------
builder.Services.AddHttpClient<ISenePayClient, SenePayClient>((sp, client) =>
{
    var senepay = sp.GetRequiredService<IOptions<SenePaySettings>>().Value;
    if (string.IsNullOrWhiteSpace(senepay.BaseUrl))
        throw new InvalidOperationException("SenePay:BaseUrl manquant dans la config.");
    if (string.IsNullOrWhiteSpace(senepay.ApiKey) || string.IsNullOrWhiteSpace(senepay.ApiSecret))
        throw new InvalidOperationException("SenePay:ApiKey / SenePay:ApiSecret manquants (configurer via /etc/idara/idara.env en prod).");

    client.BaseAddress = new Uri(senepay.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("X-Api-Key", senepay.ApiKey);
    client.DefaultRequestHeaders.Add("X-Api-Secret", senepay.ApiSecret);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ---------- Africa's Talking SMS (HttpClient typé) ----------
// On NE throw PAS si l'ApiKey manque (contrairement à SenePay) : le service
// no-op proprement tant que la clé n'est pas configurée, pour qu'un déploiement
// prod avant configuration ne casse pas le démarrage. La clé est ajoutée par
// requête dans le service (jamais bakée ici), pour rester fraîche.
builder.Services.AddHttpClient<ISmsService, AfricasTalkingSmsService>((sp, client) =>
{
    var at = sp.GetRequiredService<IOptions<AfricasTalkingSettings>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(at.BaseUrl)
        ? "https://api.sandbox.africastalking.com"
        : at.BaseUrl;
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ---------- Notifications push (Firebase Cloud Messaging) ----------
// Singleton : FirebaseApp est coûteux et créé une seule fois. No-op tant que le
// compte de service FCM n'est pas configuré (Fcm__ServiceAccountPath/Json) — un
// déploiement avant configuration ne casse rien.
builder.Services.AddSingleton<Idara.API.Services.Push.IPushService, Idara.API.Services.Push.FcmPushService>();

// ---------- Authentification JWT ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });

// ---------- CORS ----------
const string CorsPolicy = "IdaraCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

// En production, on REFUSE le démarrage si aucun domaine autorisé n'est configuré :
// un AllowAnyOrigin laisserait l'API exposée publiquement à tout site externe.
if (!builder.Environment.IsDevelopment() && (allowedOrigins == null || allowedOrigins.Length == 0))
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins doit être configuré en production. " +
        "Ajoutez au moins une origine autorisée dans appsettings.Production.json ou en variable d'environnement.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  // ⚠️ Sans cette ligne, le navigateur MASQUE l'en-tête au
                  // JavaScript : le code d'incident s'afficherait sur mobile et
                  // pas sur idara.sn. `AllowAnyHeader` ne concerne que les
                  // en-têtes de REQUÊTE, jamais ceux de réponse.
                  .WithExposedHeaders(Idara.API.Common.Observability.TraceCode.HeaderName)
                  .AllowCredentials();
        }
        else
        {
            // Fallback DEV uniquement : ouvert. Inaccessible en prod (cf. check ci-dessus).
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders(Idara.API.Common.Observability.TraceCode.HeaderName);
        }
    });
});

var app = builder.Build();

// ---------- Initialisation de la base + seed SuperAdmin ----------
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await initializer.InitializeAsync();
}

// ---------- Pipeline HTTP ----------
// Code de corrélation en PREMIER : le middleware d'exceptions juste en dessous
// doit pouvoir l'annoncer au client quand tout casse. Une réponse d'erreur sans
// code, c'est le retour à « ça ne marche pas ».
app.UseMiddleware<TraceContextMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// Une ligne de synthèse par requête (méthode, chemin, statut, durée), en
// Information → fichier seulement, jamais la console. ~430 lignes par jour à
// l'échelle actuelle : c'est le socle qui rend un incident rattachable à un appel.
app.UseSerilogRequestLogging(options =>
{
    // Le gabarit par défaut de Serilog met les valeurs entre guillemets
    // (`HTTP "GET" "/api/students" responded 403`), pénible à relire dans la page
    // SuperAdmin. Le suffixe `:l` les écrit littéralement.
    options.MessageTemplate =
        "{RequestMethod:l} {RequestPath:l} → {StatusCode} en {Elapsed:0} ms";

    // Les ressources statiques (uploads, page de résultat de paiement) n'ont
    // aucun intérêt diagnostique et représenteraient l'essentiel du volume.
    options.GetLevel = (http, elapsed, ex) =>
    {
        if (ex != null) return Serilog.Events.LogEventLevel.Error;
        if (http.Response.StatusCode >= 500) return Serilog.Events.LogEventLevel.Error;
        var path = http.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase))
            return Serilog.Events.LogEventLevel.Verbose; // sous le seuil = non écrit
        return Serilog.Events.LogEventLevel.Information;
    };
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

// Machine à états d'abonnement (Phase 4) : bloque ReadOnly/Suspended avec 402.
// APRÈS l'auth (besoin des claims rôle/école), AVANT les controllers. No-op tant
// que PlatformSettings.SubscriptionEnforcementEnabled est OFF (défaut).
app.UseMiddleware<Idara.API.Common.Middleware.SubscriptionEnforcementMiddleware>();

// Lecture seule du rôle Observateur (SchoolViewer) : bloque toute écriture (403).
// APRÈS l'auth (besoin du rôle), garde-fou unique de ce rôle.
app.UseMiddleware<Idara.API.Common.Middleware.ReadOnlyRoleMiddleware>();

// ETag sur les référentiels stables (classes, matières, enseignants, emploi du
// temps) : un client qui possède déjà la bonne version reçoit un 304 de
// quelques octets au lieu de la liste entière. Placé au PLUS PRÈS des
// contrôleurs, pour ne mettre en mémoire tampon que des réponses déjà passées
// par l'authentification et les contrôles d'accès.
app.UseMiddleware<Idara.API.Common.Middleware.ETagMiddleware>();

app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    // Un arrêt brutal était jusqu'ici invisible autrement qu'en fouillant
    // journalctl. Ici il devient une ligne fatale, dans le fichier comme sur la
    // console.
    Log.Fatal(ex, "L'API s'est arrêtée sur une erreur non gérée au démarrage.");
    throw;
}
finally
{
    // Le journal fichier écrit par lots : sans ce vidage, les dernières lignes
    // avant un redémarrage — c'est-à-dire souvent celles qui expliquent le
    // redémarrage — seraient perdues.
    Log.CloseAndFlush();
}
