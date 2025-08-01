// File: nom-api/Nom.Orch/Models/Recipe/RecipeNutritionSearchModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeNutritionSearchModel
    {
        public long Id { get; set; }
        public string NutrientName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal? DailyValuePercent { get; set; }
    }
} 