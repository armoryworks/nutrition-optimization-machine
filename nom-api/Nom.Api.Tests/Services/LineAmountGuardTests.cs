using System;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;
using static Nom.Orch.Services.Support.LineAmountGuard;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The line guard of the rebuild and the start-up fill: a rule never empties or changes a stored amount the
    /// source grounds, replaces one only where the stored amount contradicts the source and the new one is
    /// grounded, and holds every other difference for the line judge. Lines and sentences are constructed in
    /// the shapes the staging checkpoint 4 reading logged.
    /// </summary>
    public class LineAmountGuardTests
    {
        private static Amount A(decimal quantity, string? unit, string? food, bool fromMethod = false, decimal? grams = null, string? kind = null) =>
            new(quantity, unit, kind, food, grams, fromMethod);

        private static Verdict Decide(string line, Amount stored, Amount proposed, params string[] method) =>
            LineAmountGuard.Decide(line, stored, proposed, method);

        [Fact]
        public void The_same_amount_is_same_and_two_empty_amounts_are_the_same_whatever_their_unit()
        {
            Decide("2 cups flour", A(2, "Cup", "flour"), A(2, "Cup", "flour")).Should().Be(Verdict.Same);
            Decide("salt", A(0, "Gram", "salt"), A(0, "Piece", "salt")).Should().Be(Verdict.Same);
        }

        [Theory]
        [InlineData("cup of the soup stock", 1, "Cup", "soup stock")]
        [InlineData("white wine", 0.333, "Cup", "white wine", "Then add one-sixth pint of white wine.")]
        [InlineData("cream", 1, "Cup", "cream", "(1/2 pt. of cream thinned with milk is enough.)")]
        [InlineData("lemon rind", 0.5, "Piece", "lemon rind", "Add the rind of half a lemon.")]
        [InlineData("stock", 0.5, "Piece", "stock", "Pour in one-half glass of stock.")]
        [InlineData("small lemons", 6, "Piece", "lemons", "This is quite sufficient for six lemons.")]
        [InlineData("red pepper", 8, "Piece", "red pepper", "Cut the tops from eight red peppers.")]
        public void A_grounded_stored_amount_is_never_emptied(string line, double quantity, string unit, string food, string? step = null)
        {
            var method = step == null ? Array.Empty<string>() : new[] { step };
            LineAmountGuard.Decide(line, A((decimal)quantity, unit, food, grams: 100), A(0, null, food), method).Should().Be(step == null ? Verdict.Keep : Verdict.Hold);
        }

        [Fact]
        public void A_grounded_stored_amount_in_an_odd_unit_is_still_never_emptied()
        {
            Decide("stock", A(0.5m, "Piece", "stock"), A(0, null, "stock"), "Pour in one-half glass of stock.").Should().Be(Verdict.Hold);
            Decide("one-half glass of stock", A(0.5m, "Piece", "stock"), A(0, null, "stock")).Should().Be(Verdict.Keep);
        }

        [Fact]
        public void A_grounded_stored_amount_is_never_replaced_by_one_the_source_does_not_state()
        {
            Decide("white wine", A(0.333m, "Cup", "white wine"), A(3, "Cup", "white wine", fromMethod: true), "Then add one-sixth pint of white wine.")
                .Should().Be(Verdict.Keep);
        }

        [Fact]
        public void Two_amounts_that_both_stand_in_the_source_are_held_for_the_line_judge()
        {
            Decide("sweet pumpkin", A(5, "Pound", "pumpkin"), A(4, "Pound", "pumpkin", fromMethod: true),
                "Boil with half a pint of water to four or five pounds of sweet pumpkin.").Should().Be(Verdict.Hold);
            Decide("onions", A(1, "Dozen", "onions"), A(10, "Piece", "onions", fromMethod: true, grams: 110), "Boil ten or a dozen onions.")
                .Should().Be(Verdict.Hold);
            Decide("oranges", A(5, "Piece", "oranges", grams: 130), A(4, "Piece", "oranges", fromMethod: true, grams: 130), "Squeeze four or five oranges.")
                .Should().Be(Verdict.Hold);
        }

        [Theory]
        [InlineData("Dash ground pepper", 0, "Gram", 1, "Pinch")]
        [InlineData("1 teaspn. salt", 1, "Piece", 1, "Teaspoon")]
        [InlineData("one scant cup sugar", 1, "Piece", 1, "Cup")]
        [InlineData("one cupful and a half of boiling water", 1, "Cup", 1.5, "Cup")]
        [InlineData("a pint and a half of thin cream", 2, "Cup", 3, "Cup")]
        public void A_stored_amount_the_source_contradicts_gives_way_to_a_grounded_one(string line, double stored, string storedUnit, double proposed, string unit)
        {
            Decide(line, A((decimal)stored, storedUnit, "food"), A((decimal)proposed, unit, "food")).Should().Be(Verdict.Replace);
        }

        [Theory]
        [InlineData("a very little water", 1, "Piece")]
        [InlineData("Marzapane descritto al N. 579", 579, "Piece")]
        [InlineData("twenty cents' worth chicken gizzards", 20, "Piece")]
        [InlineData("Sack", 0.5, "Pound")]
        public void A_rebuilt_row_with_no_amount_never_replaces_a_stored_amount_even_one_the_source_does_not_ground(string line, double stored, string storedUnit)
        {
            LineAmountGuard.Judge(line, A((decimal)stored, storedUnit, "food"), A(0, null, "food"), Array.Empty<string>())
                .Should().Be(new Decision(Verdict.Hold, Reasons.RebuiltEmpty));
        }

        [Fact]
        public void A_stored_amount_only_a_method_sentence_grounds_is_held_against_an_empty_rebuild_for_the_line_judge()
        {
            LineAmountGuard.Judge("Sack", A(0.5m, "Pound", "Alcoholic beverage, wine, dessert, dry"), A(0, null, "Alcoholic beverage, wine, dessert, dry"),
                    new[] { "Take yellow wax half a pound, Sack six spoonfuls, and melt them together." })
                .Should().Be(new Decision(Verdict.Hold, Reasons.RebuiltEmptyMethod));
        }

        [Fact]
        public void A_stated_amount_named_by_an_initialled_brand_or_by_the_lines_own_wording_is_held_not_emptied_against_an_empty_rebuild()
        {
            LineAmountGuard.Judge("J. P. Smith's Curry Powder", A(1, "Teaspoon", "Curry Powder", kind: "ingredient"), A(0, null, "Curry Powder", kind: "ingredient"),
                new[] { "Strew over it a teaspoonful of J. P. Smith's Curry Powder." }).Should().Be(new Decision(Verdict.Hold, Reasons.RebuiltEmptyMethod));
            LineAmountGuard.Judge("sweetbreads", A(4, "Piece", "Veal, variety meats and by-products, thymus, raw"), A(0, null, "Veal, variety meats and by-products, thymus, raw"),
                new[] { "Boil four sweetbreads, let them become cold, then chop them very fine." }).Should().Be(new Decision(Verdict.Hold, Reasons.RebuiltEmptyMethod));
        }

        [Fact]
        public void A_line_that_states_its_amount_grounds_amounts_in_the_line_only()
        {
            LineAmountGuard.Judge("1/2 cup breadcrumbs", A(0.75m, "Cup", "breadcrumbs"), A(0.5m, "Cup", "breadcrumbs"), new[] { "Toss the vegetables with 3/4 cup breadcrumbs." })
                .Should().Be(new Decision(Verdict.Replace, Reasons.StoredUngrounded));
            LineAmountGuard.Judge("1/2 cup breadcrumbs", A(0.5m, "Cup", "breadcrumbs"), A(0.75m, "Cup", "breadcrumbs", fromMethod: true), new[] { "Toss the vegetables with 3/4 cup breadcrumbs." })
                .Should().Be(new Decision(Verdict.Keep, Reasons.StoredGrounded));
        }

        [Theory]
        [InlineData("butter", 1, "Tablespoon", "working in a large tablespoon of butter and enough milk to make them resemble dough")]
        [InlineData("butter", 2, "Tablespoon", "add two large spoonfuls of butter, and keep on the stove")]
        [InlineData("1 tbp chopped pickle", 1, "Tablespoon", null)]
        public void A_count_in_pieces_of_a_measure_the_source_names_for_the_food_gives_way_to_the_measure(string line, double count, string unit, string? step)
        {
            var method = step == null ? Array.Empty<string>() : new[] { step };
            var food = line.Contains("pickle") ? "Dill Pickle" : "Butter";
            LineAmountGuard.Judge(line, A((decimal)count, "Piece", food, grams: 30), A((decimal)count, unit, food, fromMethod: step != null, grams: 30), method)
                .Should().Be(new Decision(Verdict.Replace, Reasons.CountOfMeasure));
        }

        [Fact]
        public void A_count_of_the_food_is_not_read_as_the_count_of_another_foods_measure()
        {
            LineAmountGuard.Judge("eggs", A(2, "Piece", "Eggs", grams: 50), A(2, "Cup", "Eggs", fromMethod: true, grams: 50), new[] { "Beat 2 eggs with 2 cups of milk." })
                .Verdict.Should().Be(Verdict.Hold);
        }

        [Fact]
        public void A_count_the_source_also_states_as_a_bare_count_of_the_food_is_held()
        {
            LineAmountGuard.Judge("eggs", A(2, "Piece", "Eggs", grams: 50), A(2, "Tablespoon", "Eggs", fromMethod: true, grams: 50),
                    new[] { "Beat 2 eggs and brush the top with 2 tablespoons of the beaten egg." })
                .Should().Be(new Decision(Verdict.Hold, Reasons.BothGrounded));
        }

        [Fact]
        public void A_count_of_a_measure_gives_way_only_to_that_measure()
        {
            LineAmountGuard.Judge("butter", A(2, "Piece", "Butter", grams: 30), A(2, "Cup", "Butter", fromMethod: true, grams: 30),
                    new[] { "Add two large spoonfuls of butter, and 2 cups of the stock." })
                .Should().Be(new Decision(Verdict.Hold, Reasons.BothGrounded));
        }

        [Theory]
        [InlineData("grated bread", 1, 1.5, "Take a pound and a half of grated bread and two pounds of liver.")]
        [InlineData("butter", 2, 2.5, "To a quarter of a peck of fine flour use two pounds and a half of butter, and four eggs.")]
        public void The_first_part_of_an_amount_the_method_states_gives_way_to_the_whole(string line, double stored, double whole, string step)
        {
            LineAmountGuard.Judge(line, A((decimal)stored, "Pound", line), A((decimal)whole, "Pound", line, fromMethod: true), new[] { step })
                .Should().Be(new Decision(Verdict.Replace, Reasons.AddedPart));
        }

        [Theory]
        [InlineData("Brown Sauce", "reference", 2, "Cup", "Make two cups of Brown Sauce.")]
        [InlineData("currie sauce", "reference", 4, "Cup", "Have ready about a quart of currie sauce.")]
        [InlineData("boiling water", "equipment", 0.5, "Cup", "Add half a cup of boiling water and bake.")]
        [InlineData("table salt", "ingredient", 1, "Teaspoon", "Season with a scant teaspoonful of table salt.")]
        [InlineData("scant cup of water", null, 1, "Cup", null)]
        [InlineData("can of tomatoes", "ingredient", 1, "Can", "Turn a can of tomatoes into a stew-pan.")]
        public void An_empty_line_of_a_kind_that_takes_an_amount_is_filled_from_its_own_source_text(string line, string? kind, double quantity, string unit, string? step)
        {
            var method = step == null ? Array.Empty<string>() : new[] { step };
            LineAmountGuard.Judge(line, A(0, "Gram", line, kind: kind), A((decimal)quantity, unit, line, fromMethod: step != null, kind: kind), method)
                .Should().Be(new Decision(Verdict.Replace, Reasons.Fill));
        }

        [Theory]
        [InlineData("note")]
        [InlineData("instruction")]
        [InlineData("artefact")]
        public void An_empty_note_instruction_or_artefact_is_held_by_its_kind(string kind)
        {
            LineAmountGuard.Judge("brine", A(0, "Piece", "brine", kind: kind), A(1, "Cup", "brine", fromMethod: true, kind: kind), new[] { "Make a cup of brine." })
                .Should().Be(new Decision(Verdict.Hold, Reasons.Kind));
        }

        [Fact]
        public void An_empty_line_is_filled_only_from_a_grounded_mention()
        {
            Decide("water", A(0, "Gram", "water"), A(8, "Cup", "water", fromMethod: true), "Bring two quarts of water to the boil.").Should().Be(Verdict.Replace);
            Decide("water", A(0, "Gram", "water"), A(3, "Cup", "water", fromMethod: true), "Bring two quarts of water to the boil.").Should().Be(Verdict.Hold);
            Decide("syrup", A(0, "Gram", "syrup"), A(1, "Cup", "syrup", fromMethod: true), "Flavour the water with syrup.").Should().Be(Verdict.Hold);
        }

        [Fact]
        public void A_line_of_another_kind_takes_no_amount_and_a_stated_amount_is_never_emptied_from_a_line_that_states_it()
        {
            Decide("For the brine:", A(0, "Piece", "brine", kind: "note"), A(1, "Cup", "brine", fromMethod: true), "Make a cup of brine.").Should().Be(Verdict.Hold);
            Decide("2 cups flour", A(2, "Cup", "flour"), A(0, null, "flour")).Should().Be(Verdict.Keep);
            Decide("2 cups flour", A(3, "Cup", "flour"), A(0, null, "flour")).Should().Be(Verdict.Hold);
        }

        [Fact]
        public void Only_same_and_replace_let_the_new_amount_stand()
        {
            LineAmountGuard.Takes(Verdict.Same).Should().BeTrue();
            LineAmountGuard.Takes(Verdict.Replace).Should().BeTrue();
            LineAmountGuard.Takes(Verdict.Keep).Should().BeFalse();
            LineAmountGuard.Takes(Verdict.Hold).Should().BeFalse();
        }
    }
}
