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
        // Energy: many Foundation foods report only the Atwater-factor energies (2048 specific,
        // 2047 general), not the general Energy (1008) — accept any, preferring the most specific.
        private const long NutEnergy = 1008, NutEnergyAtwaterSpecific = 2048, NutEnergyAtwaterGeneral = 2047;
        private const long NutProtein = 1003, NutCarb = 1005, NutFat = 1004;

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

        public async Task<ImportReport> ImportAsync(string csvDir, CancellationToken ct = default)
        {
            var foodCsv = FindFile(csvDir, "food.csv");
            var nutrientCsv = FindFile(csvDir, "food_nutrient.csv");

            _logger.LogInformation("Reading foundation foods from {Dir}", csvDir);
            var foods = ReadFoundationFoods(foodCsv);                 // fdc_id → (desc, categoryId)
            var nutrients = ReadFoodNutrients(nutrientCsv, foods.Keys.ToHashSet()); // fdc_id → macros

            var portions = ReadPortions(TryFindFile(csvDir, "food_portion.csv"), foods.Keys.ToHashSet());
            var nutrientIds = await ResolveNutrientIdsAsync(ct);
            var (gramId, kcalId) = await ResolveMeasurementIdsAsync(ct);

            var existingFdcIds = (await _db.Ingredients
                .Where(i => i.FdcId != null).Select(i => i.FdcId!).ToListAsync(ct)).ToHashSet();
            // Ingredient.Name is unique — dedupe against existing names and within this run.
            var seenNames = (await _db.Ingredients.Select(i => i.Name).ToListAsync(ct))
                .Select(n => n.ToLowerInvariant()).ToHashSet();

            var report = new ImportReport { TotalFoundation = foods.Count };
            var pendingNutrition = new List<(IngredientEntity Ingredient, Macros Macros)>();

            foreach (var (fdcId, food) in foods)
            {
                ct.ThrowIfCancellationRequested();
                if (existingFdcIds.Contains(fdcId)) { report.SkippedExisting++; continue; }
                if (!nutrients.TryGetValue(fdcId, out var m)) { report.RejectedByReason["nutrients_missing"] = report.RejectedByReason.GetValueOrDefault("nutrients_missing") + 1; report.Rejected++; continue; }

                var result = _validator.Validate(new FoodQualityInput(
                    food.Description, m.Kcal, m.Protein, m.Carb, m.Fat));
                if (!result.Accepted)
                {
                    report.Rejected++;
                    foreach (var reason in result.Reasons)
                        report.RejectedByReason[reason] = report.RejectedByReason.GetValueOrDefault(reason) + 1;
                    continue;
                }

                var name = Truncate(food.Description, 2000);
                if (!seenNames.Add(name.ToLowerInvariant())) { report.SkippedDuplicateName++; continue; }

                long? group = (food.CategoryId is int c && CategoryToGroup.TryGetValue(c, out var g))
                    ? g : FoodGroupHeuristics.ClassifyFoodGroup(food.Description);

                var ingredient = new IngredientEntity
                {
                    Name = name,
                    FdcId = fdcId,
                    FdcDataType = "foundation_food",
                    CurationStatusId = PendingCuration,
                    FoodGroupId = group,
                    // NOTE: must be TryGetValue — GetValueOrDefault on a decimal dictionary yields
                    // 0, which would silently zero out the food's nutrition.
                    ReferenceServingGrams = portions.TryGetValue(fdcId, out var refGrams) ? refGrams : null,
                    CreatedDate = DateTime.UtcNow,
                };

                _db.Ingredients.Add(ingredient);
                pendingNutrition.Add((ingredient, m));
                if (ingredient.ReferenceServingGrams.HasValue) report.WithReferenceServing++;

                report.Accepted++;
                if (group.HasValue) report.Classified++;
            }

            // Phase 1: persist the ingredients so identity values exist.
            await _db.SaveChangesAsync(ct);

            // Phase 2: attach per-100g nutrient facts against the now-known ingredient ids.
            // (FDC amounts are per 100 g; ReferenceServingGrams carries the standard portion when
            // known, and per-person scaling happens downstream in the portions engine.)
            foreach (var (ing, m) in pendingNutrition)
            {
                AddNutrient(ing.Id, nutrientIds, "calories", kcalId, m.Kcal, ref report.NutrientRows);
                AddNutrient(ing.Id, nutrientIds, "protein", gramId, m.Protein, ref report.NutrientRows);
                AddNutrient(ing.Id, nutrientIds, "carb", gramId, m.Carb, ref report.NutrientRows);
                AddNutrient(ing.Id, nutrientIds, "fat", gramId, m.Fat, ref report.NutrientRows);
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("FDC foundation import: {Accepted} accepted, {Rejected} rejected, {Skipped} existing.",
                report.Accepted, report.Rejected, report.SkippedExisting);
            return report;
        }


        private void AddNutrient(
            long ingredientId, Dictionary<string, long> nutrientIds, string key,
            long measurementId, decimal? amount, ref int counter)
        {
            if (amount is not { } a) return;
            if (!nutrientIds.TryGetValue(key, out var nutrientId)) return;
            _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
            {
                IngredientId = ingredientId,
                NutrientId = nutrientId,
                Amount = a,                 // per 100 g
                MeasurementId = measurementId,
                CreatedDate = DateTime.UtcNow,
            });
            counter++;
        }

        /// <summary>
        /// Maps our four macro keys to the seeded Nutrient rows by name, using the same patterns
        /// the meal-plan nutrition display matches on, so imported facts are actually found.
        /// </summary>
        private async Task<Dictionary<string, long>> ResolveNutrientIdsAsync(CancellationToken ct)
        {
            var all = await _db.Set<NutrientEntity>()
                .Select(n => new { n.Id, n.Name }).ToListAsync(ct);
            var map = new Dictionary<string, long>();

            long? Find(params string[] patterns) => all
                .Where(n => patterns.Any(p => n.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n.Name.Length)
                .Select(n => (long?)n.Id)
                .FirstOrDefault();

            if (Find("calorie", "energy", "kcal") is { } cal) map["calories"] = cal;
            if (Find("protein") is { } pro) map["protein"] = pro;
            if (Find("carbohydrate", "carbs") is { } carb) map["carb"] = carb;
            if (Find("total lipid", "fat") is { } fat) map["fat"] = fat;

            foreach (var key in new[] { "calories", "protein", "carb", "fat" })
                if (!map.ContainsKey(key))
                    _logger.LogWarning("No seeded Nutrient row matched '{Key}' — those amounts will be skipped.", key);
            return map;
        }

        private async Task<(long GramId, long KcalId)> ResolveMeasurementIdsAsync(CancellationToken ct)
        {
            var all = await _db.Set<Nom.Data.Measurement.MeasurementEntity>()
                .Select(m => new { m.Id, m.Name }).ToListAsync(ct);
            long Pick(params string[] patterns) => all
                .Where(m => patterns.Any(p => m.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(m => m.Name.Length)
                .Select(m => m.Id)
                .FirstOrDefault();
            return (Pick("gram"), Pick("kilocalorie", "calorie"));
        }

        /// <summary>
        /// Standard reference portion in grams per food. Prefers a single-unit portion
        /// (amount = 1), else the median gram weight, so an outlier ("1 whole cake") doesn't win.
        /// </summary>
        private static Dictionary<string, decimal> ReadPortions(string? path, HashSet<string> fdcIds)
        {
            var result = new Dictionary<string, decimal>();
            if (path == null || !File.Exists(path)) return result;

            var byFood = new Dictionary<string, List<(decimal Amount, decimal Grams)>>();
            using var reader = new StreamReader(path);
            reader.ReadLine(); // header: id,fdc_id,seq_num,amount,measure_unit_id,portion_description,modifier,gram_weight,...
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = SplitCsv(line);
                if (f.Length < 8) continue;
                if (!fdcIds.Contains(f[1])) continue;
                if (!decimal.TryParse(f[7], System.Globalization.CultureInfo.InvariantCulture, out var grams) || grams <= 0) continue;
                decimal.TryParse(f[3], System.Globalization.CultureInfo.InvariantCulture, out var amount);
                byFood.TryAdd(f[1], new List<(decimal, decimal)>());
                byFood[f[1]].Add((amount, grams));
            }

            foreach (var (fdc, list) in byFood)
            {
                var single = list.Where(x => x.Amount == 1m).Select(x => x.Grams).OrderBy(g => g).ToList();
                var pool = single.Count > 0 ? single : list.Select(x => x.Grams).OrderBy(g => g).ToList();
                if (pool.Count == 0) continue;
                result[fdc] = pool[pool.Count / 2]; // median
            }
            return result;
        }

        private sealed record FoodRow(string Description, int? CategoryId);
        private sealed record Macros(decimal? Kcal, decimal? Protein, decimal? Carb, decimal? Fat);

        private static Dictionary<string, FoodRow> ReadFoundationFoods(string path)
        {
            var result = new Dictionary<string, FoodRow>();
            using var reader = new StreamReader(path);
            reader.ReadLine(); // header: fdc_id,data_type,description,food_category_id,publication_date
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = SplitCsv(line);
                if (f.Length < 4 || f[1] != "foundation_food") continue;
                int? cat = int.TryParse(f[3], out var c) ? c : (int?)null;
                result[f[0]] = new FoodRow(f[2], cat);
            }
            return result;
        }

        private static Dictionary<string, Macros> ReadFoodNutrients(string path, HashSet<string> fdcIds)
        {
            // k = general Energy (1008); ks = Atwater specific (2048); kg = Atwater general (2047).
            var acc = new Dictionary<string, (decimal? k, decimal? ks, decimal? kg, decimal? p, decimal? c, decimal? f)>();
            using var reader = new StreamReader(path);
            reader.ReadLine(); // header: id,fdc_id,nutrient_id,amount,...
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = SplitCsv(line);
                if (f.Length < 4) continue;
                var fdc = f[1];
                if (!fdcIds.Contains(fdc)) continue;
                if (!long.TryParse(f[2], out var nutId)) continue;
                if (!decimal.TryParse(f[3], System.Globalization.CultureInfo.InvariantCulture, out var amt)) continue;

                var cur = acc.GetValueOrDefault(fdc);
                if (nutId == NutEnergy) cur.k = amt;
                else if (nutId == NutEnergyAtwaterSpecific) cur.ks = amt;
                else if (nutId == NutEnergyAtwaterGeneral) cur.kg = amt;
                else if (nutId == NutProtein) cur.p = amt;
                else if (nutId == NutCarb) cur.c = amt;
                else if (nutId == NutFat) cur.f = amt;
                else continue;
                acc[fdc] = cur;
            }
            return acc.ToDictionary(kv => kv.Key,
                kv => new Macros(kv.Value.k ?? kv.Value.ks ?? kv.Value.kg, kv.Value.p, kv.Value.c, kv.Value.f));
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

        /// <summary>Minimal RFC-4180 CSV field splitter (FDC quotes every field; "" escapes a quote).</summary>
        private static string[] SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '"') inQuotes = true;
                    else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        public sealed class ImportReport
        {
            public int TotalFoundation { get; set; }
            public int Accepted { get; set; }
            public int Classified { get; set; }
            public int Rejected { get; set; }
            public int SkippedExisting { get; set; }
            public int SkippedDuplicateName { get; set; }
            public int WithReferenceServing { get; set; }
            public int NutrientRows;
            public Dictionary<string, int> RejectedByReason { get; } = new();
        }
    }
}
