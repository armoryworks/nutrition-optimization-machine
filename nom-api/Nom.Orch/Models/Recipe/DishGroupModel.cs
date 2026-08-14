using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class DishGroupModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int RecipeCount { get; set; }
    }

    /// <summary>One member of a dish group, shaped for the variations rail.</summary>
    public class DishGroupRecipeModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Image { get; set; }
        public decimal? Rating { get; set; }
    }

    public class DishGroupDetailModel : DishGroupModel
    {
        public List<DishGroupRecipeModel> Recipes { get; set; } = new();
    }
}
