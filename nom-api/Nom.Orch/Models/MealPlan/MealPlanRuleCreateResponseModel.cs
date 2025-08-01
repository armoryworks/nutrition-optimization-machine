// File: Nom.Orch/Models/MealPlan/MealPlanRuleCreateResponseModel.cs

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanRuleCreateResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long DayOfWeekId { get; set; }
        public long MealTypeId { get; set; }
        public string QueryFilterString { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }
} 