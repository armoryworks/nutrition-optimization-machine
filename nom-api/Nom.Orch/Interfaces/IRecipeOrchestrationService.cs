// Nom.Orch/Interfaces/IRecipeOrchestrationService.cs
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces // Corrected namespace
{
    /// <summary>
    /// Defines the business logic for managing recipes, ingredients, and their nutritional data.
    /// </summary>
    public interface IRecipeOrchestrationService
    {
        /// <summary>
        /// Searches for ingredients based on a search term.
        /// </summary>
        /// <param name="searchTerm">The term to search for in ingredient names.</param>
        /// <returns>A list of matching ingredients.</returns>
        Task<List<IngredientSearchResponseModel>> SearchIngredientsAsync(string searchTerm);

        /// <summary>
        /// Retrieves the detailed nutritional information for a specific ingredient.
        /// </summary>
        /// <param name="ingredientId">The ID of the ingredient.</param>
        /// <returns>The detailed ingredient model, including its nutrients.</returns>
        Task<IngredientModel> GetIngredientDetailsAsync(long ingredientId);
    }
}
