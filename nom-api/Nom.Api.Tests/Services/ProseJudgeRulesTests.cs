using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The rules around the prose judge, on constructed text: a different step count, a fact the prose
    /// check fails on and a heat conflict decide before any model is asked; the model's reading of a
    /// step pair is a fault when a fact is different, missing or quoted from words the rewrite does not
    /// hold, or anything is added; the check's weak signals reach the model as notes on their step.
    /// </summary>
    public class ProseJudgeRulesTests
    {
        private static readonly string[] Original =
        {
            "Toast the walnuts in a dry pan over medium heat until they smell nutty.",
            "Roast the squash in a moderately hot oven for 40 minutes.",
            "Serve the soup hot, with the walnuts on top.",
        };

        private static readonly string[] Faithful =
        {
            "Dry-toast the walnuts in a pan on medium until they smell nutty.",
            "Give the squash 40 minutes in an oven that is moderately hot.",
            "Scatter the walnuts over the soup and serve it hot.",
        };

        private static ProseCheck.Finding Check(IReadOnlyList<string> original, IReadOnlyList<string> rewrite) =>
            ProseCheck.Check(new RewriteFidelity.SourceText(string.Empty, original), new RewriteFidelity.SourceText(string.Empty, rewrite));

        [Theory]
        [InlineData("Fry the shallots over a medium-high heat.", "stove:medium-high")]
        [InlineData("Cook gently on low for an hour.", "stove:low")]
        [InlineData("Simmer over moderate heat.", "stove:medium")]
        [InlineData("Bake in a moderately hot oven.", "oven:moderately hot")]
        [InlineData("Put it in a cool oven to dry.", "oven:slow")]
        [InlineData("Serve chilled with cream.", "serve:cold")]
        [InlineData("Pour on hot water and leave it.", "")]
        public void The_heat_a_step_names_is_read_by_scale(string step, string heat) =>
            string.Join(",", ProseJudgeRules.Heats(step)).Should().Be(heat);

        [Fact]
        public void A_heat_conflict_needs_a_level_on_both_sides_of_one_scale()
        {
            ProseJudgeRules.HeatConflicts("Fry over low heat.", "Fry it on high.").Should().Equal("stove");
            ProseJudgeRules.HeatConflicts("Fry over medium heat.", "Fry it on medium.").Should().BeEmpty();
            ProseJudgeRules.HeatConflicts("Fry over medium heat.", "Fry it until brown.").Should().BeEmpty("a level left out is the model's to see");
            ProseJudgeRules.HeatConflicts("Bake in a slow oven.", "Bake in a very hot oven.").Should().Equal("oven");
            ProseJudgeRules.HeatConflicts("Serve hot.", "Serve it cold.").Should().Equal("serve");
        }

        [Fact]
        public void A_faithful_rewrite_is_left_to_the_model()
        {
            ProseJudgeRules.Before(Original, Faithful, Check(Original, Faithful)).Should().BeNull();
        }

        [Fact]
        public void Rules_decide_a_step_count_a_failed_fact_and_a_heat_conflict()
        {
            var shorter = Faithful.Take(2).ToList();
            ProseJudgeRules.Before(Original, shorter, Check(Original, shorter))!.Faithful.Should().BeFalse();

            var longer = Faithful.Select(s => s.Replace("40 minutes", "25 minutes", StringComparison.Ordinal)).ToList();
            var amount = ProseJudgeRules.Before(Original, longer, Check(Original, longer))!;
            amount.Faithful.Should().BeFalse();
            amount.FirstFlagged!.Amounts.Should().BeFalse();

            var hotter = Faithful.Select(s => s.Replace("on medium", "on high", StringComparison.Ordinal)).ToList();
            var heat = ProseJudgeRules.Before(Original, hotter, Check(Original, hotter))!;
            heat.Steps.Where(s => !s.Same).Select(s => s.Number).Should().Equal(1);
        }

        [Fact]
        public void A_weak_signal_of_the_check_is_a_note_on_its_own_step()
        {
            var rewrite = Faithful.ToArray();
            rewrite[1] = "Put the squash in an oven that is moderately hot for 40 minutes.";
            var finding = Check(Original, rewrite);

            finding.Unsure.Should().BeTrue();
            ProseJudgeRules.Notes(finding, 2).Should().ContainSingle().Which.Should().Contain("bake");
            ProseJudgeRules.Notes(finding, 1).Should().BeEmpty();
        }

        [Fact]
        public void A_step_is_the_same_only_when_every_fact_is_found_as_quoted_and_nothing_is_added()
        {
            var original = Original[0];
            var rewrite = Faithful[0];
            ProseStepFact Fact(string kind, string ours, string theirs, string match = ProseJudgeRules.Same) => new(kind, ours, theirs, match);
            var same = new[] { Fact("action", "Toast the walnuts", "toast the walnuts"), Fact("heat", "over medium heat", "on medium"), Fact("cue", "until they smell nutty", "until they smell nutty") };

            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(same, Array.Empty<ProseStepFact>())).Same.Should().BeTrue();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(same.Append(Fact("cue", "until crisp", "until crisp")).ToList(), Array.Empty<ProseStepFact>()))
                .Missing.Should().BeTrue("words the rewrite does not hold are no match");
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(new[] { Fact("heat", "over medium heat", "on high") }, Array.Empty<ProseStepFact>()))
                .Same.Should().BeFalse();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(new[] { Fact("cue", "until they smell nutty", "until browned", ProseJudgeRules.Different) }, Array.Empty<ProseStepFact>()))
                .Actions.Should().BeFalse();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(new[] { Fact("action", "Toast the walnuts", string.Empty) }, Array.Empty<ProseStepFact>()))
                .Missing.Should().BeTrue();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(same, new[] { Fact("ingredient", string.Empty, "a pinch of salt", ProseJudgeRules.Different) }))
                .Added.Should().BeTrue();
            ProseJudgeRules.Step(1, original, rewrite, new ProseStepReading(Array.Empty<ProseStepFact>(), Array.Empty<ProseStepFact>()))
                .Same.Should().BeFalse("a reading of no fact is not trusted");
        }

        [Fact]
        public void A_claim_asked_again_is_withdrawn_only_by_a_rule_or_a_same_result_whose_quote_holds()
        {
            var rewrite = "Dry-toast the walnuts in a pan on medium until they smell nutty.";
            var heat = new ProseStepFact("heat", "over medium heat", "on medium", ProseJudgeRules.Different);
            var words = new ProseStepFact("action", "Toast the walnuts", string.Empty, ProseJudgeRules.Missing);
            var cue = new ProseStepFact("cue", "until crisp", string.Empty, ProseJudgeRules.Different);

            ProseJudgeRules.FoundByRule(heat, rewrite).Should().BeTrue("the same level on the same scale");
            ProseJudgeRules.FoundByRule(heat, rewrite.Replace("on medium", "on high", StringComparison.Ordinal)).Should().BeFalse();
            ProseJudgeRules.FoundByRule(words, rewrite).Should().BeTrue("every word of it is in the step");
            ProseJudgeRules.FoundByRule(cue, rewrite).Should().BeFalse();
            ProseJudgeRules.FoundByRule(new ProseStepFact("heat", "in a deep skillet over medium heat", string.Empty, ProseJudgeRules.Different), rewrite).Should().BeTrue("a heat fact is its level");
            ProseJudgeRules.FoundByRule(new ProseStepFact("serve", "Serve hot with cream", string.Empty, ProseJudgeRules.Different), "Serve it hot with milk.").Should().BeFalse("the heat agrees, the food does not");
            ProseJudgeRules.FoundByRule(new ProseStepFact("serve", "Serve hot with cream", string.Empty, ProseJudgeRules.Different), "Pour cream over and serve it hot.").Should().BeTrue();
            ProseJudgeRules.Step(1, Original[0], rewrite, new ProseStepReading(new[] { ProseJudgeRules.Kept(heat, null, Original[0], rewrite) }, Array.Empty<ProseStepFact>())).Same.Should().BeTrue("a fact a rule found is not quoted again");

            static string Answer(string quote, string verdict) => JsonSerializer.Serialize(new { rewrite = quote, original_result = "x", rewrite_result = "y", verdict });
            ProseJudgeRules.Kept(cue, Answer("until they smell nutty", "same_result"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Same);
            ProseJudgeRules.Kept(cue, Answer("until golden and crisp", "same_result"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Different, "a quote the step does not hold withdraws nothing");
            ProseJudgeRules.Kept(cue, Answer("until they smell nutty", "other_result"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Different);
            ProseJudgeRules.Kept(cue, Answer(string.Empty, "not_said"), Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Missing);
            ProseJudgeRules.Kept(cue, "not json", Original[0], rewrite).Match.Should().Be(ProseJudgeRules.Different);
            ProseJudgeRules.AlreadySaid(JsonSerializer.Serialize(new { original = "in a dry pan", verdict = "kept" }), Original[0]).Should().BeTrue();
            ProseJudgeRules.AlreadySaid(JsonSerializer.Serialize(new { original = "with honey", verdict = "kept" }), Original[0]).Should().BeFalse();
        }

        [Fact]
        public void An_action_of_the_original_no_fact_names_is_checked_as_missing()
        {
            var original = "Take the pan off the heat and whisk in the cream.";
            var read = new[] { new ProseStepFact("action", "Take the pan off the heat", "Lift the pan off the burner", ProseJudgeRules.Same) };

            ProseJudgeRules.Unread(original, read).Should().ContainSingle().Which.Should().Be(new ProseStepFact("action", "whisk in the cream", string.Empty, ProseJudgeRules.Missing));
            ProseJudgeRules.Unread(original, read.Append(new ProseStepFact("action", "whisk in the cream", "beat the cream in", ProseJudgeRules.Same)).ToList()).Should().BeEmpty();
        }

        [Fact]
        public void A_reading_is_parsed_only_with_a_known_match()
        {
            ProseJudgeRules.Parse("{\"facts\":[{\"kind\":\"action\",\"original\":\"a\",\"rewrite\":\"b\",\"match\":\"same\"}],\"added\":[]}")!.Facts.Should().ContainSingle();
            ProseJudgeRules.Parse("{\"facts\":[{\"kind\":\"action\",\"original\":\"a\",\"rewrite\":\"b\",\"match\":\"maybe\"}],\"added\":[]}").Should().BeNull();
            ProseJudgeRules.Parse("not json").Should().BeNull();
        }

        [Fact]
        public async Task The_judge_asks_no_model_when_a_rule_decides_and_one_call_per_step_otherwise()
        {
            var handler = new Answering("{\"facts\":[],\"added\":[]}");
            var judge = new OllamaProseStepJudge(new HttpClient(handler), "http://judge.invalid", "mistral-nemo");

            var hotter = Faithful.Select(s => s.Replace("on medium", "on high", StringComparison.Ordinal)).ToList();
            (await judge.JudgeAsync(Original, hotter, CancellationToken.None))!.Faithful.Should().BeFalse();
            handler.Bodies.Should().BeEmpty();

            (await judge.JudgeAsync(Original, Faithful, CancellationToken.None))!.Steps.Should().HaveCount(3);
            handler.Bodies.Count(b => b.Contains("\"facts\"", StringComparison.Ordinal)).Should().Be(3, "one reading per step pair; a claim is asked again apart");
            using var first = JsonDocument.Parse(handler.Bodies[0]);
            first.RootElement.GetProperty("options").GetProperty("seed").GetInt32().Should().Be(ProseLanePrompts.JudgeSeed);
            first.RootElement.GetProperty("options").GetProperty("temperature").GetDouble().Should().Be(0);
            first.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should().Contain(Original[0]).And.Contain(Faithful[0]);
        }

        private sealed class Answering : HttpMessageHandler
        {
            private readonly string _content;

            public Answering(string content) => _content = content;

            public List<string> Bodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                var reply = JsonSerializer.Serialize(new { message = new { content = _content } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }
        }
    }
}
