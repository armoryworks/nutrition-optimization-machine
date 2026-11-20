using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Import.Golden;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The judged moves of links.tsv against the relink rule, over the frozen curated catalog:
    /// <c>=Food</c> the rule must move the line there, <c>~Food</c> there or nowhere, <c>stay</c>
    /// nowhere, <c>!Food</c> anywhere but there. And the judged wording → USDA pairs of
    /// link-pairs.tsv against the deterministic link rules (staple names and the name matcher)
    /// over the frozen USDA catalog: a pair judged right is the rule's answer or the rule gives
    /// none, a pair judged wrong, near or compound is never the rule's answer.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLinkTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenLinkTests(ITestOutputHelper output) => _output = output;

        private static (bool Pass, string Actual) Judge(string expect, long? target)
        {
            var catalog = GoldenCatalog.Instance;
            var actual = target is { } id ? (catalog.Names.TryGetValue(id, out var name) ? name : $"#{id}") : "stay";
            if (expect == "stay") return (target == null, actual);
            var food = expect[1..];
            var expected = catalog.Targets.TryGetValue(food, out var foodId) ? foodId : (long?)null;
            return expect[0] switch
            {
                '=' => (expected != null && target == expected, actual),
                '~' => (target == null || target == expected, actual),
                '!' => (expected == null || target != expected, actual),
                _ => (false, $"unreadable expectation '{expect}'"),
            };
        }

        [Fact]
        public void A_line_goes_to_its_reviewed_food_or_stays()
        {
            var targets = GoldenCatalog.Instance.Targets;
            var report = GoldenSet.Run("links.tsv", "status", row =>
            {
                var (_, target) = LineRelinkService.Decide(row["raw"], long.Parse(row["onId"]), row["onFood"], targets);
                var (pass, actual) = Judge(row["expect"], target);
                return new GoldenOutcome(pass, $"{row["expect"]} from {row["onFood"]}", actual, new Dictionary<string, string> { ["expect"] = target == null ? "stay" : "=" + actual });
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void A_line_already_on_its_reviewed_food_is_not_moved()
        {
            var catalog = GoldenCatalog.Instance;
            var report = GoldenSet.Run("links.tsv", "stays", row =>
            {
                if (row["expect"].Length < 2 || row["expect"][0] is not ('=' or '~')) return null;
                var food = row["expect"][1..];
                if (!catalog.Targets.TryGetValue(food, out var foodId)) return new GoldenOutcome(false, $"stays on {food}", "food not in catalog.tsv", new Dictionary<string, string>());
                var (_, target) = LineRelinkService.Decide(row["raw"], foodId, catalog.Names[foodId], catalog.Targets);
                var actual = target is { } id ? (catalog.Names.TryGetValue(id, out var name) ? name : $"#{id}") : "stay";
                return new GoldenOutcome(target == null, $"stays on {food}", actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void The_deterministic_link_rules_never_give_a_wording_a_food_judged_wrong_for_it()
        {
            var (matcher, staples) = ModelLanes.UsdaMatcher(GoldenSet.Folder);
            var answered = new List<(string Verdict, bool NameMatch)>();
            var report = GoldenSet.Run("link-pairs.tsv", "status", row =>
            {
                var rule = row["rule"];
                if (rule.Length < 2 || rule == GoldenTsv.NotJudged) return null;
                var foodId = long.Parse(rule[1..]);
                var hit = IngredientLinkService.DeterministicTarget(IngredientNameNormalizer.Normalize(row["wording"]), matcher, staples);
                var target = hit?.Food.IngredientId;
                if (target == foodId) answered.Add((row["verdict"], hit!.Value.Source == IngredientLinkService.ExactSource));
                var pass = rule[0] == '~' ? target == null || target == foodId : target != foodId;
                var actual = hit == null ? "no rule answer" : $"{hit.Value.Food.IngredientId} {hit.Value.Food.Name} ({hit.Value.Source})";
                return new GoldenOutcome(pass, $"{rule} ({row["verdict"]}: {row["wording"]} -> {row["description"]})", actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["judgedBy"]));

            foreach (var group in answered.GroupBy(a => a.NameMatch ? "name match" : "staple"))
                _output.WriteLine($"{group.Key}: {group.Count(a => a.Verdict == "ok")}/{group.Count()} judged pairs it answers are judged right ({100.0 * group.Count(a => a.Verdict == "ok") / group.Count():F1} %)");
            _output.WriteLine(report.Summary);
            foreach (var known in report.StillKnown) _output.WriteLine("known: " + known);
            GoldenSet.Assert(report);
        }
    }
}
