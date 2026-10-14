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
            var nutrients = FdcNutrients.Read(nutrientCsv, candidates.Keys.ToHashSet());

            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);
            var existingFdcIds = (await _db.Ingredients.Where(i => i.FdcId != null)
                .Select(i => i.FdcId!).ToListAsync(ct)).ToHashSet();
            var seenNames = (await _db.Ingredients.Select(i => i.Name).ToListAsync(ct))
                .Select(n => n.ToLowerInvariant()).ToHashSet();

            var pending = new List<(IngredientEntity Ingredient, FdcFoodNutrients Nutrients)>();

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
                foreach (var (key, amount) in FdcNutrients.Accepted(m, _validator, r => Drop(report, r)))
                    AddNutrient(ing.Id, targets, key, amount, ref report.NutrientRows);
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

        private static void Drop(BrandedReport r, string reason) =>
            r.DroppedValuesByReason[reason] = r.DroppedValuesByReason.GetValueOrDefault(reason) + 1;

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

        private void AddNutrient(long ingredientId, Dictionary<string, FdcNutrients.Target> targets, string key,
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
            public Dictionary<string, int> DroppedValuesByReason { get; } = new();
        }
    }
}
