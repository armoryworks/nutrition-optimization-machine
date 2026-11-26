using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Import.Golden;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The release gate for the model lanes, run in CI with no model host: every lane with a
    /// golden scorer must have a committed score record (when model-lanes.json marks it required)
    /// whose prompt signature and case set match the code and the golden files of this commit, and
    /// whose score reaches the lane's floor. A changed prompt, schema or option, or a changed set,
    /// holds the release until an operator scores the lane again. A lane that was never scored and
    /// is not required is reported as a skipped test, not a pass.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenModelLaneTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenModelLaneTests(ITestOutputHelper output) => _output = output;

        private static IEnumerable<string> CaseIds(string lane) => lane switch
        {
            ModelLanes.LinkPick => ModelLanes.PickCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.LinkVerify => ModelLanes.VerifyCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.LineClassify => ModelLanes.ClassifyCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.LinkGateLane => ModelLanes.GateCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.AlcoholContextLane => ModelLanes.AlcoholCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.AlcoholRetentionLane => ModelLanes.RetentionCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.WordingTiersLane => ModelLanes.TierCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.LineJudge => ModelLanes.JudgeCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.ProseRewriteLane or ModelLanes.ProseJudgeLane or ModelLanes.ProseAuditLane => Nom.Orch.Services.Support.ProseLaneGolden.CaseIds(lane, ModelLanes.ProseCases(GoldenSet.Folder)),
            ModelLanes.WordingReadLane => ModelLanes.ReadCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.WordingLexiconLane => ModelLanes.LexiconCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.AlcoholReadingLane => ModelLanes.AlcoholReadingCases(GoldenSet.Folder).Select(c => c.Id),
            ModelLanes.LinkAuditLane => ModelLanes.AuditCases(GoldenSet.Folder).Select(c => c.Id),
            _ => Enumerable.Empty<string>(),
        };

        [ModelLaneFact(ModelLaneSkip.WhenNoLaneIsChecked)]
        public async Task Every_scored_model_lane_has_a_current_record_at_or_above_its_floor()
        {
            var failures = new List<string>();
            foreach (var (lane, floor) in ModelLanes.Floors(GoldenSet.Folder))
            {
                if (!floor.Scorer)
                {
                    _output.WriteLine($"{lane}: no golden scorer ({floor.Note})");
                    continue;
                }
                if (!ModelLanes.Scored.Contains(lane))
                {
                    failures.Add($"{lane}: model-lanes.json marks it scored but no scorer exists");
                    continue;
                }

                var signature = await ModelLanes.SignatureAsync(lane);
                var cases = CaseIds(lane).ToList();
                var record = ModelLanes.Recorded(GoldenSet.Folder, lane);
                var rescore = $"dotnet run --project nom-api/Nom.Import -- --golden-score --lane {lane} --host <model host> --model <production model> --record";
                if (record == null)
                {
                    var line = $"{lane}: never scored (signature {signature}, {cases.Count} cases); score it with: {rescore}";
                    if (floor.Required) failures.Add(line);
                    else _output.WriteLine(line + " (not required yet)");
                    continue;
                }

                _output.WriteLine($"{lane}: {record.Model} scored {record.Score:P1} ({record.Passed}/{record.Cases}), false accepts {record.FalseAcceptRate:P1}, on {record.ScoredAtUtc:u}");
                if (record.Signature != signature)
                    failures.Add($"{lane}: the prompt signature changed from {record.Signature} to {signature} since it was scored; score it again with: {rescore}");
                if (record.CaseSet != ModelLanes.CaseSet(cases))
                    failures.Add($"{lane}: the golden cases changed since it was scored ({record.Cases} then, {cases.Count} now); score it again with: {rescore}");
                foreach (var breach in ModelLanes.Breaches(record, floor))
                    failures.Add($"{breach}; failed cases: {string.Join(", ", record.Failed.Take(50))}{(record.Failed.Count > 50 ? $" and {record.Failed.Count - 50} more" : string.Empty)}");
            }

            failures.Should().BeEmpty("a model lane below its floor, or scored against another prompt or set, holds the release");
        }

        [ModelLaneFact(ModelLaneSkip.WhenAnyLaneIsUnscored)]
        public void Every_model_lane_with_a_scorer_has_been_scored()
        {
            ModelLanes.Unscored(GoldenSet.Folder).Should().BeEmpty();
        }

        [Fact]
        public void The_gate_names_every_unscored_lane_and_checks_nothing_while_none_is_scored()
        {
            var folder = Path.Combine(Path.GetTempPath(), "lanes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, ModelLanes.ScoresFolder));
            try
            {
                File.WriteAllText(Path.Combine(folder, ModelLanes.FloorsFile),
                    "{\"link-pick\": {\"scorer\": true}, \"link-verify\": {\"scorer\": true}, \"repair\": {\"scorer\": false}}");

                ModelLaneGate.NothingChecked(folder).Should().Contain("link-pick, link-verify");
                ModelLaneGate.NotArmed(folder).Should().Contain("link-pick, link-verify").And.NotContain("repair");

                File.WriteAllText(ModelLanes.RecordPath(folder, "link-pick"), "{}");
                ModelLaneGate.NothingChecked(folder).Should().BeNull();
                ModelLaneGate.NotArmed(folder).Should().Contain("link-verify").And.NotContain("link-pick");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void The_committed_lanes_are_reported_unscored_until_a_record_is_committed()
        {
            var unscored = ModelLanes.Unscored(GoldenSet.Folder);
            var records = ModelLanes.Scored.Where(l => ModelLanes.Recorded(GoldenSet.Folder, l) != null).ToList();

            unscored.Should().BeEquivalentTo(ModelLanes.Scored.Except(records).Where(l => !ModelLanes.Floors(GoldenSet.Folder)[l].Required));
            if (unscored.Count > 0) ModelLaneGate.NotArmed(GoldenSet.Folder).Should().NotBeNull();
        }

        [Fact]
        public async Task A_lane_signature_ignores_the_model_name_and_differs_between_lanes()
        {
            var signatures = new List<string>();
            foreach (var lane in ModelLanes.Scored) signatures.Add(await ModelLanes.SignatureAsync(lane));

            signatures.Should().OnlyHaveUniqueItems();
            (await ModelLanes.SignatureAsync(ModelLanes.LinkVerify)).Should().Be(signatures[1]);
        }

        [Fact]
        public async Task The_line_judge_rows_split_into_two_fixed_halves_that_keep_both_cases_of_a_row_together()
        {
            var ids = ModelLanes.JudgeCases(GoldenSet.Folder).Select(c => c.Id).ToList();
            var working = ids.Where(id => ModelLanes.InSplit(id, ModelLanes.SplitWorking)).ToList();
            var heldOut = ids.Where(id => ModelLanes.InSplit(id, ModelLanes.SplitHeldOut)).ToList();

            working.Should().NotBeEmpty();
            heldOut.Should().NotBeEmpty();
            working.Concat(heldOut).Should().BeEquivalentTo(ids);
            working.Select(ModelLanes.SplitRow).Should().NotIntersectWith(heldOut.Select(ModelLanes.SplitRow));
            ids.Should().OnlyContain(id => ModelLanes.InSplit(id, ModelLanes.SplitAll));
            var other = () => ModelLanes.RunAsync(ModelLanes.LineClassify, GoldenSet.Folder, null!, null!, null, _ => { }, split: ModelLanes.SplitWorking);
            await other.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task The_link_pick_scorer_answers_by_rule_what_the_lane_answers_by_rule_and_asks_the_model_only_the_rest_of_its_half()
        {
            var handler = new NonePickHandler();
            using var http = new HttpClient(handler);
            var (_, staples) = ModelLanes.UsdaMatcher(GoldenSet.Folder);
            var cases = ModelLanes.PickCases(GoldenSet.Folder).Where(c => ModelLanes.InSplit(c.Id, ModelLanes.SplitWorking)).ToList();
            var none = cases.Where(c => Nom.Orch.Services.Support.LinkPickRules.RuleNone(c.Name) != null).Select(c => c.Id).ToHashSet();
            var staple = cases.Where(c => !none.Contains(c.Id) && Nom.Orch.Services.Support.LinkPickRules.RuleTarget(c.Name, staples) != null).Select(c => c.Id).ToHashSet();

            var results = await ModelLanes.RunAsync(ModelLanes.LinkPick, GoldenSet.Folder, http, ModelLanes.Config("http://model.invalid", "m"), null, _ => { }, split: ModelLanes.SplitWorking);

            none.Should().NotBeEmpty();
            staple.Should().NotBeEmpty();
            results.Select(r => r.Id).Should().BeEquivalentTo(cases.Select(c => c.Id));
            results.Where(r => none.Contains(r.Id)).Should().OnlyContain(r => r.Actual == "none");
            results.Where(r => staple.Contains(r.Id)).Should().OnlyContain(r => r.Actual != "none");
            handler.Asked.Should().HaveCount(cases.Count - none.Count - staple.Count);
            handler.Asked.Should().NotContain(name => none.Contains("pick:" + name) || staple.Contains("pick:" + name));
        }

        [Fact]
        public async Task The_link_verify_scorer_answers_by_rule_what_the_lane_answers_by_rule_and_asks_the_model_only_the_rest_of_its_half()
        {
            var handler = new SameVerifyHandler();
            using var http = new HttpClient(handler);
            var cases = ModelLanes.VerifyCases(GoldenSet.Folder).Where(c => ModelLanes.InSplit(c.Id, ModelLanes.SplitWorking)).ToList();
            var several = cases.Where(c => Nom.Orch.Services.Support.LinkVerifyRules.Before(c.Wording) != null).Select(c => c.Id).ToHashSet();
            var refused = cases.Where(c => !several.Contains(c.Id) && Nom.Orch.Services.Support.LinkVerifyRules.After(c.Wording, c.Description, Nom.Orch.Interfaces.LinkJudgement.Same) != Nom.Orch.Interfaces.LinkJudgement.Same).ToDictionary(c => c.Id, c => Nom.Orch.Services.Support.LinkVerifyRules.After(c.Wording, c.Description, Nom.Orch.Interfaces.LinkJudgement.Same).ToString());

            var results = await ModelLanes.RunAsync(ModelLanes.LinkVerify, GoldenSet.Folder, http, ModelLanes.Config("http://model.invalid", "m"), null, _ => { }, split: ModelLanes.SplitWorking);

            several.Should().NotBeEmpty();
            refused.Should().NotBeEmpty();
            results.Select(r => r.Id).Should().BeEquivalentTo(cases.Select(c => c.Id));
            results.Where(r => several.Contains(r.Id)).Should().OnlyContain(r => r.Actual == nameof(Nom.Orch.Interfaces.LinkJudgement.MoreThanOneFood));
            results.Where(r => refused.ContainsKey(r.Id)).Should().OnlyContain(r => r.Actual == refused[r.Id]);
            results.Where(r => !several.Contains(r.Id) && !refused.ContainsKey(r.Id)).Should().OnlyContain(r => r.Actual == nameof(Nom.Orch.Interfaces.LinkJudgement.Same));
            handler.Asked.Should().HaveCount(cases.Count - several.Count);
            handler.Calls.Should().OnlyContain(n => n <= Nom.Orch.Services.IngredientLinkService.PairsPerVerifyCall, "the scorer asks as many pairs to a call as the lane");
            ModelLanes.VerifyCases(GoldenSet.Folder).Should().NotContain(c => c.Wording == "teaspoonful of salt", "the judge is asked the wording as the lane normalises it");
        }

        private sealed class SameVerifyHandler : HttpMessageHandler
        {
            public List<string> Asked { get; } = new();
            public List<int> Calls { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var messages = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("messages");
                var user = messages.EnumerateArray().Last().GetProperty("content").GetString()!;
                var names = System.Text.RegularExpressions.Regex.Matches(user, "^  ingredient: \"(.*)\"$", System.Text.RegularExpressions.RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
                Asked.AddRange(names);
                Calls.Add(names.Count);
                var results = string.Join(",", names.Select((_, i) => $"{{\"n\":{i + 1},\"verdict\":\"same\"}}"));
                var content = JsonSerializer.Serialize(new { message = new { content = $"{{\"results\":[{results}]}}" } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            }
        }

        private sealed class NonePickHandler : HttpMessageHandler
        {
            public List<string> Asked { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var prompt = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("prompt").GetString()!;
                var names = System.Text.RegularExpressions.Regex.Matches(prompt, "^#\\d+ \"(.*)\"$", System.Text.RegularExpressions.RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
                Asked.AddRange(names);
                var answers = string.Join(",", names.Select((_, i) => $"{{\"n\":{i + 1},\"food\":\"x\",\"pick\":0,\"confidence\":0.5}}"));
                var content = JsonSerializer.Serialize(new { response = $"{{\"answers\":[{answers}]}}" });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            }
        }

        [Fact]
        public async Task A_call_the_host_does_not_answer_in_time_is_asked_again_instead_of_ending_the_run()
        {
            var handler = new SlowOnceHandler();
            using var http = new HttpClient(handler);

            var results = await ModelLanes.RunAsync(ModelLanes.LinkVerify, GoldenSet.Folder, http, ModelLanes.Config("http://model.invalid", "m"), ModelLanes.VerifyBatch, _ => { });

            results.Should().HaveCount(ModelLanes.VerifyBatch).And.OnlyContain(r => r.Actual == "Same");
            handler.Calls.Should().Be(2);
        }

        [Fact]
        public async Task A_call_that_keeps_timing_out_still_ends_the_run()
        {
            var handler = new SlowOnceHandler(timeouts: ModelLanes.CallAttempts);
            using var http = new HttpClient(handler);

            var run = () => ModelLanes.RunAsync(ModelLanes.LinkVerify, GoldenSet.Folder, http, ModelLanes.Config("http://model.invalid", "m"), ModelLanes.VerifyBatch, _ => { });

            await run.Should().ThrowAsync<TaskCanceledException>();
            handler.Calls.Should().Be(ModelLanes.CallAttempts);
        }

        private sealed class SlowOnceHandler : HttpMessageHandler
        {
            private readonly int _timeouts;

            public SlowOnceHandler(int timeouts = 1) => _timeouts = timeouts;

            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                if (Calls <= _timeouts)
                    throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
                var results = string.Join(",", Enumerable.Range(1, ModelLanes.VerifyBatch).Select(n => $"{{\"n\":{n},\"verdict\":\"same\"}}"));
                var content = JsonSerializer.Serialize(new { message = new { content = $"{{\"results\":[{results}]}}" } });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
            }
        }
    }
}
