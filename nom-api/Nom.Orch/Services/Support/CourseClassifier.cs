using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>Snack, dessert, or both (borderline).</summary>
    [Flags]
    public enum SweetCourse
    {
        Snack = 1,
        Dessert = 2,
        Both = Snack | Dessert,
    }

    /// <summary>
    /// One recipe line as the classifier sees it: grams in the recipe and the ingredient's USDA
    /// per-100 g facts (null when unknown).
    /// </summary>
    public sealed record CourseLine(string IngredientName, decimal? Grams, decimal? KcalPer100g, decimal? TotalSugarsPer100g, decimal? AddedSugarsPer100g);

    public sealed record CourseVerdict(SweetCourse Course, decimal AddedSugarShare, decimal Coverage);

    /// <summary>
    /// Classifies a sweet-or-snack recipe by the share of its energy that comes from added sugar,
    /// anchored on the WHO free-sugars guideline (under 10% of energy): under 10% is a snack, 25%
    /// and over a dessert, between them both. Added sugar is an ingredient's USDA "added sugars"
    /// value when it has one; otherwise a sweetener's (sugar, syrup, honey, sweetened chocolate,
    /// jam…) total sugars. Fruit and dairy sugars never count. Returns null when too few lines
    /// carry grams and calories to judge — the recipe keeps its current classification.
    /// </summary>
    public static class CourseClassifier
    {
        public const decimal SnackBelow = 0.10m;
        public const decimal DessertFrom = 0.25m;
        public const decimal MinimumCoverage = 0.70m;
        private const decimal KcalPerGramSugar = 4m;

        private static readonly Regex Sweetener = new(
            @"^(sugars?\b|honey\b|syrups?\b|molasses\b|candies\b|jams?\b|jellies\b|preserves\b|marmalade\b|frostings?\b|sweeteners?\b|toppings\b|agave\b|dulce\b)" +
            @"|\b(sweetened condensed|condensed, sweetened|semisweet|milk chocolate|chocolate chips?|maple syrup|corn syrup|brown sugar|powdered sugar|marshmallows?|caramels?|sprinkles)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NotSweetener = new(@"\b(sugar snap|unsweetened|sugar[- ]free|sugar substitute)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsSweetener(string ingredientName) =>
            Sweetener.IsMatch(ingredientName) && !NotSweetener.IsMatch(ingredientName);

        public static CourseVerdict? Classify(IReadOnlyCollection<CourseLine> lines)
        {
            if (lines.Count == 0) return null;

            var known = lines.Where(l => l.Grams is > 0 && l.KcalPer100g != null).ToList();
            var coverage = (decimal)known.Count / lines.Count;
            if (coverage < MinimumCoverage) return null;

            var kcal = known.Sum(l => l.Grams!.Value * l.KcalPer100g!.Value / 100m);
            if (kcal <= 0) return null;

            var addedGrams = known.Sum(l =>
            {
                var per100 = l.AddedSugarsPer100g ?? (IsSweetener(l.IngredientName) ? l.TotalSugarsPer100g : null);
                return per100 is > 0 ? l.Grams!.Value * per100.Value / 100m : 0m;
            });

            var share = Math.Round(addedGrams * KcalPerGramSugar / kcal, 4);
            var course = share < SnackBelow ? SweetCourse.Snack : share >= DessertFrom ? SweetCourse.Dessert : SweetCourse.Both;
            return new CourseVerdict(course, share, Math.Round(coverage, 4));
        }
    }
}
