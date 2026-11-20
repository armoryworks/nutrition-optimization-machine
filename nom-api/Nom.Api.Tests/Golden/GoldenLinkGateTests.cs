using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Data.Recipe;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The link gate against its golden files, over the frozen catalog of gate-catalog.tsv:
    /// link-gate.tsv holds rule-given rows, each a pair the gate must let through or a pair a named
    /// rule must hold; wording-pairs.tsv holds the w01 verified wordings, which the gate must let
    /// through, the pairs a second reader refuted and the design's rule cases, which it must hold,
    /// and the claret family the family check must settle on red table wine. The link-gate scorer's
    /// committed record must be what the gate scores now.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLinkGateTests
    {
        private static readonly Lazy<GateCatalog> Catalog = new(() => ModelLanes.GoldenGateCatalog(GoldenSet.Folder));
        private static readonly Lazy<Dictionary<long, GateFood>> Foods = new(() => ModelLanes.GateFoods(GoldenSet.Folder).ToDictionary(f => f.Id));

        private readonly ITestOutputHelper _output;

        public GoldenLinkGateTests(ITestOutputHelper output) => _output = output;

        private static long Status(string status) => status switch
        {
            "curated" => (long)CurationStatusEnum.Curated,
            "rejected" => (long)CurationStatusEnum.Rejected,
            _ => (long)CurationStatusEnum.PendingCuration,
        };

        private static GateFood Food(GoldenRow row) => new(
            long.Parse(row["foodId"], CultureInfo.InvariantCulture),
            row["name"],
            row.Value("description"),
            row.Value("dataType"),
            Status(row["foodStatus"]),
            int.TryParse(row["categoryId"], out var category) ? category : null,
            row.Value("admission"),
            int.TryParse(row["nutrients"], out var rows) ? rows : null,
            row["core"] switch { "yes" => true, "no" => false, _ => null },
            row["deleted"] == "yes");

        private static IReadOnlyList<GateLine>? Lines(string cell) =>
            cell == GoldenTsv.NoAnswer ? null
            : cell.Split(';').Select(l => l.Split('|')).Select(p => new GateLine(
                p[0] == GoldenTsv.NoAnswer ? null : p[0],
                p[1] == GoldenTsv.NoAnswer ? null : p[1],
                decimal.TryParse(p[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var g) ? g : null)).ToList();

        private static GateCatalog CatalogFor(GoldenRow row, GateFood food)
        {
            if (row["bound"] == GoldenTsv.NoAnswer || food.CategoryId is not { } category) return Catalog.Value;
            var bound = decimal.Parse(row["bound"], CultureInfo.InvariantCulture);
            var (matcher, staples) = ModelLanes.UsdaMatcher(GoldenSet.Folder);
            return GateCatalog.Build(Foods.Value.Values, matcher, gramBounds: new Dictionary<int, decimal> { [category] = bound }, staples: staples);
        }

        [Fact]
        public void Each_rule_row_passes_or_is_held_by_its_rule()
        {
            var report = GoldenSet.Run("link-gate.tsv", "status", row =>
            {
                var food = Food(row);
                var pair = new GatePair(row["wording"], 0, food, NameMatch: row["nameMatch"] == "yes", ModelPick: row["modelPick"] == "yes", Lines: Lines(row["lines"]));
                var holds = LinkGate.Check(pair, CatalogFor(row, food));
                var expect = row["expect"];
                var pass = expect == "pass" ? holds.Count == 0 : holds.Any(h => h.Code == expect);
                var actual = holds.Count == 0 ? "pass" : string.Join("; ", holds.Select(h => h.Code + ": " + h));
                return new GoldenOutcome(pass, $"{expect} ({row["wording"]} -> {food.Text})", actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_rule_of_the_gate_has_a_rule_given_row()
        {
            var codes = GoldenSet.Read("link-gate.tsv").Rows.Where(r => r["status"] == GoldenSet.Ok).Select(r => r["expect"]).ToHashSet(StringComparer.Ordinal);
            var rules = new[]
            {
                LinkGate.Heading, LinkGate.Compound, LinkGate.Retired, LinkGate.Branded, LinkGate.Prepared, LinkGate.Uncurated, LinkGate.Incomplete,
                LinkGate.State, LinkGate.WordOrder, LinkGate.RuleOther, LinkGate.Shortlist, LinkGate.LeadNoun, LinkGate.Containment, LinkGate.Form,
                LinkGate.FreshHerb, LinkGate.Part, LinkGate.Marked, LinkGate.Percent, LinkGate.Polarity, LinkGate.Generic, LinkGate.Group,
                LinkGate.Units, LinkGate.Grams,
            };

            rules.Where(r => !codes.Contains(r)).Should().BeEmpty("each class the gate holds has a rule-given golden row");
            codes.Should().Contain("pass");
        }

        [Fact]
        public void Verified_wordings_pass_and_refuted_pairs_are_held()
        {
            var report = GoldenSet.Run("wording-pairs.tsv", "status", row =>
            {
                if (row["expect"] == "~") return null;
                var food = Foods.Value[long.Parse(row["foodId"], CultureInfo.InvariantCulture)];
                var holds = LinkGate.Check(new GatePair(row["wording"], 0, food, ReadersAgreed: true), Catalog.Value);
                var pass = row["expect"] == "=" ? holds.Count == 0 : holds.Count > 0;
                var actual = holds.Count == 0 ? "pass" : string.Join("; ", holds.Select(h => h.Code + ": " + h));
                return new GoldenOutcome(pass, $"{row["expect"]} ({row["wording"]} -> {food.Text})", actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["judgedBy"]));
            _output.WriteLine(report.Summary);
            foreach (var known in report.StillKnown) _output.WriteLine("known: " + known);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void The_family_check_gives_every_family_row_its_food()
        {
            var rows = GoldenSet.Read("wording-pairs.tsv").Rows.Where(r => r["expect"] is "=" or "~").ToList();
            var verdicts = LinkGate.Family(rows.Select(r => (r["wording"], Foods.Value[long.Parse(r["foodId"], CultureInfo.InvariantCulture)])), Catalog.Value);

            var family = rows.Zip(verdicts).Where(x => x.First["expect"] == "~").ToList();
            family.Should().NotBeEmpty();
            family.Where(x => !x.Second.Keeps).Select(x => $"{x.First.Id}: {x.Second.Key} -> {x.Second.Winner}").Should().BeEmpty();
            foreach (var conflict in verdicts.Where(v => !v.Keeps)) _output.WriteLine($"re-pointed or held: {conflict.Wording} #{conflict.TargetId} -> {(conflict.Winner is { } w ? "#" + w : "neither")}");
        }

        [Fact]
        public async Task The_committed_link_gate_record_is_what_the_gate_scores_now()
        {
            var record = ModelLanes.Recorded(GoldenSet.Folder, ModelLanes.LinkGateLane);
            record.Should().NotBeNull("the link-gate lane needs no model host, so its record is always committed");

            var results = await ModelLanes.RunAsync(ModelLanes.LinkGateLane, GoldenSet.Folder, null!, null!, null, _ => { });
            var score = ModelLanes.Score(ModelLanes.LinkGateLane, record!.Model, record.Host, LinkGate.Signature, results);
            _output.WriteLine($"link-gate: {score.Passed}/{score.Cases} agree, {score.Accepted} accepted, {score.Precision:P2} judged right among them, false accepts {score.FalseAccepts}/{score.FalseAcceptCases}");

            score.Signature.Should().Be(record.Signature, "a changed rule table needs a new record: dotnet run --project nom-api/Nom.Import -- --golden-score --lane link-gate --record");
            score.CaseSet.Should().Be(record.CaseSet);
            (score.Passed, score.Accepted, score.FalseAccepts).Should().Be((record.Passed, record.Accepted, record.FalseAccepts),
                "the gate is deterministic, so what it scores now must be what was recorded");
            score.Failed.Should().Equal(record.Failed);
        }
    }
}
