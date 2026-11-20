using System.Collections.Generic;
using System.Linq;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The hard preconditions for approving a recipe, shared by admin approval and the
    /// auto-approval sweep: no verbatim source prose, every ingredient that feeds the
    /// nutrition label curated, and no line on a prepared food. A line that is not an ingredient (equipment, a note, a
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
            if (uncurated.Count > 0)
            {
                return $"Cannot approve recipe: The following ingredients are not curated: {string.Join(", ", uncurated)}";
            }

            var prepared = PreparedIngredientNames(recipe);
            return prepared.Count > 0
                ? $"Cannot approve recipe: The following lines are linked to a prepared food, which is never a recipe ingredient: {string.Join(", ", prepared)}"
                : null;
        }

        /// <summary>
        /// The prepared foods (<see cref="PreparedFoods"/>) the recipe's lines are linked to, counted
        /// or not: a prepared food is never a recipe line's food, so such a line is a wrong link.
        /// </summary>
        public static List<string> PreparedIngredientNames(RecipeEntity recipe) =>
            recipe.RecipeIngredients?
                .Where(ri => ri.Ingredient != null && PreparedFoods.IsPrepared(ri.Ingredient))
                .Select(ri => ri.Ingredient!.Name)
                .Distinct()
                .ToList() ?? new List<string>();

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
