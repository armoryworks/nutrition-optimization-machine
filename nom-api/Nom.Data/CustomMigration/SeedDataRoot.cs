using System.Collections.Generic;

namespace Nom.Data.CustomMigration
{
    internal class SeedDataRoot
    {
        public List<SeedIngredientModel> Ingredients { get; set; } = new();
        public List<SeedRecipeModel> Recipes { get; set; } = new();
    }
}
