using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class RecipeModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string CurationStatus { get; set; } = "NonCurated";
    }
} 