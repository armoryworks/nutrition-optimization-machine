using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class CreatePlanRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public List<GoalModel> Goals { get; set; } = new List<GoalModel>();
        public List<MealModel> Meals { get; set; } = new List<MealModel>();
        public List<RestrictionModel> Restrictions { get; set; } = new List<RestrictionModel>();
    }
} 