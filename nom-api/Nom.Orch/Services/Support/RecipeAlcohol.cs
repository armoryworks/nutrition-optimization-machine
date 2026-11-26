using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Data.Nutrition;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// What the alcohol count needs of one recipe line: its food's alcohol per 100 g (null when the
    /// food stores none above <see cref="DietCriteriaMatcher.PresencePer100g"/>), whether the food is
    /// a flavouring, the line's grams when the label can weigh it, and its stored retention. <c>Label</c>, when
    /// given, is how a reader is shown the line (<see cref="ProseWithholding.LineName(string?, string?, string?, decimal, string?, bool)"/>);
    /// the line's text is still what the count reads.
    /// </summary>
    public sealed record AlcoholLineFacts(long IngredientId, string? RawLine, string? FoodName, string? LineKind, decimal Quantity, decimal? Grams, decimal? AlcoholPer100g, bool FoodIsFlavouring, decimal? Retention, string? RetentionBasis, string? Label = null);

    /// <summary>One drink line whose alcohol the count holds: its food, its text, how a reader is shown it, and the grams that stay (null when its amount cannot be counted).</summary>
    public sealed record AlcoholDrinkLine(long IngredientId, string? RawLine, string? FoodName, decimal? AsWritten, decimal? Retained, decimal? Retention, string? RetentionBasis, string? Label = null);

    /// <summary>
    /// A recipe's alcohol as written and after cooking (alcohol design A7, owner answer 6): the
    /// drinks' grams per serving as written and as they stay, the flavourings' grams apart (a
    /// note, never an exclusion), and the drink lines whose amount cannot be counted (a drink line
    /// on a food with no alcohol figure, or one the label cannot weigh). A household that avoids
    /// alcohol by the retained amount excludes the recipe when a drink line cannot be counted or
    /// when more than its limit stays per serving.
    /// </summary>
    public sealed record RecipeAlcohol(decimal Servings, bool ServingsKnown, decimal DrinkAsWritten, decimal DrinkRetained, decimal FlavouringRetained, IReadOnlyList<AlcoholDrinkLine> Drinks, IReadOnlyList<string> Flavourings)
    {
        public static readonly RecipeAlcohol None = new(1m, false, 0m, 0m, 0m, Array.Empty<AlcoholDrinkLine>(), Array.Empty<string>());

        public decimal AsWrittenPerServing => DrinkAsWritten / Servings;

        public decimal RetainedPerServing => DrinkRetained / Servings;

        public decimal FlavouringPerServing => FlavouringRetained / Servings;

        public IEnumerable<AlcoholDrinkLine> Uncounted => Drinks.Where(d => d.Retained == null);

        public bool HasUncounted => Drinks.Any(d => d.Retained == null);

        /// <summary>Whether the retained alcohol is an upper bound: a counted drink line's retention is read "up to" (<see cref="AlcoholRetention.UpTo"/>), so the steps do not determine a single value.</summary>
        public bool UpperBound => Drinks.Any(d => d.Retained != null && AlcoholRetention.ReadBasis(d.RetentionBasis)?.Kind == AlcoholRetention.UpTo);

        public bool Any => Drinks.Count > 0 || Flavourings.Count > 0;

        /// <summary>Whether a household whose limit is <paramref name="maxPerServing"/> grams of retained alcohol per serving must not be offered the recipe.</summary>
        public bool Excludes(decimal maxPerServing) => HasUncounted || RetainedPerServing > maxPerServing;

        /// <summary>
        /// The count over a recipe's lines. A line on an alcoholic food is a drink unless the food or the
        /// line reads as a flavouring and the line's own text does not read as a drink; a drink line on a
        /// flavouring food ("1/4 cup brandy" on Vanilla Extract) cannot be counted. A drink's grams as
        /// written are the food's alcohol per 100 g times its grams, and its retained grams those times
        /// its retention (all of it when not read). A line whose own text reads as a drink while its
        /// food stores no alcohol is a drink that cannot be counted. <paramref name="servings"/> is the label's servings, or null for whole-recipe totals.
        /// </summary>
        public static RecipeAlcohol Count(IEnumerable<AlcoholLineFacts> lines, decimal? servings)
        {
            decimal asWritten = 0m, retained = 0m, flavouring = 0m;
            var drinks = new List<AlcoholDrinkLine>();
            var flavourings = new List<string>();
            foreach (var line in lines)
            {
                var reading = AlcoholLineContext.Read(line.RawLine);
                if (line.AlcoholPer100g is { } per100 && per100 > DietCriteriaMatcher.PresencePer100g)
                {
                    var weighable = (line.LineKind == null || line.LineKind == "ingredient") && line.Quantity > 0m && line.Grams is > 0m;
                    var factor = line.Retention is { } r && r >= 0m && r <= 1m ? r : 1m;
                    var lineIsDrink = AlcoholLineContext.CountsForDiet(reading);
                    if (!lineIsDrink && (line.FoodIsFlavouring || AlcoholLineContext.IsFlavouring(reading)))
                    {
                        flavourings.Add(Name(line));
                        if (weighable) flavouring += per100 * line.Grams!.Value / 100m * factor;
                        continue;
                    }
                    if (!weighable || (lineIsDrink && line.FoodIsFlavouring))
                    {
                        drinks.Add(new AlcoholDrinkLine(line.IngredientId, line.RawLine, line.FoodName, null, null, line.Retention, line.RetentionBasis, line.Label));
                        continue;
                    }
                    var grams = per100 * line.Grams!.Value / 100m;
                    asWritten += grams;
                    retained += grams * factor;
                    drinks.Add(new AlcoholDrinkLine(line.IngredientId, line.RawLine, line.FoodName, grams, grams * factor, line.Retention, line.RetentionBasis, line.Label));
                }
                else if (DietCriteriaMatcher.LineCounts(line.RawLine))
                {
                    drinks.Add(new AlcoholDrinkLine(line.IngredientId, line.RawLine, line.FoodName, null, null, line.Retention, line.RetentionBasis, line.Label));
                }
            }
            var known = servings is > 0m;
            return new RecipeAlcohol(known ? servings!.Value : 1m, known, asWritten, retained, flavouring, drinks, flavourings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        /// <summary>Whether a food is a flavouring for the alcohol count: in USDA Spices and Herbs, or named as an extract or flavouring.</summary>
        public static bool IsFlavouringFood(string? name, int? categoryId) =>
            categoryId == CatalogAdmissionRule.SpicesAndHerbs || AlcoholLineContext.IsFlavouring(AlcoholLineContext.Read(name));

        private static string Name(AlcoholLineFacts line) =>
            !string.IsNullOrWhiteSpace(line.FoodName) ? line.FoodName!.Trim() : (line.Label ?? line.RawLine ?? string.Empty).Trim();
    }
}
