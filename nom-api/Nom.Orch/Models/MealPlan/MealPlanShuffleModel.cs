using System;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanShuffleModel
    {
        public long HouseholdId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool ReplaceExisting { get; set; }
    }
}
