using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Quantity/unit recovery from stored ingredient lines. 122k harvested recipes reached prod
    /// with every quantity dropped (Quantity 0, measurement Gram); RawLine is what's left to
    /// rebuild them from. Lines here are real prod rows.
    /// </summary>
    public class IngredientLineParserTests
    {
        [Theory]
        [InlineData("2 large eggs", 2, "Piece")]
        [InlineData("1 tablespoon vanilla extract", 1, "Tablespoon")]
        [InlineData("¾ teaspoon salt", 0.75, "Teaspoon")]
        [InlineData("1 ⅔ cups bread flour", 1.667, "Cup")]
        [InlineData("1 ½ teaspoons baking powder", 1.5, "Teaspoon")]
        [InlineData("1 1/4 cup packed light brown sugar", 1.25, "Cup")]
        [InlineData("2 cups minus 2 tablespoons cake flour", 2, "Cup")]
        [InlineData("1 cup + 2 tablespoons granulated sugar", 1, "Cup")]
        [InlineData("3 cloves garlic, minced", 3, "Clove")]
        [InlineData("1 (15 oz) can black beans", 1, "Piece")]
        [InlineData("2-3 Tbsp olive oil", 2, "Tablespoon")]
        [InlineData("1 T butter", 1, "Tablespoon")]
        [InlineData("1 t salt", 1, "Teaspoon")]
        [InlineData("1 quart chicken stock", 4, "Cup")]
        [InlineData("8 fl oz milk", 236.588, "Milliliter")]
        [InlineData("2 sticks unsalted butter", 2, "Piece")]
        [InlineData("500g chicken thighs", 500, "Gram")]
        [InlineData("1.5 lbs ground beef", 1.5, "Pound")]
        [InlineData("2 Cups Rice", 2, "Cup")]
        [InlineData("3 tomatoes", 3, "Piece")]
        [InlineData("1 lemon, juiced", 1, "Piece")]
        public void Recovers_quantity_and_measurement(string line, double quantity, string measurement)
        {
            var parsed = IngredientLineParser.Parse(line);
            parsed.Should().NotBeNull();
            parsed!.Quantity.Should().Be((decimal)quantity);
            parsed.MeasurementName.Should().Be(measurement);
        }

        [Theory]
        [InlineData("salt, to taste")]
        [InlineData("flaky sea salt (for topping)")]
        [InlineData("fresh basil leaves")]
        [InlineData("2")]
        [InlineData("")]
        [InlineData(null)]
        public void Lines_without_a_leading_quantity_stay_unparsed(string? line)
        {
            IngredientLineParser.Parse(line).Should().BeNull();
        }
    }
}
