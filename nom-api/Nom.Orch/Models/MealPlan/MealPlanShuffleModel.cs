using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanShuffleModel
    {
        public long HouseholdId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool ReplaceExisting { get; set; }
    }

    public class MealPlanShuffleResponseModel
    {
        public int Created { get; set; }
        public int Deleted { get; set; }
        public MealPlanWeekResponseModel Week { get; set; } = new();
    }
}
