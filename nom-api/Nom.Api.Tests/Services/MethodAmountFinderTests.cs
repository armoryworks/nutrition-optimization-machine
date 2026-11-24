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
        [InlineData("Poivrade sauce", "Add a pinch of chopped parsley, one-sixth of a pint of Poivrade sauce, and the trotters.", 1.0 / 3, "Cup")]
        [InlineData("white wine", "Poach the sole in one-sixth pint of white wine.", 1.0 / 3, "Cup")]
        [InlineData("brown stock", "Moisten with the marinade and one-fifth pint of brown stock.", 0.4, "Cup")]
        [InlineData("white pepper", "Place in a bowl a heaped teaspoonful of salt, three-quarters of a teaspoonful of white pepper, and an onion.", 0.75, "Teaspoon")]
        [InlineData("milk", "Stir in two thirds of a cup of milk.", 2.0 / 3, "Cup")]
        [InlineData("cayenne pepper", "Mix one-tenth teaspoonful cayenne pepper with the oil.", 0.1, "Teaspoon")]
        [InlineData("Spanish onions", "Cut in quarters three large Spanish onions and place them round the meat.", 3, "Piece")]
        [InlineData("cranberries", "Cut into halves two cupfuls of cranberries and mix them with the raisins.", 2, "Cup")]
        [InlineData("pickled onion", "Chop, also, a quarter of a pickled onion, and mix in.", 0.25, "Piece")]
        [InlineData("nutmeg", "Add a teaspoon of curry powder, one-third of a nutmeg, grated, and the lobster meat.", 1.0 / 3, "Piece")]
        [InlineData("small fore-quarter of lamb", "Partly roast a small fore-quarter of lamb.", 1, "Piece")]
        public void Reads_an_ordinal_fraction_before_a_measure_whole(string line, string method, double quantity, string measurement)
        {
            var found = MethodAmountFinder.Find(line, new[] { method });
            found.Should().NotBeNull();
            ((double)found!.Quantity).Should().BeApproximately(quantity, 0.001, "staging checkpoint 6 (R1) read \"one-sixth of a pint\" as a pint");
            found.MeasurementName.Should().Be(measurement);
        }

        [Theory]
        [InlineData("Poivrade sauce", "Add the sixth of a pint of Poivrade sauce.")]
        [InlineData("brown sugar", "Add the fourth of a pound of brown sugar and the fourth of a teaspoon of spices.")]
        [InlineData("gum", "Dissolve one-hundredth of a pint of gum in the water.")]
        [InlineData("pepper", "Pound them with a teaspoonful of salt, and a sixth of pepper.")]
        [InlineData("tomato pulp", "Mix three-fifths Russian caviar, one-fifth tomato pulp, and one-fifth chopped onion.")]
        [InlineData("potatoes", "Mix fish and potatoes together while hot, taking two-thirds potatoes and one-third fish.")]
        [InlineData("shaved ice", "Fill the sherbet glasses three-quarters full of shaved ice.")]
        [InlineData("yeast cake", "When lukewarm, add one fourth yeast cake dissolved in one fourth cup lukewarm water.")]
        public void Says_nothing_for_a_fraction_it_cannot_join_to_its_measure(string line, string method)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().BeNull();
        }

        [Theory]
        [InlineData("oil for frying", "Heat oil in a pan to 350C for frying.")]
        [InlineData("warm water", "Dissolve the yeast in 40C water.")]
        [InlineData("milk", "Pour in the 80C milk and whisk.")]
        [InlineData("water", "Dissolve the yeast in 40 C water.")]
        [InlineData("water", "Dissolve the yeast in 110F water.")]
        [InlineData("fresh thyme for topping", "Blend the beans in a food processor, keeping back about ¼ cup for topping.")]
        [InlineData("Corn tortillas", "Lay the roasted squash and beans on a corn tortilla.")]
        [InlineData("flour", "Cut fine half a pound of butter, in three quarters of flour; then mix in two ounces of flour.")]
        [InlineData("olive oil", "Mix a tumbler two-thirds full of olive oil with the juice of two limes.")]
        [InlineData("celery", "Pare and slice three onions, four turnips, and a small portion of celery.")]
        public void Says_nothing_for_a_purpose_a_temperature_one_of_many_a_part_full_or_an_unread_fraction(string line, string method)
        {
            MethodAmountFinder.Find(line, new[] { method }).Should().BeNull("staging checkpoint 12 found these fills wrong");
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
