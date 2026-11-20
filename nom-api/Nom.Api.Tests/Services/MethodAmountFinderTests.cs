using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Bare ingredient lines whose amount the method states. Method text is from real prod rows.
    /// </summary>
    public class MethodAmountFinderTests
    {
        [Theory]
        [InlineData("butter", "For two quarts of muscles, put two ounces of butter in the saucepan in which they have been cooked.", 2, "Ounce")]
        [InlineData("flour", "Take a pound of flour, rub into it half a pound of butter, and add a little salt.", 1, "Pound")]
        [InlineData("butter", "Take a pound of flour, rub into it half a pound of butter, and add a little salt.", 0.5, "Pound")]
        [InlineData("eggs", "Beat two eggs with a gill of milk and mix it with the flour.", 2, "Piece")]
        [InlineData("milk", "Beat two eggs with a gill of milk and mix it with the flour.", 0.5, "Cup")]
        [InlineData("sugar", "Add three-fourths of a cup of sugar and stir.", 0.75, "Cup")]
        [InlineData("pepper", "Season with a teaspoonful of pepper.", 1, "Teaspoon")]
        public void Reads_the_amount_the_method_states(string line, string method, double quantity, string measurement)
        {
            var found = MethodAmountFinder.Find(line, new[] { method });
            found.Should().NotBeNull();
            found!.Quantity.Should().Be((decimal)quantity);
            found.MeasurementName.Should().Be(measurement);
        }

        [Theory]
        [InlineData("butter", "Melt the butter and pour over.")]
        [InlineData("butter", "Put half the butter in the pan.")]
        [InlineData("butter", "Rub two ounces of butter into the flour, then add three ounces of butter to the sauce.")]
        [InlineData("salt", "Season with salt and pepper.")]
        [InlineData("milk", "Add milk until smooth.")]
        [InlineData("salt and pepper", "Season with a teaspoonful of pepper.")]
        [InlineData("butter or lard", "Melt two ounces of lard.")]
        [InlineData("salt & pepper", "Season with a teaspoonful of pepper.")]
        public void Says_nothing_when_the_method_does_not_state_one_amount(string line, string method)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().BeNull();
        }

        [Theory]
        [InlineData("flour", "two pounds of flour", "Mix together two pounds of flour, a teaspoonful of salt and water to a stiff dough.")]
        [InlineData("a little butter", "a quarter of a pound of butter", "I then add a quarter of a pound of butter, a teaspoonful of salt, mixed with a little butter, stir in the rice.")]
        [InlineData("eggs", "2 eggs, beaten", "Beat two eggs with a gill of milk and mix it with the flour.")]
        public void Says_nothing_when_another_line_of_the_recipe_names_the_same_food(string line, string otherLine, string method)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().NotBeNull("the method states the amount once");
            MethodAmountFinder.Find(line, new[] { method }, new[] { "1 cup water", otherLine }).Should().BeNull();
        }
    }
}
