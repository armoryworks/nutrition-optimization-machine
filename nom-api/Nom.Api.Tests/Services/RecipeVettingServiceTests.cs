using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    public class RecipeVettingServiceTests
    {
        private readonly RecipeVettingService _vetting = new();

        private static ScraperRecipe PlausibleRecipe() => new()
        {
            Name = "Classic Buttermilk Pancakes",
            PrepTimeMinutes = 15,
            CookTimeMinutes = 20,
            TotalTimeMinutes = 35,
            RecipeServings = 4,
            Ingredients =
            {
                new ScraperIngredient { RawLine = "2 cups flour", Name = "flour", Quantity = 2, Unit = "cups" },
                new ScraperIngredient { RawLine = "2 cups buttermilk", Name = "buttermilk", Quantity = 2, Unit = "cups" },
                new ScraperIngredient { RawLine = "2 large eggs", Name = "large eggs", Quantity = 2 },
            },
            Steps =
            {
                new ScraperStep { Order = 1, Instruction = "Whisk the dry ingredients together in a large bowl." },
                new ScraperStep { Order = 2, Instruction = "Fold in the wet ingredients and cook on a griddle." },
            },
        };

        [Fact]
        public async Task Plausible_recipe_passes_with_no_issues()
        {
            var issues = await _vetting.VetAsync(PlausibleRecipe());
            Assert.Empty(issues);
        }

        [Fact]
        public async Task Implausible_times_and_servings_are_flagged()
        {
            var recipe = PlausibleRecipe();
            recipe.CookTimeMinutes = 100_000;
            recipe.RecipeServings = 5000;

            var issues = await _vetting.VetAsync(recipe);

            Assert.Contains(issues, i => i.Contains("Cook time"));
            Assert.Contains(issues, i => i.Contains("Servings"));
        }

        [Fact]
        public async Task Total_time_less_than_parts_is_flagged()
        {
            var recipe = PlausibleRecipe();
            recipe.TotalTimeMinutes = 5;

            var issues = await _vetting.VetAsync(recipe);

            Assert.Contains(issues, i => i.Contains("Total time"));
        }

        [Fact]
        public async Task Incomplete_extraction_is_flagged()
        {
            var recipe = PlausibleRecipe();
            recipe.Ingredients.Clear();
            recipe.Steps.Clear();

            var issues = await _vetting.VetAsync(recipe);

            Assert.Contains(issues, i => i.Contains("ingredient line"));
            Assert.Contains(issues, i => i.Contains("instruction step"));
        }

        [Fact]
        public async Task Mostly_unparsed_quantities_are_flagged_for_review()
        {
            var recipe = PlausibleRecipe();
            foreach (var ingredient in recipe.Ingredients)
            {
                ingredient.Quantity = null;
            }

            var issues = await _vetting.VetAsync(recipe);

            Assert.Contains(issues, i => i.Contains("no parseable quantity"));
        }

        [Fact]
        public async Task Long_ferments_are_not_flagged()
        {
            // Realistic multi-day recipes (fermentation, curing) must pass.
            var recipe = PlausibleRecipe();
            recipe.TotalTimeMinutes = 3 * 24 * 60;

            var issues = await _vetting.VetAsync(recipe);

            Assert.Empty(issues);
        }
    }
}
