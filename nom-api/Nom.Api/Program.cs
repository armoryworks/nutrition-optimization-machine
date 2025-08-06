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
using Nom.Orch.Models.UserManagement;
using Nom.Orch.Models.Person;
using Nom.Orch.Interfaces;

var builder = WebApplication.CreateBuilder(args);

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
            }
        });
});


builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
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

builder.Services.AddTransient<IEmailSender<IdentityUser>, NoOpEmailSender>();
// --- END OF CORRECTED CONFIGURATION ---

// Add HttpClient for web scraping
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<Nom.Orch.UtilityServices.WebScrapingService>();

// Add OCR service
// builder.Services.AddScoped<ITesseractOcrService, TesseractOcrService>();

// Utility services are automatically registered via AddOrchestrationServices()

// Security services are automatically registered via AddOrchestrationServices()

builder.Services.AddOrchestrationServices();

var app = builder.Build();

// --- Configure the HTTP request pipeline. ---

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(corsPolicyName);

// Add security middleware in order
app.UseMiddleware<AuditLoggingMiddleware>();
// app.UseMiddleware<InputValidationMiddleware>(); // Temporarily disabled for testing
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<FileUploadSecurityMiddleware>();
app.UseContainerSecurity(); // Container security middleware

app.UseAuthentication();
app.UseAuthorization();

// Custom registration endpoint that handles full name and creates PersonEntity
app.MapPost("api/auth/register-custom", async (
    [FromBody] RegisterRequest request,
    UserManager<IdentityUser> userManager,
    IPersonOrchestrationService personService,
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
        EmailConfirmed = true // Auto-confirm for now
    };

    var result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded)
    {
        return Results.BadRequest(new { message = "Registration failed.", errors = result.Errors });
    }

    // Create PersonEntity if full name is provided
    if (!string.IsNullOrWhiteSpace(request.FullName))
    {
        try
        {
            var personRequest = new PersonCreateModel
            {
                PersonName = request.FullName
            };

            // Temporarily set the user ID in the HTTP context for the service
            var httpContext = new DefaultHttpContext();
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id)
                })
            );

            // Create a scoped service provider to get the service with the correct context
            using var scope = app.Services.CreateScope();
            var scopedPersonService = scope.ServiceProvider.GetRequiredService<IPersonOrchestrationService>();
            
            // Use reflection to set the HttpContextAccessor for this request
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = httpContext;

            await scopedPersonService.UpsertPersonAsync(personRequest);
            logger.LogInformation("Created PersonEntity for user {UserId} with name {FullName}", user.Id, request.FullName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create PersonEntity for user {UserId}", user.Id);
            // Don't fail the registration if PersonEntity creation fails
        }
    }

    return Results.Ok(new { message = "Registration successful.", userId = user.Id });
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

app.Run();