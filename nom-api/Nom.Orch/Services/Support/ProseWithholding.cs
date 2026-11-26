using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The redaction of a recipe whose prose is withheld (<see cref="RecipeEntity.ProseWithheld"/>):
    /// its stored description and steps are never served, to any reader, author and admin included.
    /// Every response that carries a recipe's description or steps takes them through here, or, in a
    /// query projection, through the same test on <c>ProseWithheld</c>; <c>ProseWithheldEndpointTests</c>
    /// walks each such endpoint with a withheld recipe. The recipe itself stays listed and usable.
    /// </summary>
    public static class ProseWithholding
    {
        /// <summary>The description to serve: empty while the prose is withheld.</summary>
        public static string Description(RecipeEntity recipe) =>
            recipe.ProseWithheld ? string.Empty : recipe.Description ?? string.Empty;

        /// <summary>The description to serve, or null where the response has none: null while the prose is withheld.</summary>
        public static string? DescriptionOrNull(RecipeEntity recipe) =>
            recipe.ProseWithheld ? null : recipe.Description;

        /// <summary>
        /// The ingredient line wording to serve: none for a recipe of a facts-only source
        /// (<see cref="RecipeEntity.SourceRights"/>), whose readers get NOM's catalog names, amounts and
        /// units only, never the publisher's line as written.
        /// </summary>
        public static string? LineWording(RecipeEntity recipe, string? rawLine) =>
            recipe.SourceRights == SourceRightsRule.FactsOnly ? null : rawLine;

        /// <summary>The most words a facts-only line's own food words may have; a longer reading is the publisher's sentence, not a food.</summary>
        public const int MaxFoodWords = 4;

        /// <summary>The name a facts-only line is shown under when neither a catalog food nor its own food words name it.</summary>
        public const string UnnamedFood = "Ingredient";

        private static readonly Regex FoodWordBreak = new(@"[,;:!?()\[\]—–]|\s-\s|\*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex FoodWord = new(@"^\p{L}[\p{L}'’-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> NotFoodWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "i", "you", "your", "we", "our", "my", "me", "these", "this", "that", "those", "it", "they", "can", "could", "will", "would",
            "should", "must", "may", "make", "makes", "made", "use", "using", "used", "note", "notes", "tip", "see", "is", "are", "be", "if",
            "by", "when", "here", "there", "like", "love", "recipe", "recipes", "optional", "need", "want", "try",
        };

        /// <summary>
        /// Whether a food's own name may be shown for a facts-only line: a curated food or one taken from the USDA catalog (an
        /// FDC id). A food created from a line's own text (uncurated, no FDC id) may carry the publisher's wording as its name.
        /// </summary>
        public static bool CatalogFood(long curationStatusId, string? fdcId) =>
            curationStatusId == (long)CurationStatusEnum.Curated || !string.IsNullOrWhiteSpace(fdcId);

        /// <summary>The same for a loaded food.</summary>
        public static bool CatalogFood(IngredientEntity? food) => food != null && CatalogFood(food.CurationStatusId, food.FdcId);

        /// <summary>
        /// The food words of a line by NOM's rule and no model: the line without its amount, unit and notes
        /// (<see cref="IngredientNameNormalizer.Normalize"/>), up to its first comma, colon, bracket or dash, and only when what is
        /// left is at most <see cref="MaxFoodWords"/> plain words none of which reads as a sentence; otherwise null.
        /// </summary>
        public static string? FoodWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var normalized = IngredientNameNormalizer.Normalize(text);
            var cut = FoodWordBreak.Split(normalized, 2)[0].Trim().TrimEnd('.');
            var words = cut.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > MaxFoodWords) return null;
            if (words.Any(w => !FoodWord.IsMatch(w) || NotFoodWords.Contains(w))) return null;
            return string.Join(' ', words);
        }

        private static readonly Regex ClosingPurpose = new(@"\s+(?:for|to)\s+\p{L}.*$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex Alternative = new(@"\s+or\s+.*$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The food words of a line read more loosely, where <see cref="FoodWords"/> finds none: its preparation words
        /// (<see cref="IngredientNameNormalizer.Bare"/>), a closing purpose ("avocado and cilantro for topping") and every
        /// alternative after the first ("zest of one lemon or orange" is lemon zest) taken off; null when that leaves no food
        /// words either. For naming a household's shopping item or food by NOM's words (staging checkpoint 7, S18).
        /// </summary>
        public static string? PlainFoodWords(string? text)
        {
            if (FoodWords(text) is { } plain) return plain;
            if (string.IsNullOrWhiteSpace(text)) return null;
            var bare = IngredientNameNormalizer.Bare(text);
            var cut = ClosingPurpose.Replace(bare, string.Empty);
            if (FoodWords(cut) is { } withoutPurpose) return withoutPurpose;
            var first = Alternative.Replace(cut, string.Empty);
            return FoodWords(IngredientNameNormalizer.Bare(first));
        }

        /// <summary>
        /// The food name a response shows for one ingredient line: the food's own name, except for a line of a facts-only recipe
        /// whose food is not a catalog food (<see cref="CatalogFood(long, string?)"/>), which is shown under its own food words
        /// (<see cref="FoodWords"/>) only, or null when it has none, so a food named from the publisher's line never shows that line.
        /// </summary>
        public static string? FoodName(string? sourceRights, string? rawLine, string? foodName, bool catalogFood)
        {
            if (sourceRights != SourceRightsRule.FactsOnly || catalogFood) return foodName;
            return FoodWords(rawLine) ?? FoodWords(foodName);
        }

        /// <summary>The same for a loaded line of a recipe: the food's name (empty without a food), or for a facts-only line on a food that is not a catalog food its food words, else <see cref="UnnamedFood"/>.</summary>
        public static string FoodName(RecipeEntity recipe, RecipeIngredientEntity line) =>
            recipe.SourceRights != SourceRightsRule.FactsOnly || CatalogFood(line.Ingredient)
                ? line.Ingredient?.Name ?? string.Empty
                : FoodName(recipe.SourceRights, line.RawLine, line.Ingredient?.Name, false) ?? UnnamedFood;

        /// <summary>
        /// How a response names one ingredient line (a diet warning, the alcohol note): the line as written, or, for a
        /// recipe of a facts-only source, by <see cref="LineWording"/>'s rule NOM's own wording of it (amount, unit and
        /// the food by <see cref="FoodName(string?, string?, string?, bool)"/>), never the publisher's line.
        /// </summary>
        public static string LineName(RecipeEntity recipe, RecipeIngredientEntity line) =>
            LineName(recipe.SourceRights, line.RawLine, line.Ingredient?.Name, line.Quantity, line.Measurement?.Name, CatalogFood(line.Ingredient));

        /// <summary>The same for a query projection: the recipe's <see cref="RecipeEntity.SourceRights"/> stamp and the line's text, food, amount and unit, and whether its food is a catalog food.</summary>
        public static string LineName(string? sourceRights, string? rawLine, string? foodName, decimal quantity, string? measurement, bool catalogFood)
        {
            if (sourceRights != SourceRightsRule.FactsOnly && !string.IsNullOrWhiteSpace(rawLine)) return rawLine.Trim();
            var parts = new List<string>();
            if (quantity > 0m)
            {
                parts.Add(quantity.ToString("0.##", CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(measurement)) parts.Add(measurement.Trim().ToLowerInvariant());
            }

            var food = FoodName(sourceRights, rawLine, foodName, catalogFood);
            parts.Add(string.IsNullOrWhiteSpace(food) ? "an ingredient" : food.Trim());
            return string.Join(" ", parts);
        }

        /// <summary>
        /// The title to serve for a meal-plan entry of a recipe: for a recipe of a facts-only source the recipe's name as
        /// it is now, because the entry's stored title is a copy taken when it was planned, before the title rule may have
        /// reduced a distinctive title to the dish name; otherwise the entry's own title.
        /// </summary>
        public static string? EntryTitle(string? title, RecipeEntity? recipe) =>
            recipe?.SourceRights == SourceRightsRule.FactsOnly ? recipe.Name : title;

        /// <summary>The steps to serve, in step order: none while the prose is withheld.</summary>
        public static IReadOnlyList<RecipeStepEntity> Steps(RecipeEntity recipe) =>
            recipe.ProseWithheld || recipe.RecipeSteps == null
                ? Array.Empty<RecipeStepEntity>()
                : recipe.RecipeSteps.OrderBy(s => s.StepNumber).ToList();
    }
}
