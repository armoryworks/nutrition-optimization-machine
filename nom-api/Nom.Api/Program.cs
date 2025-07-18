// File: Nom.Api/Program.cs

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nom.Api.Authentication; // For CustomClaimsPrincipalFactory and NoOpEmailSender
using Nom.Data;
using Nom.Orch;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Kestrel to increase the maximum request body size
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524288000; // 500 MB
});

// Configure the form options to increase the multipart body length limit.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000; // 500 MB
});

// Add Memory Cache service
builder.Services.AddMemoryCache();

// Configure ApplicationDbContext to use PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NomConnection"),
                        b => b.MigrationsAssembly("Nom.Data")));

// --- UPDATED IDENTITY AND AUTHENTICATION CONFIGURATION ---
builder.Services.AddAuthorization();

// Add a no-op IEmailSender for development to satisfy Identity's requirements
builder.Services.AddTransient<IEmailSender<IdentityUser>, NoOpEmailSender>();

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
}).AddBearerToken(IdentityConstants.BearerScheme);
// --- END OF UPDATED CONFIGURATION ---

// Register all orchestration and utility services using the extension method
builder.Services.AddOrchestrationServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map the Identity API endpoints to the "/api/auth" route group
app.MapGroup("api/auth")
    .MapIdentityApi<IdentityUser>();

// Setup custom logout functionality to match frontend expectations
app.MapPost("api/auth/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Ok("User logged out successfully");
});

app.UseHttpsRedirection();

// Configure CORS policy to allow any origin for development purposes
app.UseCors(options => options.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

// IMPORTANT: Ensure UseAuthentication and UseAuthorization are called
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
