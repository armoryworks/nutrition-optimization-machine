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
    /// The prose check on the 450 recipes read against their source at staging checkpoints 2 to 6
    /// (prose-check.tsv): at checkpoints 2 and 3, 40 the re-check failed and 40 it passed, at
    /// checkpoint 4, 40 it failed, at checkpoint 5, 40 it failed, 40 it passed and 40 it was unsure of,
    /// at checkpoint 6, 30 it called copied, 40 copy-unsure, 20 unsure on facts and 40 passed, kept as
    /// their whole stored texts, masked, with the verdict the check gave on staging and three copy
    /// judgements: <c>copy</c> (copy, doubtful or not) under the strict standard of checkpoints 5 and 6,
    /// the lenient standard the earlier <c>expect</c> values were written under, and <c>sentences</c>
    /// under the owner's standard of 2026-11-23, which the check follows from v7: <c>mostly</c> (more than
    /// half of the sentences copied or kept nearly word for word), <c>doubtful</c> (two in five to half),
    /// <c>some</c> or <c>none</c>, with the count of such sentences. The copy verdict of the check is held
    /// to <c>sentences</c>: no recipe judged mostly copied may be shown, and at most 1 in 20 of the
    /// recipes the check withholds as copied may be judged to hold some copied sentences or none, on all
    /// the rows and on the checkpoint 6 rows read sentence by sentence; the line of
    /// <see cref="ProseSentences.MostlyCopied"/> sits where the judged rows put it. <c>expect</c> keeps the
    /// fact expectations: <c>passed</c> is met by a rewrite the check does not fail or withholds only as
    /// copied, <c>unsure</c> by one it does not pass clean, a fact reason by a finding with that reason
    /// that fails the rewrite or only marks it unsure while its class is not proven (a wrong amount the
    /// recipe's own line contradicts is <c>contradicts-lines</c>, which always fails); <c>copied-text</c>,
    /// <c>copy-unsure</c> and <c>-</c> leave the copy verdict to <c>sentences</c>. The verdicts the checks
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
        public const string Mostly = "mostly";
        public const string DoubtfulSentences = "doubtful";
        public const string SomeSentences = "some";
        public const string NoSentence = "none";
        public const string SentenceReader = "agent-review-ck11-";

        private readonly ITestOutputHelper _output;

        public GoldenProseCheckTests(ITestOutputHelper output) => _output = output;

        private static ProseCheck.Finding Check(GoldenRow row) =>
            ProseCheck.Check(
                new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                row.Value("lines")?.Split(" ¦ "));

        private static bool Meets(GoldenRow row, ProseCheck.Finding finding) =>
            (Sentences(row) != Mostly || finding.Copied) && row["expect"] switch
            {
                "passed" => !finding.Failed || finding.Copied,
                "unsure" => !finding.Passed,
                ProseCheck.CopiedText or "copy-unsure" or "-" => true,
                ProseCheck.WrongAmount when finding.Reasons.Contains(ProseCheck.ContradictsLines) => true,
                var reason => finding.Reasons.Contains(reason) && (finding.Failed || (ProseCheck.FactClasses.Contains(reason) && !finding.Proven.Contains(reason))),
            };

        /// <summary>The judgement of a row's sentences: mostly, doubtful, some or none.</summary>
        private static string Sentences(GoldenRow row) => row["sentences"].Split(' ')[0];

        private static bool FalseVerdict(GoldenRow row, ProseCheck.Finding finding) =>
            finding.Copied && Sentences(row) is SomeSentences or NoSentence;

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
            AssertBars("recipes the checkpoint operators read", rows.Where(ReadByOperator).ToList(), findings, falseVerdictBar: false);
            AssertBars("checkpoint 6 recipes read sentence by sentence", rows.Where(ReadBySentence).ToList(), findings, falseVerdictBar: false);
            using (ProseCheck.AssumeProven(ProseCheck.FactClasses.ToArray()))
            {
                var proven = rows.ToDictionary(r => r.Id, Check);
                AssertBars("all read recipes, every fact class proven (shown, not a bar: the judged firings prove no class)", rows, proven, falseVerdictBar: false);
                AssertBars("recipes the checkpoint operators read, every fact class proven (shown, not a bar)", rows.Where(ReadByOperator).ToList(), proven, falseVerdictBar: false);
            }
        }

        private static bool ReadByOperator(GoldenRow row) => row["source"].StartsWith(OperatorSource, System.StringComparison.Ordinal);

        private static bool ReadBySentence(GoldenRow row) => row["source"].StartsWith(SentenceReader, System.StringComparison.Ordinal) && row["set"] != "ck6-copied30";

        private void AssertBars(string label, List<GoldenRow> rows, Dictionary<string, ProseCheck.Finding> findings, bool falseVerdictBar = true)
        {
            var mostly = rows.Where(r => Sentences(r) == Mostly).ToList();
            var withheld = rows.Where(r => findings[r.Id].Copied).ToList();
            var falseVerdicts = withheld.Where(r => FalseVerdict(r, findings[r.Id])).Select(r => r.Id).ToList();
            var mostlyShown = mostly.Where(r => !findings[r.Id].Copied).Select(r => r.Id).ToList();
            var doubtful = withheld.Count(r => Sentences(r) == DoubtfulSentences);
            var outcomes = rows.GroupBy(r => findings[r.Id].Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
            _output.WriteLine($"check v{ProseCheck.Version}, {label} ({rows.Count}): {mostlyShown.Count} of {mostly.Count} judged mostly copied shown, {falseVerdicts.Count} of {withheld.Count} withheld as copied judged to hold some copied sentences or none ({doubtful} doubtful); {string.Join(", ", outcomes)}");

            mostly.Should().NotBeEmpty();
            withheld.Should().NotBeEmpty();
            mostlyShown.Should().BeEmpty($"no recipe judged mostly copied may be shown ({label})");
            if (!falseVerdictBar) return;
            ((decimal)falseVerdicts.Count / withheld.Count).Should().BeLessThanOrEqualTo(MaximumFalseFailureRate,
                $"at most 1 in 20 recipes withheld as copied may be judged to hold some copied sentences or none ({label}); these were: {string.Join(", ", falseVerdicts)}");
        }

        [Fact]
        public void The_mostly_line_sits_where_the_judged_rows_put_it()
        {
            var rows = Judged();
            rows.Select(Sentences).Should().OnlyContain(c => c == Mostly || c == DoubtfulSentences || c == SomeSentences || c == NoSentence, "every read recipe carries its sentence judgement");
            var readings = rows.ToDictionary(r => r.Id, r => Check(r).Sentences);
            var lowest = rows.Where(r => Sentences(r) == Mostly).Min(r => readings[r.Id].KeptShare);
            var line = ProseSentences.MostlyCopied;

            line.Should().BeLessThanOrEqualTo(lowest, "the line sits at or under every recipe judged mostly copied");
            line.Should().BeGreaterThan(lowest - 0.05m, "and no lower than that row needs");
            var price = rows.Where(r => Sentences(r) is SomeSentences or NoSentence).Count(r => readings[r.Id].Mostly);
            _output.WriteLine($"mostly line {line}; lowest share of a row judged mostly {lowest}; {price} rows judged some or none over the line; judged: {string.Join(", ", rows.GroupBy(Sentences).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))}");
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
