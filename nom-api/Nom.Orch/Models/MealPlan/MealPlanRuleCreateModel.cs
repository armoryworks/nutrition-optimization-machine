// File: Nom.Orch/Models/MealPlan/MealPlanRuleCreateModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanRuleCreateModel
    {
        public long HouseholdId { get; set; }
        public long DayOfWeekId { get; set; }
        public long MealTypeId { get; set; }
        public string QueryFilterString { get; set; } = string.Empty;
    }
} 