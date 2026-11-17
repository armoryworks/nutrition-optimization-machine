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
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;

namespace Nom.Import.Services
{
    /// <summary>
    /// The USDA FDC nutrients NOM stores per ingredient, shared by the Foundation/SR Legacy and
    /// Branded importers and the nutrient backfill: the FDA label set plus the vitamins and
    /// minerals that carry a Daily Value. Each key lists the FDC nutrient ids that can supply it
    /// (from nutrient.csv), most preferred first, with a deterministic unit factor where FDC
    /// reports a different unit (vitamin D in IU: 40 IU = 1 µg). A quantity FDC reports only in
    /// another definition is left out rather than converted: vitamin A is taken as RAE (not IU),
    /// folate as DFE (not total folate). Values are per 100 g and come only from the FDC files.
    /// </summary>
    public static class FdcNutrients
    {
        public const string Calories = "calories";
        public const string Protein = "protein";
        public const string Carb = "carb";
        public const string Fat = "fat";

        public sealed record Source(long FdcNutrientId, decimal Factor = 1m);

        /// <summary>
        /// One stored nutrient: its key, the measurement it is stored in (by name), the FDC ids
        /// that supply it, and the name patterns that find its seeded Nutrient row.
        /// </summary>
        public sealed record Spec(string Key, string Unit, Source[] Sources, string[] NamePatterns);

        private const string Gram = "Gram", Milligram = "Milligram", Microgram = "Microgram", Kilocalorie = "Kilocalorie";

        public static readonly IReadOnlyList<Spec> All = new[]
        {
            new Spec(Calories, Kilocalorie, new[] { new Source(1008), new Source(2048), new Source(2047) }, new[] { "calorie", "energy", "kcal" }),
            new Spec(Protein, Gram, new[] { new Source(1003) }, new[] { "protein" }),
            new Spec(Carb, Gram, new[] { new Source(1005) }, new[] { "carbohydrate", "carbs" }),
            new Spec(Fat, Gram, new[] { new Source(1004) }, new[] { "total lipid", "fat" }),
            new Spec(FoodComponent.Sugars, Gram, new[] { new Source(2000), new Source(1063) }, new[] { "total sugars" }),
            new Spec(FoodComponent.AddedSugars, Gram, new[] { new Source(1235) }, new[] { "added sugars" }),
            new Spec(FoodComponent.SaturatedFat, Gram, new[] { new Source(1258) }, new[] { "saturated" }),
            new Spec(FoodComponent.TransFat, Gram, new[] { new Source(1257) }, new[] { "trans fat", "total trans" }),
            new Spec(FoodComponent.Cholesterol, Milligram, new[] { new Source(1253) }, new[] { "cholesterol" }),
            new Spec(FoodComponent.Sodium, Milligram, new[] { new Source(1093) }, new[] { "sodium" }),
            new Spec(FoodComponent.Fiber, Gram, new[] { new Source(1079), new Source(2033) }, new[] { "fiber", "fibre" }),
            new Spec(FoodComponent.VitaminD, Microgram, new[] { new Source(1114), new Source(1110, 0.025m) }, new[] { "vitamin d" }),
            new Spec(FoodComponent.Calcium, Milligram, new[] { new Source(1087) }, new[] { "calcium" }),
            new Spec(FoodComponent.Iron, Milligram, new[] { new Source(1089) }, new[] { "iron" }),
            new Spec(FoodComponent.Potassium, Milligram, new[] { new Source(1092) }, new[] { "potassium" }),
            new Spec(FoodComponent.VitaminA, Microgram, new[] { new Source(1106) }, new[] { "vitamin a" }),
            new Spec(FoodComponent.VitaminC, Milligram, new[] { new Source(1162) }, new[] { "vitamin c" }),
            new Spec(FoodComponent.VitaminE, Milligram, new[] { new Source(1109) }, new[] { "vitamin e" }),
            new Spec(FoodComponent.VitaminK, Microgram, new[] { new Source(1185) }, new[] { "vitamin k" }),
            new Spec(FoodComponent.Thiamin, Milligram, new[] { new Source(1165) }, new[] { "thiamin" }),
            new Spec(FoodComponent.Riboflavin, Milligram, new[] { new Source(1166) }, new[] { "riboflavin" }),
            new Spec(FoodComponent.Niacin, Milligram, new[] { new Source(1167) }, new[] { "niacin" }),
            new Spec(FoodComponent.VitaminB6, Milligram, new[] { new Source(1175) }, new[] { "vitamin b6", "vitamin b-6" }),
            new Spec(FoodComponent.Folate, Microgram, new[] { new Source(1190) }, new[] { "folate" }),
            new Spec(FoodComponent.VitaminB12, Microgram, new[] { new Source(1178) }, new[] { "vitamin b12", "vitamin b-12" }),
            new Spec(FoodComponent.Biotin, Microgram, new[] { new Source(1176) }, new[] { "biotin" }),
            new Spec(FoodComponent.PantothenicAcid, Milligram, new[] { new Source(1170) }, new[] { "pantothenic" }),
            new Spec(FoodComponent.Choline, Milligram, new[] { new Source(1180) }, new[] { "choline" }),
            new Spec(FoodComponent.Phosphorus, Milligram, new[] { new Source(1091) }, new[] { "phosphorus" }),
            new Spec(FoodComponent.Iodine, Microgram, new[] { new Source(1100) }, new[] { "iodine" }),
            new Spec(FoodComponent.Magnesium, Milligram, new[] { new Source(1090) }, new[] { "magnesium" }),
            new Spec(FoodComponent.Zinc, Milligram, new[] { new Source(1095) }, new[] { "zinc" }),
            new Spec(FoodComponent.Selenium, Microgram, new[] { new Source(1103) }, new[] { "selenium" }),
            new Spec(FoodComponent.Copper, Milligram, new[] { new Source(1098) }, new[] { "copper" }),
            new Spec(FoodComponent.Manganese, Milligram, new[] { new Source(1101) }, new[] { "manganese" }),
            new Spec(FoodComponent.Chromium, Microgram, new[] { new Source(1096) }, new[] { "chromium" }),
            new Spec(FoodComponent.Molybdenum, Microgram, new[] { new Source(1102) }, new[] { "molybdenum" }),
            new Spec(FoodComponent.Chloride, Milligram, new[] { new Source(1088) }, new[] { "chloride" }),
        };

        /// <summary>The four keys the food-level quality gate judges; the rest are checked one by one.</summary>
        public static readonly IReadOnlyList<string> Macros = new[] { Calories, Protein, Carb, Fat };

        private static readonly HashSet<long> WantedIds = All.SelectMany(s => s.Sources).Select(s => s.FdcNutrientId).ToHashSet();

        /// <summary>Where a nutrient key is stored: its Nutrient row and the measurement of its amounts.</summary>
        public sealed record Target(long NutrientId, long MeasurementId);

        /// <summary>
        /// Streams food_nutrient.csv (id,fdc_id,nutrient_id,amount,...) and returns, per wanted
        /// fdc_id, every <see cref="All"/> key the file supplies.
        /// </summary>
        public static Dictionary<string, FdcFoodNutrients> Read(string path, HashSet<string> fdcIds)
        {
            var raw = new Dictionary<string, Dictionary<long, decimal>>();
            using (var reader = new StreamReader(path))
            {
                reader.ReadLine();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var f = CsvLine.Split(line);
                    if (f.Length < 4) continue;
                    var fdc = f[1];
                    if (!fdcIds.Contains(fdc)) continue;
                    if (!long.TryParse(f[2], out var nutId) || !WantedIds.Contains(nutId)) continue;
                    if (!decimal.TryParse(f[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var amt)) continue;
                    if (!raw.TryGetValue(fdc, out var byId)) raw[fdc] = byId = new Dictionary<long, decimal>();
                    byId[nutId] = amt;
                }
            }

            var result = new Dictionary<string, FdcFoodNutrients>(raw.Count);
            foreach (var (fdc, byId) in raw)
            {
                var values = new Dictionary<string, decimal>();
                foreach (var spec in All)
                {
                    foreach (var source in spec.Sources)
                    {
                        if (!byId.TryGetValue(source.FdcNutrientId, out var amt)) continue;
                        values[spec.Key] = source.Factor == 1m ? amt : Math.Round(amt * source.Factor, 4);
                        break;
                    }
                }
                result[fdc] = new FdcFoodNutrients(values);
            }
            return result;
        }

        /// <summary>
        /// The values worth storing for one food: the macros as read (the food-level gate already
        /// judged them) and every other nutrient that passes
        /// <see cref="FoodDataQualityValidator.CheckComponent"/>. A dropped value is reported
        /// through <paramref name="onRejected"/> and simply left unknown, never zeroed.
        /// </summary>
        public static IEnumerable<KeyValuePair<string, decimal>> Accepted(
            FdcFoodNutrients food, FoodDataQualityValidator validator, Action<string>? onRejected = null)
        {
            foreach (var (key, amount) in food.Values)
            {
                if (!Macros.Contains(key) && validator.CheckComponent(key, amount, food.Fat, food.Carb) is { } reason)
                {
                    onRejected?.Invoke(reason);
                    continue;
                }
                yield return new KeyValuePair<string, decimal>(key, amount);
            }
        }

        /// <summary>
        /// Maps each key to its seeded Nutrient row by name and to its unit's Measurement row. A key
        /// whose Nutrient row is missing, or whose row is stored in a different unit than FDC
        /// reports (which would be off by 1000), is left out and logged, so its amounts are skipped.
        /// </summary>
        public static async Task<Dictionary<string, Target>> ResolveAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct)
        {
            var nutrients = await db.Set<NutrientEntity>()
                .Select(n => new { n.Id, n.Name, n.DefaultMeasurementId }).ToListAsync(ct);
            var measurements = await db.Set<MeasurementEntity>()
                .Select(m => new { m.Id, m.Name }).ToListAsync(ct);

            long PickMeasurement(params string[] patterns) => measurements
                .Where(m => patterns.Any(p => m.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(m => m.Name.Length)
                .Select(m => m.Id)
                .FirstOrDefault();
            long Exact(string name) => measurements
                .Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Id)
                .Select(m => m.Id)
                .FirstOrDefault();

            var units = new Dictionary<string, long>
            {
                [Gram] = PickMeasurement("gram"),
                [Kilocalorie] = PickMeasurement("kilocalorie", "calorie"),
                [Milligram] = Exact(Milligram),
                [Microgram] = Exact(Microgram),
            };
            if (units[Gram] == 0 || units[Kilocalorie] == 0)
            {
                throw new InvalidOperationException(
                    "Cannot resolve the 'Gram' and 'Kilocalorie' measurement rows in the target " +
                    "database — run the measurement seed before importing, or nutrient amounts " +
                    "would be written against an invalid unit.");
            }

            var map = new Dictionary<string, Target>();
            foreach (var spec in All)
            {
                var nutrient = nutrients
                    .Where(n => spec.NamePatterns.Any(p => n.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(n => n.Name.Length)
                    .FirstOrDefault();
                if (nutrient == null)
                {
                    if (Macros.Contains(spec.Key))
                        logger.LogWarning("No seeded Nutrient row matched '{Key}' — those amounts will be skipped.", spec.Key);
                    else
                        logger.LogInformation("No seeded Nutrient row matched '{Key}' — those amounts will be skipped.", spec.Key);
                    continue;
                }
                var unitId = units[spec.Unit];
                if (unitId == 0)
                {
                    logger.LogWarning("No '{Unit}' measurement row — '{Key}' amounts will be skipped.", spec.Unit, spec.Key);
                    continue;
                }
                if (nutrient.DefaultMeasurementId != 0 && nutrient.DefaultMeasurementId != unitId)
                {
                    logger.LogWarning("Nutrient '{Name}' is stored in measurement {Stored}, not {Unit} as FDC reports it — '{Key}' amounts will be skipped.",
                        nutrient.Name, nutrient.DefaultMeasurementId, spec.Unit, spec.Key);
                    continue;
                }
                map[spec.Key] = new Target(nutrient.Id, unitId);
            }
            return map;
        }
    }

    /// <summary>Per-100 g values one FDC food supplies, keyed by <see cref="FdcNutrients"/> key.</summary>
    public sealed class FdcFoodNutrients
    {
        public FdcFoodNutrients(IReadOnlyDictionary<string, decimal> values) => Values = values;

        public IReadOnlyDictionary<string, decimal> Values { get; }

        public decimal? this[string key] => Values.TryGetValue(key, out var v) ? v : null;

        public decimal? Kcal => this[FdcNutrients.Calories];
        public decimal? Protein => this[FdcNutrients.Protein];
        public decimal? Carb => this[FdcNutrients.Carb];
        public decimal? Fat => this[FdcNutrients.Fat];
    }
}
