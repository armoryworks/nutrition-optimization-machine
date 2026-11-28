using System;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The key facts that stand in for a withheld method are the source method's times,
    /// temperatures and pan sizes in step order, each with its lexicon verb, in one fixed form that
    /// carries no wording of the source.
    /// </summary>
    public class ProseKeyFactsTests
    {
        private static readonly Regex FixedForm = new(
            @"^(?:[a-z]+ )?\d+(?: \d/\d)?(?:/\d)?(?: to \d+(?: \d/\d)?(?:/\d)?)?(?:°F|°C|°| sec| min| hr| days?| weeks?)(?: to \d+°[FC]?)?$|^\d+(?: x \d+)*(?:-inch|-foot| cm| mm) [a-z]+$",
            RegexOptions.CultureInvariant);

        private static RewriteFidelity.SourceText Method(params string[] steps) => new("A description.", steps, "Cake");

        [Fact]
        public void The_facts_follow_the_method_in_step_order_with_their_verbs()
        {
            var facts = ProseKeyFacts.Build(Method(
                "Preheat the oven to 350°F (175°C).",
                "Grease a 9 x 9-inch pan and pour in the batter.",
                "Bake for 30 to 40 minutes, until a toothpick comes out clean.",
                "Let the cake cool in the pan for 30 minutes."));

            facts.Should().Equal("oven 350°F", "9 x 9-inch pan", "bake 30 to 40 min", "cool 30 min");
        }

        [Fact]
        public void A_temperature_never_takes_the_verb_of_lining_or_greasing_a_pan()
        {
            ProseKeyFacts.Build(Method("To 450°F first, and line a baking sheet with parchment."))
                .Should().Equal(new[] { "450°F" }, "staging checkpoint 8 read a pan lining as an oven: 'line 450°'");
            ProseKeyFacts.Build(Method("Grease the pan well and set the oven to 375°F.")).Should().Equal("oven 375°F");
        }

        [Fact]
        public void The_most_heat_a_pan_or_paper_is_safe_up_to_is_no_step()
        {
            ProseKeyFacts.Build(Method("Preheat oven to 450 degrees.", "Cover two trays with baking paper (pick a kind that is safe up to 450 F / 232 C) and oil them."))
                .Should().Equal(new[] { "oven 450°" }, "staging checkpoint 9, U16: a bare 450° came from the pan lining's sentence");
            ProseKeyFacts.Build(Method("Use a skillet that is oven-safe to 500°F.")).Should().BeEmpty();
        }

        [Fact]
        public void A_heat_named_after_an_oven_safe_pan_is_a_step()
        {
            ProseKeyFacts.Build(Method("In an oven-safe skillet, bake at 400°F for 20 minutes."))
                .Should().Contain(f => f.EndsWith("400°F", StringComparison.Ordinal), "the pan's being oven-safe is no limit on the heat the sentence names");
            ProseKeyFacts.Build(Method("Use a dish rated for temperatures of 450°F.")).Should().BeEmpty();
        }

        [Fact]
        public void A_thaw_time_is_named_a_thaw_never_by_the_freezing_of_its_sentence()
        {
            ProseKeyFacts.Build(Method("After freezing it overnight, let it thaw for about 10-15 minutes before you scoop it."))
                .Should().Equal(new[] { "thaw 10 to 15 min" }, "staging checkpoint 9, U16: 'freeze 10 to 15 min' for a thaw");
            ProseKeyFacts.Build(Method("Freeze for 2 hours.")).Should().ContainSingle(f => f.EndsWith("2 hr", StringComparison.Ordinal) && !f.StartsWith("thaw", StringComparison.Ordinal));
        }

        [Fact]
        public void How_long_the_dish_keeps_is_no_step()
        {
            ProseKeyFacts.Build(Method("Bake for 25 minutes.", "Cover and chill up to 4 days.", "Leftovers keep in the fridge for 2 weeks."))
                .Should().Equal(new[] { "bake 25 min" }, "staging checkpoint 8 found 'cover 4 days' and 'chill 2 weeks' among the key facts");
            ProseKeyFacts.Build(Method("Store in an airtight tin for 3 hours before serving.")).Should().BeEmpty();
            ProseKeyFacts.Build(Method("Let the dough rise for 2 days in the fridge.")).Should().ContainSingle(f => f.EndsWith("2 days", StringComparison.Ordinal));
            ProseKeyFacts.Build(Method("Keep warm for 10 minutes.")).Should().HaveCount(1);
        }

        [Fact]
        public void A_step_time_in_a_sentence_that_also_stores_the_dish_stays_a_fact()
        {
            ProseKeyFacts.Build(Method("Bake 25 to 30 minutes, then cool on a rack before storing in an airtight container."))
                .Should().Equal("bake 25 to 30 min");
            ProseKeyFacts.Build(Method("Marinate up to 2 days.")).Should().ContainSingle(f => f.EndsWith("2 days", StringComparison.Ordinal));
            ProseKeyFacts.Build(Method("Refrigerate up to 3 days.")).Should().BeEmpty();
        }

        [Fact]
        public void Ingredient_amounts_counts_and_conversions_are_left_to_the_ingredient_list()
        {
            ProseKeyFacts.Build(Method("Add 2 cups of flour and 3 eggs, then stir in 1/2 teaspoon salt.")).Should().BeEmpty();
            ProseKeyFacts.Build(Method("Roll the dough to a 23 cm (9-inch) round.")).Should().BeEmpty("a size with no vessel is not a fact of its own");
        }

        [Fact]
        public void Equal_amounts_written_differently_read_the_same_way()
        {
            ProseKeyFacts.Build(Method("Simmer for a minute or two.")).Should().Equal("simmer 1 to 2 min");
            ProseKeyFacts.Build(Method("Rest the dough for half an hour.")).Should().Equal("rest 30 min");
            ProseKeyFacts.Build(Method("Chill overnight, at least 8 hours.")).Should().Equal("chill 8 hr");
        }

        [Fact]
        public void A_temperature_outside_the_oven_takes_its_verb_and_repeats_are_dropped()
        {
            ProseKeyFacts.Build(Method("Heat the oil to 375 degrees F.", "Fry in batches at 375 degrees F."))
                .Should().Equal("heat 375°F", "fry 375°F");
            ProseKeyFacts.Build(Method("Bake 10 minutes.", "Bake 10 minutes more.")).Should().Equal("bake 10 min");
        }

        [Fact]
        public void Every_fact_has_the_fixed_form_and_there_are_never_more_than_the_maximum()
        {
            var steps = Enumerable.Range(1, 20).Select(i => $"Stir and simmer for {i} minutes, scraping the bottom of the pot as the sauce thickens.").ToArray();

            var facts = ProseKeyFacts.Build(Method(steps));

            facts.Should().HaveCount(ProseKeyFacts.MaximumFacts);
            facts.Should().OnlyContain(f => FixedForm.IsMatch(f));
            facts.Should().NotContain(f => f.Contains("scraping") || f.Contains("thickens"));
        }

        [Fact]
        public void No_source_or_an_unreadable_one_gives_no_facts()
        {
            ProseKeyFacts.Build((RewriteFidelity.SourceText?)null).Should().BeEmpty();
            ProseKeyFacts.FromJsonLd(null).Should().BeEmpty();
            ProseKeyFacts.FromJsonLd("not json").Should().BeEmpty();
        }
    }
}
