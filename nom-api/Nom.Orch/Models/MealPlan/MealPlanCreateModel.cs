// File: Nom.Orch/Models/MealPlan/MealPlanCreateModel.cs

using System;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanCreateModel
    {
        public long HouseholdId { get; set; }
        public long AuthorId { get; set; }
        public DateOnly Date { get; set; }
        public long MealTypeId { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public long? RecipeId { get; set; }
    }
} 