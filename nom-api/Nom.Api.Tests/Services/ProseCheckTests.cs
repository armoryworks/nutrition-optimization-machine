using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Prose check v2 rule by rule on small constructed texts; the golden rows of prose.tsv hold
    /// the cases of the C9 review.
    /// </summary>
    public class ProseCheckTests
    {
        private static RewriteFidelity.SourceText Text(params string[] steps) => new(string.Empty, steps);

        private static ProseCheck.Finding Check(string[] source, string[] rewrite, string[]? lines = null, ProseStockIndex? stock = null) =>
            ProseCheck.Check(Text(source), Text(rewrite), lines, stock);

        private static readonly string[] Copied =
        {
            "Scatter the crumbled walnuts across the cooled sheet cake before slicing it into neat squares for the picnic basket.",
        };

        [Fact]
        public void A_finding_names_the_check_version_and_the_stock_index_it_ran_with()
        {
            var finding = Check(new[] { "Bake the bread for 30 minutes." }, new[] { "Bake the loaf 30 minutes." }, stock: new ProseStockIndex(7, Enumerable.Empty<long>()));

            finding.CheckVersion.Should().Be(ProseCheck.Version).And.Be(2);
            finding.StockVersion.Should().Be(7);
            finding.Passed.Should().BeTrue(finding.ToString());
        }

        [Fact]
        public void A_whole_distinctive_sentence_is_copied_text_but_one_made_of_stock_phrases_is_not()
        {
            var words = ProsePhrases.Words(Copied[0]);
            var stock = new ProseStockIndex(1, ProsePhrases.Hashes(words));

            Check(Copied, Copied.Select(s => "Then " + s).ToArray()).Reasons.Should().Contain(ProseCheck.CopiedText);
            Check(Copied, Copied.Select(s => "Then " + s).ToArray(), stock: stock).Reasons.Should().NotContain(ProseCheck.CopiedText);
        }

        [Fact]
        public void A_bracketed_conversion_may_be_dropped_but_not_changed()
        {
            var source = new[] { "Preheat the oven to 350°F (175°C)." };

            Check(source, new[] { "Heat the oven to 350°F." }).Passed.Should().BeTrue();
            Check(source, new[] { "Heat the oven to 350°F (190°C)." }).Reasons.Should().Contain(ProseCheck.AddedQuantity);
        }

        [Fact]
        public void An_amount_in_brackets_that_is_no_conversion_is_required()
        {
            Check(new[] { "Bake in a moderate oven (350° F.) for 20 minutes." }, new[] { "Bake in a moderate oven for 20 minutes." })
                .Reasons.Should().Contain(ProseCheck.LostQuantity);
        }

        [Fact]
        public void A_count_of_one_and_a_soft_count_are_neither_required_nor_added()
        {
            Check(new[] { "Add the eggs 1 at a time and fold all four sides over." }, new[] { "Add each egg separately, then fold the edges in." })
                .Reasons.Should().NotContain(ProseCheck.QuantityReasons);
        }

        [Fact]
        public void Stirring_an_ingredient_in_also_adds_it()
        {
            Check(new[] { "Stir in the flour and cook for 2 minutes." }, new[] { "Add the flour, then cook 2 minutes." }).Passed.Should().BeTrue();
            Check(new[] { "Pour the milk over the oats." }, new[] { "Stir the milk with the oats." }).Reasons.Should().Contain(ProseCheck.LostStep);
        }

        [Fact]
        public void Heat_as_a_noun_does_not_keep_the_verb_heat()
        {
            Check(new[] { "Heat the oil, then fry the onion over medium heat for 5 minutes." }, new[] { "Fry the onion in the oil over medium heat for 5 minutes." })
                .Faults.Should().Contain(f => f.Reason == ProseCheck.LostStep && f.Detail.Contains("heat"));
        }

        [Fact]
        public void An_ingredient_a_step_names_must_be_kept()
        {
            Check(new[] { "Fold the blueberries into the batter." }, new[] { "Fold everything together." }, new[] { "1 cup blueberries", "2 cups batter" })
                .Faults.Should().Contain(f => f.Reason == ProseCheck.LostStep && f.Detail.Contains("blueberries"));
        }

        [Fact]
        public void A_rewrite_far_longer_than_its_source_is_too_long()
        {
            var source = new[] { "Whisk the eggs with the sugar until pale, then fold in the flour and the melted butter and bake the sponge until it springs back." };
            var padded = new[]
            {
                "Whisk the eggs together with the sugar for quite a while until the mixture turns pale and thick and falls in ribbons from the whisk.",
                "Next fold in the flour gently with a large spatula, followed by the melted butter, taking great care not to knock out the air you beat in.",
                "Bake the sponge in the hot oven until it springs back when you press the middle lightly with a fingertip.",
            };

            Check(source, padded).Reasons.Should().Contain(ProseCheck.TooLong);
        }

        [Fact]
        public void Containment_counts_only_when_the_source_has_enough_distinctive_phrases()
        {
            var finding = Check(new[] { "Toast the seeds in a dry pan." }, new[] { "Toast the seeds in a dry skillet." });

            finding.DistinctivePhrases.Should().BeLessThan(ProseCheck.MinimumPhrasesForContainment);
            finding.Containment.Should().BeGreaterThan(ProseCheck.MaximumContainment);
            finding.Reasons.Should().NotContain(ProseCheck.CopiedText);
        }

        [Fact]
        public void The_source_lines_are_the_recipe_ingredients_of_its_json_ld()
        {
            const string json = """{"@type":"Recipe","recipeIngredient":["3/4 cup heavy cream","<b>1</b> tsp salt",""],"recipeInstructions":["Mix."]}""";

            ProseCheck.SourceLines(json).Should().Equal("3/4 cup heavy cream", "1 tsp salt");
            ProseCheck.SourceLines("{\"@type\":\"Recipe\"}").Should().BeEmpty();
            ProseCheck.SourceLines("not json").Should().BeEmpty();
        }
    }
}
