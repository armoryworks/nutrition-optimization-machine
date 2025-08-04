using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class MealModel
    {
        public long Id { get; set; }
        public string MealType { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public List<RecipeModel> Recipes { get; set; } = new List<RecipeModel>();
    }
} 