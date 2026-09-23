// File: Nom.Orch/Models/MealPlan/MealPlanRuleCreateModel.cs

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanRuleCreateModel
    {
        public long HouseholdId { get; set; }
        public long? DayOfWeekId { get; set; }
        public long? MealTypeId { get; set; }
        public string QueryFilter { get; set; } = string.Empty;
        public int? MaxRecipes { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
