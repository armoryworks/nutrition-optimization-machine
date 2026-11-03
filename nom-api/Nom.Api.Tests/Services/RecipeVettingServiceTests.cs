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
        public async Task A_one_paragraph_method_is_not_an_incomplete_extraction()
        {
            var recipe = PlausibleRecipe();
            recipe.Steps.Clear();
            recipe.Steps.Add(new ScraperStep { Order = 1, Instruction = "Boil the sweet potatoes until tender, then mash them with butter." });

            Assert.DoesNotContain(await _vetting.VetAsync(recipe), i => i.Contains("instruction step"));

            recipe.Steps[0].Instruction = "Boil and mash.";
            Assert.Contains(await _vetting.VetAsync(recipe), i => i.Contains("instruction step"));
        }

        [Fact]
        public async Task One_quantified_line_is_enough_for_a_public_domain_recipe()
        {
            var recipe = PlausibleRecipe();
            recipe.Ingredients.RemoveRange(1, 2);

            Assert.Contains(await _vetting.VetAsync(recipe), i => i.Contains("ingredient line"));

            recipe.PublicDomain = true;
            Assert.DoesNotContain(await _vetting.VetAsync(recipe), i => i.Contains("ingredient line"));

            recipe.Ingredients[0].Quantity = null;
            Assert.Contains(await _vetting.VetAsync(recipe), i => i.Contains("ingredient line"));
        }

        [Theory]
        [InlineData("Whisk the eggs &amp; sugar until pale.", "markup")]
        [InlineData("See <a href=\"x\">the video</a> for the fold.", "markup")]
        [InlineData("Full method at https://example.com/cake", "markup")]
        [InlineData("Lorem ipsum dolor sit amet.", "placeholder")]
        [InlineData("TODO write the steps", "placeholder")]
        [InlineData("--- --- ---", "no real words")]
        [InlineData("1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23", "mostly digits")]
        [InlineData("Bake the cake ftgklmnpr until done.", "unreadable letter runs")]
        public async Task Broken_text_is_flagged_once_per_kind(string step, string marker)
        {
            var recipe = PlausibleRecipe();
            recipe.Steps.Add(new ScraperStep { Order = 3, Instruction = step });
            recipe.Steps.Add(new ScraperStep { Order = 4, Instruction = step });

            var issues = await _vetting.VetAsync(recipe);

            Assert.Single(issues, i => i.Contains(marker));
            Assert.Contains("step 3", issues.Single(i => i.Contains(marker)));
        }

        [Theory]
        [InlineData("Boil the sweet potatoes until tender, then mash them with butter.")]
        [InlineData("Preheat the oven to 350 °F (175 °C); grease a 9x13-inch pan.")]
        [InlineData("Add 1 1/2 cups (190 g) flour, 2 tsp baking powder & a pinch of salt.")]
        [InlineData("Simmer 10–12 minutes. Serve warm.")]
        [InlineData("Mix well with 3 tbsp Worcestershire sauce — Lea & Perrins works.")]
        [InlineData("Strengths and lengths of the dough vary; knead until smooth.")]
        [InlineData("Stir in 2 tbsps butter and 1 tblsp honey.")]
        public async Task Ordinary_cooking_text_passes(string step)
        {
            var recipe = PlausibleRecipe();
            recipe.Steps.Add(new ScraperStep { Order = 3, Instruction = step });

            Assert.Empty(await _vetting.VetAsync(recipe));
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

        [Theory]
        [InlineData("salt, to taste")]
        [InlineData("a pinch of nutmeg")]
        [InlineData("oil as needed")]
        [InlineData("parsley, for garnish")]
        [InlineData("salt and pepper")]
        [InlineData("Salt & black pepper")]
        [InlineData("flour for dusting")]
        [InlineData("a little salt")]
        [InlineData("some pepper")]
        [InlineData("a little grated nutmeg")]
        [InlineData("Salt.")]
        [InlineData("freshly ground black pepper")]
        [InlineData("a little butter")]
        [InlineData("some flour")]
        [InlineData("a knob of butter, melted")]
        [InlineData("a few sprigs of thyme")]
        [InlineData("a handful of chopped parsley")]
        public async Task Seasoning_and_garnish_lines_may_go_unquantified(string line)
        {
            var recipe = PlausibleRecipe();
            recipe.Ingredients[0].Quantity = null;
            recipe.Ingredients[1].Quantity = null;
            recipe.Ingredients[0].RawLine = line;
            recipe.Ingredients[1].RawLine = line;

            var issues = await _vetting.VetAsync(recipe);

            Assert.Empty(issues);
        }

        [Theory]
        [InlineData("butter")]
        [InlineData("salt pork")]
        [InlineData("littleneck clams")]
        [InlineData("somen noodles")]
        public async Task Vague_amounts_still_count_as_unparsed(string line)
        {
            var recipe = PlausibleRecipe();
            recipe.Ingredients[0].Quantity = null;
            recipe.Ingredients[1].Quantity = null;
            recipe.Ingredients[0].RawLine = line;
            recipe.Ingredients[1].RawLine = line;

            var issues = await _vetting.VetAsync(recipe);

            Assert.Contains(issues, i => i.Contains("2 of 3 ingredient lines have no parseable quantity"));
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
