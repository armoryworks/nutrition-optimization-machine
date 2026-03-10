using System.Collections.Generic;

namespace Nom.Data.CustomMigration
{
    internal class SeedDataRoot
    {
        public List<SeedIngredientDto> Ingredients { get; set; } = new();
        public List<SeedRecipeDto> Recipes { get; set; } = new();
    }
}
