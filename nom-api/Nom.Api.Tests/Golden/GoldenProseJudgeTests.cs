using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// prose-judge.tsv, the judge's golden set and the prose lane's canaries: recipes constructed in
    /// our own words or in the public domain, each with a faithful rewrite and rewrites carrying one
    /// planted fault. Check v2 must give each row its recorded outcome (<c>check2</c>), pass every
    /// faithful rewrite, and refuse every planted fault a fail rule decides (a fact rule only while its
    /// class is proven); the faults it passes or only marks unsure are the judge's work, and there must be enough it passes to score a judge
    /// and to feed the canaries.
    /// The file compiled into Nom.Orch must be this file, and the masking and the rule rendering of
    /// the lane must handle every recipe of it.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseJudgeTests
    {
        public const int MinimumJudgeOnlyFaults = 12;
        public const int MinimumCases = 120;

        private readonly ITestOutputHelper _output;

        public GoldenProseJudgeTests(ITestOutputHelper output) => _output = output;

        private static IReadOnlyList<ProseJudgeCase> Cases() => ModelLanes.ProseCases(GoldenSet.Folder);

        private static ProseCheck.Finding CheckV2(ProseJudgeCase c) =>
            ProseCheck.Check(c.Input(ModelLanes.ProseStock(GoldenSet.Folder)).Source, c.RewriteText, c.Lines, ModelLanes.ProseStock(GoldenSet.Folder));

        [Fact]
        public void Each_rewrite_gets_its_check_v2_outcome()
        {
            var cases = Cases().ToDictionary(c => c.Id);
            var report = GoldenSet.Run(ProseLaneGolden.CasesFile, "status", row =>
            {
                var finding = CheckV2(cases[row.Id]);
                var actual = finding.Passed ? "passed" : finding.Reasons[0];
                return new GoldenOutcome(actual == row["check2"], row["check2"], finding.ToString(),
                    new Dictionary<string, string> { ["check2"] = actual });
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Check_v2_passes_every_faithful_rewrite_and_leaves_the_judge_enough_faults_it_cannot_see()
        {
            var cases = Cases();
            cases.Where(c => c.Faithful).Should().OnlyContain(c => CheckV2(c).Passed, "a faithful rewrite the gate refuses cannot test the judge");
            cases.Where(c => !c.Faithful && c.Check2 != "passed" && !ProseCheck.UnsureReasons.Contains(c.Check2) && (!ProseCheck.FactClasses.Contains(c.Check2) || ProseCheck.ProvenClasses.Contains(c.Check2)))
                .Where(c => !CheckV2(c).Failed).Select(c => c.Id).Should().BeEmpty("a planted fault a fail rule decides is refused by the rule gate");
            cases.Where(c => !c.Faithful && ProseCheck.FactClasses.Contains(c.Check2) && !ProseCheck.ProvenClasses.Contains(c.Check2)).Where(c => !CheckV2(c).Unsure).Select(c => c.Id).Should().BeEmpty(
                "a planted fault of a fact class not proven is only marked unsure and reaches the judge");
            cases.Where(c => !c.Faithful && ProseCheck.UnsureReasons.Contains(c.Check2)).Should().OnlyContain(c => CheckV2(c).Unsure,
                "a planted fault of a weak signal is only marked unsure and reaches the judge");
            cases.Count(c => !c.Faithful && c.Check2 == "passed").Should().BeGreaterThanOrEqualTo(MinimumJudgeOnlyFaults);
        }

        [Fact]
        public void Every_recipe_has_a_faithful_rewrite_and_planted_faults_and_no_row_is_judged_twice()
        {
            var cases = Cases();
            cases.Should().HaveCountGreaterThanOrEqualTo(MinimumCases, "a split into a working and a held-out half needs enough rows in each");
            cases.Select(c => c.Id).Should().OnlyHaveUniqueItems();
            cases.Select(c => string.Join(" ¦ ", c.Rewrite)).Should().OnlyHaveUniqueItems("a planted fault is never a copy of another row");
            foreach (var recipe in cases.GroupBy(c => c.Recipe))
            {
                recipe.Count(c => c.Faithful).Should().Be(1, $"recipe {recipe.Key} needs exactly one faithful rewrite");
                recipe.Count(c => !c.Faithful).Should().BeGreaterThanOrEqualTo(3, $"recipe {recipe.Key} needs planted faults");
                recipe.Select(c => c.Title).Distinct().Should().ContainSingle();
            }

            cases.Where(c => !c.Faithful).Should().OnlyContain(c => c.Source == "rule:construction" && c.Status == GoldenSet.Ok);
            cases.Where(c => c.Faithful).Should().OnlyContain(c => c.Status == GoldenSet.Agent);
        }

        [Fact]
        public void The_cases_compiled_into_the_lane_are_prose_judge_tsv()
        {
            ProseLaneGolden.Default.Cases.Select(c => c.Id).Should().Equal(Cases().Select(c => c.Id));
            ProseLaneGolden.Default.Floors.Keys.Should().Contain(new[] { ProseLaneGolden.RewriteLane, ProseLaneGolden.JudgeLane, ProseLaneGolden.AuditLane });
            File.Exists(Path.Combine(GoldenSet.Folder, ProseLaneGolden.CasesFile)).Should().BeTrue();
        }

        [Fact]
        public void The_lane_masks_every_golden_method_and_its_rule_rendering_keeps_to_the_form_rules()
        {
            foreach (var recipe in Cases().GroupBy(c => c.Recipe))
            {
                var c = recipe.First();
                var method = ProseMasking.Mask(c.Method, c.Lines);
                method.Steps.Should().HaveCount(c.Method.Count, c.Recipe);
                string.Join(" ", method.Steps.Select(s => System.Text.RegularExpressions.Regex.Replace(s.Text, "⟦[QL]\\d+⟧", " "))).Should().NotMatchRegex(@"\d", $"{c.Recipe}: every number is masked");
                method.Steps.Select(s => method.Unmask(s.Text)).Should().HaveCount(c.Method.Count);

                var sheets = ProseCleanRoom.RuleSheets(method);
                ProseFactSheet.Problems(method, sheets).Should().BeEmpty(c.Recipe);
                var rendered = sheets.Select(ProseFactSheet.Render).ToList();
                ProseCleanRoom.FormProblems(method, sheets, rendered).Should().BeEmpty(c.Recipe);
                _output.WriteLine($"{c.Recipe}: {string.Join(" | ", rendered.Select(method.Unmask))}");
            }
        }
    }
}
