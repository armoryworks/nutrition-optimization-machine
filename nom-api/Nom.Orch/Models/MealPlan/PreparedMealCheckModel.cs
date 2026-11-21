using System.Collections.Generic;

namespace Nom.Orch.Models.MealPlan
{
    public class PreparedMealCheckModel
    {
        public long IngredientId { get; set; }
        public string? Held { get; set; }
        public List<string> Names { get; set; } = new();
        public bool Severe { get; set; }
    }
}
