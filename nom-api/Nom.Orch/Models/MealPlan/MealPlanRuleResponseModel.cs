// File: Nom.Orch/Models/MealPlan/MealPlanRuleResponseModel.cs

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanRuleResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long? DayOfWeekId { get; set; }
        public string? DayOfWeekName { get; set; }
        public long? MealTypeId { get; set; }
        public string? MealTypeName { get; set; }
        public string QueryFilter { get; set; } = string.Empty;
        public int MaxRecipes { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
