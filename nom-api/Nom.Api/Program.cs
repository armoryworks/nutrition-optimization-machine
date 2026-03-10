// File: Nom.Api/Program.cs

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
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
            if (allowedOrigins != null)
            {
                var origins = allowedOrigins.Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                if (origins.Any())
                {
                    policy.WithOrigins(origins)
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                }
                else
                {
                    // Fallback: allow all origins in development
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                }
            }
            else
            {
                // Fallback: allow all origins in development
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
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
builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<CustomClaimsPrincipalFactory>(); // Register our custom claims factory



// Configure Bearer token authentication and set it as the default scheme
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
    options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
    options.DefaultScheme = IdentityConstants.BearerScheme;
}).AddBearerToken(IdentityConstants.BearerScheme, options =>
{
    // Configure Bearer token expiration (default is 15 minutes)
    // Set to 24 hours for longer sessions
    options.BearerTokenExpiration = TimeSpan.FromHours(24);
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    // Configure JWT Bearer authentication for compatibility with existing tokens
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = false,
        ClockSkew = TimeSpan.Zero
    };
});
// --- END OF UPDATED CONFIGURATION ---

builder.Services.AddAuthorization(options =>
{
    // Existing policies
    options.AddPolicy("CanManageCuration", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanManageCuration", "true"));

    options.AddPolicy("CanManageUserRoles", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanManageUserRoles", "true"));

    // Additional recommended policies
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("IsAdmin", "true"));

    options.AddPolicy("HouseholdManager", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanManageHousehold", "true"));

    options.AddPolicy("CanInviteUsers", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanInvite", "true"));

    options.AddPolicy("CanOrganize", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanOrganize", "true"));

    options.AddPolicy("GroupManager", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(IdentityConstants.BearerScheme, JwtBearerDefaults.AuthenticationScheme)
              .RequireClaim("CanManage", "true"));
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

// Add HttpClient for web scraping
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<Nom.Orch.UtilityServices.WebScrapingService>();

// Add OCR service
// builder.Services.AddScoped<ITesseractOcrService, TesseractOcrService>();



// Utility services are automatically registered via AddOrchestrationServices()

// Security services are automatically registered via AddOrchestrationServices()

builder.Services.AddOrchestrationServices();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
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



app.Run();