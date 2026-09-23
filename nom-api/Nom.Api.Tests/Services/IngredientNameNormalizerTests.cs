using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Guards the deterministic name cleanup that keeps scraper residue
    /// ("1 can black beans", "a pint of cream") out of the ingredient catalog
    /// (audit N-68/N-79). Every case here is a real prod row.
    /// </summary>
    public class IngredientNameNormalizerTests
    {
        [Theory]
        [InlineData("1 can black beans", "black beans")]
        [InlineData("½ cup black beans", "black beans")]
        [InlineData("&frac14; cup cooked black beans", "cooked black beans")]
        [InlineData("a pint of black beans", "black beans")]
        [InlineData("one quart of boiling milk", "boiling milk")]
        [InlineData("one half cup of black beans", "black beans")]
        [InlineData("two quarts of liquor from soup bone", "liquor from soup bone")]
        [InlineData("half a teaspoon of flour", "flour")]
        [InlineData("1 (15 oz) can black beans", "black beans")]
        [InlineData("1 1/2 cups shredded cheddar", "shredded cheddar")]
        [InlineData("1-2 cloves garlic", "garlic")]
        [InlineData("2 to 3 tablespoons olive oil", "olive oil")]
        [InlineData("Black beans (rinsed and drained)", "Black beans")]
        [InlineData("black bean puree (see above)", "black bean puree")]
        public void Strips_quantity_unit_and_prep_noise(string raw, string expected)
        {
            Assert.Equal(expected, IngredientNameNormalizer.Normalize(raw));
        }

        [Theory]
        [InlineData("Black Beans")]
        [InlineData("Rolled Oats")]
        [InlineData("Extra Virgin Olive Oil")]
        [InlineData("Half-and-half")]
        [InlineData("Egg")]
        public void Clean_names_pass_through_untouched(string name)
        {
            Assert.Equal(name, IngredientNameNormalizer.Normalize(name));
            Assert.False(IngredientNameNormalizer.IsResidue(name));
        }

        [Theory]
        [InlineData("2 cups")]
        [InlineData("half")]
        [InlineData("1")]
        public void Over_stripping_keeps_the_original(string raw)
        {
            Assert.Equal(raw, IngredientNameNormalizer.Normalize(raw));
        }
    }
}
