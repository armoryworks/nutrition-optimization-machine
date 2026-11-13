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

        [Theory]
        [InlineData("tablespoonful of flour", "flour")]
        [InlineData("level cupfuls flour", "flour")]
        [InlineData("table-spoonful of powdered cinnamon", "powdered cinnamon")]
        [InlineData("and one-half cups of flour", "flour")]
        [InlineData("or eight tomatoes", "tomatoes")]
        [InlineData("of a pound of flour", "flour")]
        [InlineData("gill of yeast", "yeast")]
        [InlineData("glass of white wine", "white wine")]
        [InlineData("teacupsful castor sugar", "castor sugar")]
        [InlineData("saltspoonful of pepper", "pepper")]
        [InlineData("½-lb. flour", "flour")]
        [InlineData("¹⁄₂ lb. butter", "butter")]
        [InlineData("butter, 2 oz", "butter")]
        [InlineData("Vermicelli, ½ cup", "Vermicelli")]
        [InlineData("little salt", "salt")]
        [InlineData("a very little cayenne", "cayenne")]
        [InlineData("few grains of cayenne", "cayenne")]
        [InlineData("salt to taste", "salt")]
        [InlineData("sugar, or more to taste", "sugar")]
        [InlineData("C. flour", "flour")]
        [InlineData("T-butter", "butter")]
        [InlineData("juice of one lemon", "lemon juice")]
        [InlineData("the juice of half a lemon", "lemon juice")]
        [InlineData("Grated yellow rind of 3 lemons", "lemon rind")]
        [InlineData("yolks of two hard-boiled eggs", "egg yolks")]
        [InlineData("the whites of three eggs", "egg whites")]
        [InlineData("yelks of five eggs", "egg yolks")]
        public void Strips_old_cookbook_measures_and_names_a_part_by_its_food(string raw, string expected)
        {
            Assert.Equal(expected, IngredientNameNormalizer.Normalize(raw));
        }

        [Theory]
        [InlineData("Glass Noodles")]
        [InlineData("Little Gem lettuce")]
        [InlineData("Half-and-half")]
        [InlineData("Cloves")]
        [InlineData("Canola Oil")]
        [InlineData("Tea")]
        public void Names_that_only_look_like_a_measure_are_left_alone(string name)
        {
            Assert.Equal(name, IngredientNameNormalizer.Normalize(name));
        }

        [Theory]
        [InlineData("chopped onion", "onion")]
        [InlineData("boiling water", "water")]
        [InlineData("finely chopped parsley", "parsley")]
        [InlineData("onion, sliced thin", "onion")]
        [InlineData("egg yolk, well beaten", "egg yolk")]
        [InlineData("large eggs", "eggs")]
        [InlineData("dried basil", "dried basil")]
        [InlineData("ground cinnamon", "ground cinnamon")]
        [InlineData("powdered sugar", "powdered sugar")]
        [InlineData("sweet cream", "sweet cream")]
        [InlineData("red onion", "red onion")]
        public void The_bare_name_drops_preparation_but_keeps_what_makes_the_food(string name, string expected)
        {
            Assert.Equal(expected, IngredientNameNormalizer.Bare(name));
        }

        [Fact]
        public void Lookup_keys_fold_singular_and_plural_except_where_the_plural_is_another_food()
        {
            Assert.Contains("onion", IngredientNameNormalizer.LookupKeys("small onions"));
            Assert.Contains("tomatoes", IngredientNameNormalizer.LookupKeys("diced tomato"));
            Assert.Contains("berry", IngredientNameNormalizer.LookupKeys("berries"));
            Assert.DoesNotContain("pepper", IngredientNameNormalizer.LookupKeys("peppers"));
            Assert.DoesNotContain("cloves", IngredientNameNormalizer.LookupKeys("clove"));
        }
    }
}
