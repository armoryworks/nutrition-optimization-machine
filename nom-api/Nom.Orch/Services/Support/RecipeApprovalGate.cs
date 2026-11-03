using System.Collections.Generic;
using System.Linq;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The hard preconditions for approving a recipe, shared by admin approval and the
    /// auto-approval sweep: no verbatim source prose, and every ingredient that feeds the
    /// nutrition label curated. A line that is not an ingredient (equipment, a note, a
    /// reference to another recipe) or that never had an amount ("salt to taste") contributes
    /// nothing to the label and does not block; household restrictions match on every line by
    /// name regardless. Needs <see cref="RecipeEntity.RecipeIngredients"/> loaded with their ingredients.
    /// </summary>
    public static class RecipeApprovalGate
    {
        public static string? RefusalReason(RecipeEntity recipe)
        {
            if (recipe.ContainsSourceProse)
            {
                return "Cannot approve recipe: it still contains the source site's verbatim prose. Rewrite the description and steps in original words first.";
            }

            var uncurated = UncuratedIngredientNames(recipe);
            return uncurated.Count > 0
                ? $"Cannot approve recipe: The following ingredients are not curated: {string.Join(", ", uncurated)}"
                : null;
        }

        public static List<string> UncuratedIngredientNames(RecipeEntity recipe) =>
            recipe.RecipeIngredients?
                .Where(ri => ri.Ingredient != null && ri.Ingredient.CurationStatusId != (long)CurationStatusEnum.Curated && CountsTowardNutrition(ri))
                .Select(ri => ri.Ingredient?.Name ?? "Unknown")
                .Distinct()
                .ToList() ?? new List<string>();

        public static bool CountsTowardNutrition(RecipeIngredientEntity line) =>
            !RecipeIngredientLineKind.IsExempt(line.LineKind)
            && !(line.Quantity <= 0 && Services.RecipeVettingService.IsAcceptablyUnquantified(line.RawLine, line.LineKind));
    }
}
