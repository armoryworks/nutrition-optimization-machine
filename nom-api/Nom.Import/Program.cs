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
            if (args.Contains(Nom.Import.Golden.GoldenScoreCommand.Flag))
            {
                Environment.ExitCode = await Nom.Import.Golden.GoldenScoreCommand.RunAsync(args);
                return;
            }

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
                    Console.WriteLine($"Foods retired:          {undone.FoodsRetired} (soft delete; {undone.KeptInUse} kept because a recipe line uses them; a later --import-fdc of the same ids revives them)");
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
                        Console.WriteLine("Usage: --import-fdc <csv-dir> [--sr-legacy] [--only <ids-file>] [--dry-run]   (ids-file: FDC ids separated by whitespace or commas)");
                        return;
                    }
                    only = File.ReadAllText(args[onlyFlag + 1]).Replace(',', ' ')
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet();
                }

                var dataType = args.Contains("--sr-legacy") ? FdcFoundationImportService.SrLegacyFood : FdcFoundationImportService.FoundationFood;
                var createFoods = dataType != FdcFoundationImportService.SrLegacyFood || only != null;
                var report = await importer.ImportAsync(args[fdcFlag + 1], dataType: dataType, onlyFdcIds: only, dryRun: dryRun, createFoods: createFoods);
                Console.WriteLine($"\n=== FDC {dataType} import report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"Foods in file:     {report.TotalFoundation}{(only != null ? $" (of {only.Count} listed with --only)" : string.Empty)}");
                if (report.ListedNotInFile.Count > 0)
                    Console.WriteLine($"Listed, not in the file as {dataType}: {string.Join(", ", report.ListedNotInFile)}");
                if (args.Contains("--curated"))
                    Console.WriteLine("--curated is retired: foods land pending, and --admit curates the ones the catalog admission rule passes.");
                Console.WriteLine($"Accepted:          {report.Accepted} ({report.Classified} classified into a food group)");
                Console.WriteLine($"Admission rule:    {report.Admissible} pass and wait for --admit, {report.Accepted - report.Admissible} held ({Nom.Data.Nutrition.CatalogAdmissionRule.Version})");
                foreach (var (reason, n) in report.HeldByReason.OrderByDescending(r => r.Value))
                    Console.WriteLine($"  not curated: {reason} × {n}");
                Console.WriteLine($"Rejected:          {report.Rejected}");
                Console.WriteLine($"Revived:           {report.Revived} (foods an --import-fdc --undo retired, brought back with their nutrient rows)");
                Console.WriteLine($"Skipped (existing):{report.SkippedExisting} ({report.SkippedRetired} of them retired by other means and still holding their FDC id)");
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

            var admitFlag = Array.IndexOf(args, "--admit");
            if (admitFlag >= 0)
            {
                await RunAdmissionAsync(host, args, admitFlag);
                return;
            }

            var liftFlag = Array.IndexOf(args, "--branded-lift");
            if (liftFlag >= 0)
            {
                await RunBrandedLiftAsync(host, args, liftFlag);
                return;
            }

            var containerFlag = Array.IndexOf(args, "--container-sizes");
            if (containerFlag >= 0)
            {
                await RunContainerSizesAsync(host, args, containerFlag);
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

        private static async Task RunAdmissionAsync(IHost host, string[] args, int admitFlag)
        {
            using var scope = host.Services.CreateScope();
            var admission = scope.ServiceProvider.GetRequiredService<CatalogAdmissionService>();
            var dryRun = args.Contains("--dry-run");
            string? Value(string flag)
            {
                var i = Array.IndexOf(args, flag);
                return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
            }

            if (admitFlag + 1 < args.Length && args[admitFlag + 1] == "--undo")
            {
                if (admitFlag + 2 >= args.Length
                    || !DateTime.TryParse(args[admitFlag + 2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
                    || stamp.Kind != DateTimeKind.Utc)
                {
                    Console.WriteLine("Usage: --admit --undo <stamp> [--dry-run]   (the stamp an admission run printed)");
                    return;
                }
                var undone = await admission.UndoAsync(stamp, dryRun);
                PrintUndo(undone);
                return;
            }

            var dirs = args.Skip(admitFlag + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
            var checksums = Value("--checksums");
            if (dirs.Count != 2 || !dirs.All(Directory.Exists) || checksums == null || !File.Exists(checksums)
                || (Value("--branded") is { } b && !Directory.Exists(b)))
            {
                Console.WriteLine("Usage: --admit <foundation-csv-dir> <sr-legacy-csv-dir> [--branded <branded-csv-dir>] --checksums <sha256-file> [--dry-run]");
                Console.WriteLine("       --admit --undo <stamp> [--dry-run]");
                return;
            }

            var r = await admission.AdmitAsync(new CatalogAdmissionService.AdmitOptions(dirs[0], dirs[1], Value("--branded"), checksums, dryRun));
            Console.WriteLine($"\n=== Catalog admission report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ({r.RuleVersion}) ===");
            Console.WriteLine($"Foods judged:        {r.Candidates} ({r.UsdaCandidates} USDA, {r.BrandedCandidates} branded, {r.BrandedNotInFile} branded not in the file)");
            Console.WriteLine($"Admitted (Curated):  {r.Admitted} ({r.AdmittedPrepared} prepared foods: plannable, never a recipe line's food or a food-group food)");
            Console.WriteLine($"Held pending:        {r.Held}");
            foreach (var (reason, n) in r.HeldByReason.OrderByDescending(x => x.Value))
                Console.WriteLine($"  held: {reason} × {n}");
            Console.WriteLine($"Held for the audit:  {r.HeldForAudit.Count} (would gain a new high or medium audit finding; status, group and edibility left as they are)");
            foreach (var line in r.HeldForAudit.Take(100))
                Console.WriteLine($"  audit: {line}");
            Console.WriteLine($"Kept curated:        {r.KeptCurated} (curated foods the rule would not curate; never demoted by the pass)");
            foreach (var (reason, n) in r.KeptCuratedByReason.OrderByDescending(x => x.Value))
                Console.WriteLine($"  kept: {reason} × {n}");
            Console.WriteLine($"Regrouped:           {r.Regrouped} ({r.GroupsCleared} cleared)");
            foreach (var (move, n) in r.GroupMoves.OrderByDescending(x => x.Value))
                Console.WriteLine($"  group: {move} × {n}");
            Console.WriteLine($"Edibility set:       {r.EdibilitySet}");
            Console.WriteLine($"Descriptions added:  {r.DescriptionsAdded} ({r.DescriptionsBlocked} blocked by an alias of the same text)");
            Console.WriteLine($"Picks withdrawn:     {r.PicksWithdrawn} (open model picks that point at a food an admin rejected or at a prepared food)");
            Console.WriteLine($"Energy check:        {r.EnergyPassRate:P2} of USDA foods pass ({string.Join(", ", r.EnergyByOutcome.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))})");
            Console.WriteLine($"Without a category:  {r.CategoryMissing}");
            foreach (var line in r.NotInFile.Take(50))
                Console.WriteLine($"  not in the files: {line}");
            Console.WriteLine($"Foods changed:       {r.ChangedFoods} (limit {CatalogAdmissionService.MaxChangedFoods})");
            foreach (var line in r.Checksums)
                Console.WriteLine($"  sha256 {line}");
            foreach (var line in r.ChecksumFailures)
                Console.WriteLine($"  NOT RECORDED: {line}");
            foreach (var line in r.Lines)
                Console.WriteLine($"  {line}");
            foreach (var reason in r.Stopped)
                Console.WriteLine($"STOPPED, nothing written: {reason}");
            if (r.Stopped.Count > 0)
            {
                Environment.ExitCode = 1;
                return;
            }
            if (!dryRun)
                Console.WriteLine($"Run stamp:           {r.RunStamp:O} (take this run back with --admit --undo {r.RunStamp:O})");
            if (r.SelfUndo != null)
            {
                Console.WriteLine($"UNDONE BY ITSELF: {r.NewFindings.Count} new audit finding(s) on admitted or regrouped foods");
                foreach (var finding in r.NewFindings.Take(100))
                    Console.WriteLine($"  finding: {finding}");
                PrintUndo(r.SelfUndo);
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunBrandedLiftAsync(IHost host, string[] args, int flag)
        {
            using var scope = host.Services.CreateScope();
            var lift = scope.ServiceProvider.GetRequiredService<Nom.Orch.Interfaces.IBrandedLiftService>();
            var dryRun = args.Contains("--dry-run");
            if (flag + 1 < args.Length && args[flag + 1] == "--undo")
            {
                if (flag + 2 >= args.Length
                    || !DateTime.TryParse(args[flag + 2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
                    || stamp.Kind != DateTimeKind.Utc)
                {
                    Console.WriteLine("Usage: --branded-lift --undo <stamp> [--dry-run]   (the stamp a lift run printed)");
                    return;
                }
                var undone = await lift.UndoAsync(stamp, !dryRun, Nom.Data.SystemConstants.SystemPersonId);
                Console.WriteLine($"\n=== Branded lift undo report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"Run stamp:        {undone.RunStamp:O}");
                if (undone.UndoStamp is { } undoStamp)
                    Console.WriteLine($"Undo stamp:       {undoStamp:O} (take this undo back with --branded-lift --undo {undoStamp:O})");
                Console.WriteLine($"Demoted:          {undone.Demoted} (lifted foods back to PendingCuration)");
                Console.WriteLine($"Lifted again:     {undone.Relifted} (foods an earlier undo had demoted)");
                Console.WriteLine($"Values restored:  {undone.ValuesRestored} (source counts and reasons)");
                Console.WriteLine($"Skipped:          {undone.Skipped} (changed since the run)");
                Console.WriteLine($"Evidence:         {undone.EvidenceExcluded} rows excluded from the lift, {undone.EvidenceIncluded} let back in");
                Console.WriteLine($"Re-counted:       {undone.Recounted} foods without the excluded evidence ({undone.StillLifted} still lifted by the other sources)");
                foreach (var line in undone.Lines)
                    Console.WriteLine($"  {line}");
                return;
            }

            var r = await lift.RunAsync(!dryRun, Nom.Data.SystemConstants.SystemPersonId);
            Console.WriteLine($"\n=== Branded lift report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ({r.RuleVersion}) ===");
            Console.WriteLine($"Judged:           {r.Judged} branded foods with evidence ({r.NotJudged} not yet judged by the admission rule; run --admit with --branded first)");
            Console.WriteLine($"Lifted (Curated): {r.Lifted} (two or more sources agree, the FDC record counting as one)");
            Console.WriteLine($"Held pending:     {r.Held}");
            foreach (var (reason, n) in r.HeldByReason.OrderByDescending(x => x.Value))
                Console.WriteLine($"  held: {reason} × {n}");
            Console.WriteLine($"Re-counted:       {r.Recounted} curated branded foods");
            Console.WriteLine($"Demoted:          {r.Demoted} (curated by the rules, the sources no longer lift them; back to PendingCuration)");
            Console.WriteLine($"Kept curated:     {r.KeptCurated} (curated by an admin, the sources do not lift them; never demoted, the reason is stored)");
            Console.WriteLine($"Evidence:         {r.ExcludedEvidence} rows excluded by an earlier undo");
            Console.WriteLine($"Agreeing sources: {string.Join(", ", r.ByAgreeingSources.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}: {x.Value}"))}");
            Console.WriteLine($"Foods changed:    {r.ChangedFoods} (limit {Nom.Orch.Services.BrandedLiftService.MaxChangedFoods})");
            foreach (var line in r.Lines)
                Console.WriteLine($"  {line}");
            foreach (var reason in r.Stopped)
                Console.WriteLine($"STOPPED, nothing written: {reason}");
            if (r.Stopped.Count > 0)
            {
                Environment.ExitCode = 1;
                return;
            }
            if (r.RunStamp is { } runStamp)
                Console.WriteLine($"Run stamp:        {runStamp:O} (take this run back with --branded-lift --undo {runStamp:O})");
        }

        private static async Task RunContainerSizesAsync(IHost host, string[] args, int flag)
        {
            using var scope = host.Services.CreateScope();
            var dryRun = args.Contains("--dry-run");
            string? Value(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
            }

            if (flag + 1 < args.Length && args[flag + 1] == "--undo")
            {
                if (flag + 2 >= args.Length
                    || !DateTime.TryParse(args[flag + 2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since)
                    || since.Kind != DateTimeKind.Utc)
                {
                    Console.WriteLine("Usage: --container-sizes --undo <stamp> [--dry-run]   (the stamp a run printed; everything journaled since it is taken back)");
                    return;
                }
                var sizes = scope.ServiceProvider.GetRequiredService<Nom.Orch.Interfaces.IContainerSizeService>();
                var undone = await sizes.UndoSinceAsync(since, !dryRun, Nom.Data.SystemConstants.SystemPersonId);
                Console.WriteLine($"\n=== Container size undo report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
                Console.WriteLine($"Since:    {undone.SinceUtc:O}");
                Console.WriteLine($"Undone:   {undone.Undone}");
                Console.WriteLine($"Skipped:  {undone.Skipped} (changed since)");
                foreach (var line in undone.Lines.Take(500))
                    Console.WriteLine($"  {line}");
                return;
            }

            if (flag + 1 < args.Length && args[flag + 1] == "--corpus")
            {
                var corpus = scope.ServiceProvider.GetRequiredService<ContainerCorpusImportService>();
                var c = await corpus.RunAsync(new ContainerCorpusImportService.Options(dryRun, Value("--facts")));
                Console.WriteLine($"\n=== Container sizes from the recipe corpus{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ({Nom.Orch.Services.Support.ContainerCorpusSizes.Version}, {Nom.Orch.Services.ContainerSizeService.RuleVersion}) ===");
                Console.WriteLine($"Old and undated book lines: {c.BookLines} read; {c.SizedLines} sized container lines in {c.Books} books");
                Console.WriteLine($"Same-book sizes:    {c.BookSizes} ({c.BookRefused} refused)");
                Console.WriteLine($"Same-era sizes:     {c.EraSizes} ({c.EraRefused} refused)");
                foreach (var (reason, n) in c.RefusedByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  refused: {reason} × {n}");
                if (c.FactsWritten != null)
                    Console.WriteLine($"Facts:              {c.FactsWritten} (compare with ops/container-era-facts.py on a capture of the same corpus)");
                Console.WriteLine($"Unsized lines:      {c.UnsizedLines} container lines of old and undated books, by product class and tier (worst case from the corpus spread or the period candidates; - where no source bounds it):");
                foreach (var row in c.Coverage)
                    Console.WriteLine($"  {row.ProductClass} | {row.Tier} | {row.Lines} | {(row.WorstCase is { } worst ? worst.ToString("P1", CultureInfo.InvariantCulture) : "-")}");
                foreach (var (reason, n) in c.UncountedByReason.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  uncounted: {reason} × {n}");
                if (c.Written is { } cw)
                    Console.WriteLine($"Written:            {cw.Added} added, {cw.Replaced} replaced, {cw.Retired} retired, {cw.Unchanged} unchanged (all in shadow until scored)");
                if (c.Scored != null) PrintScore(c.Scored);
                if (!dryRun)
                    Console.WriteLine($"Run stamp:          {c.Stamp:O} (take this run back with --container-sizes --undo {c.Stamp:O})");
                return;
            }

            if (flag + 1 < args.Length && args[flag + 1] == "--score")
            {
                var sizes = scope.ServiceProvider.GetRequiredService<Nom.Orch.Interfaces.IContainerSizeService>();
                PrintScore(await sizes.ScoreAsync(!dryRun, Nom.Data.SystemConstants.SystemPersonId));
                return;
            }

            var dirs = args.Skip(flag + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
            var checksums = Value("--checksums");
            if (dirs.Count is < 2 or > 3 || !dirs.All(Directory.Exists) || checksums == null || !File.Exists(checksums))
            {
                Console.WriteLine("Usage: --container-sizes [<branded-csv-dir>] <sr-legacy-csv-dir> <foundation-csv-dir> --checksums <sha256-file> [--dry-run]");
                Console.WriteLine("       --container-sizes --corpus [--facts <out.tsv>] [--dry-run]");
                Console.WriteLine("       --container-sizes --score [--dry-run]");
                Console.WriteLine("       --container-sizes --undo <stamp> [--dry-run]");
                return;
            }
            var service = scope.ServiceProvider.GetRequiredService<ContainerSizeImportService>();
            var r = await service.RunAsync(new ContainerSizeImportService.Options(dirs.Count == 3 ? dirs[0] : null, dirs[^2], dirs[^1], checksums, dryRun));
            Console.WriteLine($"\n=== Container size report{(dryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ({Nom.Orch.Services.ContainerSizeService.RuleVersion}) ===");
            foreach (var line in r.Checksums)
                Console.WriteLine($"  sha256 {line}");
            foreach (var reason in r.Stopped)
                Console.WriteLine($"STOPPED, nothing written: {reason}");
            if (r.Stopped.Count > 0)
            {
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine($"Catalog foods:      {r.Foods} USDA foods, {r.FoodsInFiles} found in the files");
            if (r.Branded != null)
                Console.WriteLine($"Branded release:    {r.BrandedRelease}: {r.Branded.Products} products in the classes' categories, {r.Branded.Deduplicated} after one per GTIN");
            foreach (var (reason, n) in r.Branded?.SkippedByReason.OrderByDescending(x => x.Value) ?? Enumerable.Empty<KeyValuePair<string, int>>())
                Console.WriteLine($"  branded skipped: {reason} × {n}");
            foreach (var line in r.Modes)
                Console.WriteLine($"  mode {line}");
            Console.WriteLine($"Proposed:           {r.Proposals.Count} ({string.Join(", ", r.ProposedBySource.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))})");
            foreach (var (reason, n) in r.RefusedByReason.OrderByDescending(x => x.Value))
                Console.WriteLine($"  refused: {reason} × {n}");
            foreach (var line in r.Refusals.Take(100))
                Console.WriteLine($"    {line}");
            if (r.Written is { } w)
                Console.WriteLine($"Written:            {w.Added} added, {w.Replaced} replaced, {w.Retired} retired, {w.Unchanged} unchanged (all in shadow until scored)");
            if (r.Scored != null) PrintScore(r.Scored);
            if (!dryRun)
                Console.WriteLine($"Run stamp:          {r.Stamp:O} (take this run back with --container-sizes --undo {r.Stamp:O})");
        }

        private static void PrintScore(Nom.Orch.Models.Curation.ContainerSizeScoreReport s)
        {
            Console.WriteLine($"\n=== Container size hold-out{(s.Applied ? string.Empty : " (DRY RUN — nothing saved)")} ===");
            Console.WriteLine($"Signature:          {s.Signature} ({s.Rows} sizes)");
            Console.WriteLine($"Hold-out lines:     {s.HoldoutAgree}/{s.HoldoutLines} agree within 10 %{(s.HoldoutLines > 0 ? $" ({(double)s.HoldoutAgree / s.HoldoutLines:P1})" : string.Empty)}; {s.LinesWithoutKey} sized lines on these foods have no size of their kind and era; {s.OtherEraAgree}/{s.OtherEraLines} lines of old or undated books agree with the size an old-book tier would take");
            Console.WriteLine($"Keys:               {string.Join(", ", s.ByStatus.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}"))}");
            foreach (var c in s.Classes)
                Console.WriteLine($"  class {c.Class}: {c.Agree}/{c.Lines} ({c.Share:P1}); keys {c.Keys}: {c.Active} active, {c.Shadow} shadow, {c.Paused} paused");
            foreach (var line in s.Changes.Take(200))
                Console.WriteLine($"  {line}");
            if (s.LaneMode != null)
                Console.WriteLine($"Lane container-sizes: {s.LaneMode} ({s.LaneReason}); {s.DecisionsRecorded} decisions recorded");
            foreach (var t in s.Tiers)
                Console.WriteLine($"Old-book tier {t.Tier}: {t.Agree}/{t.Lines} agree{(t.Mode != null ? $"; {t.Mode} ({t.Reason})" : string.Empty)}");
        }

        private static void PrintUndo(CatalogAdmissionService.AdmissionUndoReport undone)
        {
            Console.WriteLine($"\n=== Catalog admission undo report{(undone.DryRun ? " (DRY RUN — nothing saved)" : string.Empty)} ===");
            Console.WriteLine($"Run stamp:              {undone.RunStamp:O}");
            if (undone.UndoStamp is { } undoStamp)
                Console.WriteLine($"Undo stamp:             {undoStamp:O} (take this undo back with --admit --undo {undoStamp:O})");
            Console.WriteLine($"Demoted:                {undone.Demoted} (admitted foods back to PendingCuration)");
            Console.WriteLine($"Reapplied:              {undone.Reapplied} (values an earlier undo had put back)");
            Console.WriteLine($"Groups restored:        {undone.GroupsRestored}");
            Console.WriteLine($"Facts restored:         {undone.FactsRestored}");
            Console.WriteLine($"Descriptions removed:   {undone.DescriptionsRemoved} ({undone.DescriptionsRestored} restored)");
            Console.WriteLine($"Picks restored:         {undone.PicksRestored} ({undone.PicksWithdrawn} withdrawn again)");
            Console.WriteLine($"Skipped:                {undone.Skipped} (changed since the run)");
            foreach (var line in undone.Lines)
                Console.WriteLine($"  {line}");
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

                    services.AddScoped<CatalogAdmissionService>();
                    services.AddScoped<Nom.Orch.Interfaces.IBrandedLiftService, Nom.Orch.Services.BrandedLiftService>();
                    services.AddScoped<Nom.Orch.Interfaces.ILaneFrameService>(Nom.Orch.Services.LaneFrameService.Create);
                    services.AddScoped<Nom.Orch.Interfaces.IContainerSizeService, Nom.Orch.Services.ContainerSizeService>();
                    services.AddScoped<Nom.Orch.Interfaces.ILaneUndo, Nom.Orch.Services.ContainerSizeLaneUndo>();
                    services.AddScoped<ContainerSizeImportService>();
                    services.AddScoped<ContainerCorpusImportService>();
                    services.AddScoped<FoodGroupEnrichmentService>(sp => new FoodGroupEnrichmentService(
                        sp.GetRequiredService<ApplicationDbContext>(),
                        sp.GetRequiredService<ILogger<FoodGroupEnrichmentService>>(),
                        aiSettings.BatchSize > 0 ? aiSettings.BatchSize : 50));
                });
    }
}
