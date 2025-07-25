// File: Nom.Orch/Interfaces/IRecipeOrchestrationService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
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

        /// <summary>
        /// Creates a new recipe for the specified author.
        /// </summary>
        /// <param name="request">The request model containing the new recipe's data.</param>
        /// <param name="authorPersonId">The PersonId of the user creating the recipe.</param>
        /// <returns>The ID of the newly created recipe.</returns>
        Task<long> CreateRecipeAsync(CreateRecipeRequest request, long authorPersonId);

        /// <summary>
        /// Creates a new version of an existing recipe.
        /// </summary>
        /// <param name="parentRecipeId">The ID of the recipe to create a new version of.</param>
        /// <param name="authorPersonId">The PersonId of the user creating the new version.</param>
        /// <returns>The ID of the newly created recipe version.</returns>
        Task<long> CreateNewRecipeVersionAsync(long parentRecipeId, long authorPersonId);
    }
}