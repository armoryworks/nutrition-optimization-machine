using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The alcohol retention reader against alcohol-retention.tsv: every rule-given row reads its
    /// basis and the factor of its USDA row; the rows the builder judged must too, and are reported
    /// apart. Every kind the reader gives has a rule-given row, and the committed alcohol-retention
    /// record must be what the reader scores now.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenAlcoholRetentionTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenAlcoholRetentionTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_row_reads_its_basis_and_the_factor_of_its_usda_row()
        {
            var cases = ModelLanes.RetentionCases(GoldenSet.Folder).ToDictionary(c => c.Id[("retention:".Length)..]);
            var report = GoldenSet.Run("alcohol-retention.tsv", "status", row =>
            {
                var outcome = cases[row.Id].Outcome();
                return new GoldenOutcome(outcome.Pass, $"{outcome.Expected} ({row["line"]})", outcome.Actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_kind_the_reader_gives_has_a_rule_given_row_and_every_factor_is_a_usda_row()
        {
            var rows = GoldenSet.Read("alcohol-retention.tsv").Rows;
            var kinds = rows.Where(r => r["status"] == GoldenSet.Ok).Select(r => AlcoholRetention.ReadBasis(r["expect"])!.Value.Kind).ToHashSet(StringComparer.Ordinal);

            kinds.Should().BeEquivalentTo(new[]
            {
                AlcoholRetention.NoSteps, AlcoholRetention.NoHeat, AlcoholRetention.Overnight, AlcoholRetention.HotLiquid,
                AlcoholRetention.Flamed, AlcoholRetention.Timed, AlcoholRetention.UpTo,
            });
            var factors = AlcoholRetention.Rows.Select(r => r.Factor).Append(1m).ToHashSet();
            rows.Select(r => decimal.Parse(r["factor"], System.Globalization.CultureInfo.InvariantCulture)).Should().OnlyContain(f => factors.Contains(f));
            rows.Where(r => r["status"] == GoldenSet.Ok).Select(r => r["expect"]).Where(b => b.StartsWith(AlcoholRetention.Timed + ":", StringComparison.Ordinal))
                .Select(b => AlcoholRetention.RowFor(AlcoholRetention.Timed, AlcoholRetention.ReadBasis(b)!.Value.Method, AlcoholRetention.ReadBasis(b)!.Value.Minutes)!.Code)
                .Distinct().Should().BeEquivalentTo(new[] { "5004", "5005", "5006", "5007", "5008", "5009", "5010" }, "every timed row of the table is reached by a rule-given row");
        }

        [Fact]
        public async Task The_committed_alcohol_retention_record_is_what_the_reader_scores_now()
        {
            var record = ModelLanes.Recorded(GoldenSet.Folder, ModelLanes.AlcoholRetentionLane);
            record.Should().NotBeNull("the alcohol-retention lane needs no model host, so its record is always committed");

            var results = await ModelLanes.RunAsync(ModelLanes.AlcoholRetentionLane, GoldenSet.Folder, null!, null!, null, _ => { });
            var score = ModelLanes.Score(ModelLanes.AlcoholRetentionLane, record!.Model, record.Host, AlcoholRetention.Signature, results);
            _output.WriteLine($"alcohol-retention: {score.Passed}/{score.Cases}, false accepts {score.FalseAccepts}/{score.FalseAcceptCases}");

            score.Signature.Should().Be(record.Signature, "a changed rule needs a new record: dotnet run --project nom-api/Nom.Import -- --golden-score --lane alcohol-retention --record");
            score.CaseSet.Should().Be(record.CaseSet);
            (score.Passed, score.FalseAccepts).Should().Be((record.Passed, record.FalseAccepts));
            score.Failed.Should().Equal(record.Failed);
        }
    }
}
