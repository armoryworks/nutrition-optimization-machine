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
            LineAmountGuard.Decide(line, A((decimal)quantity, unit, food, grams: 100), A(0, null, food), method).Should().Be(Verdict.Keep);
        }

        [Fact]
        public void A_grounded_stored_amount_in_an_odd_unit_is_still_never_emptied()
        {
            Decide("stock", A(0.5m, "Piece", "stock"), A(0, null, "stock"), "Pour in one-half glass of stock.").Should().Be(Verdict.Keep);
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
        [InlineData("a very little water", 1, "Piece", 0, "Piece")]
        [InlineData("Marzapane descritto al N. 579", 579, "Piece", 0, "Piece")]
        [InlineData("twenty cents' worth chicken gizzards", 20, "Piece", 0, "Piece")]
        public void A_stored_amount_the_source_contradicts_gives_way_to_a_grounded_one(string line, double stored, string storedUnit, double proposed, string unit)
        {
            Decide(line, A((decimal)stored, storedUnit, "food"), A((decimal)proposed, unit, "food")).Should().Be(Verdict.Replace);
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
