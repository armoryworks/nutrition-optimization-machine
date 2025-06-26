// Nom.Orch/Models/NutrientApi/FoodDetailResult.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nom.Orch.Models.NutrientApi
{
    /// <summary>
    /// Represents a detailed food item with its nutritional components from USDA FoodData Central (FDC) API.
    /// Uses JsonPropertyName attributes to map to FDC's typical JSON field names.
    /// </summary>
    public class FoodDetailResult
    {
        /// <summary>
        /// Unique identifier for the food item in the external FDC database.
        /// Corresponds to FDC's 'fdcId'.
        /// </summary>
        [JsonPropertyName("fdcId")]
        public string FdcId { get; set; } = string.Empty;

        /// <summary>
        /// The common name/description of the food item.
        /// Corresponds to FDC's 'description'.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The base gram weight for the nutrient values (e.g., nutrients are per 100g of this food).
        /// FDC doesn't always provide a direct 'gramWeight' at the root for a single food.
        /// Nutrients often have their own 'amount' per 'gram' or other unit.
        /// This property will represent a conceptual base weight if applicable, otherwise might be derived.
        /// For simplicity, we'll keep it as a default 100g, consistent with common nutrient data presentation.
        /// </summary>
        public decimal GramWeight { get; set; } = 100.0m; // FDC's nutrients are generally per 100g or per common measure.

        /// <summary>
        /// A list of nutrient values associated with this food item.
        /// Corresponds to FDC's 'foodNutrients' array.
        /// </summary>
        [JsonPropertyName("foodNutrients")]
        public List<NutrientValueModel> Nutrients { get; set; } = new List<NutrientValueModel>();
    }
}
