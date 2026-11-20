using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The rewrite fidelity check on public-domain recipes of prose.tsv, each turned into a
    /// rewrite whose fault is known by construction: the source given back unchanged, the source
    /// without a step that alone states a number, and the source with a number it never states.
    /// The check must fail each with at least the reason the construction gives. Prose check v2
    /// runs on the same rows and on constructed recipes and amounts, with the stock phrases of
    /// prose-stock.tsv: a fault row must fail with its reason, a faithful row must pass, and an
    /// equal-form row must give no amount reason. No fault row may pass, and at most 5 % of the
    /// faithful rows may fail.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseTests
    {
        public const decimal MaximumFalseRejectRate = 0.05m;

        private readonly ITestOutputHelper _output;

        public GoldenProseTests(ITestOutputHelper output) => _output = output;

        private static RewriteFidelity.SourceText Source(GoldenRow row) =>
            new(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty);

        private static RewriteFidelity.SourceText Rewrite(RewriteFidelity.SourceText source, string variant, GoldenRow row)
        {
            if (variant == "identical") return source;
            if (variant == "given")
                return new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty);
            if (variant == "add-number")
                return source with { Steps = source.Steps.Take(source.Steps.Count - 1).Append(source.Steps[^1] + " Rest it 437 minutes.").ToList() };
            var drop = int.Parse(variant["drop-step-".Length..], CultureInfo.InvariantCulture) - 1;
            return source with { Steps = source.Steps.Where((_, i) => i != drop).ToList() };
        }

        private static ProseStockIndex Stock() =>
            new(1, GoldenSet.Read("prose-stock.tsv").Rows
                .Where(r => int.Parse(r["sites"], CultureInfo.InvariantCulture) >= ProseStockIndex.StockSites)
                .Select(r => ProsePhrases.Hash(r.Id)));

        private static ProseCheck.Finding CheckV2(GoldenRow row, ProseStockIndex stock)
        {
            var source = Source(row);
            var lines = row.Value("lines")?.Split(" ¦ ");
            return ProseCheck.Check(source, Rewrite(source, row["variant"], row), lines, row["stock"] == "none" ? ProseStockIndex.Empty : stock);
        }

        private static bool Meets(string expect, ProseCheck.Finding finding) => expect switch
        {
            "passed" => finding.Passed,
            "!quantity" => !finding.Reasons.Intersect(ProseCheck.QuantityReasons).Any(),
            _ when expect.StartsWith('!') => !finding.Reasons.Contains(expect[1..]),
            _ => finding.Reasons.Contains(expect),
        };

        [Fact]
        public void Each_constructed_rewrite_fails_the_fidelity_check_for_its_known_fault()
        {
            var report = GoldenSet.Run("prose.tsv", "status", row =>
            {
                var source = Source(row);
                var finding = RewriteFidelity.Check(source, Rewrite(source, row["variant"], row));
                var reasons = finding.Reasons;
                var pass = row["expect"] == "passed" ? finding.Passed : reasons.Contains(row["expect"]);
                return new GoldenOutcome(pass, row["expect"], finding.ToString(),
                    new Dictionary<string, string> { ["expect"] = finding.Passed ? "passed" : reasons.FirstOrDefault() ?? GoldenTsv.NoAnswer });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Each_rewrite_gets_its_check_v2_outcome()
        {
            var stock = Stock();
            var report = GoldenSet.Run("prose.tsv", "status2", row =>
            {
                var finding = CheckV2(row, stock);
                return new GoldenOutcome(Meets(row["check2"], finding), row["check2"], finding.ToString(),
                    new Dictionary<string, string> { ["check2"] = finding.Passed ? "passed" : finding.Reasons[0] });
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Check_v2_lets_no_constructed_fault_through_and_rejects_at_most_5_percent_of_faithful_rewrites()
        {
            var stock = Stock();
            var rows = GoldenSet.Read("prose.tsv").Rows.Where(r => r["status2"] is GoldenSet.Ok or GoldenSet.Agent or GoldenSet.Known).ToList();
            var faults = rows.Where(r => r["check2"] != "passed" && !r["check2"].StartsWith('!')).ToList();
            var faithful = rows.Where(r => r["check2"] == "passed").ToList();

            var falseAccepts = faults.Where(r => CheckV2(r, stock).Passed).Select(r => r.Id).ToList();
            var falseRejects = faithful.Where(r => !CheckV2(r, stock).Passed).Select(r => r.Id).ToList();
            _output.WriteLine($"check v{ProseCheck.Version}: {falseAccepts.Count} false accepts of {faults.Count} fault rows, {falseRejects.Count} false rejects of {faithful.Count} faithful rows");

            faults.Should().NotBeEmpty();
            faithful.Should().NotBeEmpty();
            falseAccepts.Should().BeEmpty("check v2 may accept no rewrite with a constructed fault");
            ((decimal)falseRejects.Count / faithful.Count).Should().BeLessThanOrEqualTo(MaximumFalseRejectRate,
                $"check v2 rejects at most 5 % of faithful rewrites; it rejected {string.Join(", ", falseRejects)}");
        }

        [Fact]
        public void Every_rule_of_check_v2_has_a_constructed_fault_row()
        {
            var expected = GoldenSet.Read("prose.tsv").Rows
                .Where(r => r["status2"] == GoldenSet.Ok && r["source"] is "rule:construction" or "rule:rewrite-fidelity")
                .Select(r => r["check2"])
                .ToHashSet(StringComparer.Ordinal);

            expected.Should().Contain(new[]
            {
                ProseCheck.WrongAmount, ProseCheck.ChangedUnit, ProseCheck.ChangedRange, ProseCheck.LostQuantity, ProseCheck.AddedQuantity,
                ProseCheck.LostStep, ProseCheck.OutOfOrder, ProseCheck.TooShort, ProseCheck.CopiedText, ProseCheck.Unchanged,
                ProseCheck.StepMarker, ProseCheck.EmptyStep, "!quantity",
            });
        }

        [Fact]
        public void The_verb_lexicon_compiled_into_the_check_is_prose_verbs_tsv()
        {
            var golden = ProseVerbLexicon.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(GoldenSet.Folder, "prose-verbs.tsv")));
            var groups = GoldenSet.Read("prose-verbs.tsv").Rows.Select(r => r.Id).ToList();

            ProseVerbLexicon.Default.Groups.Should().BeEquivalentTo(golden.Groups);
            golden.Groups.Should().BeEquivalentTo(groups);
            groups.Should().Contain(new[] { "heat", "mix", "bake", "boil", "cut", "add" });
        }
    }
}
