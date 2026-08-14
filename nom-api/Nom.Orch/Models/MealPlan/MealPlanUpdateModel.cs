// File: Nom.Orch/Models/MealPlan/MealPlanUpdateModel.cs

using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanUpdateModel
    {
        public DateOnly Date { get; set; }
        public long MealTypeId { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public long? RecipeId { get; set; }

        /// <summary>Schedule a standalone whole food (apple, protein bar) instead of a recipe. Mutually exclusive with RecipeId.</summary>
        public long? IngredientId { get; set; }
        public decimal? Quantity { get; set; }
        public long? MeasurementId { get; set; }
    }
} 