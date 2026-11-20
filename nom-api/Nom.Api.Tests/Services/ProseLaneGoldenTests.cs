using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The prose lane may qualify only on golden records current for its build and its models: a
    /// record under another signature, another model or another case set, or below its floor,
    /// holds it; the audit check's score is read from the audit lane's record. And the lane writes
    /// into recipes whose step rows have gaps or removed rows, which the undo puts back.
    /// </summary>
    public class ProseLaneGoldenTests
    {
        private static readonly ProseLaneModels Models = new("qwen2.5:14b-instruct", "mistral-nemo", "phi4");

        internal static ProseLaneRecord Record(string lane, string model, double score = 1, int falseAccepts = 0, string? signature = null, string? caseSet = null)
        {
            var cases = ProseLaneGolden.Default.Cases;
            var ids = ProseLaneGolden.CaseIds(lane, cases);
            var bad = cases.Count(c => !c.Faithful);
            return new ProseLaneRecord(lane, model, signature ?? ProseLaneGolden.Signature(lane), DateTime.UtcNow, caseSet ?? ProseLaneGolden.CaseSet(ids),
                ids.Count, (int)Math.Round(ids.Count * score), score, falseAccepts, bad, (double)falseAccepts / bad);
        }

        private static ProseLaneGolden Golden(params ProseLaneRecord[] records) =>
            new(ProseLaneGolden.Default.Cases, records.ToDictionary(r => r.Lane), ProseLaneGolden.Default.Floors);

        [Fact]
        public void Current_records_for_the_writer_and_the_judge_vouch_for_the_lane()
        {
            var golden = Golden(Record(ProseLaneGolden.RewriteLane, Models.Writer), Record(ProseLaneGolden.JudgeLane, Models.Judge));

            golden.GoldenProblem(Models).Should().BeNull();
        }

        [Fact]
        public void A_record_under_another_signature_model_or_case_set_or_below_its_floor_holds_the_lane()
        {
            var judge = Record(ProseLaneGolden.JudgeLane, Models.Judge);

            Golden(Record(ProseLaneGolden.RewriteLane, Models.Writer, signature: "old"), judge).GoldenProblem(Models).Should().Contain("signature old");
            Golden(Record(ProseLaneGolden.RewriteLane, "qwen2.5:0.5b"), judge).GoldenProblem(Models).Should().Contain("scored with qwen2.5:0.5b");
            Golden(Record(ProseLaneGolden.RewriteLane, Models.Writer, caseSet: "0000"), judge).GoldenProblem(Models).Should().Contain("cases of prose-judge.tsv changed");
            Golden(Record(ProseLaneGolden.RewriteLane, Models.Writer, score: 0.9), judge).GoldenProblem(Models).Should().Contain("below its floor");
            Golden(Record(ProseLaneGolden.RewriteLane, Models.Writer, falseAccepts: 1), judge).GoldenProblem(Models).Should().Contain("planted faults");
            Golden(Record(ProseLaneGolden.RewriteLane, Models.Writer)).GoldenProblem(Models).Should().Be("prose-judge has no committed golden score record");
        }

        [Fact]
        public void The_audit_check_score_comes_from_the_audit_record_of_the_auditor()
        {
            var cases = ProseLaneGolden.Default.Cases;
            var bad = cases.Count(c => !c.Faithful);
            var good = cases.Count - bad;
            var record = new ProseLaneRecord(ProseLaneGolden.AuditLane, Models.Auditor, ProseLaneGolden.Signature(ProseLaneGolden.AuditLane), DateTime.UtcNow,
                ProseLaneGolden.CaseSet(ProseLaneGolden.CaseIds(ProseLaneGolden.AuditLane, cases)), cases.Count, bad - 3 + good - 1, 0.9, 3, bad, 3.0 / bad);

            var score = Golden(record).AuditorScore(Models)!;

            score.Should().Match<AuditorScore>(s => s.KnownBad == bad && s.FlaggedBad == bad - 3 && s.KnownGood == good && s.FlaggedGood == 1);
            score.Problem.Should().BeNull();
            Golden(record).AuditorScore(Models with { Auditor = "granite3.3:8b" }).Should().BeNull();
            Golden().AuditorScore(Models).Should().BeNull();
        }

        [Fact]
        public async Task A_rewrite_into_steps_with_a_gap_and_a_removed_row_revives_and_adds_rows_by_number_and_the_undo_puts_them_back()
        {
            var rig = ProseLaneServiceTests.NewRig();
            var id = await ProseLaneServiceTests.SeedAsync(rig.Db, steps: new[] { "Heat the oil and cook the garlic.", "Old removed step.", "Pour in the cream and simmer, then whisk in the butter and let it stand." });
            var removed = await rig.Db.RecipeSteps.SingleAsync(s => s.RecipeId == id && s.StepNumber == 2);
            removed.IsDeleted = true;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();
            var since = DateTime.UtcNow.AddSeconds(-1);

            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            var live = await rig.Db.RecipeSteps.AsNoTracking().Where(s => s.RecipeId == id).OrderBy(s => s.StepNumber).Select(s => s.StepNumber).ToListAsync();
            live.Should().Equal(1, 2, 3, 4);

            await rig.Undo.UndoAsync(since, apply: true);

            var back = await rig.Db.RecipeSteps.AsNoTracking().Where(s => s.RecipeId == id).OrderBy(s => s.StepNumber).Select(s => s.Description).ToListAsync();
            back.Should().Equal("Heat the oil and cook the garlic.", "Pour in the cream and simmer, then whisk in the butter and let it stand.");
        }
    }
}
