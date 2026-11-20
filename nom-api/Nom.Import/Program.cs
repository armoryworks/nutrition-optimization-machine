// File: nom-api/Nom.Import/Program.cs

using System.Globalization;
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
                var dryRun = args.Contains("--dry-run");
                if (args[fdcFlag + 1] == "--undo")
                {
                    if (fdcFlag + 2 >= args.Length
                        || !DateTime.TryParse(args[fdcFlag + 2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
                        || stamp.Kind != DateTimeKind.Utc)
                    {
                        Console.WriteLine("Usage: --import-fdc --undo <stamp> [--dry-run]   (the stamp an import run printed, e.g. 2026-11-19T18:04:05.1234560Z)");
                        return;
                    }
                    var undone = await importer.UndoImportAsync(stamp, dryRun);
                    Console.WriteLine($"\n=== FDC import undo report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                    Console.WriteLine($"Run stamp:              {undone.RunStamp:O}");
                    Console.WriteLine($"Foods retired:          {undone.FoodsRetired} (soft delete; {undone.KeptInUse} kept because a recipe line uses them)");
                    Console.WriteLine($"Nutrient rows removed:  {undone.NutrientRowsRemoved} (rows the run added to foods that already existed)");
                    Console.WriteLine($"Densities cleared:      {undone.DensitiesCleared}");
                    Console.WriteLine($"Proposals rejected:     {undone.ProposalsRejected} (attach proposals still pending)");
                    Console.WriteLine($"Skipped:                {undone.Skipped} (changed since the run)");
                    foreach (var line in undone.Lines)
                        Console.WriteLine($"  {line}");
                    return;
                }

                HashSet<string>? only = null;
                var onlyFlag = Array.IndexOf(args, "--only");
                if (onlyFlag >= 0)
                {
                    if (onlyFlag + 1 >= args.Length || !File.Exists(args[onlyFlag + 1]))
                    {
                        Console.WriteLine("Usage: --import-fdc <csv-dir> [--sr-legacy] [--curated] [--only <ids-file>] [--dry-run]   (ids-file: FDC ids separated by whitespace or commas)");
                        return;
                    }
                    only = File.ReadAllText(args[onlyFlag + 1]).Replace(',', ' ')
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet();
                }

                var dataType = args.Contains("--sr-legacy") ? FdcFoundationImportService.SrLegacyFood : FdcFoundationImportService.FoundationFood;
                var createFoods = dataType != FdcFoundationImportService.SrLegacyFood || only != null;
                var report = await importer.ImportAsync(args[fdcFlag + 1], curated: args.Contains("--curated"), dataType: dataType, onlyFdcIds: only, dryRun: dryRun, createFoods: createFoods);
                Console.WriteLine($"\n=== FDC {dataType} import report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"Foods in file:     {report.TotalFoundation}{(only != null ? $" (of {only.Count} listed with --only)" : string.Empty)}");
                if (report.ListedNotInFile.Count > 0)
                    Console.WriteLine($"Listed, not in the file as {dataType}: {string.Join(", ", report.ListedNotInFile)}");
                Console.WriteLine($"Curation status:   {(args.Contains("--curated") ? "Curated (usable by meal planning)" : "PendingCuration (invisible to planning until reviewed)")}");
                Console.WriteLine($"Accepted:          {report.Accepted} ({report.Classified} classified into a food group)");
                Console.WriteLine($"Rejected:          {report.Rejected}");
                Console.WriteLine($"Skipped (existing):{report.SkippedExisting} ({report.SkippedRetired} of them retired foods that still hold their FDC id)");
                Console.WriteLine($"Skipped (dup name):{report.SkippedDuplicateName}");
                Console.WriteLine($"Skipped (claimed): {report.SkippedClaimed.Count} (FDC ids a proposal on a live food already names: a pending attach, or values taken before the food moved to another record){(report.SkippedClaimed.Count > 0 ? ": " + string.Join(", ", report.SkippedClaimed.OrderBy(id => id, StringComparer.Ordinal)) : string.Empty)}");
                if (!createFoods)
                    Console.WriteLine($"Not created:       {report.SkippedNotCreated} (an SR Legacy run creates foods only for the ids listed with --only; this run only backfills)");
                Console.WriteLine($"Nutrient rows:     {report.NutrientRows} ({report.WithReferenceServing} foods with a reference serving)");
                Console.WriteLine($"Backfilled rows:   {report.BackfilledNutrientRows} (missing nutrients added to already-imported foods)");
                foreach (var (key, n) in report.BackfilledByNutrient.OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal))
                    Console.WriteLine($"  backfilled: {key} × {n}");
                Console.WriteLine($"Densities:         {report.WithDensity} new foods, {report.BackfilledDensities} backfilled (g/ml from household measures)");
                Console.WriteLine($"Attach proposals:  {report.AttachProposals} (same-name catalog ingredients offered this food's USDA values for review)");
                foreach (var (reason, n) in report.RejectedByReason.OrderByDescending(r => r.Value))
                    Console.WriteLine($"  reject: {reason} × {n}");
                foreach (var (reason, n) in report.DroppedValuesByReason.OrderByDescending(r => r.Value))
                    Console.WriteLine($"  dropped value: {reason} × {n}");
                if (!dryRun)
                    Console.WriteLine($"Run stamp:         {report.RunStamp:O} (take this run back with --import-fdc --undo {report.RunStamp:O})");
                return;
            }

            var attachFlag = Array.IndexOf(args, "--attach-fdc");
            if (attachFlag >= 0)
            {
                var undo = attachFlag + 1 < args.Length && args[attachFlag + 1] == "--undo";
                var paths = args.Skip(attachFlag + (undo ? 2 : 1)).TakeWhile(a => !a.StartsWith("--")).ToList();
                if (paths.Count != (undo ? 1 : 2) || !File.Exists(paths[^1]))
                {
                    Console.WriteLine("Usage: --attach-fdc <csv-dir> <pairs.tsv> [--sr-legacy] [--dry-run] | --attach-fdc --undo <pairs.tsv> [--dry-run]");
                    Console.WriteLine("pairs.tsv: a header row ingredient_id<TAB>name<TAB>fdc_id, then one food per row.");
                    return;
                }
                var dryRun = args.Contains("--dry-run");
                using var attachScope = host.Services.CreateScope();
                var attacher = attachScope.ServiceProvider.GetRequiredService<FdcFoundationImportService>();
                var requests = FdcFoundationImportService.ReadAttachRequests(paths[^1]);
                var dataType = args.Contains("--sr-legacy") ? FdcFoundationImportService.SrLegacyFood : FdcFoundationImportService.FoundationFood;
                var r = undo
                    ? await attacher.UndoAttachAsync(requests, dryRun)
                    : await attacher.AttachAsync(paths[0], requests, dryRun, dataType: dataType);
                Console.WriteLine($"\n=== FDC attach {(undo ? "undo " : string.Empty)}report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"Requests in file: {r.Requests}");
                Console.WriteLine(undo
                    ? $"Undone:           {r.Undone}"
                    : $"Proposals:        {r.Proposals} (pending; applied in Admin → Food Catalog → Links or by the certain-links lane)");
                foreach (var (reason, n) in r.SkippedByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  skipped: {reason} x {n}");
                foreach (var line in r.Lines)
                    Console.WriteLine($"  {line}");
                if (r.UnresolvedNutrients.Count > 0)
                    Console.WriteLine($"No Nutrient row for (skipped): {string.Join(", ", r.UnresolvedNutrients)}");
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
                foreach (var (reason, n) in r.DroppedValuesByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  dropped value: {reason} x {n}");
                return;
            }

            var backfillFlag = Array.IndexOf(args, "--backfill-nutrients");
            if (backfillFlag >= 0 && backfillFlag + 1 < args.Length)
            {
                var dryRun = args.Contains("--dry-run");
                using var bfScope = host.Services.CreateScope();
                var backfill = bfScope.ServiceProvider.GetRequiredService<FdcNutrientBackfillService>();
                var r = await backfill.BackfillAsync(args[backfillFlag + 1], dryRun);
                Console.WriteLine($"\n=== FDC nutrient backfill report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"FDC-linked ingredients: {r.LinkedIngredients}");
                Console.WriteLine($"Found in this file:     {r.MatchedInFile}");
                Console.WriteLine($"Ingredients filled:     {r.IngredientsFilled}");
                Console.WriteLine($"Nutrient rows:          {r.Rows} (only nutrients the ingredient had no value for)");
                foreach (var (key, n) in r.RowsByNutrient.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  {key}: {n}");
                foreach (var (reason, n) in r.DroppedValuesByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  dropped value: {reason} x {n}");
                if (!dryRun && r.Rows > 0)
                    Console.WriteLine($"Rows stamped CreatedDate = {r.RunStamp:O}");
                if (r.UnresolvedNutrients.Count > 0)
                    Console.WriteLine($"No Nutrient row for (skipped): {string.Join(", ", r.UnresolvedNutrients)}");
                return;
            }

            var supplementFlag = Array.IndexOf(args, "--supplement-nutrients");
            if (supplementFlag >= 0)
            {
                var dirs = args.Skip(supplementFlag + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
                if (dirs.Count != 2)
                {
                    Console.WriteLine("Usage: --supplement-nutrients <foundation-csv-dir> <sr-legacy-csv-dir> [--dry-run]");
                    return;
                }
                var dryRun = args.Contains("--dry-run");
                using var supScope = host.Services.CreateScope();
                var backfill = supScope.ServiceProvider.GetRequiredService<FdcNutrientBackfillService>();
                var r = await backfill.SupplementFromSrLegacyAsync(dirs[0], dirs[1], dryRun);
                Console.WriteLine($"\n=== SR Legacy nutrient supplement report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"Foundation-linked ingredients with an SR Legacy food of the same NDB number or description: {r.LinkedIngredients}");
                Console.WriteLine($"Passed over, cooked differently: {r.PreparationDiffers}");
                Console.WriteLine($"Found in the SR Legacy file: {r.MatchedInFile}");
                Console.WriteLine($"Ingredients filled:          {r.IngredientsFilled}");
                Console.WriteLine($"Nutrient rows:               {r.Rows} (only nutrients the ingredient had no value for)");
                foreach (var (key, n) in r.RowsByNutrient.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  {key}: {n}");
                foreach (var (reason, n) in r.DroppedValuesByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  dropped value: {reason} x {n}");
                if (!dryRun && r.Rows > 0)
                    Console.WriteLine($"Rows stamped CreatedDate = {r.RunStamp:O}");
                if (r.UnresolvedNutrients.Count > 0)
                    Console.WriteLine($"No Nutrient row for (skipped): {string.Join(", ", r.UnresolvedNutrients)}");
                return;
            }

            var labelFlag = Array.IndexOf(args, "--import-label-nutrition");
            if (labelFlag >= 0)
            {
                if (labelFlag + 1 >= args.Length || args[labelFlag + 1].StartsWith("--"))
                {
                    Console.WriteLine("Usage: --import-label-nutrition <labels.tsv> [--dry-run]");
                    return;
                }
                var dryRun = args.Contains("--dry-run");
                using var labelScope = host.Services.CreateScope();
                var labels = labelScope.ServiceProvider.GetRequiredService<LabelNutritionImportService>();
                var r = await labels.ImportAsync(args[labelFlag + 1], dryRun);
                Console.WriteLine($"\n=== Manufacturer label nutrition report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"Labels in file: {r.Labels}");
                Console.WriteLine($"Foods filled:   {r.Foods}");
                Console.WriteLine($"Nutrient rows:  {r.Rows} (only nutrients the food had no value for)");
                Console.WriteLine($"Measures:       {r.Densities} densities, {r.ItemWeights} item weights (from the printed household measure)");
                foreach (var line in r.Lines)
                    Console.WriteLine($"  {line}");
                foreach (var (reason, n) in r.DroppedValuesByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  dropped value: {reason} x {n}");
                if (!dryRun && r.Rows > 0)
                    Console.WriteLine($"Rows stamped CreatedDate = {r.RunStamp:O}, proposal batch {r.Batch}");
                return;
            }

            var portionFlag = Array.IndexOf(args, "--backfill-portions");
            if (portionFlag >= 0)
            {
                using var portionScope = host.Services.CreateScope();
                var backfill = portionScope.ServiceProvider.GetRequiredService<FdcPortionBackfillService>();
                if (args.Contains("--undo"))
                {
                    Console.WriteLine($"Portion backfill undone: {await backfill.UndoAsync()} values cleared.");
                    return;
                }
                var dirs = args.Skip(portionFlag + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
                if (dirs.Count == 0)
                {
                    Console.WriteLine("Usage: --backfill-portions <csv-dir> [<csv-dir> ...] [--dry-run] | --backfill-portions --undo");
                    return;
                }
                var p = await backfill.BackfillAsync(dirs, dryRun: args.Contains("--dry-run"));
                Console.WriteLine($"\n=== FDC portion backfill {(p.DryRun ? "(dry run, nothing written)" : "report")} ===");
                Console.WriteLine($"USDA-linked ingredients found in the files: {p.Linked} ({p.NotInFiles} linked to foods not in these files)");
                Console.WriteLine($"Values {(p.DryRun ? "to fill" : "filled")}:   {p.Changes.Count(c => c.Field == FdcPortionBackfillService.DensityField)} g/ml, {p.Changes.Count(c => c.Field == FdcPortionBackfillService.CountField)} per-item grams");
                Console.WriteLine($"No-conversion lines:  {p.NoConversionLinesBefore} -> {p.NoConversionLinesAfter}");
                Console.WriteLine($"Recipes with one:     {p.NoConversionRecipesBefore} -> {p.NoConversionRecipesAfter} ({p.CuratedNoConversionRecipesBefore} -> {p.CuratedNoConversionRecipesAfter} curated)");
                Console.WriteLine($"Counted lines reweighed: {p.CountedLinesReweighed} (piece lines that used the reference serving now use one item)");
                foreach (var c in p.Changes)
                    Console.WriteLine($"  fill  #{c.IngredientId} {c.Ingredient} | {c.Field} = {c.Value} | fdc:{c.SourceFdcId} | {c.Evidence}"
                        + (c.ReferenceServing is { } rs ? $" | piece lines weighed {rs} g each until now" : ""));
                foreach (var c in p.KeptExisting)
                    Console.WriteLine($"  kept  #{c.IngredientId} {c.Ingredient} | {c.Field} stays {c.Current} (USDA gives {c.Value}) | fdc:{c.SourceFdcId} | {c.Evidence}");
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
                    var target = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
                    Console.WriteLine($"Database: {target.Host}:{target.Port}/{target.Database} as {target.Username}");

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
                    services.AddScoped<FdcPortionBackfillService>();
                    services.AddScoped<FdcNutrientBackfillService>();
                    services.AddScoped<LabelNutritionImportService>();

                    services.AddScoped<FoodGroupEnrichmentService>(sp => new FoodGroupEnrichmentService(
                        sp.GetRequiredService<ApplicationDbContext>(),
                        sp.GetRequiredService<ILogger<FoodGroupEnrichmentService>>(),
                        sp.GetService<Nom.Import.Services.IAiService>(),
                        aiSettings.BatchSize > 0 ? aiSettings.BatchSize : 50));
                });
    }
}
