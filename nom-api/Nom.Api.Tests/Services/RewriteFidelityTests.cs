using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// A rewritten recipe leaves the source-prose quarantine only when its amounts, times and
    /// temperatures survived, it states nothing new, and it copies no run of eight words.
    /// </summary>
    public class RewriteFidelityTests
    {
        private static RewriteFidelity.Finding Check(string original, string rewrite) =>
            RewriteFidelity.Check(new RewriteFidelity.SourceText(string.Empty, new[] { original }), new RewriteFidelity.SourceText(string.Empty, new[] { rewrite }));

        [Fact]
        public void A_faithful_rewrite_passes()
        {
            var finding = Check(
                "Place the sheet in the oven and roast the potatoes for 25 to 30 minutes, until golden and crispy.",
                "Roast the potatoes on their tray for 25-30 minutes so they turn crisp and golden.");

            finding.Passed.Should().BeTrue(finding.ToString());
        }

        [Fact]
        public void A_lost_time_fails()
        {
            var finding = Check(
                "Cover and cook for 30-40 minutes on low heat.",
                "Cover the pan and let it cook gently over a low flame for about 30 minutes.");

            finding.Passed.Should().BeFalse();
            finding.Lost.Should().Equal("40");
            finding.Reasons.Should().Equal(RewriteFidelity.LostNumber);
        }

        [Fact]
        public void An_invented_temperature_fails()
        {
            var finding = Check(
                "Bake until the top is golden.",
                "Bake at 350°F until the top turns golden.");

            finding.Added.Should().Equal("350");
            finding.Reasons.Should().Equal(RewriteFidelity.AddedNumber);
        }

        [Theory]
        [InlineData("Brush a baking sheet with 1 ½ tablespoons of olive oil.", "Coat a tray using 1 1/2 tablespoons olive oil.")]
        [InlineData("Brush a baking sheet with 1½ tablespoons of olive oil.", "Coat a tray using 1-1/2 tablespoons olive oil.")]
        [InlineData("Whisk two eggs into the batter.", "Beat 2 eggs into the mixture.")]
        [InlineData("Add 1 cup of stock.", "Pour in one cup stock.")]
        [InlineData("Let it rest for twenty-five minutes.", "Rest it 25 minutes.")]
        [InlineData("Use a 9x13 inch dish.", "Grease a 9 by 13 inch pan.")]
        public void The_same_amount_written_another_way_passes(string original, string rewrite)
        {
            var finding = Check(original, rewrite);

            finding.Passed.Should().BeTrue(finding.ToString());
        }

        [Fact]
        public void A_bracketed_conversion_may_be_dropped_but_not_invented()
        {
            Check("Preheat the oven to 350°F (175°C).", "Heat your oven to 350°F.").Passed.Should().BeTrue();
            Check("Heat the oven to 350°F.", "Set the oven to 350°F (180°C).").Added.Should().Equal("180");
        }

        [Fact]
        public void A_whole_sentence_of_ten_words_copied_fails_and_a_shorter_phrase_passes()
        {
            const string original = "Fold the whipped cream gently into the chocolate mixture until no streaks remain. Chill.";

            Check(original, "Fold the whipped cream gently into the chocolate mixture until no streaks remain. Refrigerate.").Copied
                .Should().Be("fold the whipped cream gently into the chocolate mixture until no streaks remain");
            Check(original, "Fold the whipped cream gently into the chocolate mixture until smooth, then chill.").Passed.Should().BeTrue();
            Check("Preheat the oven to 400 degrees F. Bake.", "Preheat the oven to 400 degrees F, then bake it.").Passed.Should().BeTrue();
        }

        [Fact]
        public void A_run_of_twenty_words_copied_across_sentences_fails()
        {
            const string original = "Stir the onions often and keep the heat low; they should soften and turn sweet without browning at all, which takes patience.";

            Check(original, "Cook slowly: stir the onions often and keep the heat low; they should soften and turn sweet without browning at all, which takes time.")
                .Copied.Should().NotBeNull();
        }

        [Fact]
        public void Step_numbers_are_not_amounts()
        {
            var finding = Check(
                "1. Mix the flour and salt. Rest for 10 minutes.2. Knead the dough.",
                "Combine flour with salt and leave it 10 minutes. Then work the dough by hand.");

            finding.Passed.Should().BeTrue(finding.ToString());
        }

        [Fact]
        public void A_number_only_in_the_description_need_not_survive_but_may_not_be_invented()
        {
            var original = new RewriteFidelity.SourceText("My family has made this for 30 years. Serves 4.", new[] { "Bake for 20 minutes." });

            RewriteFidelity.Check(original, new RewriteFidelity.SourceText("A family favourite.", new[] { "Bake it 20 minutes." }))
                .Passed.Should().BeTrue();
            RewriteFidelity.Check(original, new RewriteFidelity.SourceText("Serves 6.", new[] { "Bake it 20 minutes." }))
                .Added.Should().Equal("6");
        }

        [Fact]
        public void An_unchanged_text_fails_even_when_short()
        {
            var finding = Check("Serve warm.", "Serve <b>warm</b>!");

            finding.IsUnchanged.Should().BeTrue();
            finding.Reasons.Should().Equal(RewriteFidelity.Unchanged);
        }

        [Fact]
        public void The_source_method_is_read_from_steps_sections_and_plain_strings()
        {
            const string json = """
                {"@type":"Recipe","description":"A quick weeknight dish &amp; more.",
                 "recipeInstructions":[
                   {"@type":"HowToStep","text":"Preheat the oven to 425 degrees F."},
                   {"@type":"HowToSection","name":"Sauce","itemListElement":[
                     {"@type":"HowToStep","text":"Whisk the <em>butter</em> and flour."},
                     {"@type":"HowToStep","name":"Simmer for 5 minutes."}]},
                   "Serve."]}
                """;

            var source = RewriteFidelity.Source(json);

            source.Should().NotBeNull();
            source!.Description.Should().Be("A quick weeknight dish & more.");
            source.Steps.Should().Equal("Preheat the oven to 425 degrees F.", "Whisk the butter and flour.", "Simmer for 5 minutes.", "Serve.");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not json")]
        [InlineData("""{"@type":"Recipe","description":"No method."}""")]
        public void A_source_with_no_method_gives_nothing(string? json)
        {
            RewriteFidelity.Source(json).Should().BeNull();
        }
    }
}
