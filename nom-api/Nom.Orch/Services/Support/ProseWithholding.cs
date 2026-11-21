using System;
using System.Collections.Generic;
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

        /// <summary>The steps to serve, in step order: none while the prose is withheld.</summary>
        public static IReadOnlyList<RecipeStepEntity> Steps(RecipeEntity recipe) =>
            recipe.ProseWithheld || recipe.RecipeSteps == null
                ? Array.Empty<RecipeStepEntity>()
                : recipe.RecipeSteps.OrderBy(s => s.StepNumber).ToList();
    }
}
