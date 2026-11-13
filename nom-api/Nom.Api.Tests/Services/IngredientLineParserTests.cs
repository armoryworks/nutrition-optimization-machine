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
        [InlineData("a gill of cream", 0.5, "Cup")]
        [InlineData("8 fl oz milk", 236.588, "Milliliter")]
        [InlineData("2 sticks unsalted butter", 2, "Piece")]
        [InlineData("500g chicken thighs", 500, "Gram")]
        [InlineData("1.5 lbs ground beef", 1.5, "Pound")]
        [InlineData("2 Cups Rice", 2, "Cup")]
        [InlineData("3 tomatoes", 3, "Piece")]
        [InlineData("1 lemon, juiced", 1, "Piece")]
        [InlineData("a pint of boiling water", 2, "Cup")]
        [InlineData("one well-beaten egg", 1, "Piece")]
        [InlineData("two cups of sugar", 2, "Cup")]
        [InlineData("One and one-half pounds of flour", 1.5, "Pound")]
        [InlineData("one third of a cup of gelatine", 0.333, "Cup")]
        [InlineData("1 teaspoonful salt", 1, "Teaspoon")]
        [InlineData("1-1/2 cups of powdered sugar", 1.5, "Cup")]
        [InlineData("Zucchero rosso, grammi 100.", 100, "Gram")]
        [InlineData("Ricotta (or half ricotta and half cacio raviggiolo), 180 grams.", 180, "Gram")]
        [InlineData("Farina di granturco (grammi 200)", 200, "Gram")]
        [InlineData("&frac34; teaspoon ground ginger", 0.75, "Teaspoon")]
        [InlineData("Pinch of ground cinnamon (optional)", 1, "Pinch")]
        [InlineData("juice of one lime", 1, "Piece")]
        [InlineData("the well beaten yolks of three eggs", 3, "Piece")]
        [InlineData("the yolks of one dozen of Eggs and half", 12, "Piece")]
        [InlineData("lard, half a pound", 0.5, "Pound")]
        [InlineData("milk, two cupfuls", 2, "Cup")]
        [InlineData("catsup (two tablespoonfuls)", 2, "Tablespoon")]
        [InlineData("Parmesan (two tablespoonsful)", 2, "Tablespoon")]
        [InlineData("Lemons, One", 1, "Piece")]
        [InlineData("three-fourths cup flour", 0.75, "Cup")]
        [InlineData("twenty-five tablespoonfuls of melted butter", 25, "Tablespoon")]
        [InlineData("Steers Gall five spoon-fulls", 5, "Tablespoon")]
        [InlineData("from four to six eggs", 4, "Piece")]
        [InlineData("half-a-dozen oysters", 6, "Piece")]
        [InlineData("pound and a half of raised dough", 1.5, "Pound")]
        [InlineData("water, four pints and a half", 9, "Cup")]
        [InlineData("Half-a-pound of finely sifted sugar", 0.5, "Pound")]
        [InlineData("third of a cup of sugar", 0.333, "Cup")]
        [InlineData("Two-pound can of pineapples", 1, "Piece")]
        [InlineData("currie-powder, two dessertspoonsful", 4, "Teaspoon")]
        [InlineData("dozen large ripe tomatoes with the skins taken off", 1, "Dozen")]
        [InlineData("one tablespoonful", 1, "Tablespoon")]
        [InlineData("latte, 6 decilitri", 600, "Milliliter")]
        [InlineData("grammi 200 ricotta", 200, "Gram")]
        [InlineData("mezzo limone", 0.5, "Piece")]
        [InlineData("una cipolla di mediocre grandezza", 1, "Piece")]
        [InlineData("otto cucchiaiate o più di olio d'oliva", 8, "Tablespoon")]
        [InlineData("un pizzico di parmigiano", 1, "Pinch")]
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
        [InlineData("a little salt and cayenne")]
        [InlineData("a few sprigs of thyme")]
        [InlineData("Kosher salt and black pepper, (to taste)")]
        [InlineData("un poco di burro")]
        [InlineData("Sugar, half a pound for each pound of fruit")]
        [InlineData("liver, cut into slices one half inch thick")]
        [InlineData("2")]
        [InlineData("")]
        [InlineData(null)]
        public void Lines_without_a_leading_quantity_stay_unparsed(string? line)
        {
            IngredientLineParser.Parse(line).Should().BeNull();
        }

        [Theory]
        [InlineData("2 qts. water", 8, "Cup")]
        [InlineData("2-1/2 pts. of milk", 5, "Cup")]
        [InlineData("4 ozs. sugar", 4, "Ounce")]
        [InlineData("6-ozs. flour", 6, "Ounce")]
        [InlineData("1/4-lb. sugar", 0.25, "Pound")]
        [InlineData("1 Tb. butter", 1, "Tablespoon")]
        [InlineData("1 large tablespoonful of butter", 1, "Tablespoon")]
        [InlineData("3 level teaspoons Royal Baking Powder", 3, "Teaspoon")]
        [InlineData("four large heaping table-spoonfuls of farina", 4, "Tablespoon")]
        [InlineData("1 scant tablespoon of flour", 1, "Tablespoon")]
        [InlineData("4 or 5 tablespoonsful flour", 4, "Tablespoon")]
        [InlineData("4-4 ½ cups all-purpose flour", 4, "Cup")]
        [InlineData("one fourth cup of milk", 0.25, "Cup")]
        [InlineData("one-fourth a teaspoonful of salt", 0.25, "Teaspoon")]
        [InlineData("one dessert spoon of sugar", 2, "Teaspoon")]
        [InlineData("a salt-spoonful of cayenne pepper", 0.25, "Teaspoon")]
        [InlineData("1 teacup butter", 0.5, "Cup")]
        [InlineData("Half a tea-cup of sugar", 0.25, "Cup")]
        [InlineData("1 wine-glass of brandy", 0.25, "Cup")]
        [InlineData("3 coffee cups of lemonade", 3, "Cup")]
        [InlineData("teacupful of sugar", 0.5, "Cup")]
        [InlineData("¹⁄₂ lb. butter", 0.5, "Pound")]
        [InlineData("³⁄₄ cup flour", 0.75, "Cup")]
        [InlineData("1 large can tomatoes", 1, "Can")]
        [InlineData("2 large eggs, beaten", 2, "Piece")]
        [InlineData("1 medium onion", 1, "Piece")]
        public void Reads_old_cookbook_units_and_qualified_measures(string raw, double quantity, string measurement)
        {
            var parsed = IngredientLineParser.Parse(raw);

            parsed.Should().NotBeNull();
            parsed!.Quantity.Should().Be((decimal)quantity);
            parsed.MeasurementName.Should().Be(measurement);
        }

        [Theory]
        [InlineData("two of butter")]
        [InlineData("three scant ones of flour")]
        [InlineData("1 of salt")]
        [InlineData("a rounded one of sugar")]
        public void A_number_that_borrows_its_unit_from_the_line_before_is_no_quantity(string raw)
        {
            IngredientLineParser.IsElliptical(raw).Should().BeTrue();
            IngredientLineParser.Parse(raw).Should().BeNull();
        }

        [Theory]
        [InlineData("1/2 of a lemon")]
        [InlineData("half of the butter")]
        [InlineData("2 cups of flour")]
        public void A_part_of_a_thing_is_not_mistaken_for_a_borrowed_unit(string raw)
        {
            IngredientLineParser.IsElliptical(raw).Should().BeFalse();
        }
    }
}
