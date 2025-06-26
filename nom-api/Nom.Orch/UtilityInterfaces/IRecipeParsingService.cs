// Nom.Orch/UtilityInterfaces/IRecipeParsingService.cs
using Nom.Data.Recipe; // To reference RecipeEntity and its nested entities
using Nom.Orch.Models.Recipe; // To reference KaggleRawRecipeDataModel
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Nom.Orch.UtilityInterfaces // Corrected namespace for Utility Interfaces
{
    /// <summary>
    /// Defines the contract for a utility service responsible for parsing raw recipe data
    /// from external sources (like Kaggle CSV) into structured Nom.Data.Recipe entities.
    /// </summary>
    public interface IRecipeParsingService
    {
        /// <summary>
        /// Parses a raw recipe data model (e.g., from Kaggle CSV) into a fully structured
        /// RecipeEntity object, including its ingredients and steps.
        /// </summary>
        /// <param name="rawRecipeData">The raw recipe data model from an external source.</param>
        /// <returns>A fully populated RecipeEntity, or null if essential parsing fails.</returns>
        Task<RecipeEntity?> ParseRawRecipeDataAsync(KaggleRawRecipeDataModel rawRecipeData);
    }
}
