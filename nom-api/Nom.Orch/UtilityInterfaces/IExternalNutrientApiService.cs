// Nom.Orch/UtilityInterfaces/IExternalNutrientApiService.cs
using Nom.Orch.Models.NutrientApi; // For FoodSearchResult, FoodDetailResult
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Defines the contract for a service that interacts with the external
    /// USDA FoodData Central (FDC) nutrient database API to fetch
    /// food item information and their nutritional values.
    /// </summary>
    public interface IExternalNutrientApiService
    {
        /// <summary>
        /// Searches the FDC database for food items matching a query string.
        /// </summary>
        /// <param name="query">The search term (e.g., "chicken breast", "all-purpose flour").</param>
        /// <param name="limit">Maximum number of results to return.</param>
        /// <returns>A list of matching <see cref="FoodSearchResult"/> objects.</returns>
        Task<List<FoodSearchResult>> SearchFoodsAsync(string query, int limit = 5);

        /// <summary>
        /// Retrieves detailed nutrient information for a specific food item
        /// using its external FDC ID.
        /// </summary>
        /// <param name="fdcId">The unique ID of the food item in the FDC database.</param>
        /// <returns>A <see cref="FoodDetailResult"/> object containing detailed nutrient data, or null if not found.</returns>
        Task<FoodDetailResult?> GetFoodDetailsAsync(string fdcId);
    }
}
