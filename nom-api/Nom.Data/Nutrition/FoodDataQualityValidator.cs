using System;
using System.Collections.Generic;

namespace Nom.Data.Nutrition
{
    /// <summary>
    /// Per-100g nutrition facts for one candidate food, pre-validation. Per-100g is the stable,
    /// person-independent fact; the actual serving a person eats is derived per-person from their
    /// caloric need (like portions), so it is intentionally NOT part of quality validation.
    /// <see cref="UsedInSmallAmounts"/> marks seasonings and leaveners (by source category), whose
    /// energy is negligible in a dish and whose fiber, acids and alcohol defeat the Atwater check.
    /// </summary>
    public sealed record FoodQualityInput(
        string? Name,
        decimal? KcalPer100g,
        decimal? ProteinGramsPer100g,
        decimal? CarbGramsPer100g,
        decimal? FatGramsPer100g,
        bool UsedInSmallAmounts = false);

    /// <summary>
    /// Keys for the label nutrients beyond the four macros, shared by the importers and
    /// <see cref="FoodDataQualityValidator.CheckComponent"/>. Amounts are per 100 g in the
    /// unit FDA labels use: grams, milligrams (cholesterol, sodium, calcium, iron, potassium)
    /// or micrograms (vitamin D).
    /// </summary>
    public static class FoodComponent
    {
        public const string SaturatedFat = "saturated_fat";
        public const string TransFat = "trans_fat";
        public const string Cholesterol = "cholesterol";
        public const string Sodium = "sodium";
        public const string Fiber = "fiber";
        public const string Sugars = "sugars";
        public const string AddedSugars = "added_sugars";
        public const string VitaminD = "vitamin_d";
        public const string Calcium = "calcium";
        public const string Iron = "iron";
        public const string Potassium = "potassium";
    }

    /// <summary>Outcome of quality validation; <see cref="Reasons"/> lists every failed check.</summary>
    public sealed record FoodQualityResult(bool Accepted, IReadOnlyList<string> Reasons)
    {
        public static readonly FoodQualityResult Ok = new(true, Array.Empty<string>());
    }

    /// <summary>
    /// Sanity gate for imported/fetched food nutrition. USDA FDC Branded Foods is
    /// manufacturer-submitted and noisy (impossible calories, junk names, zero-everything
    /// records, nonsense serving sizes, mis-scaled units); this rejects records that fail
    /// basic physical plausibility so they don't pollute the catalog. Facts that pass but
    /// are still unverified should land as non-curated pending review — this validator only
    /// answers "is this record physically plausible?", not "is it trustworthy?".
    /// Pure and deterministic so it can be unit-tested and run in the import pipeline.
    /// </summary>
    public sealed class FoodDataQualityValidator
    {
        /// <summary>Energy ceiling per 100g. Pure fat is ~900 kcal/100g; anything above is impossible.</summary>
        public decimal MaxKcalPer100g { get; init; } = 900m;

        /// <summary>A single macro can't exceed 100 g per 100 g of food.</summary>
        public decimal MaxMacroGramsPer100g { get; init; } = 100m;

        /// <summary>Protein+carb+fat can't meaningfully exceed 100 g/100g (small slack for rounding).</summary>
        public decimal MaxMacroSumPer100g { get; init; } = 105m;

        public int MaxNameLength { get; init; } = 200;

        /// <summary>
        /// Atwater cross-check tolerance: 4·protein + 4·carb + 9·fat should land within this
        /// fraction of the stated calories. Loose (±30%) because fiber/alcohol/rounding shift it.
        /// </summary>
        public decimal AtwaterTolerance { get; init; } = 0.30m;

        /// <summary>
        /// When both the stated and the macro-implied energy are below this many kcal the Atwater
        /// ratio is too noisy to judge; skip that check.
        /// </summary>
        public decimal AtwaterMinKcal { get; init; } = 20m;

        /// <summary>
        /// Per-100 g ceilings for the <see cref="FoodComponent"/> nutrients. Generous on purpose:
        /// the richest real foods (table salt for sodium, cream of tartar for potassium, cod liver
        /// oil for vitamin D) sit below them, so only mis-scaled units trip them.
        /// </summary>
        public IReadOnlyDictionary<string, decimal> MaxComponentPer100g { get; init; } = new Dictionary<string, decimal>
        {
            [FoodComponent.SaturatedFat] = 100m,
            [FoodComponent.TransFat] = 100m,
            [FoodComponent.Cholesterol] = 3500m,
            [FoodComponent.Sodium] = 45000m,
            [FoodComponent.Fiber] = 100m,
            [FoodComponent.Sugars] = 100m,
            [FoodComponent.AddedSugars] = 100m,
            [FoodComponent.VitaminD] = 1000m,
            [FoodComponent.Calcium] = 40000m,
            [FoodComponent.Iron] = 1000m,
            [FoodComponent.Potassium] = 20000m,
        };

        /// <summary>
        /// A part of total fat or total carbohydrate may exceed its parent by at most this fraction
        /// plus <see cref="ComponentSlackGrams"/>, which absorbs rounding in the source data.
        /// </summary>
        public decimal ComponentTolerance { get; init; } = 0.05m;

        /// <summary>Grams a part may exceed its parent by on top of <see cref="ComponentTolerance"/>.</summary>
        public decimal ComponentSlackGrams { get; init; } = 1m;

        /// <summary>
        /// Checks one label nutrient of a food whose macros already passed <see cref="Validate"/>.
        /// Returns null when plausible, else a reason such as "sodium_impossible" or
        /// "saturated_fat_exceeds_fat". A failed component is dropped on its own; it does not
        /// reject the food.
        /// </summary>
        public string? CheckComponent(string key, decimal amount, decimal? fatGramsPer100g, decimal? carbGramsPer100g)
        {
            if (amount < 0) return $"{key}_negative";
            if (MaxComponentPer100g.TryGetValue(key, out var max) && amount > max) return $"{key}_impossible";

            var (parent, parentName) = key switch
            {
                FoodComponent.SaturatedFat or FoodComponent.TransFat => (fatGramsPer100g, "fat"),
                FoodComponent.Fiber or FoodComponent.Sugars or FoodComponent.AddedSugars => (carbGramsPer100g, "carb"),
                _ => ((decimal?)null, string.Empty),
            };
            if (parent is { } p && p >= 0 && amount > p * (1m + ComponentTolerance) + ComponentSlackGrams)
                return $"{key}_exceeds_{parentName}";
            return null;
        }

        public FoodQualityResult Validate(FoodQualityInput f)
        {
            var reasons = new List<string>();

            // Name
            var name = f.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                reasons.Add("name_missing");
            else
            {
                if (name.Length > MaxNameLength)
                    reasons.Add("name_too_long");
                if (!ContainsLetter(name))
                    reasons.Add("name_not_alphabetic");
            }

            // Calories
            if (f.KcalPer100g is not { } kcal)
                reasons.Add("calories_missing");
            else if (kcal < 0)
                reasons.Add("calories_negative");
            else if (kcal > MaxKcalPer100g)
                reasons.Add("calories_impossible");

            // Macros present + individually bounded
            CheckMacro("protein", f.ProteinGramsPer100g, reasons);
            CheckMacro("carb", f.CarbGramsPer100g, reasons);
            CheckMacro("fat", f.FatGramsPer100g, reasons);

            // Macro sum sanity
            if (f.ProteinGramsPer100g is { } p && f.CarbGramsPer100g is { } c && f.FatGramsPer100g is { } fat
                && p >= 0 && c >= 0 && fat >= 0 && (p + c + fat) > MaxMacroSumPer100g)
                reasons.Add("macro_sum_impossible");

            // Atwater cross-check (only when we have all macros + a usable calorie figure)
            if (!f.UsedInSmallAmounts && f.KcalPer100g is { } k && k >= 0
                && f.ProteinGramsPer100g is { } pr && f.CarbGramsPer100g is { } cb && f.FatGramsPer100g is { } ft
                && pr >= 0 && cb >= 0 && ft >= 0)
            {
                var computed = (4m * pr) + (4m * cb) + (9m * ft);
                var low = k * (1m - AtwaterTolerance);
                var high = k * (1m + AtwaterTolerance);
                if (Math.Max(k, computed) >= AtwaterMinKcal && (computed < low || computed > high))
                    reasons.Add("atwater_mismatch");
            }

            return reasons.Count == 0 ? FoodQualityResult.Ok : new FoodQualityResult(false, reasons);
        }

        private void CheckMacro(string label, decimal? value, List<string> reasons)
        {
            if (value is not { } v)
                reasons.Add($"{label}_missing");
            else if (v < 0)
                reasons.Add($"{label}_negative");
            else if (v > MaxMacroGramsPer100g)
                reasons.Add($"{label}_impossible");
        }

        private static bool ContainsLetter(string s)
        {
            foreach (var ch in s)
                if (char.IsLetter(ch)) return true;
            return false;
        }
    }
}
