using System.Collections.Generic;

namespace Nom.Data.CustomMigration
{
    internal class ExtendedSeedDataRoot
    {
        public List<SeedIngredientDto> Ingredients { get; set; } = new();
        public List<ExtendedSeedRecipeDto> Recipes { get; set; } = new();
    }
}
