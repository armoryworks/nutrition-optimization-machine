using System.Collections.Generic;

namespace Nom.Data.CustomMigration
{
    internal class ExtendedSeedDataRoot
    {
        public List<SeedIngredientModel> Ingredients { get; set; } = new();
        public List<ExtendedSeedRecipeModel> Recipes { get; set; } = new();
    }
}
