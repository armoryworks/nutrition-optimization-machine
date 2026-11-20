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
    /// <see cref="AlcoholGramsPer100g"/> is the ethanol the source reports (USDA or a label); it
    /// counts toward the macro sum and the Atwater energy, and a food without it is judged as before.
    /// </summary>
    public sealed record FoodQualityInput(
        string? Name,
        decimal? KcalPer100g,
        decimal? ProteinGramsPer100g,
        decimal? CarbGramsPer100g,
        decimal? FatGramsPer100g,
        bool UsedInSmallAmounts = false,
        decimal? AlcoholGramsPer100g = null);

    /// <summary>
    /// Keys for the label nutrients beyond the four macros and for the vitamins and minerals
    /// that carry an FDA Daily Value, shared by the importers and
    /// <see cref="FoodDataQualityValidator.CheckComponent"/>. Amounts are per 100 g in the
    /// unit FDA labels use: grams, milligrams (cholesterol, sodium, calcium, iron, potassium,
    /// vitamin C, the B vitamins, most minerals) or micrograms (vitamins A, D, K and B12,
    /// folate, biotin, iodine, selenium, chromium, molybdenum). Alcohol is ethanol in grams.
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
        public const string VitaminA = "vitamin_a";
        public const string VitaminC = "vitamin_c";
        public const string VitaminE = "vitamin_e";
        public const string VitaminK = "vitamin_k";
        public const string Thiamin = "thiamin";
        public const string Riboflavin = "riboflavin";
        public const string Niacin = "niacin";
        public const string VitaminB6 = "vitamin_b6";
        public const string Folate = "folate";
        public const string VitaminB12 = "vitamin_b12";
        public const string Biotin = "biotin";
        public const string PantothenicAcid = "pantothenic_acid";
        public const string Choline = "choline";
        public const string Phosphorus = "phosphorus";
        public const string Iodine = "iodine";
        public const string Magnesium = "magnesium";
        public const string Zinc = "zinc";
        public const string Selenium = "selenium";
        public const string Copper = "copper";
        public const string Manganese = "manganese";
        public const string Chromium = "chromium";
        public const string Molybdenum = "molybdenum";
        public const string Chloride = "chloride";
        public const string Alcohol = "alcohol";
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
        public decimal MaxKcalPer100g { get; init; } = 902m;

        /// <summary>A single macro can't exceed 100 g per 100 g of food.</summary>
        public decimal MaxMacroGramsPer100g { get; init; } = 100m;

        /// <summary>Protein+carb+fat+alcohol can't meaningfully exceed 100 g/100g (small slack for rounding).</summary>
        public decimal MaxMacroSumPer100g { get; init; } = 105m;

        public int MaxNameLength { get; init; } = 200;

        /// <summary>
        /// Atwater cross-check tolerance: 4·protein + 4·carb + 9·fat, plus
        /// <see cref="AlcoholKcalPerGram"/>·alcohol when the source reports alcohol, should land
        /// within this fraction of the stated calories. Loose (±30%) because fiber/rounding shift it.
        /// </summary>
        public decimal AtwaterTolerance { get; init; } = 0.30m;

        /// <summary>Energy of ethanol in the Atwater cross-check: 7 kcal per gram.</summary>
        public decimal AlcoholKcalPerGram { get; init; } = 7m;

        /// <summary>
        /// When both the stated and the macro-implied energy are below this many kcal the Atwater
        /// ratio is too noisy to judge; skip that check.
        /// </summary>
        public decimal AtwaterMinKcal { get; init; } = 20m;

        /// <summary>
        /// Foods whose carbohydrate is largely organic acid (citrus, vinegar, cranberries): the USDA
        /// energy figure is rightly below 4·carb, so a stated value under the Atwater figure is accepted.
        /// </summary>
        public IReadOnlyList<string> AcidicFoodNameMarkers { get; init; } = new[]
        {
            "lemon", "lime", "grapefruit", "citrus", "vinegar", "cranberr", "tamarind", "sour cherr",
        };

        /// <summary>
        /// Per-100 g ceilings for the <see cref="FoodComponent"/> nutrients. Generous on purpose:
        /// the richest real foods (table salt for sodium and chloride, cream of tartar for
        /// potassium, cod liver oil for vitamins A and D, fortified yeast for the B vitamins, dried
        /// kelp for iodine, brazil nuts for selenium) sit below them, so only mis-scaled units trip them.
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
            [FoodComponent.VitaminA] = 100000m,
            [FoodComponent.VitaminC] = 20000m,
            [FoodComponent.VitaminE] = 500m,
            [FoodComponent.VitaminK] = 5000m,
            [FoodComponent.Thiamin] = 200m,
            [FoodComponent.Riboflavin] = 200m,
            [FoodComponent.Niacin] = 1000m,
            [FoodComponent.VitaminB6] = 200m,
            [FoodComponent.Folate] = 20000m,
            [FoodComponent.VitaminB12] = 1000m,
            [FoodComponent.Biotin] = 2000m,
            [FoodComponent.PantothenicAcid] = 200m,
            [FoodComponent.Choline] = 5000m,
            [FoodComponent.Phosphorus] = 30000m,
            [FoodComponent.Iodine] = 500000m,
            [FoodComponent.Magnesium] = 10000m,
            [FoodComponent.Zinc] = 500m,
            [FoodComponent.Selenium] = 5000m,
            [FoodComponent.Copper] = 50m,
            [FoodComponent.Manganese] = 200m,
            [FoodComponent.Chromium] = 5000m,
            [FoodComponent.Molybdenum] = 5000m,
            [FoodComponent.Chloride] = 62000m,
            [FoodComponent.Alcohol] = 100m,
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
            if (f.AlcoholGramsPer100g is { } stated)
            {
                if (stated < 0) reasons.Add("alcohol_negative");
                else if (stated > MaxMacroGramsPer100g) reasons.Add("alcohol_impossible");
            }
            var alcohol = f.AlcoholGramsPer100g is { } grams && grams > 0 ? grams : 0m;

            // Macro sum sanity
            if (f.ProteinGramsPer100g is { } p && f.CarbGramsPer100g is { } c && f.FatGramsPer100g is { } fat
                && p >= 0 && c >= 0 && fat >= 0 && (p + c + fat + alcohol) > MaxMacroSumPer100g)
                reasons.Add("macro_sum_impossible");

            // Atwater cross-check (only when we have all macros + a usable calorie figure)
            if (!f.UsedInSmallAmounts && f.KcalPer100g is { } k && k >= 0
                && f.ProteinGramsPer100g is { } pr && f.CarbGramsPer100g is { } cb && f.FatGramsPer100g is { } ft
                && pr >= 0 && cb >= 0 && ft >= 0)
            {
                var computed = (4m * pr) + (4m * cb) + (9m * ft) + (AlcoholKcalPerGram * alcohol);
                var low = k * (1m - AtwaterTolerance);
                var high = k * (1m + AtwaterTolerance);
                var acidic = computed > high && IsAcidicFood(name);
                if (Math.Max(k, computed) >= AtwaterMinKcal && (computed < low || computed > high) && !acidic)
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

        private bool IsAcidicFood(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var marker in AcidicFoodNameMarkers)
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool ContainsLetter(string s)
        {
            foreach (var ch in s)
                if (char.IsLetter(ch)) return true;
            return false;
        }
    }
}
