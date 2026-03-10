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
}
