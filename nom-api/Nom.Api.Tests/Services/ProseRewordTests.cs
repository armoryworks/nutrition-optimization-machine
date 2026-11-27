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
            writer.Reworded.Last().Sentences.Single().Problem.Should().Contain("another frame").And.Contain("these words of yours follow it in its order: remove", "the writer is told which of its words keep the published order");
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

        [Fact]
        public async Task The_writer_is_shown_the_facts_each_sentence_must_keep_by_rule_and_its_last_refused_rewrite_with_what_to_put_right()
        {
            var calls = new Dictionary<string, int>();
            var dropped = "With the cream and the salt poured into the pan, let it all simmer so the sauce gets a little thicker.";
            var writer = new FakeWriter
            {
                Reword = s =>
                {
                    calls[s] = calls.GetValueOrDefault(s) + 1;
                    return Key(s) == Key(GarlicSauce.Method[1]) && calls[s] == 1 ? dropped : Reworded(s);
                },
            };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.SelectMany(a => a.Reasons)));
            var first = writer.Reworded[0].Sentences.Single();
            first.Keep.Should().Contain("½ tablespoon").And.Contain("olive oil").And.Contain("garlic").And.Contain("over medium heat");
            first.Keep.Should().Contain("heat (or").And.Contain("cook (or");
            first.Last.Should().BeNull();
            var again = writer.Reworded.Last().Sentences.Single();
            again.Last.Should().Be("With ¾ cup cream and ¾ teaspoon salt poured into the pan, let it all simmer so the sauce gets a little thicker.");
            again.Problem.Should().Contain("write \"7 to 10 minutes\" exactly as listed");
        }

        [Fact]
        public async Task A_step_the_judge_refuses_is_reworded_again_in_the_next_round_with_its_reading_and_the_whole_is_judged_again()
        {
            var writer = new FakeWriter { Reword = Reworded };
            var reads = 0;
            var judge = new FakeJudge { Faithful = (_, _) => ++reads > 1 };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), writer, judge);

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.Select(a => a.Style + ": " + string.Join(", ", a.Reasons))));
            judge.Read.Should().HaveCount(2);
            outcome.Attempts.Select(a => a.Style).Should().Equal(ProseReword.StyleReword, "judge", ProseReword.StyleReword, "gate");
            outcome.Attempts[1].Reasons.Should().Equal(ProseCleanRoom.JudgeReason + ":missing");
            var again = writer.Reworded.Last();
            again.Step.Should().Be(1, "only the step the judge refused is asked again");
            again.Sentences.Single().Problem.Should().Contain("the judge").And.Contain("missing");
            again.Sentences.Single().Last.Should().Be(GarlicSauce.Reworded[GarlicSauce.Method[0]]);
            writer.Reworded.Should().HaveCount(GarlicSauce.Method.Length + 1);
        }

        [Fact]
        public async Task When_the_first_rewrite_of_a_sentence_fails_its_second_in_another_frame_is_kept_if_it_passes()
        {
            var writer = new FakeWriter
            {
                Reword = s => Key(s) == Key(GarlicSauce.Method[2]) ? "Remove the pan from the heat and whisk the butter in." : Reworded(s),
                Other = s => Key(s) == Key(GarlicSauce.Method[2]) ? Reworded(s) : null,
            };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.SelectMany(a => a.Reasons)));
            outcome.Attempts.Count(a => a.Style == ProseReword.StyleReword).Should().Be(1, "the second rewrite passed in the same round");
            outcome.Rewrite!.Steps[2].Should().Be(GarlicSauce.Reworded[GarlicSauce.Method[2]]);
            writer.Reworded.Should().HaveCount(GarlicSauce.Method.Length);
        }

        [Fact]
        public async Task A_rewrite_that_leaves_out_how_a_food_is_cut_is_refused_by_rule_before_the_judge_and_the_writer_is_told_to_keep_it()
        {
            var lines = new[] { "2 carrots", "1 tablespoon oil" };
            var input = new ProseLaneInput("Carrot Ribbons", new RewriteFidelity.SourceText(string.Empty, new[] { "Peel the carrots and slice them thinly.", "Toss with the oil." }, "Carrot Ribbons"), lines, null);
            var judge = new FakeJudge();

            var dropped = await ProseCleanRoom.GateAsync(input, new RewriteFidelity.SourceText(string.Empty, new[] { "Peel each carrot, then slice it.", "Coat the slices in the oil." }), judge, default);
            var kept = await ProseCleanRoom.GateAsync(input, new RewriteFidelity.SourceText(string.Empty, new[] { "Peel each carrot, then cut it into thin slices.", "Coat the slices in the oil." }), judge, default);

            dropped.Should().Equal(ProseCleanRoom.MannerReason);
            kept.Should().BeEmpty();
            judge.Read.Should().HaveCount(1, "the judge reads only the rewrite the rule lets through");
            ProseReword.Keep("Peel the carrots and slice them thinly.", lines).Should().Contain("cut: thinly");
            ProseReword.SentenceProblems("Peel the carrots and slice them thinly.", "Once peeled, each carrot gets sliced.", input.Source, lines)
                .Should().Contain(p => p.Reason == ProseReword.FactReason && p.Text.Contains("keep \"thinly\""));
        }

        [Fact]
        public void A_filler_frame_the_source_sentence_has_no_word_for_is_refused_by_rule_and_the_next_rewrite_is_tried()
        {
            var source = GarlicSauce.Input().Source;
            var before = ProseQuantities.Prepare("Melt 2 tablespoons butter in the pan over low heat.");

            ProseReword.Filler(before, "Over low heat, 2 tablespoons butter should melt in the pan, by which time it is ready for the flour.")
                .Should().Equal("by which time", "ready");
            ProseReword.Filler(before, "Over low heat, let 2 tablespoons butter melt in the pan until it turns liquid.").Should().Equal("until");
            ProseReword.Filler(before, "The pan takes 2 tablespoons butter, melted over low heat to ensure an even sauce.").Should().Equal("to ensure");
            ProseReword.Filler(before, "Over low heat, 2 tablespoons butter goes into the pan to melt.").Should().BeEmpty();
            ProseReword.Filler("Cook for 2 minutes, until golden.", "It needs 2 minutes of cooking so that it turns golden.").Should().BeEmpty("a result the sentence gives as its sign of doneness is its own");
            ProseReword.Filler("Let it cool until ready to serve.", "It should cool until it is ready to serve.").Should().BeEmpty();
            ProseReword.SentenceProblems(before, "Over low heat, let 2 tablespoons butter melt in the pan until it turns liquid.", source, GarlicSauce.Lines)
                .Should().ContainSingle(p => p.Reason == ProseReword.FillerReason && p.Text.Contains("\"until\""));
        }

        [Fact]
        public async Task A_rewrite_with_filler_gives_way_to_a_later_rewrite_of_the_same_round_that_has_none()
        {
            var filled = "Once the pan is away from the heat, the butter gets whisked through the sauce, by which time it is ready to serve.";
            var writer = new FakeWriter
            {
                Reword = s => Key(s) == Key(GarlicSauce.Method[2]) ? filled : Reworded(s),
                Other = s => Key(s) == Key(GarlicSauce.Method[2]) ? Reworded(s) : null,
            };

            var outcome = await ProseReword.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Passed.Should().BeTrue(string.Join(" | ", outcome.Attempts.SelectMany(a => a.Reasons)));
            outcome.Rewrite!.Steps[2].Should().Be(GarlicSauce.Reworded[GarlicSauce.Method[2]]);
            outcome.Attempts.Count(a => a.Style == ProseReword.StyleReword).Should().Be(1);
        }

        [Fact]
        public void A_fraction_is_written_the_way_its_source_sentence_writes_it_so_the_same_amount_keeps_its_fact()
        {
            ProseReword.SameFractions("Pour in ¾ cup cream.", "With 3/4 cup cream poured in, stir 1/2 teaspoon of it.").Should().Be("With ¾ cup cream poured in, stir 1/2 teaspoon of it.");
            ProseReword.SameFractions("Add 1/2 teaspoon salt.", "The salt, ½ teaspoon of it, goes in.").Should().Be("The salt, 1/2 teaspoon of it, goes in.");
            ProseReword.SameFractions("Add 1/2 teaspoon salt.", "The salt, 11/2 teaspoon of it, goes in.").Should().Be("The salt, 11/2 teaspoon of it, goes in.");
        }

        [Fact]
        public void A_measured_food_the_rewrite_names_once_without_its_amount_gets_the_amount_back_and_nothing_else_is_moved()
        {
            var lines = new[] { "1 tablespoon olive oil", "2 cloves garlic", "1/4 teaspoon salt", "1 cup chopped parsley" };

            ProseReword.PutBackAmounts("Heat 1 tablespoon olive oil and add the garlic.", "Once the olive oil is hot, the garlic goes in.", lines)
                .Should().Be("Once 1 tablespoon olive oil is hot, the garlic goes in.");
            ProseReword.PutBackAmounts("Beat the eggs with ¼ teaspoon salt.", "The eggs should be beaten in a bowl with salt.", lines)
                .Should().Be("The eggs should be beaten in a bowl with ¼ teaspoon salt.");
            ProseReword.PutBackAmounts("Beat the eggs with ¼ teaspoon salt.", "The salt goes in first, and the salt is beaten with the eggs.", lines)
                .Should().Be("The salt goes in first, and the salt is beaten with the eggs.", "a food named twice is not guessed at");
            ProseReword.PutBackAmounts("Beat the eggs with ¼ teaspoon salt.", "The eggs take ¼ teaspoon salt.", lines)
                .Should().Be("The eggs take ¼ teaspoon salt.");
            ProseReword.PutBackAmounts("Stir in 1 cup chopped parsley.", "The fresh parsley gets stirred in.", lines)
                .Should().Be("The fresh parsley gets stirred in.", "an amount is put back only where the source gives it right before the food");
            ProseReword.PutBackAmounts("Cook for 3 to 4 minutes, then add the salt.", "Add the salt after a short cook.", lines)
                .Should().Be("Add the salt after a short cook.", "a time is never put back");
        }

        [Fact]
        public void An_irregular_past_participle_keeps_its_verb_in_a_rewrite_but_is_never_read_as_a_source_verb()
        {
            var verbs = ProseVerbLexicon.Default;

            verbs.GroupsIn(ProsePhrases.Words("the eggs should be beaten until frothy")).Should().NotBeEmpty();
            verbs.VerbsIn(ProsePhrases.Words("the beaten eggs")).Should().BeEmpty();
        }

        [Fact]
        public void A_verb_after_a_named_food_keeps_its_action_and_an_amount_written_out_in_words_is_named_to_be_put_right()
        {
            var source = GarlicSauce.Input().Source;
            var before = ProseQuantities.Prepare("Melt 2 tablespoons butter in the pan over low heat.");

            ProseReword.SentenceProblems(before, "Over low heat, let 2 tablespoons butter melt in the pan until it turns liquid.", source, GarlicSauce.Lines)
                .Should().NotContain(p => p.Reason == ProseReword.FactReason, "\"the butter melt\" names the action melt; the food's token is not a noun after \"the\"");
            ProseReword.SentenceProblems(before, "Over low heat, let two tablespoons of butter melt in the pan until it turns liquid.", source, GarlicSauce.Lines)
                .Should().Contain(p => p.Reason == ProseReword.FactReason && p.Text.Contains("write \"2 tablespoons\" exactly as listed"));
        }
    }
}
