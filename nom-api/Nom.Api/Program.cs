// File: Nom.Api/Program.cs

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nom.Api.Authentication;
using Nom.Api.Middleware;
using Nom.Data;
using Nom.Orch;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Orch.Models.UserManagement;
using Nom.Orch.Models.Person;
using Nom.Orch.Interfaces;
using Nom.Api.Settings;
using Nom.Orch.Settings;
using Serilog;
using Nom.Orch.Services.Measurement;
using OpenIddict.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "NomApi")
        .WriteTo.Console()
        .WriteTo.File("logs/nom-.log", rollingInterval: RollingInterval.Day));

// --- Add services to the container. ---

const string corsPolicyName = "AllowWebApp";
var allowedOrigins = builder.Configuration.GetValue<string>("AllowedOrigins");

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: corsPolicyName,
        policy =>
        {
            // Accept both ';' and ',' as delimiters (.env files historically used commas).
            var origins = allowedOrigins?.Split(new[] { ';', ',' },
                System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>();

            if (origins.Any())
            {
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
            else if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
            // Outside Development, no configured origins means no cross-origin access.
        });
});


builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524288000;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000;
});

builder.Services.AddMemoryCache();

builder.Services.Configure<Nom.Orch.Models.Shopping.RetailPackagingLookupSettings>(
    builder.Configuration.GetSection("RetailPackagingLookup"));

// Strongly-typed options
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<FrontendSettings>(builder.Configuration.GetSection("Frontend"));
builder.Services.Configure<SecurityHeadersSettings>(builder.Configuration.GetSection("SecurityHeaders"));
builder.Services.Configure<VulnerabilityScanSettings>(opts =>
{
    opts.DefaultConnection = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    opts.JwtKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
    opts.PasswordPolicy = builder.Configuration["Identity:PasswordPolicy"] ?? string.Empty;
    opts.LockoutSettings = builder.Configuration["Identity:LockoutSettings"] ?? string.Empty;
    opts.PasswordHasher = builder.Configuration["Identity:PasswordHasher"] ?? string.Empty;
    opts.EnableRbac = builder.Configuration["Authorization:EnableRBAC"] ?? string.Empty;
    opts.Environment = builder.Configuration["Environment"] ?? string.Empty;
    opts.LogLevelDefault = builder.Configuration["Logging:LogLevel:Default"] ?? string.Empty;
    opts.LogLevelTrace = builder.Configuration["Logging:LogLevel:Trace"] ?? string.Empty;
    opts.LogFilePath = builder.Configuration["Logging:FilePath"] ?? string.Empty;
    opts.KestrelHttpsEndpoint = builder.Configuration["Kestrel:Endpoints:Https"] ?? string.Empty;
    opts.CorsPolicy = builder.Configuration["Cors:Policy"] ?? string.Empty;
    opts.DebugEnabled = builder.Configuration["Debug:Enabled"] ?? string.Empty;
    opts.SecurityHeaders = builder.Configuration["Security:Headers"] ?? string.Empty;
    opts.SessionTimeoutMinutes = builder.Configuration["Session:TimeoutMinutes"] ?? string.Empty;
    opts.EncryptionKey = builder.Configuration["Encryption:Key"] ?? string.Empty;
    opts.TargetFramework = builder.Configuration["TargetFramework"] ?? string.Empty;
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NomConnection"),
                        b => b.MigrationsAssembly("Nom.Data")));

// Use AddIdentity for more control, allowing for custom claims factory registration
// Email confirmation is enforced at sign-in whenever this server can actually
// send the confirmation mail (SMTP configured); a mail-less dev/test instance
// would otherwise lock every new account out. Auth:RequireConfirmedEmail
// overrides the default either way.
var smtpConfigured = !string.IsNullOrEmpty(builder.Configuration["Email:SmtpHost"]);
var requireConfirmedEmail = builder.Configuration.GetValue<bool?>("Auth:RequireConfirmedEmail") ?? smtpConfigured;

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = requireConfirmedEmail;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<CustomClaimsPrincipalFactory>(); // Register our custom claims factory

// The Identity cookie is used ONLY by the OIDC authorization flow (the SPA uses
// bearer tokens), so its challenge lands on the authority's own sign-in page.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/connect/login";
    options.LogoutPath = "/connect/logout";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.SameSite = SameSiteMode.Lax; // the flow is a top-level redirect
});



// Default scheme routes to the API-token handler when X-Api-Key is present,
// otherwise to Identity bearer tokens.
const string authSelectorScheme = "BearerOrApiToken";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = authSelectorScheme;
    options.DefaultChallengeScheme = authSelectorScheme;
    options.DefaultScheme = authSelectorScheme;
})
.AddPolicyScheme(authSelectorScheme, "Bearer or API token", options =>
{
    options.ForwardDefaultSelector = context =>
        context.Request.Headers.ContainsKey(Nom.Api.Authentication.ApiTokenAuthenticationHandler.HeaderName)
            ? Nom.Api.Authentication.ApiTokenAuthenticationHandler.SchemeName
            : IdentityConstants.BearerScheme;
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, Nom.Api.Authentication.ApiTokenAuthenticationHandler>(
    Nom.Api.Authentication.ApiTokenAuthenticationHandler.SchemeName, null)
.AddBearerToken(IdentityConstants.BearerScheme, options =>
{
    // Configure Bearer token expiration (default is 15 minutes)
    // Set to 24 hours for longer sessions
    options.BearerTokenExpiration = TimeSpan.FromHours(24);
});
// --- END OF UPDATED CONFIGURATION ---

// ---------------------------------------------------------------------------
// OIDC authority (OpenIddict). NOM owns the identities, so it issues the tokens
// its sibling apps consume — today the Brigade provider console, tomorrow
// anything else that needs to sign a user in without a second user store.
//
// Access tokens are UNENCRYPTED JWTs so resource servers can validate them, but
// the resource servers are configured to introspect instead, which is what makes
// a suspension or logout take effect immediately rather than at token expiry.
// ---------------------------------------------------------------------------
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetUserInfoEndpointUris("connect/userinfo")
               .SetIntrospectionEndpointUris("connect/introspect")
               .SetRevocationEndpointUris("connect/revoke")
               .SetEndSessionEndpointUris("connect/logout");

        // Authorization code + PKCE for interactive clients; refresh for renewal.
        // No implicit, no password grant — both are retired for good reasons.
        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange()
               .AllowRefreshTokenFlow();

        // S256 only. Advertising `plain` lets a client downgrade to a challenge
        // that is the verifier in clear text, which defeats PKCE entirely.
        options.Configure(o => o.CodeChallengeMethods.Remove(
            OpenIddictConstants.CodeChallengeMethods.Plain));

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.OfflineAccess,
            "brigade");

        // Refresh-token rotation is OpenIddict's default (the opt-out is
        // DisableRollingRefreshTokens) — a redeemed refresh token is invalidated,
        // so a stolen one works at most once. Deliberately not disabled.

        // The issuer must be the PUBLIC url, stated rather than inferred: behind
        // a TLS-terminating proxy an inferred issuer can advertise http:// and
        // fail every client's issuer check.
        var issuer = builder.Configuration["Oidc:Issuer"];
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            options.SetIssuer(issuer);
        }

        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
               .SetRefreshTokenLifetime(TimeSpan.FromDays(14))
               .SetIdentityTokenLifetime(TimeSpan.FromMinutes(15));

        // Signing/encryption material. In production these are PEM files mounted
        // into the container (Oidc:SigningCertificatePath); development falls
        // back to ephemeral keys so a fresh clone just runs.
        // Rotation: every certificate listed is registered, and OpenIddict signs
        // with the newest while still publishing the others in the JWKS. To roll
        // a key you add the new one, deploy, wait out the old tokens' lifetime,
        // then drop the retired path — no coordinated restart, no outage.
        // Oidc:SigningCertificatePath is the single-key form of the same thing.
        var signingCerts = builder.Configuration.GetSection("Oidc:SigningCertificatePaths").Get<string[]>() ?? [];
        var signingCert = builder.Configuration["Oidc:SigningCertificatePath"];
        if (!string.IsNullOrWhiteSpace(signingCert))
        {
            signingCerts = [.. signingCerts, signingCert];
        }

        var encryptionCert = builder.Configuration["Oidc:EncryptionCertificatePath"];
        var loadedSigningCerts = signingCerts.Where(File.Exists).ToArray();
        if (loadedSigningCerts.Length > 0)
        {
            foreach (var path in loadedSigningCerts)
            {
                options.AddSigningCertificate(
                    System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                        path, builder.Configuration["Oidc:CertificatePassword"]));
            }
        }
        else
        {
            options.AddDevelopmentSigningCertificate();
        }

        if (!string.IsNullOrWhiteSpace(encryptionCert) && File.Exists(encryptionCert))
        {
            options.AddEncryptionCertificate(
                System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                    encryptionCert, builder.Configuration["Oidc:CertificatePassword"]));
        }
        else
        {
            options.AddDevelopmentEncryptionCertificate();
        }

        // Resource servers validate by introspection, which requires the access
        // token to be readable by this server — not by them — so encryption of
        // the access token is disabled while the identity token stays protected.
        options.DisableAccessTokenEncryption();

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserInfoEndpointPassthrough()
               .EnableEndSessionEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// Registers the clients described in Oidc:Clients (idempotent).
builder.Services.AddHostedService<Nom.Api.Services.OidcClientSeeder>();

builder.Services.AddAuthorization(options =>
{
    // The only two system-wide policies; both are satisfiable via stored user claims.
    // (Former AdminOnly/HouseholdManager/CanInviteUsers/CanOrganize/GroupManager policies
    // required claim types that were never minted and were referenced by no endpoint.)
    options.AddPolicy("CanManageCuration", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("CanManageCuration", "true"));

    options.AddPolicy("CanManageUserRoles", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("CanManageUserRoles", "true"));
});

if (!string.IsNullOrEmpty(builder.Configuration["Email:SmtpHost"]))
{
    builder.Services.AddTransient<IEmailSender<IdentityUser>, SmtpEmailSender>();
}
else
{
    builder.Services.AddTransient<IEmailSender<IdentityUser>, NoOpEmailSender>();
}
// --- END OF CORRECTED CONFIGURATION ---

// Add HttpClient
builder.Services.AddHttpClient();

// SSRF-guarded client for outbound calls to user-supplied URLs (webhooks):
// re-validates the resolved IP at connect time and refuses non-public targets.
builder.Services.AddHttpClient("webhook")
    .ConfigurePrimaryHttpMessageHandler(Nom.Orch.UtilityServices.SsrfGuard.BuildGuardedHandler);

// Tesseract OCR for recipe photo import (open-core). The self-hosted Ollama
// client and the commercial commerce layer (pricing/budgets/marketplace/
// receipts) live in the private nom-commerce overlay, not this open-core repo.
builder.Services.AddScoped<Nom.Orch.UtilityInterfaces.ITesseractOcrService, Nom.Orch.UtilityServices.TesseractOcrService>();

// General-purpose system email (admin notifications) — mirrors the Identity sender selection
if (!string.IsNullOrEmpty(builder.Configuration["Email:SmtpHost"]))
{
    builder.Services.AddTransient<Nom.Orch.UtilityInterfaces.ISystemEmailService, SmtpSystemEmailService>();
}
else
{
    builder.Services.AddTransient<Nom.Orch.UtilityInterfaces.ISystemEmailService, NoOpSystemEmailService>();
}

// External recipe-scraper service client (operator-provided; scraping features
// are disabled when RecipeScraper:BaseUrl is not configured).
// See docs/scraper-integration.md.
builder.Services.Configure<Nom.Orch.Settings.RecipeScraperSettings>(
    builder.Configuration.GetSection(Nom.Orch.Settings.RecipeScraperSettings.SectionName));
builder.Services.AddHttpClient<Nom.Orch.UtilityInterfaces.IRecipeScraperClient, Nom.Orch.UtilityServices.RecipeScraperClient>();

// External grocery-export service (operator-provided; shopping-list export is
// disabled when GroceryExport:BaseUrl is not configured).
// See docs/grocery-integration.md.
builder.Services.Configure<Nom.Orch.Settings.GroceryExportSettings>(
    builder.Configuration.GetSection(Nom.Orch.Settings.GroceryExportSettings.SectionName));
builder.Services.AddHttpClient<Nom.Orch.UtilityInterfaces.IGroceryExportClient, Nom.Orch.UtilityServices.GroceryExportClient>();

// Automatic source discovery (off by default; whitelist-gated — discovered
// sites become Pending sources for admin approval, never direct imports).
builder.Services.AddHostedService<Nom.Api.Services.SourceDiscoveryHostedService>();

// Canonical dish groups ("chocolate chip cookies"): browse/merge API plus a
// background sweep that classifies unclassified recipes — heuristic name
// normalization by default, AI-backed when Ai:OllamaUrl is configured. All
// assignments stay admin-correctable.
builder.Services.AddScoped<Nom.Orch.Interfaces.IDishGroupService, Nom.Orch.Services.DishGroupService>();

// Platform feature switches — lets a subsystem ship dark and be turned on from
// the admin UI rather than by a release landing.
builder.Services.AddScoped<Nom.Orch.Interfaces.IPlatformFeatureService, Nom.Orch.Services.PlatformFeatureService>();
builder.Services.AddHttpClient<Nom.Orch.Interfaces.IDishGroupSuggester, Nom.Orch.Services.OllamaDishGroupSuggester>(
    client => client.Timeout = TimeSpan.FromSeconds(300));
builder.Services.AddHostedService<Nom.Api.Services.DishGroupingHostedService>();

// Prose-rewrite batch lane (off unless Ai:BatchOllamaUrl is set): clears the
// ContainsSourceProse quarantine by rewriting scraped prose in original words;
// curation approval still gates publish, originals stay in ScrapedDocument.
builder.Services.AddHostedService<Nom.Api.Services.ProseRewriteHostedService>();

// Add OCR service
// builder.Services.AddScoped<ITesseractOcrService, TesseractOcrService>();



// Utility services are automatically registered via AddOrchestrationServices()

// Security services are automatically registered via AddOrchestrationServices()

builder.Services.AddOrchestrationServices();
builder.Services.AddSingleton<IMeasurementPerformanceMonitor, MeasurementPerformanceMonitor>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("Database", tags: new[] { "ready" })
    .AddCheck("Application", () => 
    {
        // Simple application health check
        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Application is running");
    }, tags: new[] { "live" });

// Optionally add Redis health check if Redis connection string is configured
var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection");
if (!string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddHealthChecks()
        .AddRedis(redisConnectionString, "Redis", tags: new[] { "ready" });
}

var app = builder.Build();

// --- Configure the HTTP request pipeline. ---

// The API always runs behind nginx, which terminates TLS and forwards over
// plain HTTP. Without this the app believes every request is insecure, and
// OpenIddict — correctly — refuses to serve OIDC over http and would advertise
// http:// URLs in its discovery document.
var forwardedHeaderOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor,
};

// An EMPTY known-proxy list means "trust nothing", not "trust everything", so
// the trusted hops must be named: the container network the proxy reaches us
// over, and the LAN it lives on. The API is never exposed publicly.
foreach (var network in new[]
{
    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8),
    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12),
    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16),
})
{
    forwardedHeaderOptions.KnownNetworks.Add(network);
}

app.UseForwardedHeaders(forwardedHeaderOptions);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseStaticFiles(); // serves wwwroot (e.g. /user-images)
app.UseCors(corsPolicyName);
app.UseRouting();

// Add security middleware in order
// app.UseSecurityHeaders(); // Temporarily disabled for CORS testing
app.UseMiddleware<AuditLoggingMiddleware>();
// app.UseMiddleware<InputValidationMiddleware>(); // Temporarily disabled for testing
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<FileUploadSecurityMiddleware>();
app.UseContainerSecurity(); // Container security middleware

app.UseAuthentication();
app.UseAuthorization();

// Custom registration endpoint that always creates both IdentityUser and PersonEntity
app.MapPost("api/auth/register-custom", async (
    [FromBody] RegisterRequest request,
    UserManager<IdentityUser> userManager,
    IPersonOrchestrationService personService,
    IEmailSender<IdentityUser> emailSender,
    IOptions<FrontendSettings> frontendSettings,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    // Check if user already exists
    var existingUser = await userManager.FindByEmailAsync(request.Email);
    if (existingUser != null)
    {
        return Results.BadRequest(new { message = "User with this email already exists." });
    }

    // Create the IdentityUser
    var user = new IdentityUser
    {
        UserName = request.Email,
        Email = request.Email,
        EmailConfirmed = false
    };

    var result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded)
    {
        return Results.BadRequest(new { message = "Registration failed.", errors = result.Errors });
    }

    // Send confirmation email
    try
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var frontendUrl = frontendSettings.Value.Url;
        var confirmLink = $"{frontendUrl}/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendConfirmationLinkAsync(user, request.Email, confirmLink);
        logger.LogInformation("Confirmation email sent to {Email}", request.Email);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to send confirmation email to {Email}", request.Email);
        // Don't fail registration if email sending fails
    }

    // Always create a PersonEntity for the new user
    long personId = 0;
    try
    {
        var personName = !string.IsNullOrWhiteSpace(request.FullName)
            ? request.FullName
            : request.Email.Split('@')[0]; // Use email prefix as fallback name

        var person = await personService.SetupNewRegisteredPersonAsync(user.Id, personName);
        personId = person.Id;
        logger.LogInformation("Created PersonEntity {PersonId} for user {UserId}", personId, user.Id);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create PersonEntity for user {UserId}", user.Id);
        // Don't fail the registration if PersonEntity creation fails
    }

    return Results.Ok(new { message = "Registration successful.", userId = user.Id, personId });
});

// Keep the default Identity endpoints for login, logout, etc.
app.MapGroup("api/auth")
    .MapIdentityApi<IdentityUser>();

app.MapPost("api/auth/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Ok("User logged out successfully");
});

app.MapPost("api/auth/refresh-claims", async (
    HttpContext httpContext,
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager) =>
{
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        httpContext.Response.StatusCode = 401;
        return;
    }

    // CreateUserPrincipalAsync triggers CustomClaimsPrincipalFactory with fresh DB claims
    var principal = await signInManager.CreateUserPrincipalAsync(user);

    // SignInAsync with bearer scheme writes the token JSON response body
    // (same code path as the built-in Identity login endpoint)
    await httpContext.SignInAsync(IdentityConstants.BearerScheme, principal);
}).RequireAuthorization();

// One-time login handoff: a sign-in performed on the marketing origin (the
// embedded popover on nommeal.com) trades its bearer token for a short-lived,
// single-use code; the app origin redeems the code for its own tokens. Tokens
// themselves never transit URLs — only the code rides the redirect fragment.
app.MapPost("api/auth/handoff", async (
    HttpContext httpContext,
    UserManager<IdentityUser> userManager,
    IMemoryCache cache) =>
{
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user == null)
    {
        httpContext.Response.StatusCode = 401;
        return;
    }

    var code = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    cache.Set($"login-handoff:{code}", user.Id, TimeSpan.FromSeconds(60));
    await httpContext.Response.WriteAsJsonAsync(new { code });
}).RequireAuthorization();

app.MapPost("api/auth/handoff/redeem", async (
    [FromBody] HandoffRedeemRequest request,
    HttpContext httpContext,
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager,
    IMemoryCache cache) =>
{
    var cacheKey = $"login-handoff:{request.Code}";
    if (string.IsNullOrWhiteSpace(request.Code) ||
        !cache.TryGetValue<string>(cacheKey, out var userId) || userId is null)
    {
        httpContext.Response.StatusCode = 401;
        return;
    }
    cache.Remove(cacheKey); // single-use

    var user = await userManager.FindByIdAsync(userId);
    if (user == null || await userManager.IsLockedOutAsync(user))
    {
        httpContext.Response.StatusCode = 401;
        return;
    }

    var principal = await signInManager.CreateUserPrincipalAsync(user);
    await httpContext.SignInAsync(IdentityConstants.BearerScheme, principal);
});

// Your API controllers will use JWT Bearer authentication via explicit attributes.
app.MapControllers();

// Map health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
});

// Baseline restriction criteria: the UI saves restrictions as categories ("Nut
// Allergy"), and only a category's criteria make it enforceable in planning and
// search. Types that already have criteria (seeded or admin-edited) are untouched.
using (var startupScope = app.Services.CreateScope())
{
    var db = startupScope.ServiceProvider.GetRequiredService<Nom.Data.ApplicationDbContext>();
    var startupLogger = startupScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        await Nom.Orch.Services.Support.DefaultRestrictionCriteria.EnsureAsync(db, startupLogger);
    }
    catch (Exception ex)
    {
        // Never block startup on a data-baseline step (e.g. DB not yet migrated).
        startupLogger.LogWarning(ex, "Default restriction criteria could not be ensured; planning will use whatever criteria exist");
    }
}

app.Run();