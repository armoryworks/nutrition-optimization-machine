using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class RestrictionModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? RestrictionType { get; set; }
        public string? IngredientName { get; set; }
        public string? NutrientName { get; set; }
        public int? Severity { get; set; }
    }
} 