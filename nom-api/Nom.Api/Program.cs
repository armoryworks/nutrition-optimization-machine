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
using Nom.Data;
using Nom.Orch;
using System.Linq;
using System.Text;

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
builder.Services.AddHttpClient<WebScrapingService>();

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
app.UseMiddleware<InputValidationMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<FileUploadSecurityMiddleware>();
app.UseContainerSecurity(); // Container security middleware

app.UseAuthentication();
app.UseAuthorization();

// This will now use Identity's cookie schemes without conflict.
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