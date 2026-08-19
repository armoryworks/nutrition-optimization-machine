using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Derives a recipe's per-serving nutrition (recipe."RecipeNutrition") from its
    /// ingredients' per-100 g facts. Hand-authored/seeded rows (DateCalculated NULL)
    /// are never overwritten.
    /// </summary>
    public interface IRecipeNutritionService
    {
        /// <summary>Recompute one recipe. Returns the number of nutrient rows written (0 = nothing derivable or hand-authored rows kept).</summary>
        Task<int> RecalculateAsync(long recipeId);

        /// <summary>Recompute every recipe that uses the ingredient (after its nutrition changed).</summary>
        Task<int> RecalculateForIngredientAsync(long ingredientId);
    }
}
