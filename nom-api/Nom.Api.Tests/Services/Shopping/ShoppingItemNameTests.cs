using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    public class ShoppingItemNameTests
    {
        [Theory]
        [InlineData("2 gallons milk", "milk", 2, "gallons")]
        [InlineData("6 eggs", "eggs", 6, null)]
        [InlineData("1/2 lb butter", "butter", 0.5, "lb")]
        [InlineData("1.5 kg flour", "flour", 1.5, "kg")]
        [InlineData("2 x avocados", "avocados", 2, "x")]
        [InlineData("½ dozen bagels", "bagels", 0.5, "dozen")]
        [InlineData("3 cans black beans", "black beans", 3, "cans")]
        public void Splits_a_leading_amount_from_the_item(string typed, string name, double quantity, string? unit)
        {
            ShoppingItemName.SplitLeadingQuantity(typed).Should().Be((name, (decimal?)(decimal)quantity, unit));
        }

        [Theory]
        [InlineData("milk")]
        [InlineData("2% milk")]
        [InlineData("7-up")]
        [InlineData("12")]
        [InlineData("3 4")]
        [InlineData("Granola bar")]
        public void Leaves_a_name_whole_when_there_is_no_amount_or_nothing_would_remain(string typed)
        {
            ShoppingItemName.SplitLeadingQuantity(typed).Should().Be((typed, (decimal?)null, (string?)null));
        }
    }
}
