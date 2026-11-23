using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The clean-room pipeline of prose lane v2 with a fake writer and judge: the source is masked
    /// so the writer never sees its wording or retypes an amount, the fact sheets and the written
    /// method are held to rules, the prose check and the judge gate every attempt, the writer is told why
    /// an attempt failed in the lane's own words, and a recipe the attempts cannot rewrite is given
    /// up with the class that held it.
    /// </summary>
    public class ProseCleanRoomTests
    {
        [Fact]
        public void Masking_turns_every_amount_and_ingredient_into_a_token_and_unmasking_puts_the_source_text_back()
        {
            var method = ProseMasking.Mask(GarlicSauce.Method, GarlicSauce.Lines);

            method.Steps.Should().HaveCount(4);
            method.Steps[0].Text.Should().Be("Heat ⟦Q1⟧ of the olive ⟦L1⟧ in a small saucepan over medium heat and cook the ⟦L2⟧ for ⟦Q2⟧, until fragrant.");
            method.Amounts["Q1"].Should().Be("½ tablespoon");
            method.Amounts["Q2"].Should().Be("a minute or two");
            method.Amounts["Q5"].Should().Be("7 to 10 minutes");
            method.Steps[1].Amounts.Should().Equal("Q3", "Q4", "Q5");
            method.Steps[1].Lines.Should().Equal("L3", "L4");
            method.Lines.Select(l => (l.Id, l.Head, l.Name)).Should().StartWith(("L1", "oil", "olive oil"));
            method.Unmask("Warm [Q1] {L1}, then ⟦Q2⟧.").Should().Be("Warm ½ tablespoon oil, then a minute or two.");
        }

        [Fact]
        public void A_bracketed_conversion_is_its_own_token_that_may_be_dropped()
        {
            var method = ProseMasking.Mask(new[] { "Preheat the oven to 350°F (175°C)." }, Array.Empty<string>());

            method.Steps[0].Amounts.Should().Equal("Q1");
            method.Steps[0].Conversions.Should().Equal("Q2");
            ProseCleanRoom.FormProblems(method, ProseCleanRoom.RuleSheets(method), new[] { "Heat the oven to ⟦Q1⟧." }).Should().BeEmpty();
        }

        [Theory]
        [InlineData("Simmer ⟦Q5⟧.", 0)]
        [InlineData("Simmer for 8 minutes.", 2)]
        [InlineData("Simmer for a minute.", 1)]
        [InlineData("Simmer for two cups.", 1)]
        public void The_form_rule_refuses_any_number_or_amount_outside_the_tokens(string text, int problems)
        {
            ProseMasking.FormProblems(text).Should().HaveCount(problems);
        }

        [Fact]
        public void The_fact_sheet_rules_name_each_missing_token_verb_and_overwordycue()
        {
            var method = ProseMasking.Mask(GarlicSauce.Method, GarlicSauce.Lines);
            var sheets = ProseCleanRoom.RuleSheets(method).ToList();
            ProseFactSheet.Problems(method, sheets).Should().BeEmpty();

            sheets[1] = sheets[1] with { Amounts = new[] { "⟦Q3⟧", "⟦Q3⟧", "⟦Q6⟧" }, Objects = new[] { "⟦L3⟧" }, Actions = new[] { "pour" } };
            sheets[2] = sheets[2] with { Cues = new[] { "until the butter has melted into the sauce" }, Equipment = new[] { "pan for 3 minutes" } };

            ProseFactSheet.Problems(method, sheets).Should().BeEquivalentTo(
                "step 2: the amount token ⟦Q3⟧ is given 2 times",
                "step 2: the amount token ⟦Q4⟧ is missing",
                "step 2: the amount token ⟦Q5⟧ is missing",
                "step 2: the amount token ⟦Q6⟧ does not belong to this step",
                "step 2: the ingredient token ⟦L4⟧ is missing from objects",
                "step 2: the action \"cook/simmer\" is missing from actions",
                "step 3: \"until the butter has melted into the sauce\" is longer than 6 words",
                "step 3: \"pan for 3 minutes\" states a number or an amount; use the amount tokens");
        }

        [Fact]
        public void An_extractor_answer_is_read_with_its_tokens_in_one_form()
        {
            var sheets = ProseFactSheet.Parse("""{"steps":[{"n":1,"actions":["heat"],"objects":["[L1]"],"equipment":["pan"],"amounts":["{Q1}","[[Q2]]"],"cues":["until fragrant"]}]}""");

            sheets.Should().ContainSingle();
            sheets![0].Objects.Should().Equal("⟦L1⟧");
            sheets[0].Amounts.Should().Equal("⟦Q1⟧", "⟦Q2⟧");
            ProseFactSheet.Parse("not json").Should().BeNull();
            ProseFactSheet.Render(sheets[0]).Should().Be("Heat ⟦L1⟧ in the pan: ⟦Q1⟧, ⟦Q2⟧; until fragrant.");
        }

        [Fact]
        public async Task A_faithful_method_passes_in_the_first_attempt_with_the_source_amounts_put_back_by_code()
        {
            var writer = new FakeWriter();
            var judge = new FakeJudge();

            var outcome = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), writer, judge);

            outcome.Passed.Should().BeTrue(string.Join("; ", outcome.Attempts.SelectMany(a => a.Reasons)));
            outcome.Attempts.Should().ContainSingle();
            outcome.Rewrite!.Steps[0].Should().Be("Warm ½ tablespoon oil in a small pan on medium and fry the garlic a minute or two, just until it smells good.");
            outcome.Rewrite.Steps[1].Should().Contain("¾ cup cream").And.Contain("7 to 10 minutes");
            outcome.Rewrite.Description.Should().Be("Garlic Cream Sauce, made with heavy cream and butter.");
            judge.Read.Should().ContainSingle();
        }

        [Fact]
        public async Task The_writer_is_given_only_the_sheets_the_ingredient_names_and_the_dish_never_the_source_wording()
        {
            var writer = new FakeWriter();
            await ProseCleanRoom.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            var request = writer.Requests.Should().ContainSingle().Subject;
            var given = JsonSerializer.Serialize(request);
            foreach (var word in new[] { "fragrant", "saucepan", "thickened", "½", "¾", "7 to 10", "Pour in", "before serving" })
                given.Should().NotContain(word);
            request.Lines.Select(l => l.Name).Should().Equal("olive oil", "garlic", "heavy cream", "salt", "butter");
            writer.Extracted.Should().ContainSingle().Which.Steps.Should().OnlyContain(s => !s.Text.Contains('½') && !s.Text.Contains("7 to 10"));
        }

        [Fact]
        public async Task An_amount_the_writer_types_itself_is_refused_and_the_next_attempt_is_told_why()
        {
            var retyped = GarlicSauce.Written.ToArray();
            retyped[1] = "Add the ½ cup ⟦L3⟧ with the ⟦Q4⟧ ⟦L4⟧ and let it simmer ⟦Q5⟧, until a little thicker.";
            var writer = new FakeWriter(FakeWriter.Steps(retyped), FakeWriter.Steps(GarlicSauce.Written));

            var outcome = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Passed.Should().BeTrue();
            outcome.Attempts.Should().HaveCount(2);
            outcome.Attempts[0].Reasons.Should().Contain(ProseCleanRoom.FormReason)
                .And.Contain(r => r.Contains("step 2 must use ⟦Q3⟧ exactly once")).And.Contain(r => r.Contains("amount of its own (\"½ cup\")"));
            writer.Requests[1].Problems.Should().Contain(p => p.Contains("⟦Q3⟧"));
            writer.Requests[1].Temperature.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task A_copied_method_fails_check_v2_and_the_feedback_quotes_no_source_text()
        {
            var method = ProseMasking.Mask(GarlicSauce.Method, GarlicSauce.Lines);
            var copied = FakeWriter.Steps(method.Steps.Select(s => s.Text));
            var writer = new FakeWriter(copied, copied, copied);

            var outcome = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Attempts.Take(3).Should().OnlyContain(a => a.Reasons.Contains(ProseCheck.Unchanged) || a.Reasons.Contains(ProseCheck.CopiedText));
            writer.Requests.Skip(1).SelectMany(r => r.Problems).Should().NotBeEmpty()
                .And.OnlyContain(p => !p.Contains("fragrant") && !p.Contains("saucepan") && !p.Contains("thickened"));
            writer.Requests[2].Style.Should().Be(ProseLanePrompts.StyleActions);
        }

        [Fact]
        public async Task A_rewrite_the_check_is_only_unsure_of_goes_to_the_judge_which_decides()
        {
            var wordy = new[]
            {
                "Warm ⟦Q1⟧ ⟦L1⟧ in a small pan on medium, then fry the ⟦L2⟧ in it for ⟦Q2⟧, stirring now and then, just until the kitchen starts to smell good and the pieces look soft.",
                "Add the ⟦Q3⟧ ⟦L3⟧ together with the ⟦Q4⟧ ⟦L4⟧ and let everything simmer gently for ⟦Q5⟧, until the sauce turns a little thicker and coats a spoon.",
                "Remove the pan from the burner, then whisk the ⟦L5⟧ in a little at a time until it has fully disappeared into the sauce.",
                "Rest the sauce ⟦Q6⟧ on the side so it settles, then serve it warm.",
            };
            var method = ProseMasking.Mask(GarlicSauce.Method, GarlicSauce.Lines);
            var finding = ProseCheck.Check(GarlicSauce.Input().Source, new RewriteFidelity.SourceText(string.Empty, wordy.Select(method.Unmask).ToList()), GarlicSauce.Lines);
            finding.Outcome.Should().Be("unsure", finding.ToString());
            finding.Reasons.Should().Equal(ProseCheck.TooLong);

            var judge = new FakeJudge();
            var accepted = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), new FakeWriter(FakeWriter.Steps(wordy)), judge);

            accepted.Passed.Should().BeTrue(string.Join("; ", accepted.Attempts.SelectMany(a => a.Reasons)));
            accepted.Attempts.Should().ContainSingle();
            judge.Read.Should().ContainSingle("an unsure rewrite is the judge's to read");

            var refused = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), new FakeWriter(FakeWriter.Steps(wordy)), new FakeJudge { Faithful = (_, _) => false });

            refused.Passed.Should().BeFalse();
            refused.Attempts[0].Reasons.Should().OnlyContain(r => r.StartsWith(ProseCleanRoom.JudgeReason));
        }

        [Fact]
        public async Task A_rewrite_the_judge_refuses_is_never_passed_and_gives_up_with_the_judge_class_after_the_rule_attempt()
        {
            var judge = new FakeJudge { Faithful = (_, _) => false };

            var outcome = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), new FakeWriter(), judge);

            outcome.Passed.Should().BeFalse();
            outcome.Attempts.Should().HaveCount(ProseCleanRoom.AttemptCount);
            outcome.Attempts[^1].Style.Should().Be(ProseLanePrompts.RuleAttempt);
            outcome.FailureClass.Should().Be("judge:missing");
            judge.Read.Should().HaveCount(ProseCleanRoom.AttemptCount);
        }

        [Fact]
        public async Task The_rule_rendering_of_the_extractors_sheets_is_the_last_attempt_when_the_writer_keeps_failing()
        {
            var writer = new FakeWriter("{\"steps\":[]}")
            {
                Extract = m => ProseFactSheet.ToJson(ProseCleanRoom.RuleSheets(m).Select(s => s.Number == 1 ? s with { Cues = new[] { "until it smells good" } } : s).ToList()),
            };

            var outcome = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.Attempts.Take(3).Should().OnlyContain(a => a.Reasons.Contains(ProseCleanRoom.WriterUnreadable));
            outcome.Attempts[^1].Style.Should().Be(ProseLanePrompts.RuleAttempt);
            outcome.Attempts[^1].Reasons.Should().NotContain(ProseCleanRoom.FormReason);
        }

        [Fact]
        public async Task Sheets_the_extractor_cannot_state_are_built_by_rule_and_then_no_rule_rendering_is_tried()
        {
            var writer = new FakeWriter("{\"steps\":[]}") { Extract = _ => "{}" };

            var outcome = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), writer, new FakeJudge());

            outcome.RuleSheets.Should().BeTrue();
            writer.Extracted.Should().HaveCount(2);
            outcome.Attempts.Should().HaveCount(ProseLanePrompts.Attempts.Count);
            outcome.FailureClass.Should().Be(ProseCleanRoom.WriterUnreadable);
        }

        [Fact]
        public void The_failure_class_names_why_the_writer_failed_not_why_the_rule_rendering_after_it_did()
        {
            var attempts = new[]
            {
                new ProseAttempt(1, ProseLanePrompts.StyleSteps, new[] { ProseCheck.CopiedText }),
                new ProseAttempt(2, ProseLanePrompts.StyleSteps, new[] { ProseCheck.CopiedText }),
                new ProseAttempt(3, ProseLanePrompts.StyleActions, new[] { ProseCheck.CopiedText, ProseCheck.TooShort }),
                new ProseAttempt(4, ProseLanePrompts.RuleAttempt, new[] { ProseCleanRoom.FormReason, "form: step 1 must use ⟦Q1⟧ exactly once" }),
            };

            new ProseLaneOutcome(null, attempts, false, null, null, 4).FailureClass.Should().Be(ProseCheck.CopiedText);
            new ProseLaneOutcome(null, attempts.TakeLast(1).ToList(), false, null, null, 4).FailureClass.Should().Be(ProseCleanRoom.FormReason);
        }

        [Fact]
        public async Task A_judge_answer_that_cannot_be_read_never_passes_a_rewrite()
        {
            var outcome = await ProseCleanRoom.RunAsync(GarlicSauce.Input(), new FakeWriter(), new FakeJudge { Faithful = (_, _) => null });

            outcome.Passed.Should().BeFalse();
            outcome.FailureClass.Should().Be(ProseCleanRoom.JudgeUnreadable);
        }

        [Fact]
        public async Task A_source_without_a_method_is_given_up_as_no_method_without_asking_a_model()
        {
            var writer = new FakeWriter();
            var input = new ProseLaneInput("Toast", new RewriteFidelity.SourceText("Toast.", Array.Empty<string>(), "Toast"), Array.Empty<string>(), ProseStockIndex.Empty);

            var outcome = await ProseCleanRoom.RunAsync(input, writer, new FakeJudge());

            outcome.FailureClass.Should().Be(ProseCleanRoom.NoMethod);
            writer.Extracted.Should().BeEmpty();
        }

        [Fact]
        public void A_judge_answer_is_read_only_when_it_gives_every_step_once()
        {
            ProseJudgeVerdict.Parse("""{"steps":[{"n":1,"actions":true,"amounts":true,"missing":false,"added":false,"note":""},{"n":2,"actions":true,"amounts":false,"missing":false,"added":false,"note":"x"}]}""", 2)!
                .Should().Match<ProseJudgeVerdict>(v => !v.Faithful && v.FirstFlagged!.Number == 2);
            ProseJudgeVerdict.Parse("""{"steps":[{"n":1,"actions":true,"amounts":true,"missing":false,"added":false}]}""", 2).Should().BeNull();
            ProseJudgeVerdict.Parse("""{"steps":[{"n":1,"actions":"yes","amounts":true,"missing":false,"added":false}]}""", 1).Should().BeNull();
        }

        [Fact]
        public void The_models_may_not_check_their_own_family()
        {
            new ProseLaneModels("qwen2.5:14b-instruct", "mistral-nemo", "phi4").Problem.Should().BeNull();
            new ProseLaneModels("qwen2.5:14b-instruct", "qwen2.5:7b-instruct", "phi4").Problem.Should().Contain("writer");
            new ProseLaneModels("mistral-nemo", "mistral-small", "phi4").Problem.Should().NotBeNull();
            new ProseLaneModels("qwen2.5:14b-instruct", "mistral-nemo", "qwen3:8b").Problem.Should().Contain("auditor");
            new ProseLaneModels("qwen2.5:14b-instruct", "mistral-nemo", "phi4").Version
                .Should().NotBe(new ProseLaneModels("qwen2.5:0.5b", "mistral-nemo", "phi4").Version);
        }
    }
}
