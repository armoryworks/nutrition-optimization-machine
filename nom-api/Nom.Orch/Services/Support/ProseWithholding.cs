using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        /// <summary>
        /// How a response names one ingredient line (a diet warning, the alcohol note): the line as written, or, for a
        /// recipe of a facts-only source, by <see cref="LineWording"/>'s rule NOM's own wording of it (amount, unit and
        /// catalog food name), never the publisher's line.
        /// </summary>
        public static string LineName(RecipeEntity recipe, RecipeIngredientEntity line) =>
            LineName(recipe.SourceRights, line.RawLine, line.Ingredient?.Name, line.Quantity, line.Measurement?.Name);

        /// <summary>The same for a query projection: the recipe's <see cref="RecipeEntity.SourceRights"/> stamp and the line's text, food, amount and unit.</summary>
        public static string LineName(string? sourceRights, string? rawLine, string? foodName, decimal quantity, string? measurement)
        {
            if (sourceRights != SourceRightsRule.FactsOnly && !string.IsNullOrWhiteSpace(rawLine)) return rawLine.Trim();
            var parts = new List<string>();
            if (quantity > 0m)
            {
                parts.Add(quantity.ToString("0.##", CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(measurement)) parts.Add(measurement.Trim().ToLowerInvariant());
            }

            parts.Add(string.IsNullOrWhiteSpace(foodName) ? "an ingredient" : foodName.Trim());
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
