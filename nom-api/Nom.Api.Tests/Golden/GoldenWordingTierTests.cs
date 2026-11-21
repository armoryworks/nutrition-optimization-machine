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
    /// The deterministic wording tiers against their golden files, over the frozen catalog of
    /// gate-catalog.tsv and usda-catalog.tsv: wording-tiers.tsv holds rule-given rows, each a
    /// wording with its lines, the verified rows, trusted aliases and curated head lines it is read
    /// with, its harder key, the tier that must answer first and its food, and the food that may be
    /// written once the gate has read the pair (the lines go to the gate as weighed, since the frozen
    /// catalog holds no conversions). The wording-tiers scorer's committed record (the
    /// tiers on every judged pair of link-pairs.tsv) must be what the tiers score now.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenWordingTierTests
    {
        private const string Separator = " ¦ ";
        private static readonly Lazy<List<GateFood>> Foods = new(() => ModelLanes.GateFoods(GoldenSet.Folder).ToList());
        private static readonly Lazy<(FoodNameMatcher Matcher, Dictionary<string, FoodCandidate> Staples)> Usda = new(() => ModelLanes.UsdaMatcher(GoldenSet.Folder));

        private readonly ITestOutputHelper _output;

        public GoldenWordingTierTests(ITestOutputHelper output) => _output = output;

        private static IEnumerable<string> Cells(string cell) => cell == GoldenTsv.NoAnswer ? Enumerable.Empty<string>() : cell.Split(Separator);

        private static WordingTierCatalog CatalogFor(GoldenRow row)
        {
            var verified = new List<(string, string, long?)>();
            var aliases = new List<(string, long)>();
            foreach (var entry in Cells(row["table"]))
            {
                var (kind, rest) = (entry[..entry.IndexOf(':')], entry[(entry.IndexOf(':') + 1)..]);
                var (name, value) = (rest[..rest.LastIndexOf('=')], rest[(rest.LastIndexOf('=') + 1)..]);
                if (kind == "alias") aliases.Add((name, long.Parse(value, CultureInfo.InvariantCulture)));
                else if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var food)) verified.Add((name, IngredientWordingVerdict.Food, food));
                else verified.Add((name, value, null));
            }
            var heads = Cells(row["curated"]).Select(c => c.Split('|'))
                .SelectMany(p => Enumerable.Repeat(new HeadLine(p[0], p[1], long.Parse(p[2], CultureInfo.InvariantCulture)), int.Parse(p[3], CultureInfo.InvariantCulture)));
            return WordingTierCatalog.Build(Foods.Value, Usda.Value.Matcher, Usda.Value.Staples, aliases, verified, WordingHeadDefaults.Build(heads));
        }

        private static List<TierLine> LinesFor(GoldenRow row)
        {
            var raw = Cells(row["lines"]).Select(c => c.Split('|')).ToList();
            var title = row.Value("title");
            return raw.Select((p, i) => new TierLine(p[0], p[1],
                new AlcoholRecipeContext(title, raw.Where((_, j) => j != i).Select(o => o[0]).ToList(), null, row["site"] == "yes"))).ToList();
        }

        [Fact]
        public void Each_rule_row_gets_its_key_tier_and_food()
        {
            var report = GoldenSet.Run("wording-tiers.tsv", "status", row =>
            {
                var lines = LinesFor(row);
                var decision = WordingTiers.Decide(row["wording"], 0, lines, CatalogFor(row),
                    _ => lines.Count == 0 ? null : lines.Select(l => new GateLine(l.Raw, l.UnitClass, 0m, l.Recipe)).ToList());
                if (decision.Hold != null) _output.WriteLine($"{row.Id}: held: {decision.Hold}");
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["key"] = decision.Key ?? GoldenTsv.NoAnswer,
                    ["tier"] = decision.Answer?.Tier ?? GoldenTsv.NoAnswer,
                    ["answer"] = decision.Answer?.FoodId.ToString(CultureInfo.InvariantCulture) ?? GoldenTsv.NoAnswer,
                    ["expect"] = decision.Writes ? decision.Answer!.FoodId.ToString(CultureInfo.InvariantCulture) : GoldenTsv.NoAnswer,
                });
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_tier_has_a_rule_given_row_that_writes_and_one_that_is_held_or_gives_nothing()
        {
            var rows = GoldenSet.Read("wording-tiers.tsv").Rows.Where(r => r["status"] == GoldenSet.Ok).ToList();
            var writing = rows.Where(r => r["expect"] != GoldenTsv.NoAnswer).Select(r => r["tier"]).ToHashSet(StringComparer.Ordinal);
            var answering = rows.Select(r => r["tier"]).ToHashSet(StringComparer.Ordinal);

            WordingTiers.All.Where(t => !writing.Contains(t)).Should().BeEmpty("each tier has a rule-given row whose answer the gate lets through");
            WordingTiers.All.Where(t => !answering.Contains(t)).Should().BeEmpty("each tier has a rule-given row it answers");
            rows.Should().Contain(r => r["tier"] == GoldenTsv.NoAnswer, "a wording no tier answers is a case too");
        }

        [Fact]
        public async Task The_committed_wording_tiers_record_is_what_the_tiers_score_now()
        {
            var record = ModelLanes.Recorded(GoldenSet.Folder, ModelLanes.WordingTiersLane);
            record.Should().NotBeNull("the wording-tiers lane needs no model host, so its record is always committed");

            var results = await ModelLanes.RunAsync(ModelLanes.WordingTiersLane, GoldenSet.Folder, null!, null!, null, _ => { });
            var score = ModelLanes.Score(ModelLanes.WordingTiersLane, record!.Model, record.Host, WordingTiers.Signature, results);
            _output.WriteLine($"wording-tiers: {score.Passed}/{score.Cases} agree, {score.Accepted} accepted, {score.Precision:P2} judged right among them, false accepts {score.FalseAccepts}/{score.FalseAcceptCases}");

            score.Signature.Should().Be(record.Signature, "a changed tier, gate or alcohol rule needs a new record: dotnet run --project nom-api/Nom.Import -- --golden-score --lane wording-tiers --record");
            score.CaseSet.Should().Be(record.CaseSet);
            (score.Passed, score.Accepted, score.FalseAccepts).Should().Be((record.Passed, record.Accepted, record.FalseAccepts),
                "the tiers are deterministic, so what they score now must be what was recorded");
            score.Failed.Should().Equal(record.Failed);
        }

        [Fact]
        public void The_coverage_and_precision_of_each_tier_on_the_judged_pairs_are_reported()
        {
            var catalog = ModelLanes.GoldenTierCatalog(GoldenSet.Folder);
            var cases = ModelLanes.TierCases(GoldenSet.Folder).ToList();
            var decisions = cases.Select(c => c.Wording).Distinct(StringComparer.Ordinal)
                .ToDictionary(w => w, w => WordingTiers.Decide(w, 0, Array.Empty<TierLine>(), catalog), StringComparer.Ordinal);

            _output.WriteLine($"{decisions.Count} distinct wordings in {cases.Count} judged pairs");
            foreach (var tier in WordingTiers.All.Append(GoldenTsv.NoAnswer))
            {
                var answered = decisions.Values.Where(d => (d.Answer?.Tier ?? GoldenTsv.NoAnswer) == tier).ToList();
                var written = answered.Where(d => d.Writes).Select(d => d.Wording).ToHashSet(StringComparer.Ordinal);
                var judged = cases.Where(c => written.Contains(c.Wording) && decisions[c.Wording].Answer!.FoodId == c.FoodId).ToList();
                _output.WriteLine($"{tier}: answers {answered.Count} wordings ({100.0 * answered.Count / decisions.Count:F1} %), writes {written.Count} " +
                    $"({100.0 * written.Count / decisions.Count:F1} %); of the judged pairs it writes, {judged.Count(c => c.Right)} right and {judged.Count(c => !c.Right)} wrong, near or compound");
            }

            foreach (var rule in decisions.Values.Where(d => d.Answer != null && !d.Writes).SelectMany(d => d.Holds.Select(h => h.Code).Distinct())
                .GroupBy(c => c).OrderByDescending(g => g.Count()).Take(8))
                _output.WriteLine($"held by {rule.Key}: {rule.Count()} wordings");
            foreach (var reason in decisions.Values.Where(d => d.Answer == null).GroupBy(d => d.Abstain ?? WordingTiers.NoTier).OrderByDescending(g => g.Count()).Take(4))
                _output.WriteLine($"no answer, {reason.Key}: {reason.Count()} wordings");

            decisions.Values.Count(d => d.Writes).Should().BeGreaterThan(0);
        }
    }
}
