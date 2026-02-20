using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanExclusionResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long PersonId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public long? MealTypeId { get; set; }
        public string? MealType { get; set; }
    }
}
