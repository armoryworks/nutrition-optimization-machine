// Nom.Orch/Models/NutrientApi/FoodDetailResult.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nom.Orch.Models.NutrientApi
{
    /// <summary>
    /// Represents a single nutrient and its value within a FoodDetailResult.
    /// </summary>
    public class NutrientValueModel
    {
        /// <summary>
        /// The name of the nutrient (e.g., "Protein", "Vitamin C", "Energy").
        /// This is nested within a 'nutrient' object in FDC response.
        /// </summary>
        [JsonPropertyName("nutrient")]
        public FdcNutrientInfoModel NutrientInfo { get; set; } = new FdcNutrientInfoModel();

        /// <summary>
        /// The amount of the nutrient. Corresponds to FDC's 'amount'.
        /// </summary>
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        /// <summary>
        /// The unit of measurement for the nutrient's amount (e.g., "g", "mg", "kcal").
        /// This is nested within a 'nutrient' object and then 'unitName'.
        /// </summary>
        public string Unit => NutrientInfo.UnitName; // Read-only property that gets unit from nested object
    }
}
