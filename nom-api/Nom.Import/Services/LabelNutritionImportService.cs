using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Orch.Services;

namespace Nom.Import.Services
{
    /// <summary>
    /// Gives a catalog food with no USDA record the Nutrition Facts its manufacturer publishes.
    /// Reads a tab-separated file, one transcribed label per row: <c>ingredient_id</c>, <c>name</c>
    /// (must match the food, so a file made for another database writes nothing), <c>brand</c>,
    /// <c>product</c>, <c>url</c>, <c>serving</c> (as printed), <c>serving_grams</c>, the printed
    /// household measure as <c>serving_volume</c> with <c>serving_volume_unit</c> (a volume
    /// Measurement name) or as <c>serving_pieces</c>, then one column per <see cref="FdcNutrients"/>
    /// key holding the amount per serving, empty where the label prints no number. Each amount is
    /// rescaled to per 100 g by the printed serving weight and nothing else; a label without a
    /// gram weight is refused. Only nutrients the food has no row for are written, the macros pass
    /// <see cref="FoodDataQualityValidator.Validate"/> and every other value
    /// <see cref="FoodDataQualityValidator.CheckComponent"/>. A food with no density or item
    /// weight takes the one the household measure gives ("3/4 cup (170g)", "5 crackers (16g)");
    /// the serving is never stored as the weight of one item. The label as printed is
    /// kept as an applied proposal (field <see cref="FoodCatalogReviewService.LabelNutritionField"/>,
    /// source <c>label:manufacturer:&lt;host&gt;</c>), so each value traces back to its page.
    /// Run: <c>dotnet run -- --import-label-nutrition &lt;labels.tsv&gt; [--dry-run]</c>.
    /// </summary>
    public class LabelNutritionImportService
    {
        public const string SourcePrefix = "label:manufacturer:";

        private const long VolumeCategory = 2;
        private const decimal MinDensity = 0.05m, MaxDensity = 3m, MaxItemGrams = 5000m;

        private readonly ApplicationDbContext _db;
        private readonly ILogger<LabelNutritionImportService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public LabelNutritionImportService(ApplicationDbContext db, ILogger<LabelNutritionImportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <param name="dryRun">Report what would be written without saving anything.</param>
        public async Task<LabelReport> ImportAsync(string path, bool dryRun, CancellationToken ct = default)
        {
            var report = new LabelReport { DryRun = dryRun };
            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);
            var labels = Read(path);
            report.Labels = labels.Count;
            var volumeUnits = (await _db.Set<MeasurementEntity>()
                .Where(m => m.MeasurementCategoryId == VolumeCategory && m.BaseUnitConversionFactor > 0)
                .Select(m => new { m.Name, m.BaseUnitConversionFactor })
                .ToListAsync(ct))
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().BaseUnitConversionFactor!.Value, StringComparer.OrdinalIgnoreCase);

            var ids = labels.Select(l => l.IngredientId).ToList();
            var foods = await _db.Ingredients.Where(i => ids.Contains(i.Id) && !i.IsDeleted).ToDictionaryAsync(i => i.Id, ct);
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
            report.Batch = $"label-nutrition-{now:yyyyMMdd-HHmmss}";
            var measured = new List<long>();
            foreach (var label in labels)
            {
                ct.ThrowIfCancellationRequested();
                void Skip(string reason)
                {
                    report.SkippedByReason[reason] = report.SkippedByReason.GetValueOrDefault(reason) + 1;
                    report.Lines.Add($"skipped  #{label.IngredientId} {label.Name}: {reason}");
                }

                if (!foods.TryGetValue(label.IngredientId, out var food)) { Skip("food_not_found"); continue; }
                if (!string.Equals(food.Name.Trim(), label.Name, StringComparison.OrdinalIgnoreCase)) { Skip("name_differs"); continue; }
                if (food.FdcId != null) { Skip("has_usda_record"); continue; }
                if (label.ServingGrams is not { } grams || grams <= 0) { Skip("serving_weight_missing"); continue; }
                if (!Uri.TryCreate(label.Url, UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps) { Skip("page_missing"); continue; }
                var source = SourcePrefix + (page.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? page.Host[4..] : page.Host).ToLowerInvariant();

                var per100 = label.PerServing.ToDictionary(v => v.Key, v => Math.Round(v.Value * 100m / grams, 2));
                decimal? Known(string key) => Stored(food.Id, key) ?? (per100.TryGetValue(key, out var v) ? v : null);
                var kcal = Known(FdcNutrients.Calories);
                var protein = Known(FdcNutrients.Protein);
                var carb = Known(FdcNutrients.Carb);
                var fat = Known(FdcNutrients.Fat);
                var macroProblems = kcal != null && protein != null && carb != null && fat != null
                    ? _validator.Validate(new FoodQualityInput(food.Name, kcal, protein, carb, fat, AlcoholGramsPer100g: Known(FoodComponent.Alcohol), FiberGramsPer100g: Known(FoodComponent.Fiber))).Reasons
                    : OutOfRange(kcal, protein, carb, fat);
                if (macroProblems.Count > 0) { Skip(string.Join("+", macroProblems)); continue; }

                var written = new List<string>();
                foreach (var (key, amount) in per100)
                {
                    if (!targets.TryGetValue(key, out var t) || have.Contains((food.Id, t.NutrientId))) continue;
                    if (!FdcNutrients.Macros.Contains(key) && _validator.CheckComponent(key, amount, fat, carb) is { } reason)
                    {
                        report.DroppedValuesByReason[reason] = report.DroppedValuesByReason.GetValueOrDefault(reason) + 1;
                        continue;
                    }
                    have.Add((food.Id, t.NutrientId));
                    written.Add(key);
                    if (!dryRun)
                    {
                        _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
                        {
                            IngredientId = food.Id,
                            NutrientId = t.NutrientId,
                            Amount = amount,
                            MeasurementId = t.MeasurementId,
                            CreatedDate = now,
                        });
                    }
                }

                decimal? density = null, itemGrams = null;
                if (food.GramsPerMilliliter == null && label.ServingVolume is > 0 && volumeUnits.TryGetValue(label.ServingVolumeUnit, out var unitMl)
                    && Math.Round(grams / (label.ServingVolume.Value * unitMl), 4) is > MinDensity and < MaxDensity and var d)
                {
                    density = d;
                }
                if (food.GramsPerItem == null && label.ServingPieces is > 0 && Math.Round(grams / label.ServingPieces.Value, 2) is > 0 and <= MaxItemGrams and var w)
                {
                    itemGrams = w;
                }
                if (written.Count == 0 && density == null && itemGrams == null) { Skip("nothing_missing"); continue; }

                report.Foods++;
                report.Rows += written.Count;
                if (density != null) report.Densities++;
                if (itemGrams != null) report.ItemWeights++;
                var measures = (density != null ? $", {density} g/ml" : string.Empty) + (itemGrams != null ? $", {itemGrams} g each" : string.Empty);
                report.Lines.Add($"filled   #{food.Id} {food.Name}: {written.Count} values{measures} from {label.Brand} {label.Product} ({label.Serving}), {source}");
                if (dryRun) continue;

                food.GramsPerMilliliter ??= density;
                food.GramsPerItem ??= itemGrams;
                if (density != null || itemGrams != null) measured.Add(food.Id);
                food.LastModifiedDate = now;
                _db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                {
                    Action = FoodProposalAction.Update,
                    IngredientId = food.Id,
                    Field = FoodCatalogReviewService.LabelNutritionField,
                    ProposedValue = JsonSerializer.Serialize(new
                    {
                        brand = label.Brand,
                        product = label.Product,
                        url = label.Url,
                        serving = label.Serving,
                        servingGrams = grams,
                        perServing = label.PerServing,
                        written,
                        gramsPerMilliliter = density,
                        gramsPerItem = itemGrams,
                    }),
                    Reason = $"Manufacturer label: {label.Brand} {label.Product}, {label.Serving}. Per 100 g by the printed serving weight.",
                    Source = source,
                    Batch = report.Batch,
                    Status = FoodProposalStatus.Applied,
                    ReviewedByPersonId = SystemConstants.SystemPersonId,
                    ReviewedAt = now,
                    CreatedDate = now,
                });
            }

            if (!dryRun)
            {
                await _db.SaveChangesAsync(ct);
                await TouchLinesAsync(measured, now, ct);
            }
            _logger.LogInformation("Label nutrition import{DryRun}: {Rows} rows on {Foods} foods.",
                dryRun ? " (dry run)" : string.Empty, report.Rows, report.Foods);
            return report;
        }

        /// <summary>
        /// Marks the recipe lines of foods that gained a density or item weight as changed, so the
        /// API's nutrition sweep recalculates the labels those lines now count toward.
        /// </summary>
        private async Task TouchLinesAsync(List<long> ingredientIds, DateTime now, CancellationToken ct)
        {
            if (ingredientIds.Count == 0) return;
            if (_db.Database.IsRelational())
            {
                await _db.RecipeIngredients.Where(ri => ingredientIds.Contains(ri.IngredientId))
                    .ExecuteUpdateAsync(u => u.SetProperty(ri => ri.LastModifiedDate, now), ct);
                return;
            }
            foreach (var line in await _db.RecipeIngredients.Where(ri => ingredientIds.Contains(ri.IngredientId)).ToListAsync(ct))
            {
                line.LastModifiedDate = now;
            }
            await _db.SaveChangesAsync(ct);
        }

        private IReadOnlyList<string> OutOfRange(decimal? kcal, decimal? protein, decimal? carb, decimal? fat)
        {
            var reasons = new List<string>();
            if (kcal is { } k && (k < 0 || k > _validator.MaxKcalPer100g)) reasons.Add("calories_impossible");
            foreach (var (name, value) in new[] { ("protein", protein), ("carb", carb), ("fat", fat) })
            {
                if (value is { } v && (v < 0 || v > _validator.MaxMacroGramsPer100g)) reasons.Add($"{name}_impossible");
            }
            return reasons;
        }

        private static List<Label> Read(string path)
        {
            var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList();
            var labels = new List<Label>();
            if (lines.Count < 2) return labels;

            var header = lines[0].Split('\t').Select(h => h.Trim().ToLowerInvariant()).ToArray();
            var keys = FdcNutrients.All.Select(s => s.Key).ToHashSet();
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('\t');
                string At(string column)
                {
                    var i = Array.IndexOf(header, column);
                    return i >= 0 && i < f.Length ? f[i].Trim() : string.Empty;
                }

                if (!long.TryParse(At("ingredient_id"), out var id)) continue;
                var perServing = new Dictionary<string, decimal>();
                for (var i = 0; i < header.Length && i < f.Length; i++)
                {
                    if (keys.Contains(header[i]) && decimal.TryParse(f[i].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                        perServing[header[i]] = amount;
                }
                decimal? Number(string column) =>
                    decimal.TryParse(At(column), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
                labels.Add(new Label(id, At("name"), At("brand"), At("product"), At("url"), At("serving"),
                    Number("serving_grams"), Number("serving_volume"), At("serving_volume_unit"), Number("serving_pieces"), perServing));
            }
            return labels;
        }

        private sealed record Label(long IngredientId, string Name, string Brand, string Product, string Url, string Serving,
            decimal? ServingGrams, decimal? ServingVolume, string ServingVolumeUnit, decimal? ServingPieces, Dictionary<string, decimal> PerServing);

        public sealed class LabelReport
        {
            public bool DryRun { get; set; }
            public DateTime RunStamp { get; set; }
            public string Batch { get; set; } = string.Empty;
            public int Labels { get; set; }
            public int Foods { get; set; }
            public int Rows { get; set; }
            public int Densities { get; set; }
            public int ItemWeights { get; set; }
            public Dictionary<string, int> SkippedByReason { get; } = new();
            public Dictionary<string, int> DroppedValuesByReason { get; } = new();
            public List<string> Lines { get; } = new();
        }
    }
}
