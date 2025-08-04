using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class GoalModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? GoalType { get; set; }
        public DateOnly? BeginDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public List<GoalItemModel> GoalItems { get; set; } = new List<GoalItemModel>();
    }
} 