using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanEntryModel
    {
        public long Id { get; set; }
        public long? RecipeId { get; set; }
        public string? RecipeName { get; set; }
        public string? RecipeImage { get; set; }

        /// <summary>Set when this slot holds a standalone whole food (apple, protein bar) instead of a recipe.</summary>
        public long? IngredientId { get; set; }
        public string? IngredientName { get; set; }
        public decimal? Quantity { get; set; }
        public long? MeasurementId { get; set; }
        public string? MeasurementName { get; set; }
        public long? FoodGroupId { get; set; }
        public string? FoodGroupName { get; set; }

        public string? Title { get; set; }
        public string? Notes { get; set; }
        public decimal? Calories { get; set; }
        public decimal? ProteinGrams { get; set; }
        public decimal? CarbGrams { get; set; }
        public decimal? FatGrams { get; set; }
        public DateOnly? CompletedDate { get; set; }
        public DateTime? ShoppingCompletedAt { get; set; }
    }
}
