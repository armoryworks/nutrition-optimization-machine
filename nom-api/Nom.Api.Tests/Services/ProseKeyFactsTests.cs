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
