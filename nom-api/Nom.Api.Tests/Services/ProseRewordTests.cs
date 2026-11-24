using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The reword lane (prose lane v3) on the garlic sauce of prose-judge.tsv: the writer is given the
    /// whole method, the step and the sentences to reword; a recipe withheld as a whole has every
    /// sentence reworded and a shown one only its copied sentences; a reworded sentence that loses a fact
    /// or stays a copy is reworded again with what failed, up to the last round, after which the recipe is
    /// given up; the whole must pass the prose check, a contradiction of the recipe's own lines included,
    /// and the judge.
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

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), null, writer, new FakeJudge());

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.SelectMany(a => a.Reasons)) + " " + outcome.Finding);
            outcome.Rewrite!.Steps.Should().HaveCount(GarlicSauce.Method.Length);
            outcome.Rewrite.Steps[2].Should().Be(GarlicSauce.Reworded[GarlicSauce.Method[2]]);
            writer.Reworded.Should().HaveCount(GarlicSauce.Method.Length, "one call a step, with the sentences of that step");
            writer.Reworded.Should().OnlyContain(r => r.Method.Count == GarlicSauce.Method.Length && r.Lines.Count == GarlicSauce.Lines.Length, "the writer reads the whole method and the recipe's lines");
            writer.Reworded[1].StepText.Should().Be(Key(GarlicSauce.Method[1]));
            ProseCheck.Check(GarlicSauce.Input().Source, outcome.Rewrite, GarlicSauce.Lines).Sentences.Copied.Should().Be(0);
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

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), null, writer, new FakeJudge());

            outcome.Passed.Should().BeTrue();
            outcome.Attempts[0].Reasons.Should().Contain(ProseReword.FactReason);
            writer.Reworded.Last().Sentences.Single().Problem.Should().Contain("keep every fact");

            var stubborn = new FakeWriter { Reword = s => s };
            var given = await ProseReword.RunAsync(GarlicSauce.Input(), null, stubborn, new FakeJudge());
            given.Passed.Should().BeFalse();
            given.Attempts.Count(a => a.Style == ProseReword.StyleReword).Should().Be(ProseLanePrompts.RewordRounds.Count);
            given.FailureClass.Should().Be(ProseReword.CopyReason);
        }

        [Fact]
        public async Task A_shown_recipe_has_only_its_copied_sentences_reworded_and_its_own_sentences_kept()
        {
            var shown = new RewriteFidelity.SourceText("Garlic cream sauce, made with olive oil, garlic and heavy cream.", new[]
            {
                "Over medium heat, warm ½ tablespoon of the olive oil in a small saucepan, then cook the garlic in it for a minute or two until you can smell it.",
                "Pour in ¾ cup cream and ¾ teaspoon salt, then simmer for 7 to 10 minutes until slightly thickened.",
                "Whisk the butter through the sauce once you have lifted the pan off the heat.",
                "Before you serve it, leave the sauce to stand for a half hour.",
            }, GarlicSauce.Dish);
            ProseCheck.Check(GarlicSauce.Input().Source, shown, GarlicSauce.Lines).Repair.Should().BeTrue();
            var writer = new FakeWriter { Reword = Reworded };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), shown, writer, new FakeJudge());

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.SelectMany(a => a.Reasons)));
            writer.Reworded.Should().ContainSingle().Which.Sentences.Select(s => s.Number).Should().Equal(1);
            outcome.Rewrite!.Steps.Should().Equal(shown.Steps[0], GarlicSauce.Reworded[GarlicSauce.Method[1]], shown.Steps[2], shown.Steps[3]);
            outcome.Rewrite.Description.Should().Be(shown.Description);
        }

        [Fact]
        public async Task A_rewording_that_contradicts_the_recipes_own_lines_or_that_the_judge_refuses_is_not_shown()
        {
            var wrong = new FakeWriter
            {
                Reword = s => Key(s) == Key(GarlicSauce.Method[2]) ? "Whisk 4 tablespoons butter through the sauce once you have lifted the pan off the heat." : Reworded(s),
            };
            (await ProseReword.RunAsync(GarlicSauce.Input(), null, wrong, new FakeJudge())).Passed.Should().BeFalse("an amount the sentence does not state is a lost fact");

            var refused = await ProseReword.RunAsync(GarlicSauce.Input(), null, new FakeWriter { Reword = Reworded }, new FakeJudge { Faithful = (_, _) => false });
            refused.Passed.Should().BeFalse();
            refused.FailureClass.Should().StartWith(ProseCleanRoom.JudgeReason);
        }
    }
}
