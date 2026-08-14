// File: nom-api/Nom.Import/Program.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Import.Services;
using Nom.Import.Settings;
using Nom.Import;

namespace Nom.Import
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            // Standalone command: classify the ingredient catalog into food groups + whole-food
            // flags. `dotnet run -- --enrich-food-groups [--overwrite]`. Runs and exits.
            if (args.Contains("--enrich-food-groups"))
            {
                using var enrichScope = host.Services.CreateScope();
                var enrich = enrichScope.ServiceProvider.GetRequiredService<FoodGroupEnrichmentService>();
                var updated = await enrich.EnrichAsync(overwrite: args.Contains("--overwrite"));
                Console.WriteLine($"Food-group enrichment complete: {updated} ingredients updated.");
                return;
            }

            // Roll back an FDC import batch: soft-delete FDC-sourced ingredients that no authored
            // recipe references. `dotnet run -- --purge-fdc`. Safety valve for a bad import.
            if (args.Contains("--purge-fdc"))
            {
                using var purgeScope = host.Services.CreateScope();
                var db = purgeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var referenced = db.RecipeIngredients.Select(ri => ri.IngredientId);
                var toPurge = await db.Ingredients
                    .Where(i => i.FdcId != null && !i.IsDeleted && !referenced.Contains(i.Id))
                    .ToListAsync();
                foreach (var i in toPurge)
                {
                    i.IsDeleted = true;
                    i.DeletedAt = DateTime.UtcNow;
                    i.LastModifiedDate = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
                Console.WriteLine($"Purged {toPurge.Count} unreferenced FDC-sourced ingredients (soft delete).");
                return;
            }

            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    logger.LogInformation("Starting Nom.Import application...");

                    // Seed measurement data
                    var measurementService = services.GetRequiredService<MeasurementDataImportService>();
                    await measurementService.SeedInitialMeasurementDataAsync();

                    logger.LogInformation("Nom.Import application completed successfully.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while running Nom.Import application.");
                    throw;
                }
            }

            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.Sources.Clear();
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile("appsettings.enhanced.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var connectionString = hostContext.Configuration.GetConnectionString("NomConnection")
                        ?? throw new InvalidOperationException(
                            "Connection string 'NomConnection' not found. " +
                            "Set via appsettings.json, environment variable ConnectionStrings__NomConnection, " +
                            "or command line --ConnectionStrings:NomConnection=...");

                    Console.WriteLine($"Environment: {hostContext.HostingEnvironment.EnvironmentName}");
                    Console.WriteLine($"Database: {connectionString}");

                    services.Configure<ImportSettings>(opts =>
                    {
                        hostContext.Configuration.GetSection("ImportSettings").Bind(opts);
                        opts.ConnectionString = connectionString;
                    });

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseNpgsql(connectionString, o => o.CommandTimeout(300)));

                    // Register the combined USDA + OFF import service
                    var useCombinedImport = hostContext.Configuration.GetValue<bool>("ImportSettings:UseCombinedImport", false);
                    if (useCombinedImport)
                    {
                        services.AddHostedService<CombinedSourceImporterService>();
                    }

                    // Add logging
                    services.AddLogging();
                    services.AddHttpClient();

                    // Register measurement data import service
                    services.AddScoped<MeasurementDataImportService>();

                    // Food-group / whole-food enrichment. AI (Ollama) is optional and
                    // config-gated on AiEnhancement:OllamaUrl; absent = heuristic only.
                    var aiSettings = new Nom.Import.Settings.AiEnhancementSettings();
                    hostContext.Configuration.GetSection("ImportSettings:AiEnhancement").Bind(aiSettings);
                    services.AddSingleton(aiSettings);

                    if (aiSettings.EnableAiEnhancement
                        && aiSettings.AiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(aiSettings.OllamaUrl))
                    {
                        services.AddScoped<Nom.Import.Services.IAiService>(sp =>
                            new Nom.Import.Services.AiServices.OllamaService(
                                sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
                                aiSettings.OllamaModel, aiSettings.OllamaUrl));
                    }

                    services.AddScoped<FoodGroupEnrichmentService>(sp => new FoodGroupEnrichmentService(
                        sp.GetRequiredService<ApplicationDbContext>(),
                        sp.GetRequiredService<ILogger<FoodGroupEnrichmentService>>(),
                        sp.GetService<Nom.Import.Services.IAiService>(),
                        aiSettings.BatchSize > 0 ? aiSettings.BatchSize : 50));
                });
    }
}
