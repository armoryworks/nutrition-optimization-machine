using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanEntryModel
    {
        public long Id { get; set; }
        public long? RecipeId { get; set; }
        public string? RecipeName { get; set; }
        public string? RecipeImage { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public decimal? Calories { get; set; }
        public decimal? ProteinGrams { get; set; }
        public decimal? CarbGrams { get; set; }
        public decimal? FatGrams { get; set; }
        public DateOnly? CompletedDate { get; set; }
    }
}
