// File: Nom.Orch/Models/MealPlan/MealPlanCreateResponseModel.cs

using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanCreateResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long AuthorId { get; set; }
        public DateOnly Date { get; set; }
        public long MealTypeId { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public long? RecipeId { get; set; }
        public DateTime CreatedDate { get; set; }
    }
} 