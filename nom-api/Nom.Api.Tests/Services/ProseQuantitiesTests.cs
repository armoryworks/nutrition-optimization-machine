using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The quantity reader of prose check v2: amounts with their unit, family and range, the equal
    /// forms of the C9 false alarms, and the step markers and cross-references that are not amounts.
    /// </summary>
    public class ProseQuantitiesTests
    {
        private static string Shape(string text) =>
            string.Join(" | ", ProseQuantities.Read(text).Select(q =>
                $"{q.Family} {q.Low.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"
                + (q.IsRange ? "-" + q.High.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : string.Empty)
                + (q.Unit.Length > 0 ? " " + q.Unit : string.Empty)
                + (q.Conversion ? " (conversion)" : string.Empty)
                + (q.Soft ? " (soft)" : string.Empty)));

        [Theory]
        [InlineData("Bake for 25 to 30 minutes.", "time 25-30 minute")]
        [InlineData("Simmer 7–10 min, stirring.", "time 7-10 minute")]
        [InlineData("Cook the garlic for a minute or two.", "time 1-2 minute")]
        [InlineData("Let it stand for a half hour.", "time 0.5 hour")]
        [InlineData("Chill for half an hour.", "time 0.5 hour")]
        [InlineData("Roast for an hour and a half.", "time 1.5 hour")]
        [InlineData("Bake 1 hour 30 minutes.", "time 90 minute")]
        [InlineData("Add 1 and 1/2 cups of milk.", "volume 1.5 cup")]
        [InlineData("Add one and a half cups of milk.", "volume 1.5 cup")]
        [InlineData("Stir in 1½ cups flour and ¾ teaspoon salt.", "volume 1.5 cup | volume 0.75 teaspoon")]
        [InlineData("Stir in 1-1/2 cups flour.", "volume 1.5 cup")]
        [InlineData("Add about a tablespoon of oil.", "volume 1 tablespoon")]
        [InlineData("Add ½ Tbsp oil.", "volume 0.5 tablespoon")]
        [InlineData("Use ¼ lb of butter.", "mass 0.25 pound")]
        [InlineData("Preheat the oven to 350°F (175°C).", "temperature 350 F | temperature 175 C (conversion)")]
        [InlineData("Bake in a moderate oven (350° F.) for 1½ hours.", "temperature 350 F | time 1.5 hour")]
        [InlineData("Heat to 425 degrees F.", "temperature 425 F")]
        [InlineData("Boil until the thermometer registers 240 degrees.", "temperature 240 degree")]
        [InlineData("Grease a 9-inch (23cm) loaf pan.", "length 9 inch | length 23 cm (conversion)")]
        [InlineData("Spread in a 5 × 9-inch loaf pan.", "length 5 inch | length 9 inch")]
        [InlineData("Use a 9x13-inch dish.", "length 9 inch | length 13 inch")]
        [InlineData("Flatten to ½-¾ inch thickness.", "length 0.5-0.75 inch")]
        [InlineData("Fill the cups ⅔ to ¾ full.", "count 0.667-0.75")]
        [InlineData("Spray 4 5-oz ramekins.", "count 4 | mass 5 ounce")]
        [InlineData("Top with ⅓ of the filling and one-third of the cheese.", "count 0.333 | count 0.333")]
        [InlineData("Let it reduce by ¼.", "count 0.25")]
        [InlineData("Divide it into two equal parts.", "count 2 (soft)")]
        [InlineData("Cover all four sides with foil.", "count 4 (soft)")]
        [InlineData("Separate 3 eggs.", "count 3")]
        [InlineData("Cut the dough into two halves.", "count 2")]
        [InlineData("Simmer for 15 or 20 minutes.", "time 15-20 minute")]
        [InlineData("Cook between 7 and 10 minutes.", "time 7-10 minute")]
        [InlineData("Add 2 pints of stock.", "volume 2 pint")]
        [InlineData("Add a pinch of salt.", "count 1 pinch (soft)")]
        [InlineData("Let rest for a second sheet of foil.", "")]
        [InlineData("Cook 10 minutes or until golden.", "time 10 minute")]
        public void Reads_each_amount_with_its_unit_and_range(string text, string expected)
        {
            Shape(text).Should().Be(expected);
        }

        [Theory]
        [InlineData("2. Make the salad.", "")]
        [InlineData("2In a medium bowl, whisk the flour.", "")]
        [InlineData("Line the tray with parchment. 3.", "")]
        [InlineData("Step 4: Bake the bread.", "")]
        [InlineData("Chill for 30 minutes as in step 3.", "time 30 minute")]
        [InlineData("Dress them as No. 111.", "")]
        [InlineData("Put them into the batter N. 156 and fry.", "")]
        [InlineData("Cut the dough in half.", "")]
        [InlineData("Add the eggs one at a time.", "")]
        public void Step_markers_cross_references_and_bare_halves_are_not_amounts(string text, string expected)
        {
            Shape(text).Should().Be(expected);
        }

        [Theory]
        [InlineData("a minute or two", "1 to 2 minutes")]
        [InlineData("a half hour", "30 minutes")]
        [InlineData("half an hour", "30 minutes")]
        [InlineData("¼ lb", "4 oz")]
        [InlineData("about a tablespoon", "1 tablespoon")]
        [InlineData("1 and 1/2 cups", "1½ cups")]
        [InlineData("350°F", "175°C")]
        [InlineData("400 degrees F", "200°C")]
        [InlineData("1½ hours", "90 minutes")]
        [InlineData("1 hour 30 minutes", "1½ hours")]
        [InlineData("2 pints", "4 cups")]
        [InlineData("1 tablespoon", "3 teaspoons")]
        [InlineData("350 degrees", "350°F")]
        [InlineData("9-inch", "9 in. ")]
        public void Equal_forms_are_the_same_amount(string a, string b)
        {
            var x = ProseQuantities.Read(a).Single();
            var y = ProseQuantities.Read(b).Single();
            ProseQuantities.Equal(x, y).Should().BeTrue($"{a} and {b} state one amount");
        }

        [Theory]
        [InlineData("½ tablespoon", "½ teaspoon")]
        [InlineData("¾ cup", "1/2 cup")]
        [InlineData("⅛ tsp", "1/4 tsp")]
        [InlineData("7 to 10 minutes", "8 minutes")]
        [InlineData("2 to 5 minutes", "2 to 10 minutes")]
        [InlineData("½-¾ inch", "1/2-1/4 inch")]
        [InlineData("350°F", "375°F")]
        [InlineData("1 pound", "1 kilogram")]
        [InlineData("30 minutes", "30 seconds")]
        public void Different_amounts_are_not_equal(string a, string b)
        {
            var x = ProseQuantities.Read(a).Single();
            var y = ProseQuantities.Read(b).Single();
            ProseQuantities.Equal(x, y).Should().BeFalse($"{a} and {b} are different amounts");
        }

        [Fact]
        public void A_unit_swapped_inside_a_family_states_the_same_numbers_in_another_unit()
        {
            var tablespoon = ProseQuantities.Read("½ Tbsp").Single();
            var teaspoon = ProseQuantities.Read("1/2 tsp").Single();

            ProseQuantities.SameNumbersOtherUnit(tablespoon, teaspoon).Should().BeTrue();
        }

        [Fact]
        public void A_collapsed_range_overlaps_the_range_it_came_from()
        {
            var range = ProseQuantities.Read("7 to 10 minutes").Single();
            var single = ProseQuantities.Read("8 minutes").Single();

            ProseQuantities.RangeChanged(range, single).Should().BeTrue();
        }
    }
}
