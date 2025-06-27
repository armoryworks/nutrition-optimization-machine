// Nom.Orch/Models/NutrientApi/FoodSearchResult.cs
using System;
using System.Text.Json.Serialization; // Required for JsonPropertyName

namespace Nom.Orch.Models.NutrientApi
{
    /// <summary>
    /// A small DTO to represent the root of the FDC search response, which often contains a "foods" array.
    /// </summary>
    internal class FdcSearchResponse
    {
        [JsonPropertyName("foods")]
        public List<FoodSearchResult>? Foods { get; set; }
        // FDC search response also has other properties like 'totalHits', 'currentPage', 'totalPages', etc.
        // We only need the 'foods' array for now.
    }
}
