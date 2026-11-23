using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Bare ingredient lines whose amount the method states. Method text is from real prod rows,
    /// or built around a phrase one of them holds.
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
        [InlineData("water", "Put on half vinegar and half water and let it boil.")]
        [InlineData("water", "Make a brine of one teacup of salt to a gallon of water.")]
        [InlineData("salt", "Make a brine of one teacup of salt to a gallon of water.")]
        [InlineData("eggs", "Allow two eggs to each pint of milk.")]
        [InlineData("sugar", "Use two ounces of sugar per pint of fruit.")]
        [InlineData("water", "Wash the tripe in three or four waters.")]
        [InlineData("water", "Put 1-inch of water in the pan.")]
        [InlineData("water", "Fill a kettle of seething water.")]
        [InlineData("water", "Tie leather over and set them in a water bath for three hours.")]
        [InlineData("oil", "Put ten pounds of potash into an oil barrel.")]
        [InlineData("water", "Add a water and stir.")]
        [InlineData("milk", "Pour in two milk and stir.")]
        public void Says_nothing_for_a_ratio_a_vessel_washing_water_or_a_count_of_a_liquid(string line, string method)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().BeNull();
        }

        [Theory]
        [InlineData("eggs", "Add the sugar and the two beaten eggs, and stir well.", 2, "Piece")]
        [InlineData("water", "Pour three pints of boiling water over the meal.", 6, "Cup")]
        [InlineData("water", "Boil the rice in two quarts of water until tender.", 8, "Cup")]
        public void A_number_word_before_a_preparation_word_still_counts(string line, string method, double quantity, string measurement)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().Be(new ParsedQuantity((decimal)quantity, measurement));
        }

        [Fact]
        public void A_count_of_pieces_is_not_read_for_a_food_not_counted_in_pieces()
        {
            var method = new[] { "Throw in two onions and stir." };
            MethodAmountFinder.Find("onions", method).Should().Be(new ParsedQuantity(2m, "Piece"));
            MethodAmountFinder.Find("onions", method, countedInPieces: false).Should().BeNull();
            MethodAmountFinder.Find("butter", new[] { "Put two ounces of butter in." }, countedInPieces: false).Should().Be(new ParsedQuantity(2m, "Ounce"));
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

        [Theory]
        [InlineData("sugar", "To six pounds of peaches allow two pounds of sugar. ¦ Make a syrup of three-quarters of a pound of sugar and a pint of water.")]
        [InlineData("sugar", "Allow a teaspoonful of sugar for a dozen tomatoes. ¦ Where they are liked sweet, add two tablespoonfuls of sugar.")]
        [InlineData("consomme", "Cook the tomatoes in half a cup of consomme. ¦ Rub them through a sieve and add to two quarts of consomme.")]
        [InlineData("flour", "Make a foam of the yeast with four cups of flour. ¦ Add to the yeast foam about seven to eight cups of flour.")]
        [InlineData("pounded mace", "To every pound of meat allow a quarter of a pound of butter; one teaspoonful of pounded mace.")]
        [InlineData("pounded mace", "To every lb. of meat allow one teaspoonful of pounded mace.")]
        [InlineData("beef suet", "For thirty pounds use two pounds beef suet.")]
        public void A_sentence_dropped_as_a_proportion_still_counts_as_an_amount_of_the_food(string line, string steps)
        {
            MethodAmountFinder.Find(line, steps.Split(" ¦ ")).Should().BeNull();
        }

        [Theory]
        [InlineData("consomme", "Rub them through a sieve and add to two quarts of consomme.", 8, "Cup")]
        [InlineData("water", "Add to one pint of boiling water the beaten yolks.", 2, "Cup")]
        public void Add_or_return_to_an_amount_is_the_amount_not_a_proportion(string line, string method, double quantity, string measurement)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().Be(new ParsedQuantity((decimal)quantity, measurement));
        }

        [Theory]
        [InlineData("tin plate", "Butter a tin plate and line it with paste.")]
        [InlineData("lemon, raspberry, or any other acid syrup", "Put into a tumbler lemon, raspberry, or any other acid syrup, sufficient in quantity to flavour the water.")]
        [InlineData("acid syrup", "Put into a tumbler enough acid syrup to flavour it, and fill up with water.")]
        [InlineData("syrup", "Pour into a glass some syrup, sufficient to flavour the water.")]
        public void A_utensil_or_the_vessel_a_food_is_put_into_is_no_amount(string line, string method)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().BeNull();
        }
    }
}
