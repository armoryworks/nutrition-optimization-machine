using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Data.Reference;

namespace Nom.Import.Services
{
    /// <summary>
    /// Focused loader for USDA FDC Foundation Foods (CC0). Reads the bulk CSVs, applies the
    /// quality gate (per-100g plausibility + Atwater), classifies each food by its FDC food
    /// category (authoritative — far better than name keywords), and lands accepted foods as
    /// PendingCuration ingredients with per-100g macro nutrients. Idempotent by FdcId.
    /// Reports accepted/rejected so quality can be gauged in staging before touching prod.
    /// Run: `dotnet run -- --import-fdc &lt;csv-dir&gt;`.
    /// </summary>
    public class FdcFoundationImportService
    {
        private const long PendingCuration = 9001; // CurationStatusEnum.PendingCuration
        private const long Curated = 9003;         // CurationStatusEnum.Curated

        public const string FoundationFood = "foundation_food";
        public const string AttachField = "fdc_attach";
        public const string SrLegacyFood = "sr_legacy_food";

        private readonly ApplicationDbContext _db;
        private readonly ILogger<FdcFoundationImportService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public FdcFoundationImportService(ApplicationDbContext db, ILogger<FdcFoundationImportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // FDC food_category_id → our food group. Mixed/ambiguous categories map to null (unclassified).
        private static readonly Dictionary<int, long> CategoryToGroup = new()
        {
            [1] = (long)FoodGroupEnum.Dairy,           // Dairy and Egg Products
            [4] = (long)FoodGroupEnum.FatsOils,        // Fats and Oils
            [5] = (long)FoodGroupEnum.ProteinFoods,    // Poultry
            [7] = (long)FoodGroupEnum.ProteinFoods,    // Sausages and Luncheon Meats
            [8] = (long)FoodGroupEnum.Grains,          // Breakfast Cereals
            [9] = (long)FoodGroupEnum.Fruits,          // Fruits and Fruit Juices
            [10] = (long)FoodGroupEnum.ProteinFoods,   // Pork
            [11] = (long)FoodGroupEnum.Vegetables,     // Vegetables
            [12] = (long)FoodGroupEnum.NutsSeeds,      // Nut and Seed Products
            [13] = (long)FoodGroupEnum.ProteinFoods,   // Beef
            [14] = (long)FoodGroupEnum.Beverages,      // Beverages
            [15] = (long)FoodGroupEnum.ProteinFoods,   // Finfish and Shellfish
            [16] = (long)FoodGroupEnum.Legumes,        // Legumes
            [17] = (long)FoodGroupEnum.ProteinFoods,   // Lamb, Veal, Game
            [18] = (long)FoodGroupEnum.Grains,         // Baked Products
            [19] = (long)FoodGroupEnum.SweetsSnacks,   // Sweets
            [20] = (long)FoodGroupEnum.Grains,         // Cereal Grains and Pasta
            [23] = (long)FoodGroupEnum.SweetsSnacks,   // Snacks
            [28] = (long)FoodGroupEnum.Beverages,      // Alcoholic Beverages
        };

        /// <summary>
        /// Whether a Foundation food in this FDC category is eaten as-is. Foundation is largely raw
        /// commodities, so this is not "is it food" but "would someone put it in a meal slot without
        /// cooking it": an apple yes, raw chicken and dry pasta no. Unlisted categories stay null.
        /// </summary>
        private static readonly Dictionary<int, bool> CategoryDirectlyEdible = new()
        {
            [1] = true,    // Dairy and Egg Products
            [2] = false,   // Spices and Herbs
            [3] = true,    // Baby Foods
            [4] = false,   // Fats and Oils
            [5] = false,   // Poultry — raw
            [7] = true,    // Sausages and Luncheon Meats — ready to eat
            [8] = true,    // Breakfast Cereals
            [9] = true,    // Fruits and Fruit Juices
            [10] = false,  // Pork — raw
            [11] = true,   // Vegetables
            [12] = true,   // Nut and Seed Products
            [13] = false,  // Beef — raw
            [14] = true,   // Beverages
            [15] = false,  // Finfish and Shellfish — raw
            // 16 Legumes is deliberately absent: it mixes dry commodities that need cooking with
            // ready-to-eat products (hummus, peanut butter), so the category cannot decide. Leaving
            // it unknown is better than asserting the wrong answer for half the category.
            [17] = false,  // Lamb, Veal, Game — raw
            [18] = true,   // Baked Products
            [19] = true,   // Sweets
            [20] = false,  // Cereal Grains and Pasta — needs cooking
            [23] = true,   // Snacks
            [28] = true,   // Alcoholic Beverages
        };

        /// <param name="curated">
        /// Land accepted foods as Curated rather than PendingCuration. Meal-plan food-group
        /// top-up and the severe-restriction safety gate only draw from Curated ingredients, so
        /// foods imported with the default (false) stay invisible to planning until reviewed.
        /// Defensible for Foundation Foods specifically: they are USDA-authored reference data
        /// with validated nutrition, unlike the manufacturer-submitted Branded catalog.
        /// </param>
        /// <param name="dataType">
        /// Which FDC data type to read from food.csv: <see cref="FoundationFood"/> (default) or
        /// <see cref="SrLegacyFood"/> (USDA Standard Reference — the generic staples recipes name).
        /// </param>
        public async Task<ImportReport> ImportAsync(string csvDir, bool curated = false, CancellationToken ct = default, string dataType = FoundationFood)
        {
            var foodCsv = FindFile(csvDir, "food.csv");
            var nutrientCsv = FindFile(csvDir, "food_nutrient.csv");

            _logger.LogInformation("Reading foundation foods from {Dir}", csvDir);
            var foods = ReadFoods(foodCsv, dataType);                 // fdc_id → (desc, categoryId)
            var nutrients = FdcNutrients.Read(nutrientCsv, foods.Keys.ToHashSet());

            var portionRows = UsdaPortionFile.Read(TryFindFile(csvDir, "food_portion.csv"), foods.Keys.ToHashSet());
            var portions = ReferenceServings(portionRows);
            var densities = Densities(portionRows);
            var itemWeights = ItemWeights(portionRows, foods);
            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);

            var existingByFdc = (await _db.Ingredients
                .Where(i => i.FdcId != null).Select(i => new { i.Id, i.FdcId }).ToListAsync(ct))
                .GroupBy(i => i.FdcId!).ToDictionary(g => g.Key, g => g.First().Id);
            var existingFdcIds = existingByFdc.Keys.ToHashSet();
            // Ingredient.Name is unique — dedupe against existing names and within this run.
            var catalogByName = (await _db.Ingredients
                .Where(i => !i.IsDeleted)
                .Select(i => new { i.Id, i.Name, i.FdcId }).ToListAsync(ct))
                .GroupBy(i => i.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => (g.First().Id, g.First().FdcId));
            var seenNames = catalogByName.Keys.ToHashSet();
            var attachRequested = (await _db.FoodCatalogProposals
                .Where(p => p.Field == AttachField && p.IngredientId != null)
                .Select(p => p.IngredientId!.Value).ToListAsync(ct)).ToHashSet();
            var attach = new List<(long IngredientId, string FdcId, string Name, FdcFoodNutrients Nutrients, decimal? Serving, decimal? Density, decimal? Item)>();

            var report = new ImportReport { TotalFoundation = foods.Count };
            var pendingNutrition = new List<(IngredientEntity Ingredient, FdcFoodNutrients Nutrients)>();

            var backfill = new List<(long IngredientId, FdcFoodNutrients Nutrients)>();
            foreach (var (fdcId, food) in foods)
            {
                ct.ThrowIfCancellationRequested();
                if (existingFdcIds.Contains(fdcId))
                {
                    report.SkippedExisting++;
                    if (nutrients.TryGetValue(fdcId, out var known)) backfill.Add((existingByFdc[fdcId], known));
                    continue;
                }
                if (!nutrients.TryGetValue(fdcId, out var m)) { report.RejectedByReason["nutrients_missing"] = report.RejectedByReason.GetValueOrDefault("nutrients_missing") + 1; report.Rejected++; continue; }

                var result = _validator.Validate(new FoodQualityInput(
                    food.Description, m.Kcal, m.Protein, m.Carb, m.Fat, UsedInSmallAmounts(food)));
                if (!result.Accepted)
                {
                    report.Rejected++;
                    foreach (var reason in result.Reasons)
                        report.RejectedByReason[reason] = report.RejectedByReason.GetValueOrDefault(reason) + 1;
                    continue;
                }

                var name = Truncate(food.Description, 2000);
                if (!seenNames.Add(name.ToLowerInvariant()))
                {
                    report.SkippedDuplicateName++;
                    if (catalogByName.TryGetValue(name.ToLowerInvariant(), out var same) && same.FdcId == null && attachRequested.Add(same.Id))
                    {
                        attach.Add((same.Id, fdcId, name, m, portions.TryGetValue(fdcId, out var sg) ? sg : null, densities.TryGetValue(fdcId, out var sd) ? sd : null,
                            itemWeights.TryGetValue(fdcId, out var si) ? si : null));
                    }
                    continue;
                }

                long? group = (food.CategoryId is int c && CategoryToGroup.TryGetValue(c, out var g))
                    ? g : FoodGroupHeuristics.ClassifyFoodGroup(food.Description);

                var ingredient = new IngredientEntity
                {
                    Name = name,
                    FdcId = fdcId,
                    FdcDataType = dataType,
                    CurationStatusId = curated ? Curated : PendingCuration,
                    FoodGroupId = group,
                    IsWholeFood = food.CategoryId is int ec && CategoryDirectlyEdible.TryGetValue(ec, out var edible)
                        ? edible : null,
                    // NOTE: must be TryGetValue — GetValueOrDefault on a decimal dictionary yields
                    // 0, which would silently zero out the food's nutrition.
                    ReferenceServingGrams = portions.TryGetValue(fdcId, out var refGrams) ? refGrams : null,
                    GramsPerMilliliter = densities.TryGetValue(fdcId, out var density) ? density : null,
                    GramsPerItem = itemWeights.TryGetValue(fdcId, out var item) ? item : null,
                    CreatedDate = DateTime.UtcNow,
                };

                _db.Ingredients.Add(ingredient);
                pendingNutrition.Add((ingredient, m));
                if (ingredient.ReferenceServingGrams.HasValue) report.WithReferenceServing++;
                if (ingredient.GramsPerMilliliter.HasValue) report.WithDensity++;

                report.Accepted++;
                if (group.HasValue) report.Classified++;
            }

            // Phase 1: persist the ingredients so identity values exist.
            await _db.SaveChangesAsync(ct);

            // Phase 2: attach per-100g nutrient facts against the now-known ingredient ids.
            // (FDC amounts are per 100 g; ReferenceServingGrams carries the standard portion when
            // known, and per-person scaling happens downstream in the portions engine.)
            foreach (var (ing, m) in pendingNutrition)
                foreach (var (key, amount) in FdcNutrients.Accepted(m, _validator, r => CountDropped(report, r)))
                    AddNutrient(ing.Id, targets, key, amount, ref report.NutrientRows);

            if (backfill.Count > 0)
            {
                var ids = backfill.Select(b => b.IngredientId).ToList();
                var have = (await _db.Set<IngredientNutrientEntity>()
                    .IgnoreQueryFilters()
                    .Where(n => ids.Contains(n.IngredientId))
                    .Select(n => new { n.IngredientId, n.NutrientId })
                    .ToListAsync(ct))
                    .Select(n => (n.IngredientId, n.NutrientId)).ToHashSet();
                foreach (var (ingredientId, m) in backfill)
                    foreach (var (key, amount) in FdcNutrients.Accepted(m, _validator, r => CountDropped(report, r)))
                        if (targets.TryGetValue(key, out var t) && !have.Contains((ingredientId, t.NutrientId)))
                            AddNutrient(ingredientId, targets, key, amount, ref report.BackfilledNutrientRows);
            }
            var densityBackfill = existingByFdc
                .Where(e => densities.ContainsKey(e.Key))
                .ToDictionary(e => e.Value, e => densities[e.Key]);
            if (densityBackfill.Count > 0)
            {
                var ids = densityBackfill.Keys.ToList();
                foreach (var ing in await _db.Ingredients.Where(i => ids.Contains(i.Id) && i.GramsPerMilliliter == null).ToListAsync(ct))
                {
                    ing.GramsPerMilliliter = densityBackfill[ing.Id];
                    report.BackfilledDensities++;
                }
            }

            foreach (var (ingredientId, fdc, name, m, serving, density, item) in attach)
            {
                var facts = new List<object>();
                foreach (var (key, amount) in FdcNutrients.Accepted(m, _validator))
                    if (targets.TryGetValue(key, out var t))
                        facts.Add(new { nutrientId = t.NutrientId, amount, measurementId = t.MeasurementId });

                _db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                {
                    Action = FoodProposalAction.Update,
                    IngredientId = ingredientId,
                    FdcId = fdc,
                    Field = AttachField,
                    CurrentValue = name,
                    ProposedValue = System.Text.Json.JsonSerializer.Serialize(new { fdcDataType = dataType, referenceServingGrams = serving, gramsPerMilliliter = density, gramsPerItem = item, nutrients = facts }),
                    Confidence = 1m,
                    Reason = $"Same name as USDA {dataType} food {fdc}: attach its {facts.Count} nutrient values",
                    Source = $"fdc:{fdc}",
                    Batch = "fdc-attach",
                    Status = FoodProposalStatus.Pending,
                    CreatedDate = DateTime.UtcNow,
                });
                report.AttachProposals++;
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("FDC foundation import: {Accepted} accepted, {Rejected} rejected, {Skipped} existing.",
                report.Accepted, report.Rejected, report.SkippedExisting);
            return report;
        }


        private void AddNutrient(
            long ingredientId, Dictionary<string, FdcNutrients.Target> targets, string key,
            decimal amount, ref int counter)
        {
            if (!targets.TryGetValue(key, out var target)) return;
            _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
            {
                IngredientId = ingredientId,
                NutrientId = target.NutrientId,
                Amount = amount,
                MeasurementId = target.MeasurementId,
                CreatedDate = DateTime.UtcNow,
            });
            counter++;
        }

        private static void CountDropped(ImportReport report, string reason) =>
            report.DroppedValuesByReason[reason] = report.DroppedValuesByReason.GetValueOrDefault(reason) + 1;

        /// <summary>
        /// Standard reference portion in grams per food. Prefers a single-unit portion
        /// (amount = 1), else the median gram weight, so an outlier ("1 whole cake") doesn't win.
        /// </summary>
        private static Dictionary<string, decimal> ReferenceServings(Dictionary<string, List<UsdaPortion>> portionRows)
        {
            var result = new Dictionary<string, decimal>();
            foreach (var (fdc, rows) in portionRows)
            {
                var list = rows.Where(x => x.Grams > 0).ToList();
                var single = list.Where(x => x.Amount == 1m).Select(x => x.Grams).OrderBy(g => g).ToList();
                var pool = single.Count > 0 ? single : list.Select(x => x.Grams).OrderBy(g => g).ToList();
                if (pool.Count == 0) continue;
                result[fdc] = pool[pool.Count / 2];
            }
            return result;
        }

        private static Dictionary<string, decimal> ItemWeights(Dictionary<string, List<UsdaPortion>> portionRows, Dictionary<string, FoodRow> foods)
        {
            var result = new Dictionary<string, decimal>();
            foreach (var (fdc, rows) in portionRows)
                if (UsdaCountWeight.GramsPerCount(rows, foods.TryGetValue(fdc, out var food) ? food.Description : null) is { } item)
                    result[fdc] = item.Grams;
            return result;
        }

        private static Dictionary<string, decimal> Densities(Dictionary<string, List<UsdaPortion>> portionRows)
        {
            var result = new Dictionary<string, decimal>();
            foreach (var (fdc, list) in portionRows)
                if (UsdaVolumeDensity.GramsPerMilliliter(list) is { } d) result[fdc] = d;
            return result;
        }

        private sealed record FoodRow(string Description, int? CategoryId);

        private const int SpicesAndHerbs = 2;

        private static bool UsedInSmallAmounts(FoodRow food) =>
            food.CategoryId == SpicesAndHerbs || food.Description.StartsWith("Leavening agents,", StringComparison.Ordinal);

        private static Dictionary<string, FoodRow> ReadFoods(string path, string dataType)
        {
            var result = new Dictionary<string, FoodRow>();
            using var reader = new StreamReader(path);
            reader.ReadLine(); // header: fdc_id,data_type,description,food_category_id,publication_date
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length < 4 || f[1] != dataType) continue;
                int? cat = int.TryParse(f[3], out var c) ? c : (int?)null;
                result[f[0]] = new FoodRow(f[2], cat);
            }
            return result;
        }

        private static string? TryFindFile(string dir, string name)
        {
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            return Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string FindFile(string dir, string name)
        {
            if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
            var hit = Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
            return hit ?? throw new FileNotFoundException($"{name} not found under {dir}");
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);


        public sealed class ImportReport
        {
            public int TotalFoundation { get; set; }
            public int Accepted { get; set; }
            public int Classified { get; set; }
            public int Rejected { get; set; }
            public int SkippedExisting { get; set; }
            public int SkippedDuplicateName { get; set; }
            public int WithReferenceServing { get; set; }
            public int WithDensity { get; set; }
            public int BackfilledDensities { get; set; }
            public int NutrientRows;
            public int BackfilledNutrientRows;
            public int AttachProposals { get; set; }
            public Dictionary<string, int> RejectedByReason { get; } = new();
            public Dictionary<string, int> DroppedValuesByReason { get; } = new();
        }
    }
}
