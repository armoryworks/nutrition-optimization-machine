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
    /// The prose check on the 320 recipes read against their source at staging checkpoints 2 to 5
    /// (prose-check.tsv): at checkpoints 2 and 3, 40 the re-check failed and 40 it passed, at
    /// checkpoint 4, 40 it failed, at checkpoint 5, 40 it failed, 40 it passed and 40 it was unsure of,
    /// kept as their whole stored texts, masked, with the verdict the check gave on staging and the
    /// copy judgement of each (<c>copy</c>: copy, doubtful or not). An <c>expect</c> of <c>passed</c> is
    /// met by a rewrite the check does not fail or only withholds as copy-unsure (the price of the copy
    /// rule's band), <c>unsure</c> by one it does not pass clean, <c>copied-text</c> and
    /// <c>copy-unsure</c> by one the copy rule withholds (copied or copy-unsure), and a fact reason by a
    /// finding with that reason that fails the rewrite, or only marks it unsure while its class is not
    /// proven. No recipe judged a copy or doubtful may pass the copy rule, the copy rule's two lines sit
    /// where the judged rows put them, no recipe judged not a copy is called copied, and at most 1 in
    /// 20 failed or copied verdicts may be false, on all the rows and on the rows the checkpoint
    /// operators read alone, with the committed proven classes (with every fact class taken as proven
    /// the figures are shown but not held: the judged firings prove no class). The verdicts the checks
    /// got wrong on staging each name the rule behind the error.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseCheckTests
    {
        public const string File = "prose-check.tsv";
        public const decimal MaximumFalseFailureRate = 0.05m;
        public const string OperatorSource = "agent-operator-";
        public const string Copy = "copy";
        public const string Doubtful = "doubtful";
        public const string NotCopy = "not";

        private readonly ITestOutputHelper _output;

        public GoldenProseCheckTests(ITestOutputHelper output) => _output = output;

        private static ProseCheck.Finding Check(GoldenRow row) =>
            ProseCheck.Check(
                new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                row.Value("lines")?.Split(" ¦ "));

        private static bool Meets(GoldenRow row, ProseCheck.Finding finding) => row["expect"] switch
        {
            "passed" => !finding.Failed || finding.CopyUnsure,
            "unsure" => !finding.Passed,
            ProseCheck.CopiedText or ProseCheck.CopyUnsure => WithheldAsCopy(finding),
            var reason => finding.Reasons.Contains(reason) && (finding.Failed || (ProseCheck.FactClasses.Contains(reason) && !finding.Proven.Contains(reason))),
        };

        private static bool WithheldAsCopy(ProseCheck.Finding finding) => finding.Failed && finding.Reasons.Any(ProseCheck.CopyReasons.Contains);

        private static bool IsCopy(GoldenRow row) => row["copy"] is Copy or Doubtful;

        private static bool FalseVerdict(GoldenRow row, ProseCheck.Finding finding) =>
            row["copy"] == NotCopy && (finding.Copied || (finding.Failed && !finding.CopyUnsure && row["expect"] == "passed"));

        private static List<GoldenRow> Judged() =>
            GoldenSet.Read(File).Rows.Where(r => r["status"] is GoldenSet.Ok or GoldenSet.Agent or GoldenSet.Known).ToList();

        [Fact]
        public void Each_read_recipe_gets_its_verdict()
        {
            var report = GoldenSet.Run(File, "status", row =>
            {
                var finding = Check(row);
                return new GoldenOutcome(Meets(row, finding), row["expect"], finding.ToString(),
                    new Dictionary<string, string> { ["expect"] = finding.Failed ? finding.FailReasons[0] : finding.Outcome });
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void No_copied_recipe_passes_and_at_most_1_in_20_failures_is_false()
        {
            var rows = Judged();
            var findings = rows.ToDictionary(r => r.Id, Check);
            foreach (var set in rows.GroupBy(r => r["set"]))
                _output.WriteLine($"  {set.Key}: {string.Join(", ", set.GroupBy(r => findings[r.Id].Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))}");

            AssertBars("all read recipes", rows, findings);
            AssertBars("recipes the checkpoint operators read", rows.Where(ReadByOperator).ToList(), findings);
            using (ProseCheck.AssumeProven(ProseCheck.FactClasses.ToArray()))
            {
                var proven = rows.ToDictionary(r => r.Id, Check);
                AssertBars("all read recipes, every fact class proven (shown, not a bar: the judged firings prove no class)", rows, proven, falseVerdictBar: false);
                AssertBars("recipes the checkpoint operators read, every fact class proven (shown, not a bar)", rows.Where(ReadByOperator).ToList(), proven, falseVerdictBar: false);
            }
        }

        private static bool ReadByOperator(GoldenRow row) => row["source"].StartsWith(OperatorSource, System.StringComparison.Ordinal);

        private void AssertBars(string label, List<GoldenRow> rows, Dictionary<string, ProseCheck.Finding> findings, bool falseVerdictBar = true)
        {
            var copies = rows.Where(IsCopy).ToList();
            var notCopies = rows.Where(r => r["copy"] == NotCopy).ToList();
            var verdicts = rows.Where(r => findings[r.Id].Failed && !findings[r.Id].CopyUnsure).ToList();
            var falseVerdicts = verdicts.Where(r => FalseVerdict(r, findings[r.Id])).Select(r => r.Id).ToList();
            var copiesPassed = copies.Where(r => !WithheldAsCopy(findings[r.Id])).Select(r => r.Id).ToList();
            var price = notCopies.Count(r => findings[r.Id].Reasons.Contains(ProseCheck.CopyUnsure));
            var outcomes = rows.GroupBy(r => findings[r.Id].Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
            _output.WriteLine($"check v{ProseCheck.Version}, {label} ({rows.Count}): {falseVerdicts.Count} false of {verdicts.Count} failed or copied verdicts, {copiesPassed.Count} of {copies.Count} copies and doubtful passed the copy rule, {price} of {notCopies.Count} judged not copies withheld as copy-unsure; {string.Join(", ", outcomes)}");

            copies.Should().NotBeEmpty();
            verdicts.Should().NotBeEmpty();
            copiesPassed.Should().BeEmpty($"no recipe judged a copy or doubtful may pass the copy rule ({label})");
            notCopies.Where(r => findings[r.Id].Copied).Select(r => r.Id).Should().BeEmpty($"no recipe judged not a copy may be called copied ({label})");
            if (!falseVerdictBar) return;
            ((decimal)falseVerdicts.Count / verdicts.Count).Should().BeLessThanOrEqualTo(MaximumFalseFailureRate,
                $"at most 1 in 20 failed or copied verdicts may be false ({label}); these were: {string.Join(", ", falseVerdicts)}");
        }

        [Fact]
        public void The_two_copy_lines_sit_where_the_judged_rows_put_them()
        {
            var rows = Judged();
            rows.Select(r => r["copy"]).Should().OnlyContain(c => c == Copy || c == Doubtful || c == NotCopy, "every read recipe carries its copy judgement");
            var measures = rows.ToDictionary(r => r.Id, r => Check(r).Measures);
            var high = ProseCheck.CopiedLimits;
            var low = ProseCheck.CopyUnsureLimits;
            var below = rows.Where(r => r["copy"] is NotCopy or Doubtful).Select(r => measures[r.Id]).ToList();

            below.Max(m => m.Run).Should().BeLessThan(high.Run, "the copied run line sits above every row judged not a copy or doubtful");
            below.Max(m => m.Stretch).Should().BeLessThan(high.Stretch);
            below.Max(m => m.StretchShare).Should().BeLessThan(high.StretchShare);
            rows.Where(r => r["copy"] == NotCopy).Max(r => measures[r.Id].StretchShare).Should().BeLessThan(low.StretchShare, "the stretch share's copy-unsure line, v5's line, sits above every row judged not a copy and adds nothing to the price");
            below.Max(m => m.Share).Should().BeLessThan(high.Share);
            below.Max(m => m.DescriptionRun).Should().BeLessThan(high.DescriptionRun);
            below.Max(m => m.Sentence).Should().BeLessThan(high.Sentence);

            var margin = ProseCheck.CopyUnsureMargin;
            rows.Where(IsCopy)
                .Where(r => !(measures[r.Id].Run * margin >= low.Run || measures[r.Id].Stretch * margin >= low.Stretch || measures[r.Id].StretchShare * margin >= low.StretchShare || measures[r.Id].Share * margin >= low.Share
                    || measures[r.Id].DescriptionRun * margin >= low.DescriptionRun || measures[r.Id].Sentence * margin >= low.Sentence))
                .Select(r => $"{r.Id} ({measures[r.Id]})")
                .Should().BeEmpty("every recipe judged a copy or doubtful clears a copy-unsure line by the margin");

            var notCopies = rows.Where(r => r["copy"] == NotCopy).ToList();
            var price = notCopies.Count(r => measures[r.Id].Over(low).Count > 0);
            _output.WriteLine($"copied lines {high}; copy-unsure lines {low}; margin {margin}; judged: {rows.Count(r => r["copy"] == Copy)} copies, {rows.Count(r => r["copy"] == Doubtful)} doubtful, {notCopies.Count} not copies, {price} of them withheld as copy-unsure");
        }

        [Fact]
        public void Each_verdict_the_checks_got_wrong_on_staging_names_its_rule_and_the_check_gets_it_right()
        {
            var wrong = GoldenSet.Read(File).Rows.Where(r => r["verdict"] is "failure wrong" or "copy").ToList();

            wrong.Where(r => r["v2"] != "-").Should().HaveCount(15, "checkpoint 2 read 9 false failures and 6 copies that passed check v2");
            wrong.Where(r => r["v3"] != "-").Should().HaveCount(12, "checkpoint 3 read 9 false failures and 3 copies that passed check v3");
            wrong.Where(r => r["v4"] != "-").Should().HaveCount(8, "checkpoint 4 read 8 false failures of check v4, all quantity rules");
            wrong.Where(r => r["v5"] != "-").Should().HaveCount(6, "checkpoint 5 read 4 passes and 2 unsure recipes of check v5 that copy their source (Q9, Q10)");
            wrong.Where(r => r.Value("rule") == null).Select(r => r.Id).Should().BeEmpty("each wrong verdict names the rule behind it");
            wrong.Where(r => !Meets(r, Check(r))).Select(r => r.Id).Should().BeEmpty();
        }

        [Fact]
        public void A_weak_signal_alone_never_fails_a_read_recipe()
        {
            var failedOnWeakSignals = Judged()
                .Select(r => (Row: r, Finding: Check(r)))
                .Where(x => x.Finding.Faults.Count > 0 && x.Finding.Faults.All(f => ProseCheck.UnsureReasons.Contains(f.Reason)) && x.Finding.Failed)
                .Select(x => x.Row.Id)
                .ToList();
            var unsure = Judged().Count(r => Check(r).Unsure);

            failedOnWeakSignals.Should().BeEmpty();
            unsure.Should().BeGreaterThan(0, "the read recipes include verbs said another way, which the check marks unsure");
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
