using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Loader for USDA FDC Branded Foods (CC0) — the manufacturer-submitted catalog that carries
    /// the packaged products people actually eat as standalone items (protein bars, frozen
    /// dinners, yogurts). It is far noisier than Foundation, so every record goes through the
    /// quality gate, is classified from its <c>branded_food_category</c> (much more reliable than
    /// the product name), and lands as PendingCuration for review.
    ///
    /// The dataset is ~2M rows and multi-GB, so all three CSVs are streamed line by line and the
    /// candidate set is bounded by <c>limit</c>. Run:
    /// <c>dotnet run -- --import-fdc-branded &lt;csv-dir&gt; [--limit N]</c>.
    /// </summary>
    public class FdcBrandedImportService
    {
        private const long PendingCuration = 9001;
        private const long NutEnergy = 1008, NutEnergyAtwaterSpecific = 2048, NutEnergyAtwaterGeneral = 2047;
        private const long NutProtein = 1003, NutCarb = 1005, NutFat = 1004;

        private readonly ApplicationDbContext _db;
        private readonly ILogger<FdcBrandedImportService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public FdcBrandedImportService(ApplicationDbContext db, ILogger<FdcBrandedImportService> logger)
        {
            _db = db;
            _logger = logger;
        }



        public async Task<BrandedReport> ImportAsync(string csvDir, int limit, CancellationToken ct = default)
        {
            var brandedCsv = FindFile(csvDir, "branded_food.csv");
            var foodCsv = FindFile(csvDir, "food.csv");
            var nutrientCsv = FindFile(csvDir, "food_nutrient.csv");
            var report = new BrandedReport { Limit = limit };

            _logger.LogInformation("Scanning branded catalog (limit {Limit})…", limit);
            var candidates = ReadBrandedCandidates(brandedCsv, limit, report);
            _logger.LogInformation("Selected {Count} candidates; reading descriptions…", candidates.Count);

            var descriptions = ReadDescriptions(foodCsv, candidates.Keys.ToHashSet());
            _logger.LogInformation("Streaming nutrients for {Count} candidates…", candidates.Count);
            var nutrients = ReadNutrients(nutrientCsv, candidates.Keys.ToHashSet());

            var nutrientIds = await ResolveNutrientIdsAsync(ct);
            var (gramId, kcalId) = await ResolveMeasurementIdsAsync(ct);
            var existingFdcIds = (await _db.Ingredients.Where(i => i.FdcId != null)
                .Select(i => i.FdcId!).ToListAsync(ct)).ToHashSet();
            var seenNames = (await _db.Ingredients.Select(i => i.Name).ToListAsync(ct))
                .Select(n => n.ToLowerInvariant()).ToHashSet();

            var pending = new List<(IngredientEntity Ingredient, Macros Macros)>();

            foreach (var (fdcId, b) in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (existingFdcIds.Contains(fdcId)) { report.SkippedExisting++; continue; }
                if (!descriptions.TryGetValue(fdcId, out var description) || string.IsNullOrWhiteSpace(description))
                { Reject(report, "description_missing"); continue; }
                if (!nutrients.TryGetValue(fdcId, out var m)) { Reject(report, "nutrients_missing"); continue; }

                var result = _validator.Validate(new FoodQualityInput(description, m.Kcal, m.Protein, m.Carb, m.Fat));
                if (!result.Accepted)
                {
                    report.Rejected++;
                    foreach (var reason in result.Reasons)
                        report.RejectedByReason[reason] = report.RejectedByReason.GetValueOrDefault(reason) + 1;
                    continue;
                }

                // Brand-qualified name keeps "Nestlé Crunch" distinct from a generic "Crunch".
                var name = Truncate(ComposeName(description, b.BrandOwner), 2000);
                if (!seenNames.Add(name.ToLowerInvariant())) { report.SkippedDuplicateName++; continue; }

                var ingredient = new IngredientEntity
                {
                    Name = name,
                    FdcId = fdcId,
                    FdcDataType = "branded_food",
                    CurationStatusId = PendingCuration,
                    // A condiment category is conclusive: don't let the product name talk us into
                    // a food group ("Prego Sauces Tomato Basil" is not a serving of vegetables).
                    FoodGroupId = FoodGroupHeuristics.IsNonFoodGroupCategory(b.Category)
                        ? null
                        : FoodGroupHeuristics.ClassifyByCategory(b.Category)
                            ?? FoodGroupHeuristics.ClassifyFoodGroup(description),
                    ReferenceServingGrams = b.ServingGrams,
                    GtinUpc = b.Gtin,
                    IsWholeFood = FoodGroupHeuristics.IsDirectlyEdibleCategory(b.Category),
                    CreatedDate = DateTime.UtcNow,
                };

                _db.Ingredients.Add(ingredient);
                pending.Add((ingredient, m));
                report.Accepted++;
                if (ingredient.FoodGroupId.HasValue) report.Classified++;
                if (ingredient.ReferenceServingGrams.HasValue) report.WithReferenceServing++;
                if (ingredient.IsWholeFood == true) report.MarkedWholeFood++;
                if (ingredient.GtinUpc != null) report.WithGtin++;
            }

            await _db.SaveChangesAsync(ct);

            foreach (var (ing, m) in pending)
            {
                AddNutrient(ing.Id, nutrientIds, "calories", kcalId, m.Kcal, ref report.NutrientRows);
                AddNutrient(ing.Id, nutrientIds, "protein", gramId, m.Protein, ref report.NutrientRows);
                AddNutrient(ing.Id, nutrientIds, "carb", gramId, m.Carb, ref report.NutrientRows);
                AddNutrient(ing.Id, nutrientIds, "fat", gramId, m.Fat, ref report.NutrientRows);
            }
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Branded import: {Accepted} accepted, {Rejected} rejected.",
                report.Accepted, report.Rejected);
            return report;
        }

        private static void Reject(BrandedReport r, string reason)
        {
            r.Rejected++;
            r.RejectedByReason[reason] = r.RejectedByReason.GetValueOrDefault(reason) + 1;
        }

        private static string ComposeName(string description, string? brandOwner)
        {
            var desc = description.Trim();
            var brand = brandOwner?.Trim();
            if (string.IsNullOrEmpty(brand)) return desc;
            // Avoid "KELLOGG'S Kellogg's Corn Flakes".
            if (desc.Contains(brand, StringComparison.OrdinalIgnoreCase)) return desc;
            return $"{desc} ({Capitalize(brand)})";
        }

        private static string Capitalize(string s) =>
            s.Length <= 3 ? s : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());



        private sealed record Branded(string? BrandOwner, string? Category, decimal? ServingGrams, string? Gtin);
        private sealed record Macros(decimal? Kcal, decimal? Protein, decimal? Carb, decimal? Fat);

        /// <summary>
        /// Streams branded_food.csv, keeping US, non-discontinued products that publish a
        /// gram/ml serving size, up to <paramref name="limit"/>.
        /// </summary>
        private static Dictionary<string, Branded> ReadBrandedCandidates(string path, int limit, BrandedReport report)
        {
            var result = new Dictionary<string, Branded>();
            using var reader = new StreamReader(path);
            var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
            int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
            int iFdc = Col("fdc_id"), iBrand = Col("brand_owner"), iServing = Col("serving_size"),
                iGtin = Col("gtin_upc"),
                iUnit = Col("serving_size_unit"), iCategory = Col("branded_food_category"),
                iCountry = Col("market_country"), iDiscontinued = Col("discontinued_date");

            string? line;
            while ((line = reader.ReadLine()) != null && result.Count < limit)
            {
                report.Scanned++;
                var f = CsvLine.Split(line);
                if (iFdc < 0 || f.Length <= iFdc) continue;

                if (iCountry >= 0 && f.Length > iCountry && f[iCountry].Length > 0
                    && !f[iCountry].Contains("United States", StringComparison.OrdinalIgnoreCase))
                { report.SkippedNonUs++; continue; }

                if (iDiscontinued >= 0 && f.Length > iDiscontinued && f[iDiscontinued].Length > 0)
                { report.SkippedDiscontinued++; continue; }

                decimal? servingGrams = null;
                if (iServing >= 0 && f.Length > iServing
                    && decimal.TryParse(f[iServing], NumberStyles.Any, CultureInfo.InvariantCulture, out var ss) && ss > 0)
                {
                    var unit = (iUnit >= 0 && f.Length > iUnit ? f[iUnit] : string.Empty).Trim().ToLowerInvariant();
                    // g and ml are both treated as grams (water-density approximation for drinks).
                    if (unit is "g" or "grm" or "gram" or "ml" or "mlt") servingGrams = ss;
                }

                var gtin = iGtin >= 0 && f.Length > iGtin ? f[iGtin].Trim() : null;
                if (!string.IsNullOrEmpty(gtin) && !gtin.All(char.IsDigit)) gtin = null;

                result[f[iFdc]] = new Branded(
                    iBrand >= 0 && f.Length > iBrand ? f[iBrand] : null,
                    iCategory >= 0 && f.Length > iCategory ? f[iCategory] : null,
                    servingGrams,
                    string.IsNullOrEmpty(gtin) ? null : gtin);
            }
            return result;
        }

        private static Dictionary<string, string> ReadDescriptions(string path, HashSet<string> fdcIds)
        {
            var result = new Dictionary<string, string>();
            using var reader = new StreamReader(path);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length < 3) continue;
                if (fdcIds.Contains(f[0])) result[f[0]] = f[2];
            }
            return result;
        }

        private static Dictionary<string, Macros> ReadNutrients(string path, HashSet<string> fdcIds)
        {
            var acc = new Dictionary<string, (decimal? k, decimal? ks, decimal? kg, decimal? p, decimal? c, decimal? f)>();
            using var reader = new StreamReader(path);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length < 4) continue;
                var fdc = f[1];
                if (!fdcIds.Contains(fdc)) continue;
                if (!long.TryParse(f[2], out var nutId)) continue;
                if (!decimal.TryParse(f[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var amt)) continue;

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

        private void AddNutrient(long ingredientId, Dictionary<string, long> nutrientIds, string key,
            long measurementId, decimal? amount, ref int counter)
        {
            if (amount is not { } a) return;
            if (!nutrientIds.TryGetValue(key, out var nutrientId)) return;
            _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
            {
                IngredientId = ingredientId,
                NutrientId = nutrientId,
                Amount = a,
                MeasurementId = measurementId,
                CreatedDate = DateTime.UtcNow,
            });
            counter++;
        }

        private async Task<Dictionary<string, long>> ResolveNutrientIdsAsync(CancellationToken ct)
        {
            var all = await _db.Set<NutrientEntity>().Select(n => new { n.Id, n.Name }).ToListAsync(ct);
            var map = new Dictionary<string, long>();
            long? Find(params string[] patterns) => all
                .Where(n => patterns.Any(p => n.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n.Name.Length).Select(n => (long?)n.Id).FirstOrDefault();
            if (Find("calorie", "energy", "kcal") is { } cal) map["calories"] = cal;
            if (Find("protein") is { } pro) map["protein"] = pro;
            if (Find("carbohydrate", "carbs") is { } carb) map["carb"] = carb;
            if (Find("total lipid", "fat") is { } fat) map["fat"] = fat;
            return map;
        }

        private async Task<(long GramId, long KcalId)> ResolveMeasurementIdsAsync(CancellationToken ct)
        {
            var all = await _db.Set<Nom.Data.Measurement.MeasurementEntity>()
                .Select(m => new { m.Id, m.Name }).ToListAsync(ct);
            long Pick(params string[] patterns) => all
                .Where(m => patterns.Any(p => m.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(m => m.Name.Length).Select(m => m.Id).FirstOrDefault();
            return (Pick("gram"), Pick("kilocalorie", "calorie"));
        }

        private static string FindFile(string dir, string name)
        {
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            return Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException($"{name} not found under {dir}");
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        public sealed class BrandedReport
        {
            public int Limit { get; set; }
            public int Scanned { get; set; }
            public int SkippedNonUs { get; set; }
            public int SkippedDiscontinued { get; set; }
            public int Accepted { get; set; }
            public int Classified { get; set; }
            public int MarkedWholeFood { get; set; }
            public int WithReferenceServing { get; set; }
            public int WithGtin { get; set; }
            public int Rejected { get; set; }
            public int SkippedExisting { get; set; }
            public int SkippedDuplicateName { get; set; }
            public int NutrientRows;
            public Dictionary<string, int> RejectedByReason { get; } = new();
        }
    }
}
