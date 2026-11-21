using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The prose check on the 80 recipes read against their source at staging checkpoint 2
    /// (prose-check.tsv): 40 the re-check failed and 40 it passed, each kept as the short source and
    /// rewrite excerpts its verdict needs, with the verdict v2 gave on staging. No copied recipe may
    /// pass, and at most 1 in 20 of the check's failures may be false. The 15 verdicts v2 got wrong
    /// each name the rule behind the error.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseCheckTests
    {
        public const string File = "prose-check.tsv";
        public const decimal MaximumFalseFailureRate = 0.05m;

        private static readonly string[] CopyReasons = { ProseCheck.CopiedText, ProseCheck.Unchanged };

        private readonly ITestOutputHelper _output;

        public GoldenProseCheckTests(ITestOutputHelper output) => _output = output;

        private static ProseCheck.Finding Check(GoldenRow row) =>
            ProseCheck.Check(
                new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                row.Value("lines")?.Split(" ¦ "));

        private static bool Meets(GoldenRow row, ProseCheck.Finding finding) =>
            row["expect"] == "passed" ? finding.Passed : finding.Reasons.Contains(row["expect"]);

        [Fact]
        public void Each_read_recipe_gets_its_verdict()
        {
            var report = GoldenSet.Run(File, "status", row =>
            {
                var finding = Check(row);
                return new GoldenOutcome(Meets(row, finding), row["expect"], finding.ToString(),
                    new Dictionary<string, string> { ["expect"] = finding.Passed ? "passed" : finding.Reasons[0] });
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void No_copied_recipe_passes_and_at_most_1_in_20_failures_is_false()
        {
            var rows = GoldenSet.Read(File).Rows.Where(r => r["status"] is GoldenSet.Ok or GoldenSet.Agent or GoldenSet.Known).ToList();
            var copies = rows.Where(r => CopyReasons.Contains(r["expect"])).ToList();
            var failures = rows.Where(r => !Check(r).Passed).ToList();
            var falseFailures = failures.Where(r => r["expect"] == "passed").Select(r => r.Id).ToList();
            var copiesPassed = copies.Where(r => Check(r).Passed).Select(r => r.Id).ToList();
            var v2Failures = rows.Where(r => r["v2"] != "passed").ToList();
            var v2CopiesPassed = copies.Count(r => r["v2"] == "passed");
            _output.WriteLine($"check v{ProseCheck.Version}: {falseFailures.Count} false of {failures.Count} failures, {copiesPassed.Count} of {copies.Count} copies passed");
            _output.WriteLine($"check v2 on staging: {v2Failures.Count(r => r["expect"] == "passed")} false of {v2Failures.Count} failures, {v2CopiesPassed} of {copies.Count} copies passed");

            copies.Should().NotBeEmpty();
            failures.Should().NotBeEmpty();
            copiesPassed.Should().BeEmpty("a copied recipe must never pass the prose check");
            ((decimal)falseFailures.Count / failures.Count).Should().BeLessThanOrEqualTo(MaximumFalseFailureRate,
                $"at most 1 in 20 failures may be false; these faithful rewrites failed: {string.Join(", ", falseFailures)}");
        }

        [Fact]
        public void Each_verdict_v2_got_wrong_names_its_rule_and_the_check_gets_it_right()
        {
            var wrong = GoldenSet.Read(File).Rows.Where(r => r["verdict"] is "failure wrong" or "copy").ToList();

            wrong.Should().HaveCount(15, "checkpoint 2 read 9 false failures and 6 copies that passed");
            wrong.Where(r => r.Value("rule") == null).Select(r => r.Id).Should().BeEmpty("each wrong verdict names the rule behind it");
            wrong.Where(r => !Meets(r, Check(r))).Select(r => r.Id).Should().BeEmpty();
        }

        [Fact]
        public void No_verdict_depends_on_the_stock_index()
        {
            var changed = new List<string>();
            foreach (var row in GoldenSet.Read(File).Rows)
            {
                var texts = row["steps"].Split(" ¦ ").Concat(row["rewriteSteps"].Split(" ¦ "))
                    .Append(row.Value("description") ?? string.Empty).Append(row.Value("rewriteDescription") ?? string.Empty)
                    .Select(ProseQuantities.Prepare);
                var every = new ProseStockIndex(1, texts.SelectMany(t => ProsePhrases.Hashes(ProseCheck.CopyTokens(t)).Concat(ProsePhrases.Hashes(ProsePhrases.WordsOfPrepared(t)))));
                var stocked = ProseCheck.Check(
                    new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                    new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                    row.Value("lines")?.Split(" ¦ "), every);
                if (!stocked.Faults.SequenceEqual(Check(row).Faults)) changed.Add(row.Id);
            }

            changed.Should().BeEmpty("the copy rules count stock phrases like any other words, so the bars measured here hold for the stock index the re-check runs with");
        }
    }
}
