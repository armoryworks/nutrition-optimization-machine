// Nom.Api/Program.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Orch;
using Nom.Orch.UtilityInterfaces; // For IExternalNutrientApiService, IReferenceDataSeederService
using System; // For Uri
using Microsoft.Extensions.DependencyInjection;
using Nom.Orch.UtilityServices; // For CreateScope and GetRequiredService

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure ApplicationDbContext to use PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NomConnection"),
                        b => b.MigrationsAssembly("Nom.Data")));

builder.Services.AddAuthorization();

// Add Identity API Endpoints for authentication and user management
builder.Services.AddIdentityApiEndpoints<IdentityUser>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// --- Configure HttpClient for ExternalNutrientApiService ---
// Retrieve FDC API settings from configuration
var fdcApiBaseUrl = builder.Configuration["FoodDataCentralApi:BaseUrl"];
if (string.IsNullOrEmpty(fdcApiBaseUrl))
{
    throw new InvalidOperationException("FoodDataCentralApi:BaseUrl configuration is missing or empty.");
}

builder.Services.AddHttpClient<IExternalNutrientApiService, ExternalNutrientApiService>(client =>
{
    client.BaseAddress = new Uri(fdcApiBaseUrl);
});

// REMOVED: No need to explicitly register IServiceScopeFactory; it's provided by the framework.
// builder.Services.AddSingleton<IServiceScopeFactory, ServiceScopeFactory>();

// Register all orchestration and utility services using the extension method
builder.Services.AddOrchestrationServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // --- Development-only: Seed Reference Data ---
    // Create a scope to resolve the seeder service
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IReferenceDataSeederService>();
        await seeder.SeedReferenceDataAsync();
    }
}

app.MapGroup("api/auth")
    .MapIdentityApi<IdentityUser>();

// setup custom logout functionality
app.MapPost("api/auth/logout", async (SignInManager<IdentityUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok("User logged out successfully");
        });

app.UseHttpsRedirection();

// Configure CORS policy to allow any origin for development purposes
app.UseCors(options => options.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

app.UseAuthorization();

app.MapControllers();

app.Run();
