using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanWeekResponseModel
    {
        public DateOnly WeekStart { get; set; }
        public DateOnly WeekEnd { get; set; }
        public List<MealPlanDayModel> Days { get; set; } = new();
    }

    public class MealPlanDayModel
    {
        public DateOnly Date { get; set; }
        public string DayOfWeek { get; set; } = string.Empty;
        public List<MealPlanCellModel> Cells { get; set; } = new();
        public List<MealPlanExclusionResponseModel> Exclusions { get; set; } = new();
    }

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
