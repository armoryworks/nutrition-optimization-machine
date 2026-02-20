using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanExclusionCreateModel
    {
        public long HouseholdId { get; set; }
        public long PersonId { get; set; }
        public DateOnly Date { get; set; }
        public long? MealTypeId { get; set; }
    }
}
