using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The reword lane (prose lane v4) on the garlic sauce of prose-judge.tsv: the writer is given the
    /// whole method, the step and the sentences to reword; every recipe has every sentence of its method
    /// reworded; a reworded sentence that loses a fact or is not clearly rebuilt (a synonym swap in the
    /// source's frame included) is reworded again with what failed, up to the last round, after which the
    /// recipe is given up; the whole must pass the prose check, a contradiction of the recipe's own lines
    /// included, the strict copy check and the judge.
    /// </summary>
    public class ProseRewordTests
    {
        private static string Key(string text) => ProseQuantities.Prepare(text);

        private static string Reworded(string sentence) =>
            GarlicSauce.Reworded.FirstOrDefault(kv => Key(kv.Key) == Key(sentence)).Value ?? sentence;

        [Fact]
        public async Task A_recipe_withheld_as_a_whole_has_every_sentence_reworded_after_the_writer_reads_the_method_the_step_and_the_sentence()
        {
            var writer = new FakeWriter { Reword = Reworded };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.SelectMany(a => a.Reasons)) + " " + outcome.Finding);
            outcome.Rewrite!.Steps.Should().HaveCount(GarlicSauce.Method.Length);
            outcome.Rewrite.Steps[2].Should().Be(GarlicSauce.Reworded[GarlicSauce.Method[2]]);
            writer.Reworded.Should().HaveCount(GarlicSauce.Method.Length, "one call a step, with the sentences of that step");
            writer.Reworded.Should().OnlyContain(r => r.Method.Count == GarlicSauce.Method.Length && r.Lines.Count == GarlicSauce.Lines.Length, "the writer reads the whole method and the recipe's lines");
            writer.Reworded[1].StepText.Should().Be(Key(GarlicSauce.Method[1]));
            ProseCheck.Check(GarlicSauce.Input().Source, outcome.Rewrite, GarlicSauce.Lines).Sentences.Copied.Should().Be(0);
            ProseRebuilt.AllRebuilt(GarlicSauce.Input().Source, new RewriteFidelity.SourceText(string.Empty, outcome.Rewrite.Steps), GarlicSauce.Lines).Should().BeTrue("every reworded sentence is clearly rebuilt");
        }

        [Fact]
        public async Task A_sentence_that_stays_a_copy_or_loses_a_fact_is_reworded_again_with_what_failed_and_the_recipe_is_given_up_after_the_last_round()
        {
            var calls = new Dictionary<string, int>();
            var writer = new FakeWriter
            {
                Reword = s =>
                {
                    calls[s] = calls.GetValueOrDefault(s) + 1;
                    if (Key(s) == Key(GarlicSauce.Method[1]) && calls[s] == 1) return "Pour cream into the pan along with salt and let the mixture simmer until it has thickened slightly.";
                    return Reworded(s);
                },
            };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Passed.Should().BeTrue();
            outcome.Attempts[0].Reasons.Should().Contain(ProseReword.FactReason);
            writer.Reworded.Last().Sentences.Single().Problem.Should().Contain("keep every fact");

            var stubborn = new FakeWriter { Reword = s => s };
            var given = await ProseReword.RunAsync(GarlicSauce.Input(), stubborn, new FakeJudge());
            given.Passed.Should().BeFalse();
            given.Attempts.Count(a => a.Style == ProseReword.StyleReword).Should().Be(ProseLanePrompts.RewordRounds.Count);
            given.FailureClass.Should().Be(ProseReword.CopyReason);
        }

        [Fact]
        public async Task A_synonym_swap_in_the_sources_frame_is_refused_as_a_copy_and_reworded_again()
        {
            var swap = "Remove the pan from the heat and whisk the butter in.";
            ProseSentences.Read(GarlicSauce.Input().Source, new RewriteFidelity.SourceText(string.Empty, new[] { swap }), GarlicSauce.Lines).Kept.Should().Be(0, "the word-order rule of the old text reads no copy in it");
            var calls = new Dictionary<string, int>();
            var writer = new FakeWriter
            {
                Reword = s =>
                {
                    calls[s] = calls.GetValueOrDefault(s) + 1;
                    return Key(s) == Key(GarlicSauce.Method[2]) && calls[s] == 1 ? swap : Reworded(s);
                },
            };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.SelectMany(a => a.Reasons)));
            outcome.Attempts[0].Reasons.Should().Equal(ProseReword.CopyReason);
            writer.Reworded.Last().Sentences.Single().Problem.Should().Contain("another frame");
            outcome.Rewrite!.Steps[2].Should().Be(GarlicSauce.Reworded[GarlicSauce.Method[2]]);
            ProseRebuilt.AllRebuilt(GarlicSauce.Input().Source, new RewriteFidelity.SourceText(string.Empty, outcome.Rewrite.Steps), GarlicSauce.Lines).Should().BeTrue();
        }

        [Fact]
        public async Task A_rewording_that_contradicts_the_recipes_own_lines_or_that_the_judge_refuses_is_not_shown()
        {
            var wrong = new FakeWriter
            {
                Reword = s => Key(s) == Key(GarlicSauce.Method[2]) ? "Whisk 4 tablespoons butter through the sauce once you have lifted the pan off the heat." : Reworded(s),
            };
            (await ProseReword.RunAsync(GarlicSauce.Input(), wrong, new FakeJudge())).Passed.Should().BeFalse("an amount the sentence does not state is a lost fact");

            var refused = await ProseReword.RunAsync(GarlicSauce.Input(), new FakeWriter { Reword = Reworded }, new FakeJudge { Faithful = (_, _) => false });
            refused.Passed.Should().BeFalse();
            refused.FailureClass.Should().StartWith(ProseCleanRoom.JudgeReason);
        }
    }
}
