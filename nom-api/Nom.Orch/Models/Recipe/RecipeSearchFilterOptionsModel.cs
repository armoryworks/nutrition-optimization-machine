using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeSearchFilterOptionModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Group { get; set; }
    }

    public class RecipeSearchFilterOptionsModel
    {
        public List<RecipeSearchFilterOptionModel> MealTypes { get; set; } = new();
        public List<RecipeSearchFilterOptionModel> Courses { get; set; } = new();
        public List<RecipeSearchFilterOptionModel> KitchenTools { get; set; } = new();
    }
}
