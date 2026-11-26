using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The strict copy check of the reword lane's new text (<see cref="ProseRebuilt"/>), on sentences written for
    /// this test: the source's sentence with words swapped for synonyms in the same frame, dropped or moved is a
    /// copy, whatever the word-order rule of the old text reads; a sentence rebuilt from what the cook must do
    /// passes.
    /// </summary>
    public class ProseRebuiltTests
    {
        private static readonly string[] Lines = { "2 cups rolled oats", "1/2 cup honey", "1/2 cup peanut butter", "1/4 cup cocoa powder", "1 teaspoon vanilla extract" };

        private static readonly RewriteFidelity.SourceText Source = new(string.Empty, new[]
        {
            "Warm the honey and peanut butter together in a small saucepan over low heat, stirring until the mixture is smooth and runny.",
            "Take the pan off the heat and stir in the vanilla extract and the cocoa powder until no streaks remain.",
            "Pour the warm mixture over the oats in a large bowl and mix until every flake is coated.",
            "Press the mixture firmly into a lined tin and chill it for at least an hour before you cut it into squares.",
        }, "Cocoa Oat Squares");

        private static bool Copy(string sentence) =>
            ProseRebuilt.Read(Source, new RewriteFidelity.SourceText(string.Empty, new[] { sentence }), Lines).Any(s => s.Copy);

        [Theory]
        [InlineData("Heat the honey and peanut butter together in a small saucepan over low heat, mixing until the mixture is smooth and runny.")]
        [InlineData("Remove the pan from the heat and mix in the vanilla extract and the cocoa powder until no streaks remain.")]
        [InlineData("Pour the warm mixture over the oats in a big bowl and stir until each flake is coated.")]
        [InlineData("Firmly press the mixture into a lined tin and refrigerate it for at least an hour before cutting it into squares.")]
        [InlineData("Take the pan from the heat and stir the vanilla extract and cocoa powder through it until no streaks are left.")]
        public void A_synonym_swap_drop_or_move_in_the_sources_frame_is_a_copy(string lightEdit)
        {
            Copy(lightEdit).Should().BeTrue();
        }

        [Fact]
        public void A_light_edit_the_word_order_rule_of_the_old_text_reads_no_copy_in_is_a_copy_here()
        {
            var source = GarlicSauce.Input().Source;
            var lightEdit = new RewriteFidelity.SourceText(string.Empty, new[] { "Remove the pan from the heat and whisk the butter in." });
            var old = ProseSentences.Read(source, lightEdit, GarlicSauce.Lines).Sentences.Single();

            (old.Copy || old.NearVerbatim).Should().BeFalse("swapped and moved words break the old rule's runs");
            ProseRebuilt.Read(source, lightEdit, GarlicSauce.Lines).Single().Copy.Should().BeTrue();
        }

        [Theory]
        [InlineData("Melt honey with peanut butter in a small saucepan on low; it is ready once it runs smooth.")]
        [InlineData("Once the saucepan is off the burner, beat the vanilla extract and cocoa powder through it until it is one even color.")]
        [InlineData("Have the oats ready in a large bowl so the warm syrup can coat every flake as you fold it through.")]
        [InlineData("For clean squares, the slab needs an hour or more of cold after you pack it into a lined tin.")]
        public void A_sentence_rebuilt_from_what_the_cook_must_do_passes(string rebuilt)
        {
            Copy(rebuilt).Should().BeFalse();
        }

        [Fact]
        public void A_text_passes_only_when_every_sentence_is_rebuilt()
        {
            var rebuilt = new RewriteFidelity.SourceText(string.Empty, new[]
            {
                "Melt honey with peanut butter in a small saucepan on low; it is ready once it runs smooth.",
                "Once the saucepan is off the burner, beat the vanilla extract and cocoa powder through it until it is one even color.",
            });
            var oneCopy = new RewriteFidelity.SourceText(string.Empty, new[]
            {
                "Melt honey with peanut butter in a small saucepan on low; it is ready once it runs smooth.",
                "Remove the pan from the heat and mix in the vanilla extract and the cocoa powder until no streaks remain.",
            });

            ProseRebuilt.AllRebuilt(Source, rebuilt, Lines).Should().BeTrue();
            ProseRebuilt.AllRebuilt(Source, oneCopy, Lines).Should().BeFalse();
            ProseRebuilt.Read(Source, oneCopy, Lines).Where(s => s.Copy).Select(s => s.Label).Should().Equal("step 2 sentence 1");
        }

        [Fact]
        public void A_word_has_one_plain_form_with_its_equivalents_and_inflections()
        {
            ProseRebuilt.Plain("combine").Should().Be("mix");
            ProseRebuilt.Plain("stirring").Should().Be("mix");
            ProseRebuilt.Plain("allowed").Should().Be("let");
            ProseRebuilt.Plain("instructions").Should().Be("direction");
            ProseRebuilt.Plain("honey").Should().Be("honey");
        }
    }
}
