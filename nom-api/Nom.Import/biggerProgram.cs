// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Microsoft.Extensions.Logging;
// using Nom.Data; // For ApplicationDbContext
// using Nom.Import.Data.Fdc.Importers;
// using Nom.Import.Data.Recipe.Importers;
// using Nom.Import.Data.Shared;
// using Nom.Import.Extensions; // For AddImportServices extension method
// using Nom.Import.Models;
// using System;
// using System.IO;
// using System.Linq;
// using System.Threading;
// using System.Threading.Tasks;
// using Microsoft.EntityFrameworkCore; // ADDED THIS USING DIRECTIVE (still needed for .Database.MigrateAsync etc.)

// namespace Nom.Import
// {
//     public class Program
//     {
//         public static async Task Main(string[] args)
//         {
//             // Build the host
//             var host = CreateHostBuilder(args).Build();

//             // Get services from the host
//             using (var scope = host.Services.CreateScope())
//             {
//                 var services = scope.ServiceProvider;
//                 var logger = services.GetRequiredService<ILogger<Program>>();
//                 var configuration = services.GetRequiredService<IConfiguration>();
//                 var importConfig = configuration.GetSection("ImportSettings").Get<ImportConfig>(); // Use Get<ImportConfig>()
//                 var progressTracker = services.GetRequiredService<ImportProgressTracker>(); // Get the progress tracker
//                 var reportGenerator = services.GetRequiredService<ImportReportGenerator>(); // Get the report generator

//                 // --- DIAGNOSTIC LOGGING START ---
//                 logger.LogInformation("--- Configuration Diagnostics ---");
//                 logger.LogInformation("Environment variable ASPNETCORE_ENVIRONMENT (from Environment.GetEnvironmentVariable): {EnvVar}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
//                 logger.LogInformation("Hosting Environment Name (from IHostEnvironment): {HostEnvName}", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

//                 logger.LogInformation("Loaded Configuration Sources:");
//                 foreach (var source in ((IConfigurationRoot)configuration).Providers)
//                 {
//                     logger.LogInformation("  - {Provider}", source.GetType().Name);
//                 }
//                 logger.LogInformation("---------------------------------");
//                 // --- DIAGNOSTIC LOGGING END ---


//                 if (importConfig == null)
//                 {
//                     string fatalError = "ImportSettings section not found or could not be bound to ImportConfig. Exiting.";
//                     logger.LogCritical(fatalError);
//                     reportGenerator.RecordFatalError(fatalError);
//                     // Generate report before exiting in case of critical config error
//                     string reportFilePath = Path.Combine(AppContext.BaseDirectory, "import_report.json");
//                     await reportGenerator.GenerateReportFileAsync(reportFilePath);
//                     return;
//                 }

//                 logger.LogInformation("Nom.Import application started.");
//                 logger.LogInformation("FDC CSV Base Path: {FdcCsvBasePath}", importConfig.FdcCsvBasePath);
//                 logger.LogInformation("Batch Size: {BatchSize}", importConfig.BatchSize);
//                 logger.LogInformation("Default Debug Limit: {DefaultDebugLimit}", importConfig.DefaultDebugLimit);
//                 logger.LogInformation("System Person ID: {SystemPersonId}", importConfig.SystemPersonId);
//                 logger.LogInformation("Max Parallelism: {MaxParallelism}", importConfig.MaxParallelism); // Log new setting

//                 // Initialize DbContext to ensure migrations are applied if needed
//                 try
//                 {
//                     var dbContext = services.GetRequiredService<ApplicationDbContext>();
//                     // Check if the database exists and if migrations are pending
//                     if (!await dbContext.Database.CanConnectAsync())
//                     {
//                         string fatalError = "Cannot connect to the database. Please ensure PostgreSQL is running and connection string is correct.";
//                         logger.LogCritical(fatalError);
//                         reportGenerator.RecordFatalError(fatalError);
//                         string reportFilePath = Path.Combine(AppContext.BaseDirectory, "import_report.json");
//                         await reportGenerator.GenerateReportFileAsync(reportFilePath);
//                         return;
//                     }

//                     var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
//                     if (pendingMigrations.Any())
//                     {
//                         logger.LogInformation("Applying pending database migrations: {Migrations}", string.Join(", ", pendingMigrations));
//                         await dbContext.Database.MigrateAsync();
//                         logger.LogInformation("Database migrations applied successfully.");
//                     }
//                     else
//                     {
//                         logger.LogInformation("No pending database migrations found. Database is up-to-date.");
//                     }
//                 }
//                 catch (Exception ex)
//                 {
//                     string fatalError = $"Failed to apply database migrations or connect to database. Exiting. Exception: {ex.Message}";
//                     logger.LogCritical(ex, fatalError);
//                     reportGenerator.RecordFatalError(fatalError);
//                     string reportFilePath = Path.Combine(AppContext.BaseDirectory, "import_report.json");
//                     await reportGenerator.GenerateReportFileAsync(reportFilePath);
//                     return;
//                 }

//                 // --- User Interaction for Import Settings ---
//                 Console.WriteLine("\n--- FDC Import Configuration ---");
//                 string fdcCsvBasePath = ConsoleUtility.PromptForString("Enter FDC CSV files directory", importConfig.FdcCsvBasePath);
//                 importConfig.FdcCsvBasePath = fdcCsvBasePath; // Update config for importers

//                 int batchSize = ConsoleUtility.PromptForInteger("Enter batch size for imports", importConfig.BatchSize);
//                 importConfig.BatchSize = batchSize; // Update config for importers

//                 // MaxParallelism is now also configurable
//                 int maxParallelism = ConsoleUtility.PromptForInteger("Enter maximum parallelism (1 for single-threaded)", importConfig.MaxParallelism);
//                 importConfig.MaxParallelism = maxParallelism;

//                 bool enableDebugLimit = ConsoleUtility.PromptWithCountdown("Enable global debug record limit? (y/N)", 5).Equals("y", StringComparison.OrdinalIgnoreCase);
//                 long debugLimit = 0;
//                 if (enableDebugLimit)
//                 {
//                     debugLimit = ConsoleUtility.PromptForInteger($"Enter total number of records to process (default: {importConfig.DefaultDebugLimit})", importConfig.DefaultDebugLimit);
//                 }
//                 logger.LogInformation("Global debug limit enabled: {Enabled}, Limit: {Limit}", enableDebugLimit, debugLimit);

//                 // Option to clear previous progress (now handled by reportGenerator)
//                 string clearProgressChoice = ConsoleUtility.PromptWithCountdown("Clear all previous import progress? (y/N)", 5);
//                 if (clearProgressChoice.Equals("y", StringComparison.OrdinalIgnoreCase))
//                 {
//                     // The progress tracker now reports to the report generator, clearing its internal state
//                     // is implicit through the new run, or can be added as a method if truly needed.
//                     // For now, we'll just log that the user chose to clear.
//                     logger.LogInformation("User chose to clear previous import progress. Note: Progress is now tracked per run via report files.");
//                     // If you truly need to reset internal counts of progressTracker for a new run,
//                     // you'd add a method like progressTracker.ResetAllCounts();
//                 }

//                 // --- Define ALL Import Stages ---
//                 // Each tuple: (Stage Name, Import Action, CSV File Name, CSV Base Path Selector)
//                 // CSV Base Path Selector: "Fdc" or "Recipe" to pick the correct base path from ImportConfig
//                 var importStages = new (string Name, Func<FdcNutrientImporter, FdcFoodImporter, FdcFoodNutrientImporter, RecipeImporter, string, long, CancellationToken, Task> ImportAction, string CsvFileName, string CsvBasePathSelector)[]
//                 {
//                     ("FDC Nutrients", async (ni, fi, fni, ri, path, total, ct) => await ni.ImportAsync(Path.Combine(path, "nutrient.csv"), total, ct), "nutrient.csv", "Fdc"),
//                     ("FDC Foods", async (ni, fi, fni, ri, path, total, ct) => await fi.ImportAsync(Path.Combine(path, "food.csv"), total, ct), "food.csv", "Fdc"),
//                     ("FDC Food Nutrients", async (ni, fi, fni, ri, path, total, ct) => await fni.ImportAsync(Path.Combine(path, "food_nutrient.csv"), total, ct), "food_nutrient.csv", "Fdc"),
//                     // NEW RECIPE IMPORT STAGE
//                     ("Recipes (Metadata, Ingredients, Instructions)", async (ni, fi, fni, ri, path, total, ct) => await ri.ImportAsync(Path.Combine(path, "Recipe.csv"), total, ct), "Recipe.csv", "Recipe")
//                 };

//                 // --- User Interaction for Recipe CSV Path ---
//                 Console.WriteLine("\n--- Recipe Import Configuration ---");
//                 string recipeCsvBasePath = ConsoleUtility.PromptForString("Enter Recipe CSV files directory", importConfig.RecipeCsvBasePath ?? importConfig.FdcCsvBasePath);
//                 importConfig.RecipeCsvBasePath = recipeCsvBasePath; // Update config for importers


//                 // --- Interactive Stage Selection (Optional) ---
//                 var stageNames = importStages.Select(s => s.Name).ToArray();
//                 int startIndex = ConsoleUtility.SelectOption(stageNames, "\n--- Select Import Starting Stage ---", 0);

//                 // Handle debug mode selection from ConsoleUtility.SelectOption
//                 if (startIndex == -1) // -1 indicates "debug" was chosen
//                 {
//                     enableDebugLimit = true;
//                     debugLimit = ConsoleUtility.PromptForInteger($"Enter total number of records to process (default: {importConfig.DefaultDebugLimit})", importConfig.DefaultDebugLimit);
//                     logger.LogInformation("Debug mode activated via stage selection. Limit: {Limit}", debugLimit);
//                     startIndex = 0; // Start from the first stage in debug mode
//                 }


//                 // --- Import Orchestration ---
//                 var nutrientImporter = services.GetRequiredService<FdcNutrientImporter>();
//                 var foodImporter = services.GetRequiredService<FdcFoodImporter>();
//                 var foodNutrientImporter = services.GetRequiredService<FdcFoodNutrientImporter>();
//                 var recipeImporter = services.GetRequiredService<RecipeImporter>();

//                 var cts = new CancellationTokenSource();
//                 Console.CancelKeyPress += (sender, e) =>
//                 {
//                     e.Cancel = true; // Prevent the process from terminating immediately
//                     logger.LogWarning("Cancellation requested. Attempting to gracefully stop import...");
//                     cts.Cancel();
//                 };

//                 for (int i = startIndex; i < importStages.Length; i++)
//                 {
//                     var stage = importStages[i];
//                     try
//                     {
//                         string currentCsvBasePath = stage.CsvBasePathSelector == "Fdc" ? importConfig.FdcCsvBasePath : importConfig.RecipeCsvBasePath;
//                         string csvFilePath = Path.Combine(currentCsvBasePath, stage.CsvFileName);
//                         long totalRecords = GetCsvRecordCount(csvFilePath, logger); // Estimate total records

//                         // Apply debug limit if enabled
//                         if (enableDebugLimit)
//                         {
//                             totalRecords = Math.Min(totalRecords, debugLimit);
//                             logger.LogInformation("Applying debug limit of {Limit} to {StageName} stage. Effective total: {Total}", debugLimit, stage.Name, totalRecords);
//                         }

//                         logger.LogInformation("Starting Import Stage: {StageName} from {CsvFile}", stage.Name, stage.CsvFileName);
//                         await stage.ImportAction(nutrientImporter, foodImporter, foodNutrientImporter, recipeImporter, currentCsvBasePath, totalRecords, cts.Token);
//                         logger.LogInformation("Completed Import Stage: {StageName}", stage.Name);
//                     }
//                     catch (OperationCanceledException)
//                     {
//                         logger.LogInformation("Import process cancelled at stage: {StageName}", stage.Name);
//                         reportGenerator.RecordFatalError($"Import cancelled at stage: {stage.Name}");
//                         break; // Exit the loop on cancellation
//                     }
//                     catch (Exception ex)
//                     {
//                         logger.LogError(ex, "An error occurred during import stage '{StageName}'.", stage.Name);
//                         reportGenerator.RecordError($"Error in stage '{stage.Name}': {ex.Message}. StackTrace: {ex.StackTrace}");

//                         // Decide whether to continue or exit on error
//                         Console.WriteLine($"\nError in stage '{stage.Name}': {ex.Message}");
//                         Console.Write("Continue to next stage? (y/N): ");
//                         if (!Console.ReadLine()?.Equals("y", StringComparison.OrdinalIgnoreCase) ?? true)
//                         {
//                             logger.LogInformation("User chose to stop after error in stage: {StageName}", stage.Name);
//                             reportGenerator.RecordFatalError($"User stopped import after error in stage: {stage.Name}");
//                             break;
//                         }
//                     }
//                 }

//                 logger.LogInformation("Nom.Import application finished.");
//             }

//             // The host.RunAsync() is typically for long-running services. For a console app that finishes,
//             // it might not be strictly necessary unless there are background tasks that need to complete.
//             // If removed, the app will exit immediately after the Main method completes.
//             // Keeping it for now as it was in your original, but it might just block.
//             // For a console app that performs a set of tasks and exits, it's often omitted.
//             // If you want the app to exit after Main completes, remove this.
//             await host.RunAsync();
//         }

//         /// <summary>
//         /// Configures the host for the console application.
//         /// </summary>
//         /// <param name="args">Command line arguments.</param>
//         /// <returns>An IHostBuilder instance.</returns>
//         public static IHostBuilder CreateHostBuilder(string[] args) =>
//             Host.CreateDefaultBuilder(args)
//                 .UseEnvironment("Development") // Ensure Development environment is used for appsettings.Development.json
//                 .ConfigureAppConfiguration((hostingContext, config) =>
//                 {
//                     config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
//                     config.AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
//                     config.AddEnvironmentVariables(); // Allow environment variables to override settings
//                     if (args != null)
//                     {
//                         config.AddCommandLine(args); // Allow command line arguments to override settings
//                     }
//                 })
//                 .ConfigureServices((hostContext, services) =>
//                 {
//                     // All service registrations are now handled by the extension method
//                     services.AddImportServices(hostContext.Configuration);
//                 })
//                 .ConfigureLogging(logging =>
//                 {
//                     logging.ClearProviders(); // Clear default providers
//                     logging.AddConsole();     // Add console logger
//                     logging.AddDebug();       // Add debug logger
//                     logging.SetMinimumLevel(LogLevel.Information); // Default log level
//                     // Ensure EF Core logging is configured to show relevant warnings/errors
//                     logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
//                     logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.Warning);
//                     logging.AddFilter("Nom.Import", LogLevel.Information); // Ensure your application's logs are visible
//                 });

//         /// <summary>
//         /// Estimates the total number of records in a CSV file by counting lines,
//         /// subtracting one for the header.
//         /// </summary>
//         /// <param name="filePath">The path to the CSV file.</param>
//         /// <param name="logger">The logger instance.</param>
//         /// <returns>The estimated number of data records.</returns>
//         private static long GetCsvRecordCount(string filePath, ILogger logger)
//         {
//             if (!File.Exists(filePath))
//             {
//                 logger.LogWarning("CSV file not found for counting records: {FilePath}", filePath);
//                 return 0;
//             }
//             try
//             {
//                 // Count lines, subtract 1 for header. Handle potential empty file.
//                 long lineCount = File.ReadLines(filePath).LongCount();
//                 return Math.Max(0, lineCount - 1);
//             }
//             catch (Exception ex)
//             {
//                 logger.LogError(ex, "Error counting records in CSV file: {FilePath}", filePath);
//                 return 0;
//             }
//         }
//     }
// }
