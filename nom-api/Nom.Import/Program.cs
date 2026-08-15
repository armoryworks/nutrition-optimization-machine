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

            // Import USDA FDC Foundation Foods from a CSV directory, gauging quality.
            // `dotnet run -- --import-fdc <csv-dir>`.
            var fdcFlag = Array.IndexOf(args, "--import-fdc");
            if (fdcFlag >= 0 && fdcFlag + 1 < args.Length)
            {
                using var importScope = host.Services.CreateScope();
                var importer = importScope.ServiceProvider.GetRequiredService<FdcFoundationImportService>();
                var report = await importer.ImportAsync(args[fdcFlag + 1]);
                Console.WriteLine($"\n=== FDC Foundation import report ===");
                Console.WriteLine($"Foundation foods:  {report.TotalFoundation}");
                Console.WriteLine($"Accepted:          {report.Accepted} ({report.Classified} classified into a food group)");
                Console.WriteLine($"Rejected:          {report.Rejected}");
                Console.WriteLine($"Skipped (existing):{report.SkippedExisting}");
                Console.WriteLine($"Skipped (dup name):{report.SkippedDuplicateName}");
                Console.WriteLine($"Nutrient rows:     {report.NutrientRows} ({report.WithReferenceServing} foods with a reference serving)");
                foreach (var (reason, n) in report.RejectedByReason.OrderByDescending(r => r.Value))
                    Console.WriteLine($"  reject: {reason} × {n}");
                return;
            }

            // Import USDA FDC Branded Foods (bounded sample).
            // `dotnet run -- --import-fdc-branded <csv-dir> [--limit N]`.
            var brandedFlag = Array.IndexOf(args, "--import-fdc-branded");
            if (brandedFlag >= 0 && brandedFlag + 1 < args.Length)
            {
                var limitFlag = Array.IndexOf(args, "--limit");
                var limit = (limitFlag >= 0 && limitFlag + 1 < args.Length && int.TryParse(args[limitFlag + 1], out var l))
                    ? l : 5000;
                using var bScope = host.Services.CreateScope();
                var bImporter = bScope.ServiceProvider.GetRequiredService<FdcBrandedImportService>();
                var r = await bImporter.ImportAsync(args[brandedFlag + 1], limit);
                Console.WriteLine("\n=== FDC Branded import report ===");
                Console.WriteLine($"Scanned rows:       {r.Scanned} (limit {r.Limit})");
                Console.WriteLine($"Skipped non-US:     {r.SkippedNonUs}");
                Console.WriteLine($"Skipped discontinued:{r.SkippedDiscontinued}");
                Console.WriteLine($"Accepted:           {r.Accepted} ({r.Classified} classified, {r.MarkedWholeFood} whole-food, {r.WithReferenceServing} with reference serving)");
                Console.WriteLine($"Rejected:           {r.Rejected}");
                Console.WriteLine($"Skipped (existing): {r.SkippedExisting}");
                Console.WriteLine($"Skipped (dup name): {r.SkippedDuplicateName}");
                Console.WriteLine($"Nutrient rows:      {r.NutrientRows}");
                foreach (var (reason, n) in r.RejectedByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  reject: {reason} x {n}");
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

                    services.AddScoped<FdcFoundationImportService>();
                    services.AddScoped<FdcBrandedImportService>();

                    services.AddScoped<FoodGroupEnrichmentService>(sp => new FoodGroupEnrichmentService(
                        sp.GetRequiredService<ApplicationDbContext>(),
                        sp.GetRequiredService<ILogger<FoodGroupEnrichmentService>>(),
                        sp.GetService<Nom.Import.Services.IAiService>(),
                        aiSettings.BatchSize > 0 ? aiSettings.BatchSize : 50));
                });
    }
}
