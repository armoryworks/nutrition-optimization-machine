using System.Collections.Generic;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanCellModel
    {
        public long MealTypeId { get; set; }
        public string MealType { get; set; } = string.Empty;
        public List<MealPlanEntryModel> Entries { get; set; } = new();

        // Aggregated nutrition across all entries
        public decimal? TotalCalories { get; set; }
        public decimal? TotalProteinGrams { get; set; }
        public decimal? TotalCarbGrams { get; set; }
        public decimal? TotalFatGrams { get; set; }
    }
}
