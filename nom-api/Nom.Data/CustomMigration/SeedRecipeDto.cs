using System.Collections.Generic;

namespace Nom.Data.CustomMigration
{
    internal class SeedRecipeDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long PrepTimeMinutes { get; set; }
        public long CookTimeMinutes { get; set; }
        public long Servings { get; set; }
        public long CategoryId { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string? Image { get; set; }
        public List<SeedRecipeStepDto> Steps { get; set; } = new();
        public List<SeedRecipeIngredientDto> Ingredients { get; set; } = new();
        public List<SeedRecipeNutritionDto> Nutrition { get; set; } = new();
    }
}
