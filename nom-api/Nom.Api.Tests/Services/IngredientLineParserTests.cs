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
        [InlineData("1 cup + 2 tablespoons granulated sugar", 1.125, "Cup")]
        [InlineData("1/4 cup + 1 tablespoon light brown sugar", 0.313, "Cup")]
        [InlineData("3 cloves garlic, minced", 3, "Clove")]
        [InlineData("1 (15 oz) can black beans", 15, "Ounce")]
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
        [InlineData("Two-pound can of pineapples", 2, "Pound")]
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

        [Theory]
        [InlineData("1 (15 oz) can black beans", 15, "Ounce")]
        [InlineData("2 (14.5-ounce) cans diced tomatoes", 29, "Ounce")]
        [InlineData("1 14-ounce can chickpeas", 14, "Ounce")]
        [InlineData("one 28-ounce can whole tomatoes", 28, "Ounce")]
        [InlineData("1 15oz. can chickpeas ($0.69)", 15, "Ounce")]
        [InlineData("1 can (10 1/2 ounces) condensed soup", 10.5, "Ounce")]
        [InlineData("1 can (10-1/2-ounces) pizza sauce", 10.5, "Ounce")]
        [InlineData("1 7¾-oz. can salmon", 7.75, "Ounce")]
        [InlineData("2 cans (15 oz each) corn", 30, "Ounce")]
        [InlineData("2  cans (15 oz each diced tomatoes)", 30, "Ounce")]
        [InlineData("2  cans (29 oz total gandules)", 29, "Ounce")]
        [InlineData("1 can pumpkin (15 oz)", 15, "Ounce")]
        [InlineData("2 cans diced tomatoes (14.5 ounce cans, drained)", 29, "Ounce")]
        [InlineData("1/2 package raspberries (3 ounces total)", 3, "Ounce")]
        [InlineData("1 (24 ounce) jar pasta sauce", 24, "Ounce")]
        [InlineData("2 (16 ounce cans) refried beans", 32, "Ounce")]
        [InlineData("1/2 (15 oz) can pumpkin", 7.5, "Ounce")]
        [InlineData("2 (.25 oz) packets of gelatin", 0.5, "Ounce")]
        [InlineData("1 (400 g) tin tomatoes", 400, "Gram")]
        [InlineData("1 (13.5 fl oz) can coconut milk", 399.242, "Milliliter")]
        [InlineData("1 750 mL bottle dry rosé wine", 750, "Milliliter")]
        [InlineData("1 (15.5-ounce/439g) can kidney beans", 15.5, "Ounce")]
        [InlineData("a 10-12 oz bag of cauliflower rice", 10, "Ounce")]
        [InlineData("3 one-pound cans of salmon", 3, "Pound")]
        [InlineData("1 (4-5 pound) boneless pork shoulder", 4, "Pound")]
        [InlineData("1 3-pound chuck roast", 3, "Pound")]
        [InlineData("a four-pound fowl", 4, "Pound")]
        [InlineData("1  medium (8-ounce zucchini, quartered lengthwise)", 8, "Ounce")]
        [InlineData("4  medium (7 oz each zucchinis, ends trimmed)", 28, "Ounce")]
        [InlineData("1 1/2 (180g) cups all-purpose flour", 180, "Gram")]
        [InlineData("2 and 1/4 (281g) cups all-purpose flour", 281, "Gram")]
        public void A_stated_size_is_the_amount(string raw, double quantity, string measurement)
        {
            var expected = new ParsedQuantity((decimal)quantity, measurement);

            IngredientLineParser.Parse(raw).Should().Be(expected);
            IngredientLineParser.StatedSize(raw).Should().Be(expected);
            IngredientLineParser.ReadsPackage(raw).Should().BeTrue();
        }

        [Theory]
        [InlineData("1 jar marinara", 1, "Package")]
        [InlineData("2 packages cream cheese", 2, "Package")]
        [InlineData("1 bottle ketchup", 1, "Package")]
        [InlineData("1 box gelatine", 1, "Package")]
        [InlineData("1 envelope onion soup mix", 1, "Package")]
        [InlineData("1 pkg. yeast", 1, "Package")]
        [InlineData("half a package of gelatine", 0.5, "Package")]
        [InlineData("1 carton eggs", 1, "Package")]
        [InlineData("1 can tomatoes", 1, "Can")]
        [InlineData("1 tin sardines", 1, "Can")]
        public void A_container_without_a_size_is_a_can_or_a_package(string raw, double quantity, string measurement)
        {
            IngredientLineParser.Parse(raw).Should().Be(new ParsedQuantity((decimal)quantity, measurement));
            IngredientLineParser.StatedSize(raw).Should().BeNull();
            IngredientLineParser.ReadsPackage(raw).Should().BeTrue();
        }

        [Theory]
        [InlineData("4 (6-ounce) salmon filets", 4, "Piece")]
        [InlineData("40 (1/2 pound) marshmallows", 40, "Piece")]
        [InlineData("2 4-ounce lobster tails", 2, "Piece")]
        [InlineData("2 (1-inch) pieces ginger", 2, "Piece")]
        [InlineData("1  914-ounce) can coconut cream", 1, "Piece")]
        [InlineData("three-pint, channeled, ice-cream mold", 1, "Piece")]
        [InlineData("2 cans diced tomatoes (14.5 oz)", 2, "Can")]
        [InlineData("1 can or 1/2 lb. fresh tomatoes", 1, "Can")]
        [InlineData("1 can (#2 size or 2 1/2 cups) pineapple tidbits", 1, "Can")]
        [InlineData("1 can (1 pound 5 ounces) cherry pie filling", 1, "Can")]
        [InlineData("1 can (1 pound, 8 ounces) corned beef hash", 1, "Can")]
        public void A_size_that_may_be_each_or_total_or_is_unclear_is_not_read(string raw, double quantity, string measurement)
        {
            IngredientLineParser.Parse(raw).Should().Be(new ParsedQuantity((decimal)quantity, measurement));
            IngredientLineParser.StatedSize(raw).Should().BeNull();
        }

        [Theory]
        [InlineData("3 eggs", 3, "Piece")]
        [InlineData("2 sticks unsalted butter", 2, "Piece")]
        [InlineData("4 sticks cinnamon", 4, "Piece")]
        [InlineData("2 bunches kale", 2, "Piece")]
        [InlineData("4 tea bags", 4, "Piece")]
        [InlineData("1 bagel", 1, "Piece")]
        [InlineData("1 1/2 ounces chocolate", 1.5, "Ounce")]
        [InlineData("15-ounce can chickpeas", 15, "Ounce")]
        [InlineData("2 cups (16 oz) sour cream", 2, "Cup")]
        [InlineData("1 1/2 pound can tomatoes", 1.5, "Pound")]
        [InlineData("2 qts. water", 8, "Cup")]
        public void A_line_that_is_no_can_or_package_reads_as_before(string raw, double quantity, string measurement)
        {
            IngredientLineParser.Parse(raw).Should().Be(new ParsedQuantity((decimal)quantity, measurement));
            IngredientLineParser.StatedSize(raw).Should().BeNull();
            IngredientLineParser.ReadsPackage(raw).Should().BeFalse();
        }

        [Theory]
        [InlineData(1, "can", "1 (15 oz) can black beans", 15, "Ounce")]
        [InlineData(1, null, "1 (15 oz) can black beans", 15, "Ounce")]
        [InlineData(2, "package", "2 (8 ounce) packages cream cheese", 16, "Ounce")]
        [InlineData(1, "can", "1 can tomatoes", 1, "Can")]
        [InlineData(1, "jar", "1 jar marinara", 1, "Package")]
        [InlineData(2, "cups", "2 cups (16 oz) sour cream", 2, "Cup")]
        [InlineData(4, null, "4 (6-ounce) salmon filets", 4, "Piece")]
        public void A_scraped_count_of_cans_or_packages_takes_the_size_its_line_states(int quantity, string? unit, string raw, double expected, string measurement)
        {
            IngredientLineParser.Normalize(quantity, unit, raw).Should().Be(new ParsedQuantity((decimal)expected, measurement));
        }

        [Theory]
        [InlineData(1.0, "can", "1 (15-ounce) can chickpeas, drained and rinsed", 1, "Can")]
        [InlineData(null, null, "1 (15-ounce) can chickpeas, drained and rinsed", 1, "Can")]
        [InlineData(2.0, null, "2 cans (15 oz each) pinto beans, drained", 2, "Can")]
        [InlineData(1.0, null, "1 jar (12 oz) roasted red peppers, drained", 1, "Package")]
        [InlineData(1.0, "cup", "1 cup chickpeas, drained", 1, "Cup")]
        public void A_food_weighed_drained_keeps_its_count_of_cans_instead_of_the_net_weight(double? quantity, string? unit, string raw, double expected, string measurement)
        {
            IngredientLineParser.Normalize((decimal?)quantity, unit, raw, drained: true).Should().Be(new ParsedQuantity((decimal)expected, measurement));
        }

        [Fact]
        public void The_containers_a_stated_size_belongs_to_are_counted()
        {
            IngredientLineParser.SizedContainers("2 (15 oz) cans black beans").Should().Be(new ParsedQuantity(2m, "Can"));
            IngredientLineParser.SizedContainers("1 (3-pound) chicken").Should().BeNull();
            IngredientLineParser.SizedContainers("1 can tomatoes").Should().BeNull();
        }

        [Theory]
        [InlineData("2 cans (about 30 ounces) low-sodium diced tomatoes, drained", 30, "Ounce")]
        [InlineData("1/2 can (about 8 ounces) low-sodium kidney beans, drained", 8, "Ounce")]
        [InlineData("1/2 can (7.5 oz) chickpeas", 7.5, "Ounce")]
        [InlineData("1 1/2 bags (24 ounces of dry small red beans)", 24, "Ounce")]
        [InlineData("1/2 can (8 oz fat-free refried beans)", 8, "Ounce")]
        [InlineData("2 cans (15 oz each) corn", 30, "Ounce")]
        [InlineData("2 cans (15 oz) black beans", 30, "Ounce")]
        [InlineData("1 can (15 oz) corn", 15, "Ounce")]
        public void A_size_after_a_part_count_or_an_about_is_the_amount_of_the_line(string raw, double expected, string measurement)
        {
            IngredientLineParser.StatedSize(raw).Should().Be(new ParsedQuantity((decimal)expected, measurement));
        }
    }
}
