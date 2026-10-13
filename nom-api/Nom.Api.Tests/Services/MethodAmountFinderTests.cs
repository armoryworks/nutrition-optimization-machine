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
        public void Says_nothing_when_the_method_does_not_state_one_amount(string line, string method)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().BeNull();
        }
    }
}
