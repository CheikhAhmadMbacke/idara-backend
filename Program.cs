using System.Text;
using System.Text.Json.Serialization;
using Idara.API.Common.Middleware;
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

// QuestPDF Community license : gratuit jusqu'à 1 M$ de revenus annuels.
// Doit être appelé avant toute génération de document.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ---------- Options (strongly-typed configuration) ----------
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.Configure<SuperAdminSettings>(builder.Configuration.GetSection(SuperAdminSettings.SectionName));
builder.Services.Configure<OtpSettings>(builder.Configuration.GetSection(OtpSettings.SectionName));
builder.Services.Configure<UploadSettings>(builder.Configuration.GetSection(UploadSettings.SectionName));
builder.Services.Configure<SenePaySettings>(builder.Configuration.GetSection(SenePaySettings.SectionName));
builder.Services.Configure<AfricasTalkingSettings>(builder.Configuration.GetSection(AfricasTalkingSettings.SectionName));
builder.Services.Configure<FcmSettings>(builder.Configuration.GetSection(FcmSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Section 'Jwt' manquante dans la configuration.");
if (string.IsNullOrWhiteSpace(jwtSettings.Key) || jwtSettings.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key doit faire au moins 32 caractères (configurez via User Secrets ou variables d'environnement).");

// ---------- MVC + JSON ----------
builder.Services.AddControllers()
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
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IReportCardPdfService, ReportCardPdfService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IReceiptPdfService, ReceiptPdfService>();
builder.Services.AddScoped<IInvoiceRepricingService, InvoiceRepricingService>();
builder.Services.AddScoped<IUserDeletionService, UserDeletionService>();
builder.Services.AddScoped<Idara.API.Services.Notifications.INotificationService, Idara.API.Services.Notifications.NotificationService>();

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
                  .AllowCredentials();
        }
        else
        {
            // Fallback DEV uniquement : ouvert. Inaccessible en prod (cf. check ci-dessus).
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
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
app.UseMiddleware<GlobalExceptionMiddleware>();

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

app.MapControllers();

app.Run();
