// File: Nom.Orch/Models/MealPlan/MealPlanResponseModel.cs

using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long AuthorId { get; set; }
        public DateOnly Date { get; set; }
        public long MealTypeId { get; set; }
        public string MealType { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public long? RecipeId { get; set; }
        public string? RecipeName { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
} 