// File: Nom.Orch/Models/MealPlan/MealPlanRuleResponseModel.cs

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanRuleResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long DayOfWeekId { get; set; }
        public string DayOfWeek { get; set; } = string.Empty;
        public long MealTypeId { get; set; }
        public string MealType { get; set; } = string.Empty;
        public string QueryFilterString { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
} 