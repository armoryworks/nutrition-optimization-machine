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
    /// How the prose auditor reads a rewrite: rules first with the recipe's own ingredient lines (a
    /// text that disagrees with its lines, a heat changed or left out, a step count changed), then one
    /// call per changed step with the dish, the lines, the source method and the rules' notes, and a
    /// blind re-read of a flagged step unless a rule decided it.
    /// </summary>
    public class ProseAuditTests
    {
        private static readonly string[] Lines = { "2 tablespoons butter", "1/4 cup sesame seeds" };
        private static readonly string[] Original = { "Melt 2 tablespoons butter in a skillet over low heat.", "Toast the seeds for 3 to 4 minutes, until golden." };

        private static ProseAuditPair Pair(params string[] rewrite) => new("Sesame Butter", Lines, Original, rewrite);

        [Fact]
        public void A_heat_changed_or_a_step_left_out_is_flagged_by_rule_and_needs_no_model()
        {
            var heat = ProseAudit.ByRule(Pair("Melt 2 tablespoons butter in a skillet over high heat.", "Toast the seeds for 3 to 4 minutes, until golden."));
            var dropped = ProseAudit.ByRule(Pair("Melt 2 tablespoons butter in a skillet over low heat."));

            heat!.ByRule.Should().BeTrue();
            heat.Flagged.Should().Equal(1);
            dropped!.Flagged.Should().Contain(2);
            ProseAudit.ByRule(Pair("Melt the 2 tablespoons butter in a frying pan on low heat.", "Toast the seeds 3 to 4 minutes until golden.")).Should().BeNull();
        }

        [Fact]
        public void An_amount_that_disagrees_with_the_recipes_own_line_is_flagged_by_rule()
        {
            var verdict = ProseAudit.ByRule(Pair("Melt 6 tablespoons butter in a skillet over low heat.", "Toast the seeds for 3 to 4 minutes, until golden."));

            verdict.Should().NotBeNull();
            verdict!.Note.Should().Contain(ProseCheck.ContradictsLines);
        }

        [Fact]
        public void A_sign_of_doneness_the_rewrite_step_leaves_out_is_flagged_by_rule()
        {
            var lost = ProseAudit.ByRule(Pair(Original[0], "Toast the seeds for 3 to 4 minutes."));

            lost!.ByRule.Should().BeTrue();
            lost.Flagged.Should().Equal(2);
            lost.Note.Should().Contain("cue missing");
            ProseAudit.ByRule(Pair(Original[0], "Toast the seeds for 3 to 4 minutes, so they turn golden.")).Should().BeNull();
            ProseAudit.ByRule(Pair(Original[0], "Toast the seeds for 3 to 4 minutes, until they colour.")).Should().BeNull("a rewrite that still says until is left to the model");
        }

        [Fact]
        public void Only_changed_steps_are_read_and_each_message_holds_the_lines_the_method_and_the_rules_notes()
        {
            var pair = Pair(Original[0], "Toast the seeds 4 minutes, until golden.");

            ProseAudit.ChangedSteps(pair).Should().Equal(2);
            var message = ProseAudit.StepMessage(pair, 2);
            message.Should().Contain("DISH: Sesame Butter").And.Contain("- 1/4 cup sesame seeds").And.Contain("Step 1: " + Original[0])
                .And.Contain("ORIGINAL STEP 2: " + Original[1]).And.Contain("REWRITE STEP 2: Toast the seeds 4 minutes")
                .And.Contain("the original step states \"3 to 4 minutes\"");
            ProseAudit.StepMessage(Pair(Original), 1).Should().Contain("RULE NOTES\nnone");
        }

        [Fact]
        public void A_step_answer_differs_when_its_verdict_says_so_or_it_lists_a_difference_and_is_unreadable_otherwise()
        {
            ProseAudit.ParseStep("{\"differences\":[],\"verdict\":\"same\"}")!.Differs.Should().BeFalse();
            ProseAudit.ParseStep("{\"differences\":[{\"what\":\"left out\",\"original\":\"until golden\",\"rewrite\":\"\"}],\"verdict\":\"same\"}")!.Differs.Should().BeTrue();
            ProseAudit.ParseStep("{\"differences\":[],\"verdict\":\"differs\"}")!.Differs.Should().BeTrue();
            ProseAudit.ParseStep("{\"differences\":[]}").Should().BeNull();
            ProseAudit.ParseStep("not json").Should().BeNull();
        }

        [Fact]
        public void A_step_that_differs_only_in_articles_punctuation_or_a_word_said_twice_is_not_read()
        {
            ProseAudit.ChangedSteps(Pair("Melt 2 tablespoons butter in skillet over low heat", "Toast seeds for 3 to 4 minutes minutes until golden.")).Should().BeEmpty();
            ProseAudit.ChangedSteps(Pair(Original[0], "Toast the seeds for 3 to 4 minutes, until brown.")).Should().Equal(2);
        }

        [Fact]
        public void A_difference_a_rule_finds_in_the_other_step_is_wording_only_and_a_new_word_a_lost_word_or_a_new_order_stands()
        {
            var pair = Pair(Original[0], "Toast the seeds 3 to 4 minutes, until they turn golden, then season with a pinch of salt.");
            static ProseAudit.Difference D(string what, string original, string rewrite) => new(what, original, rewrite);
            var reading = new ProseAudit.StepReading(true, new[]
            {
                D(ProseAudit.LeftOut, "until golden", string.Empty),
                D(ProseAudit.Changed, "for 3 to 4 minutes", "3 to 4 minutes"),
                D(ProseAudit.Added, string.Empty, "season with a pinch of salt"),
            });

            ProseAudit.Standing(pair, 2, reading).Select(d => d.What).Should().Equal(ProseAudit.Added);

            var cue = Pair(Original[0], "Toast the seeds for 3 to 4 minutes, until they smell nutty.");
            ProseAudit.Standing(cue, 2, new ProseAudit.StepReading(true, new[] { D(ProseAudit.Changed, "until golden", "until they smell nutty") })).Should().ContainSingle();
            ProseAudit.Standing(cue, 2, new ProseAudit.StepReading(true, new[] { D(ProseAudit.LeftOut, "until crisp", string.Empty) })).Should().ContainSingle("words not in the original step are not checked by rule");
            ProseAudit.Standing(cue, 2, new ProseAudit.StepReading(true, System.Array.Empty<ProseAudit.Difference>())).Should().ContainSingle();

            var brown = new ProseAuditPair("Toast", Lines, new[] { "Toast the bread until deep brown." }, new[] { "Toast the bread until brown." });
            ProseAudit.Standing(brown, 1, new ProseAudit.StepReading(true, new[] { D(ProseAudit.Changed, "until deep brown", "until brown") })).Should().ContainSingle("a word of size or manner must be found too");
            ProseAudit.Standing(pair, 2, new ProseAudit.StepReading(true, new[] { D(ProseAudit.Changed, "until golden", "golden") })).Should().BeEmpty();
            var jar = new ProseAuditPair("Oats", Lines, new[] { "Cover the jar." }, new[] { "Cover the mixture with a jar." });
            ProseAudit.Standing(jar, 1, new ProseAudit.StepReading(true, new[] { D(ProseAudit.Changed, "Cover the jar.", "Cover the mixture with a jar.") })).Should().ContainSingle("the rewrite's words name a thing the original step does not");

            var order = new ProseAuditPair("Scones", Lines, new[] { "Mix and sift the flour and the salt." }, new[] { "Sift and mix the flour and the salt." });
            ProseAudit.Standing(order, 1, new ProseAudit.StepReading(true, new[] { D(ProseAudit.Changed, "Mix and sift", "Sift and mix") })).Should().ContainSingle();
        }

        [Fact]
        public async Task A_step_whose_every_listed_difference_is_wording_only_is_not_flagged()
        {
            var handler = new Answering(_ => "{\"differences\":[{\"what\":\"left out\",\"original\":\"until golden\",\"rewrite\":\"\"}],\"verdict\":\"differs\"}");
            var auditor = new OllamaProseAuditor(new HttpClient(handler), "http://auditor.invalid", "phi4");

            (await auditor.AuditAsync(Pair(Original[0], "Toast the seeds 3 to 4 minutes, until they turn golden."), CancellationToken.None))!.Faithful.Should().BeTrue();
        }

        [Fact]
        public async Task The_blind_re_read_reads_the_flagged_step_with_the_lines_and_a_fixed_seed_and_says_whether_it_is_wrong()
        {
            var handler = new Answering(_ => "{\"difference\":\"cue changed\",\"wrong\":true}");
            (await new OllamaProseAuditor(new HttpClient(handler), "http://auditor.invalid", "phi4").ConfirmAsync(Pair(Original), 2, CancellationToken.None)).Should().BeTrue();
            using var body = JsonDocument.Parse(handler.Bodies.Single());
            body.RootElement.GetProperty("options").GetProperty("seed").GetInt32().Should().Be(ProseLanePrompts.AuditSeed);
            body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should().Contain("- 2 tablespoons butter").And.Contain("REWRITE STEP 2:");

            ProseAudit.ParseConfirm("{\"difference\":\"\",\"wrong\":false}").Should().BeFalse();
            ProseAudit.ParseConfirm("{\"difference\":\"\"}").Should().BeNull();
        }

        [Fact]
        public async Task The_auditor_asks_once_per_changed_step_with_a_fixed_seed_and_flags_the_steps_it_finds_different()
        {
            var handler = new Answering(_ => "{\"differences\":[{\"what\":\"changed\",\"original\":\"until golden\",\"rewrite\":\"until they smell nutty\"}],\"verdict\":\"differs\"}");
            var auditor = new OllamaProseAuditor(new HttpClient(handler), "http://auditor.invalid", "phi4");

            var verdict = await auditor.AuditAsync(Pair(Original[0], "Toast the seeds for 3 to 4 minutes, until they smell nutty."), CancellationToken.None);

            verdict!.Flagged.Should().Equal(2);
            verdict.ByRule.Should().BeFalse();
            handler.Bodies.Should().ContainSingle();
            using var body = JsonDocument.Parse(handler.Bodies[0]);
            body.RootElement.GetProperty("options").GetProperty("seed").GetInt32().Should().Be(ProseLanePrompts.AuditSeed);
            body.RootElement.GetProperty("options").GetProperty("temperature").GetDouble().Should().Be(0);
        }

        [Fact]
        public async Task A_rule_flag_stands_without_a_re_read_and_a_model_flag_stands_once_a_blind_re_read_confirms_a_flagged_step()
        {
            var handler = new Answering(n => n == 1 ? "{\"difference\":\"\",\"wrong\":false}" : "{\"difference\":\"cue left out\",\"wrong\":true}");
            var auditor = new OllamaProseAuditor(new HttpClient(handler), "http://auditor.invalid", "phi4");
            var pair = Pair(Original);

            (await ProseLaneAudit.ConfirmAsync(auditor, pair, new ProseAuditVerdict(new[] { 1 }, true, "step 1: heat"), CancellationToken.None)).Should().BeTrue();
            handler.Bodies.Should().BeEmpty();
            (await ProseLaneAudit.ConfirmAsync(auditor, pair, new ProseAuditVerdict(new[] { 1, 2 }, false, string.Empty), CancellationToken.None)).Should().BeTrue();
            handler.Bodies.Should().HaveCount(2);

            var none = new OllamaProseAuditor(new HttpClient(new Answering(_ => "{\"difference\":\"\",\"wrong\":false}")), "http://auditor.invalid", "phi4");
            (await ProseLaneAudit.ConfirmAsync(none, pair, new ProseAuditVerdict(new[] { 1, 2 }, false, string.Empty), CancellationToken.None)).Should().BeFalse();
        }

        [Fact]
        public async Task An_unreadable_step_answer_makes_the_audit_unreadable()
        {
            var auditor = new OllamaProseAuditor(new HttpClient(new Answering(_ => "{\"verdict\":\"same\"}")), "http://auditor.invalid", "phi4");

            (await auditor.AuditAsync(Pair(Original[0], "Toast the seeds 3 to 4 minutes until golden."), CancellationToken.None)).Should().BeNull();
        }

        private sealed class Answering : HttpMessageHandler
        {
            private readonly System.Func<int, string> _content;

            public Answering(System.Func<int, string> content) => _content = content;

            public List<string> Bodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                var reply = JsonSerializer.Serialize(new { message = new { content = _content(Bodies.Count) } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }
        }
    }
}
