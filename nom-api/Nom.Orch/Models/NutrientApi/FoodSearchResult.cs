// Nom.Orch/Models/NutrientApi/FoodSearchResult.cs
using System;
using System.Text.Json.Serialization;

namespace Nom.Orch.Models.NutrientApi
{
    /// <summary>
    /// Represents a simplified food item search result from USDA FoodData Central (FDC) API.
    /// Uses JsonPropertyName attributes to map to FDC's typical JSON field names.
    /// </summary>
    public class FoodSearchResult
    {
        /// <summary>
        /// Unique identifier for the food item in the external FDC database.
        /// Corresponds to FDC's 'fdcId'.
        /// </summary>
        [JsonPropertyName("fdcId")]
        public string FdcId { get; set; } = string.Empty;

        /// <summary>
        /// The common name/description of the food item from FDC.
        /// Corresponds to FDC's 'description'.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// A category for the food item from FDC (e.g., "Dairy and Egg Products", "Vegetables and Vegetable Products").
        /// Corresponds to FDC's 'foodCategory'.
        /// </summary>
        [JsonPropertyName("foodCategory")]
        public string? Category { get; set; }
    }

}
