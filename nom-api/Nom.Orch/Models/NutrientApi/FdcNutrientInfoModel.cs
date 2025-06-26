// Nom.Orch/Models/NutrientApi/FoodDetailResult.cs
using System.Collections.Generic;
using System.Text.Json.Serialization; // Required for JsonPropertyName

namespace Nom.Orch.Models.NutrientApi
{

    /// <summary>
    /// Represents the nested 'nutrient' object within FDC's 'foodNutrients' array,
    /// containing nutrient name and unit.
    /// </summary>
    public class FdcNutrientInfoModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("unitName")]
        public string UnitName { get; set; } = string.Empty;
    }
}
