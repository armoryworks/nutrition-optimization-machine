using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;

namespace Nom.Import.Services
{
    /// <summary>
    /// Fills the <see cref="FdcNutrients"/> values onto ingredients that are already linked to a
    /// USDA food (by FdcId), reading one FDC bulk CSV directory (Foundation, SR Legacy or Branded;
    /// FDC ids are unique across them). Only nutrients an ingredient has no row for are written,
    /// so a stored value is never overwritten and a rerun writes nothing. Values come only from
    /// the FDC file and pass <see cref="FoodDataQualityValidator.CheckComponent"/>.
    /// Run: <c>dotnet run -- --backfill-nutrients &lt;csv-dir&gt; [--dry-run]</c>.
    /// A Foundation food reports only what USDA analysed for it, so
    /// <c>--supplement-nutrients &lt;foundation-dir&gt; &lt;sr-legacy-dir&gt; [--dry-run]</c> then fills
    /// what is still missing from the SR Legacy food USDA lists under the same NDB number.
    /// </summary>
    public class FdcNutrientBackfillService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<FdcNutrientBackfillService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public FdcNutrientBackfillService(ApplicationDbContext db, ILogger<FdcNutrientBackfillService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <param name="dryRun">Report what would be written without saving anything.</param>
        public async Task<BackfillReport> BackfillAsync(string csvDir, bool dryRun, CancellationToken ct = default)
        {
            var nutrientCsv = FindFile(csvDir, "food_nutrient.csv");
            var report = new BackfillReport { DryRun = dryRun };

            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);
            report.UnresolvedNutrients.AddRange(FdcNutrients.All.Select(s => s.Key).Where(k => !targets.ContainsKey(k)));

            var linked = await _db.Ingredients
                .Where(i => i.FdcId != null && !i.IsDeleted)
                .Select(i => new { i.Id, i.FdcId })
                .ToListAsync(ct);
            report.LinkedIngredients = linked.Count;
            var byFdc = linked.GroupBy(i => i.FdcId!).ToDictionary(g => g.Key, g => g.Select(i => i.Id).ToList());

            _logger.LogInformation("Reading {File} for {Count} FDC-linked ingredients…", nutrientCsv, linked.Count);
            var foods = FdcNutrients.Read(nutrientCsv, byFdc.Keys.ToHashSet());

            var have = (await _db.Set<IngredientNutrientEntity>()
                .IgnoreQueryFilters()
                .Where(n => n.Ingredient.FdcId != null)
                .Select(n => new { n.IngredientId, n.NutrientId })
                .ToListAsync(ct))
                .Select(n => (n.IngredientId, n.NutrientId)).ToHashSet();

            var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            report.RunStamp = now;
            var filled = new HashSet<long>();
            foreach (var (fdcId, food) in foods)
            {
                ct.ThrowIfCancellationRequested();
                var accepted = FdcNutrients.Accepted(food, _validator, r =>
                    report.DroppedValuesByReason[r] = report.DroppedValuesByReason.GetValueOrDefault(r) + 1).ToList();
                foreach (var ingredientId in byFdc[fdcId])
                {
                    report.MatchedInFile++;
                    foreach (var (key, amount) in accepted)
                    {
                        if (!targets.TryGetValue(key, out var t) || !have.Add((ingredientId, t.NutrientId))) continue;
                        if (!dryRun)
                        {
                            _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
                            {
                                IngredientId = ingredientId,
                                NutrientId = t.NutrientId,
                                Amount = amount,
                                MeasurementId = t.MeasurementId,
                                CreatedDate = now,
                            });
                        }
                        report.RowsByNutrient[key] = report.RowsByNutrient.GetValueOrDefault(key) + 1;
                        report.Rows++;
                        filled.Add(ingredientId);
                    }
                }
            }
            report.IngredientsFilled = filled.Count;

            if (!dryRun) await _db.SaveChangesAsync(ct);
            _logger.LogInformation("FDC nutrient backfill{DryRun}: {Rows} rows on {Ingredients} ingredients.",
                dryRun ? " (dry run)" : string.Empty, report.Rows, report.IngredientsFilled);
            return report;
        }

        /// <summary>
        /// Fills nutrients an ingredient linked to a Foundation food still lacks from the SR Legacy
        /// food with the same NDB number (foundation_food.csv and sr_legacy_food.csv) or, for a
        /// Foundation food the file gives no NDB number, from the one SR Legacy food with the very
        /// same description. A pair whose descriptions differ in how the food was cooked (raw
        /// against grilled) is passed over. The
        /// four macros are never taken from the other record; a part of fat or carbohydrate is checked
        /// against the ingredient's own stored total. Each row carries the SR Legacy FDC id it came
        /// from, and one run shares one <c>CreatedDate</c>, so it can be told apart and undone.
        /// </summary>
        /// <param name="dryRun">Report what would be written without saving anything.</param>
        public async Task<BackfillReport> SupplementFromSrLegacyAsync(string foundationDir, string srLegacyDir, bool dryRun, CancellationToken ct = default)
        {
            var report = new BackfillReport { DryRun = dryRun };
            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);
            report.UnresolvedNutrients.AddRange(FdcNutrients.All.Select(s => s.Key).Where(k => !targets.ContainsKey(k)));

            var srByNdb = ReadNdbNumbers(FindFile(srLegacyDir, "sr_legacy_food.csv"))
                .GroupBy(x => x.Ndb).ToDictionary(g => g.Key, g => g.First().FdcId);
            var foundationNdb = ReadNdbNumbers(FindFile(foundationDir, "foundation_food.csv"))
                .GroupBy(x => x.FdcId).ToDictionary(g => g.Key, g => g.First().Ndb);
            var foundationNames = ReadDescriptions(FindFile(foundationDir, "food.csv"), "foundation_food");
            var srNames = ReadDescriptions(FindFile(srLegacyDir, "food.csv"), "sr_legacy_food");
            var srByName = srNames.GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
            var srByFoundation = new Dictionary<string, string>();
            foreach (var (fdcId, name) in foundationNames)
            {
                if (foundationNdb.TryGetValue(fdcId, out var ndb))
                {
                    if (srByNdb.TryGetValue(ndb, out var sameNumber)) srByFoundation[fdcId] = sameNumber;
                }
                else if (srByName.TryGetValue(name, out var sameName))
                {
                    srByFoundation[fdcId] = sameName;
                }
            }

            var paired = (await _db.Ingredients
                .Where(i => i.FdcId != null && !i.IsDeleted)
                .Select(i => new { i.Id, i.FdcId })
                .ToListAsync(ct))
                .Where(i => srByFoundation.ContainsKey(i.FdcId!))
                .ToList();
            var linked = paired
                .Where(i => CookingWords(foundationNames.GetValueOrDefault(i.FdcId!))
                    .SetEquals(CookingWords(srNames.GetValueOrDefault(srByFoundation[i.FdcId!]))))
                .ToList();
            report.PreparationDiffers = paired.Count - linked.Count;
            report.LinkedIngredients = linked.Count;
            var ids = linked.Select(i => i.Id).ToList();

            var foods = FdcNutrients.Read(FindFile(srLegacyDir, "food_nutrient.csv"), linked.Select(i => srByFoundation[i.FdcId!]).ToHashSet());

            var stored = await _db.Set<IngredientNutrientEntity>()
                .IgnoreQueryFilters()
                .Where(n => ids.Contains(n.IngredientId))
                .Select(n => new { n.IngredientId, n.NutrientId, n.Amount, n.IsDeleted })
                .ToListAsync(ct);
            var have = stored.Select(n => (n.IngredientId, n.NutrientId)).ToHashSet();
            decimal? Stored(long ingredientId, string key) => targets.TryGetValue(key, out var t)
                ? stored.FirstOrDefault(n => n.IngredientId == ingredientId && n.NutrientId == t.NutrientId && !n.IsDeleted)?.Amount
                : null;

            var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            report.RunStamp = now;
            var filled = new HashSet<long>();
            foreach (var ingredient in linked)
            {
                ct.ThrowIfCancellationRequested();
                var srFdcId = srByFoundation[ingredient.FdcId!];
                if (!foods.TryGetValue(srFdcId, out var food)) continue;
                report.MatchedInFile++;
                var fat = Stored(ingredient.Id, FdcNutrients.Fat);
                var carb = Stored(ingredient.Id, FdcNutrients.Carb);
                foreach (var (key, amount) in food.Values)
                {
                    if (FdcNutrients.Macros.Contains(key) || !targets.TryGetValue(key, out var t)) continue;
                    if (have.Contains((ingredient.Id, t.NutrientId))) continue;
                    if (_validator.CheckComponent(key, amount, fat, carb) is { } reason)
                    {
                        report.DroppedValuesByReason[reason] = report.DroppedValuesByReason.GetValueOrDefault(reason) + 1;
                        continue;
                    }
                    have.Add((ingredient.Id, t.NutrientId));
                    if (!dryRun)
                    {
                        _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
                        {
                            IngredientId = ingredient.Id,
                            NutrientId = t.NutrientId,
                            Amount = amount,
                            MeasurementId = t.MeasurementId,
                            FdcId = srFdcId,
                            CreatedDate = now,
                        });
                    }
                    report.RowsByNutrient[key] = report.RowsByNutrient.GetValueOrDefault(key) + 1;
                    report.Rows++;
                    filled.Add(ingredient.Id);
                }
            }
            report.IngredientsFilled = filled.Count;

            if (!dryRun) await _db.SaveChangesAsync(ct);
            _logger.LogInformation("SR Legacy nutrient supplement{DryRun}: {Rows} rows on {Ingredients} ingredients.",
                dryRun ? " (dry run)" : string.Empty, report.Rows, report.IngredientsFilled);
            return report;
        }

        private static readonly string[] CookingTerms =
        {
            "cooked", "grilled", "boiled", "roasted", "fried", "baked", "broiled", "steamed", "braised",
            "stewed", "toasted", "heated", "microwaved", "sauteed", "smoked",
        };

        private static HashSet<string> CookingWords(string? description) =>
            (description ?? string.Empty).ToLowerInvariant()
                .Split(new[] { ' ', ',', '(', ')', '/', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => CookingTerms.Contains(w))
                .ToHashSet();

        private static Dictionary<string, string> ReadDescriptions(string path, string dataType)
        {
            var names = new Dictionary<string, string>();
            using var reader = new StreamReader(path);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length >= 3 && f[1] == dataType) names[f[0]] = f[2];
            }
            return names;
        }

        private static List<(string FdcId, string Ndb)> ReadNdbNumbers(string path)
        {
            var rows = new List<(string, string)>();
            using var reader = new StreamReader(path);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length >= 2 && f[0].Length > 0 && f[1].Length > 0) rows.Add((f[0], f[1]));
            }
            return rows;
        }

        private static string FindFile(string dir, string name)
        {
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            return Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException($"{name} not found under {dir}");
        }

        public sealed class BackfillReport
        {
            public bool DryRun { get; set; }
            public DateTime RunStamp { get; set; }
            public int LinkedIngredients { get; set; }
            public int PreparationDiffers { get; set; }
            public int MatchedInFile { get; set; }
            public int IngredientsFilled { get; set; }
            public int Rows { get; set; }
            public Dictionary<string, int> RowsByNutrient { get; } = new();
            public Dictionary<string, int> DroppedValuesByReason { get; } = new();
            public List<string> UnresolvedNutrients { get; } = new();
        }
    }
}
