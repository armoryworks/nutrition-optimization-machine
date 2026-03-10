using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanDayModel
    {
        public DateOnly Date { get; set; }
        public string DayOfWeek { get; set; } = string.Empty;
        public List<MealPlanCellModel> Cells { get; set; } = new();
        public List<MealPlanExclusionResponseModel> Exclusions { get; set; } = new();
    }
}
