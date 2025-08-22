using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class GoalItemModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsQuantifiable { get; set; }
        public string? IngredientName { get; set; }
        public string? NutrientName { get; set; }
        public string? TimeframeType { get; set; }
        public string? Measurement { get; set; }
        public decimal? MeasurementMinimum { get; set; }
        public decimal? MeasurementMaximum { get; set; }
    }
} 