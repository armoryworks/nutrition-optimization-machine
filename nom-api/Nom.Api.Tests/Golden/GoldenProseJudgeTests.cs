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
    /// planted fault, and recipes written for the set, each with a faithful rewrite of our own and the
    /// lane writer's rewrite judged step by step, so false refusals are measured on enough faithful
    /// rows. Check v2 must give each row its recorded outcome (<c>check2</c>), never fail a faithful
    /// rewrite or a writer's rewrite (the judge reads only what the check passes or is unsure of), pass
    /// the faithful rewrite of each planted recipe, and refuse every planted fault a fail rule decides
    /// (a fact rule only while its class is proven); the faults it passes or only marks unsure are the
    /// judge's work, and there must be enough it passes to score a judge and to feed the canaries.
    /// The file compiled into Nom.Orch must be this file, and the masking and the rule rendering of
    /// the lane must handle every recipe of it.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseJudgeTests
    {
        public const int MinimumJudgeOnlyFaults = 12;
        public const int MinimumCases = 120;
        public const int MinimumFaithful = 48;
        public const string WriterSource = "agent-review-writer-2026-11-23";
        public const string CoverageSource = "agent-review-construction-2026-11-23";

        private readonly ITestOutputHelper _output;

        public GoldenProseJudgeTests(ITestOutputHelper output) => _output = output;

        private static IReadOnlyList<ProseJudgeCase> Cases() => ModelLanes.ProseCases(GoldenSet.Folder);

        private static bool WriterRow(ProseJudgeCase c) => c.Source == WriterSource;

        private static ProseCheck.Finding CheckV2(ProseJudgeCase c) =>
            ProseFactView.Of(ProseCheck.Check(c.Input(ModelLanes.ProseStock(GoldenSet.Folder)).Source, c.RewriteText, c.Lines, ModelLanes.ProseStock(GoldenSet.Folder)));

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
        public void Check_v2_fails_no_faithful_or_writer_rewrite_and_leaves_the_judge_enough_faults_it_cannot_see()
        {
            var cases = Cases();
            var planted = cases.Where(c => c.Source == "rule:construction").Select(c => c.Recipe).ToHashSet();
            cases.Where(c => c.Faithful && planted.Contains(c.Recipe)).Should().OnlyContain(c => CheckV2(c).Passed, "a planted recipe's faults are one edit of a rewrite the check passes");
            cases.Where(c => c.Faithful || WriterRow(c)).Should().OnlyContain(c => !CheckV2(c).Failed, "a faithful rewrite the gate refuses cannot test the judge, and the judge reads only what the check passes or is unsure of");
            cases.Where(c => !c.Faithful && c.Check2 != "passed" && !ProseCheck.UnsureReasons.Contains(c.Check2) && (!ProseCheck.FactClasses.Contains(c.Check2) || ProseCheck.ProvenClasses.Contains(c.Check2)))
                .Where(c => !CheckV2(c).Failed).Select(c => c.Id).Should().BeEmpty("a planted fault a fail rule decides is refused by the rule gate");
            cases.Where(c => !c.Faithful && ProseCheck.FactClasses.Contains(c.Check2) && !ProseCheck.ProvenClasses.Contains(c.Check2)).Where(c => !CheckV2(c).Unsure).Select(c => c.Id).Should().BeEmpty(
                "a planted fault of a fact class not proven is only marked unsure and reaches the judge");
            cases.Where(c => !c.Faithful && ProseCheck.UnsureReasons.Contains(c.Check2)).Should().OnlyContain(c => CheckV2(c).Unsure,
                "a planted fault of a weak signal is only marked unsure and reaches the judge");
            cases.Count(c => !c.Faithful && c.Check2 == "passed").Should().BeGreaterThanOrEqualTo(MinimumJudgeOnlyFaults);
        }

        [Fact]
        public void Every_recipe_is_planted_or_written_for_coverage_and_no_row_is_judged_twice()
        {
            var cases = Cases();
            cases.Should().HaveCountGreaterThanOrEqualTo(MinimumCases, "a split into a working and a held-out half needs enough rows in each");
            cases.Select(c => c.Id).Should().OnlyHaveUniqueItems();
            cases.Select(c => string.Join(" ¦ ", c.Rewrite)).Should().OnlyHaveUniqueItems("a planted fault is never a copy of another row");
            foreach (var recipe in cases.GroupBy(c => c.Recipe))
            {
                recipe.Select(c => c.Title).Distinct().Should().ContainSingle();
                if (recipe.Any(c => c.Source == "rule:construction"))
                {
                    recipe.Should().NotContain(c => WriterRow(c) || c.Source == CoverageSource, $"recipe {recipe.Key} is a planted recipe");
                    recipe.Count(c => c.Faithful).Should().Be(1, $"recipe {recipe.Key} needs exactly one faithful rewrite");
                    recipe.Count(c => !c.Faithful).Should().BeGreaterThanOrEqualTo(3, $"recipe {recipe.Key} needs planted faults");
                    continue;
                }

                recipe.Should().OnlyContain(c => WriterRow(c) || c.Source == CoverageSource, $"recipe {recipe.Key} holds only our rewrite and the writer's");
                recipe.Count(c => c.Source == CoverageSource).Should().BeLessThanOrEqualTo(1, $"recipe {recipe.Key} has at most one rewrite of ours");
                recipe.Count(WriterRow).Should().BeLessThanOrEqualTo(1, $"recipe {recipe.Key} has at most one rewrite of the writer");
                recipe.Should().NotContain(c => c.Source == CoverageSource && !c.Faithful, "a rewrite of ours is written faithful");
            }

            cases.Where(c => c.Source == "rule:construction").Should().OnlyContain(c => !c.Faithful && c.Status == GoldenSet.Ok);
            cases.Where(c => c.Source != "rule:construction").Should().OnlyContain(c => c.Status == GoldenSet.Agent && GoldenSet.ByModel(c.Source));
            cases.Count(c => c.Faithful).Should().BeGreaterThanOrEqualTo(MinimumFaithful, "false refusals are measured on enough faithful rewrites");
        }

        [Fact]
        public void The_judge_rules_before_the_model_refuse_no_faithful_rewrite()
        {
            var refused = Cases().Where(c => c.Faithful)
                .Where(c => ProseJudgeRules.Before(c.Method.Select(ProseQuantities.Prepare).ToList(), c.Rewrite,
                    ProseCheck.Check(new RewriteFidelity.SourceText(string.Empty, c.Method.Select(ProseQuantities.Prepare).ToList()), new RewriteFidelity.SourceText(string.Empty, c.Rewrite))) != null)
                .Select(c => c.Id);
            refused.Should().BeEmpty("a rule that refuses a faithful rewrite is no rule");
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
