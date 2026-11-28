using System.Collections.Generic;
using FluentAssertions;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// A stored ingredient line is served with its HTML entities read: staging checkpoint 8 found "&amp;frac14;" and
    /// "&amp;frac12;" shown as written in the lines of two withheld recipes.
    /// </summary>
    public class ProseWithholdingShownLineTests
    {
        [Theory]
        [InlineData("1 &frac14; cups oat flour", "1 ¼ cups oat flour")]
        [InlineData("&frac12; teaspoon salt", "½ teaspoon salt")]
        [InlineData("1 &amp;frac12; cups milk", "1 ½ cups milk")]
        [InlineData("salt &amp; pepper", "salt & pepper")]
        [InlineData("2 cups flour", "2 cups flour")]
        public void A_line_is_served_with_its_entities_read(string stored, string shown)
        {
            ProseWithholding.LineWording(new RecipeEntity(), stored).Should().Be(shown);
            ProseWithholding.LineName(null, stored, "Flour", 1, "Cup", true).Should().Be(shown);
        }

        [Fact]
        public void The_alcohol_note_serves_an_uncounted_drink_line_with_its_entities_read()
        {
            var sugar = new IngredientEntity { Id = 1, Name = "Sugar" };
            var recipe = new RecipeEntity
            {
                Name = "Cucumber cooler",
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { IngredientId = 1, Ingredient = sugar, Quantity = 1m, RawLine = "1 &frac12; ounces clear rum", SortOrder = 0 },
                },
            };

            var note = RecipeNutritionService.AlcoholNote(recipe, new Dictionary<long, decimal> { [1] = 0m });

            note!.Uncounted.Should().Equal(new[] { "1 ½ ounces clear rum" }, "staging checkpoint 9, U15: the note served '1 &frac12; ounces clear rum'");
        }

        [Fact]
        public void A_facts_only_line_is_still_never_served()
        {
            ProseWithholding.LineWording(new RecipeEntity { SourceRights = SourceRightsRule.FactsOnly }, "1 &frac14; cups oat flour").Should().BeNull();
        }
    }
}
