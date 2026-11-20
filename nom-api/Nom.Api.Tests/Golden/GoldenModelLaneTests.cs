using System.Collections.Generic;
using System.Linq;
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
    /// holds the release until an operator scores the lane again.
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
            _ => Enumerable.Empty<string>(),
        };

        [Fact]
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

        [Fact]
        public async Task A_lane_signature_ignores_the_model_name_and_differs_between_lanes()
        {
            var signatures = new List<string>();
            foreach (var lane in ModelLanes.Scored) signatures.Add(await ModelLanes.SignatureAsync(lane));

            signatures.Should().OnlyHaveUniqueItems();
            (await ModelLanes.SignatureAsync(ModelLanes.LinkVerify)).Should().Be(signatures[1]);
        }
    }
}
