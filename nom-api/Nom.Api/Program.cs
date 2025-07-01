// Nom.Api/Program.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Orch;
using Microsoft.AspNetCore.Http.Features; // Required for FormOptions
using Microsoft.Extensions.Caching.Memory; // Required for AddMemoryCache

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
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


// Register all orchestration and utility services using the extension method
builder.Services.AddOrchestrationServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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
