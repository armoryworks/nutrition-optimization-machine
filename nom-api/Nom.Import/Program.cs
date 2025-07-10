using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nom.Data; // For ApplicationDbContext
using Nom.Import.Data.Fdc.Importers;
using Nom.Import.Data.Shared;
using Nom.Import.Extensions; // For AddImportServices extension method
using Nom.Import.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore; // ADDED THIS USING DIRECTIVE

namespace Nom.Import
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Build the host
            var host = CreateHostBuilder(args).Build();

            // Get services from the host
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();
                var configuration = services.GetRequiredService<IConfiguration>();
                var importConfig = configuration.GetSection("ImportSettings").Get<ImportConfig>();
                var progressTracker = services.GetRequiredService<ImportProgressTracker>(); // Get the progress tracker

                // --- DIAGNOSTIC LOGGING START ---
                logger.LogInformation("--- Configuration Diagnostics ---");
                logger.LogInformation("Environment variable ASPNETCORE_ENVIRONMENT (from Environment.GetEnvironmentVariable): {EnvVar}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
                logger.LogInformation("Hosting Environment Name (from IHostEnvironment): {HostEnvName}", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

                logger.LogInformation("Loaded Configuration Sources:");
                foreach (var source in ((IConfigurationRoot)configuration).Providers)
                {
                    logger.LogInformation("  - {Provider}", source.GetType().Name);
                }
                logger.LogInformation("---------------------------------");
                // --- DIAGNOSTIC LOGGING END ---


                if (importConfig == null)
                {
                    logger.LogCritical("ImportSettings section not found or could not be bound to ImportConfig. Exiting.");
                    return;
                }

                logger.LogInformation("Nom.Import application started.");
                logger.LogInformation("FDC CSV Base Path: {FdcCsvBasePath}", importConfig.FdcCsvBasePath);
                logger.LogInformation("Batch Size: {BatchSize}", importConfig.BatchSize);
                logger.LogInformation("Default Debug Limit: {DefaultDebugLimit}", importConfig.DefaultDebugLimit);
                logger.LogInformation("System Person ID: {SystemPersonId}", importConfig.SystemPersonId);

                // Initialize DbContext to ensure migrations are applied if needed
                try
                {
                    var dbContext = services.GetRequiredService<ApplicationDbContext>();
                    // Check if the database exists and if migrations are pending
                    if (!await dbContext.Database.CanConnectAsync())
                    {
                        logger.LogCritical("Cannot connect to the database. Please ensure PostgreSQL is running and connection string is correct.");
                        return;
                    }

                    var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
                    if (pendingMigrations.Any())
                    {
                        logger.LogInformation("Applying pending database migrations: {Migrations}", string.Join(", ", pendingMigrations));
                        await dbContext.Database.MigrateAsync();
                        logger.LogInformation("Database migrations applied successfully.");
                    }
                    else
                    {
                        logger.LogInformation("No pending database migrations found. Database is up-to-date.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Failed to apply database migrations or connect to database. Exiting.");
                    return;
                }

                // --- User Interaction for Import Settings ---
                Console.WriteLine("\n--- FDC Import Configuration ---");
                string fdcCsvBasePath = ConsoleUtility.PromptForString("Enter FDC CSV files directory", importConfig.FdcCsvBasePath);
                importConfig.FdcCsvBasePath = fdcCsvBasePath; // Update config for importers

                int batchSize = ConsoleUtility.PromptForInteger("Enter batch size for imports", importConfig.BatchSize);
                importConfig.BatchSize = batchSize; // Update config for importers

                bool enableDebugLimit = ConsoleUtility.PromptWithCountdown("Enable global debug record limit? (y/N)", 5).Equals("y", StringComparison.OrdinalIgnoreCase);
                long debugLimit = 0; // Changed to long
                if (enableDebugLimit)
                {
                    debugLimit = ConsoleUtility.PromptForInteger($"Enter total number of records to process (default: {importConfig.DefaultDebugLimit})", importConfig.DefaultDebugLimit);
                }
                logger.LogInformation("Global debug limit enabled: {Enabled}, Limit: {Limit}", enableDebugLimit, debugLimit);

                // Option to clear previous progress
                string clearProgressChoice = ConsoleUtility.PromptWithCountdown("Clear all previous import progress? (y/N)", 5);
                if (clearProgressChoice.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    await progressTracker.ClearAllProgressAsync();
                    logger.LogInformation("All previous import progress cleared.");
                }

                // --- Define FDC Import Stages ---
                var fdcStages = new (string Name, Func<FdcNutrientImporter, FdcFoodImporter, FdcFoodNutrientImporter, string, long, CancellationToken, Task> ImportAction, string CsvFileName)[]
                {
                    ("FDC Nutrients", async (ni, fi, fni, path, total, ct) => await ni.ImportAsync(Path.Combine(path, "nutrient.csv"), total, ct), "nutrient.csv"),
                    ("FDC Foods", async (ni, fi, fni, path, total, ct) => await fi.ImportAsync(Path.Combine(path, "food.csv"), total, ct), "food.csv"),
                    ("FDC Food Nutrients", async (ni, fi, fni, path, total, ct) => await fni.ImportAsync(Path.Combine(path, "food_nutrient.csv"), total, ct), "food_nutrient.csv")
                };

                // --- Interactive Stage Selection (Optional) ---
                var stageNames = fdcStages.Select(s => s.Name).ToArray();
                int startIndex = ConsoleUtility.SelectOption(stageNames, "\n--- Select FDC Import Starting Stage ---", 0);

                // Handle debug mode selection from ConsoleUtility.SelectOption
                if (startIndex == -1) // -1 indicates "debug" was chosen
                {
                    enableDebugLimit = true;
                    debugLimit = ConsoleUtility.PromptForInteger($"Enter total number of records to process (default: {importConfig.DefaultDebugLimit})", importConfig.DefaultDebugLimit);
                    logger.LogInformation("Debug mode activated via stage selection. Limit: {Limit}", debugLimit);
                    startIndex = 0; // Start from the first stage in debug mode
                }


                // --- Import Orchestration ---
                var nutrientImporter = services.GetRequiredService<FdcNutrientImporter>();
                var foodImporter = services.GetRequiredService<FdcFoodImporter>();
                var foodNutrientImporter = services.GetRequiredService<FdcFoodNutrientImporter>();

                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true; // Prevent the process from terminating immediately
                    logger.LogWarning("Cancellation requested. Attempting to gracefully stop import...");
                    cts.Cancel();
                };

                for (int i = startIndex; i < fdcStages.Length; i++)
                {
                    var stage = fdcStages[i];
                    try
                    {
                        string csvFilePath = Path.Combine(importConfig.FdcCsvBasePath, stage.CsvFileName);
                        long totalRecords = GetCsvRecordCount(csvFilePath, logger); // Estimate total records

                        // Apply debug limit if enabled
                        if (enableDebugLimit)
                        {
                            totalRecords = Math.Min(totalRecords, debugLimit);
                            logger.LogInformation("Applying debug limit of {Limit} to {StageName} stage. Effective total: {Total}", debugLimit, stage.Name, totalRecords);
                        }

                        logger.LogInformation("Starting FDC Import Stage: {StageName} from {CsvFile}", stage.Name, stage.CsvFileName);
                        await stage.ImportAction(nutrientImporter, foodImporter, foodNutrientImporter, importConfig.FdcCsvBasePath, totalRecords, cts.Token);
                        logger.LogInformation("Completed FDC Import Stage: {StageName}", stage.Name);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation("FDC Import process cancelled at stage: {StageName}", stage.Name);
                        break; // Exit the loop on cancellation
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "An error occurred during FDC import stage '{StageName}'.", stage.Name);
                        // Decide whether to continue or exit on error
                        Console.WriteLine($"\nError in stage '{stage.Name}': {ex.Message}");
                        Console.Write("Continue to next stage? (y/N): ");
                        if (!Console.ReadLine()?.Equals("y", StringComparison.OrdinalIgnoreCase) ?? true)
                        {
                            logger.LogInformation("User chose to stop after error in stage: {StageName}", stage.Name);
                            break;
                        }
                    }
                }

                logger.LogInformation("Nom.Import application finished.");
            }

            await host.RunAsync(); // Run the host to keep background services alive if any (though not strictly needed for this console app)
        }

        /// <summary>
        /// Configures the host for the console application.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>An IHostBuilder instance.</returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseEnvironment("Development") // <--- ADDED THIS LINE TO FORCE DEVELOPMENT ENVIRONMENT
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables(); // Allow environment variables to override settings
                    if (args != null)
                    {
                        config.AddCommandLine(args); // Allow command line arguments to override settings
                    }
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddImportServices(hostContext.Configuration);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders(); // Clear default providers
                    logging.AddConsole();     // Add console logger
                    logging.AddDebug();       // Add debug logger
                    logging.SetMinimumLevel(LogLevel.Information); // Default log level
                });

        /// <summary>
        /// Estimates the total number of records in a CSV file by counting lines,
        /// subtracting one for the header.
        /// </summary>
        /// <param name="filePath">The path to the CSV file.</param>
        /// <param name="logger">The logger instance.</param>
        /// <returns>The estimated number of data records.</returns>
        private static long GetCsvRecordCount(string filePath, ILogger logger)
        {
            if (!File.Exists(filePath))
            {
                logger.LogWarning("CSV file not found for counting records: {FilePath}", filePath);
                return 0;
            }
            try
            {
                // Count lines, subtract 1 for header. Handle potential empty file.
                long lineCount = File.ReadLines(filePath).LongCount();
                return Math.Max(0, lineCount - 1);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error counting records in CSV file: {FilePath}", filePath);
                return 0;
            }
        }
    }
}
