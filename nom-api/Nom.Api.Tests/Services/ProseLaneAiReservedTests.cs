using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// A recipe whose source reserves its content against AI use takes the prose lane's model-free path
    /// only: no extractor, writer or judge reads its method, its decision is recorded in the rule-only
    /// tier (which the audit never samples), and a recipe of an unreserved source in the same sweep still
    /// goes through the models.
    /// </summary>
    public class ProseLaneAiReservedTests
    {
        private static readonly ProseLaneModels Models = new("qwen2.5:14b-instruct", "mistral-nemo", "phi4");

        private static readonly string[] EggLines = { "2 eggs", "1 cup milk", "1 loaf bread" };

        private static readonly string[] EggMethod =
        {
            "Whisk together the eggs with the milk until the mixture looks smooth and even.",
            "Pour the egg mixture into the hot pan and cook for 3 minutes.",
        };

        private static readonly string[] EggMethodRenderedInItsFrame =
        {
            "Whisk the eggs and the milk in a bowl.",
            "Pour the egg mixture into the hot pan and cook for 3 minutes.",
        };

        private static string EggJsonLd(string[] method) => System.Text.Json.JsonSerializer.Serialize(new System.Collections.Generic.Dictionary<string, object>
        {
            ["@type"] = "Recipe",
            ["name"] = GarlicSauce.Dish,
            ["recipeIngredient"] = EggLines,
            ["recipeInstructions"] = method.Select(m => new System.Collections.Generic.Dictionary<string, string> { ["@type"] = "HowToStep", ["text"] = m }).ToArray(),
        });

        private static ProseLaneInput EggInput(string[] method) =>
            new(GarlicSauce.Dish, new RewriteFidelity.SourceText("", method, GarlicSauce.Dish), EggLines, ProseStockIndex.Empty);

        [Fact]
        public async Task No_model_reads_the_method_of_a_recipe_whose_source_reserves_against_ai()
        {
            var rig = ProseLaneServiceTests.NewRig();
            var reserved = await ProseLaneServiceTests.SeedAsync(rig.Db, steps: GarlicSauce.Method);
            var open = await ProseLaneServiceTests.SeedAsync(rig.Db, steps: GarlicSauce.Method);
            var recipe = await rig.Db.Recipes.SingleAsync(r => r.Id == reserved);
            recipe.SourceAiReserved = true;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();
            var writer = new FakeWriter();
            var judge = new FakeJudge();

            var result = await rig.Lane.SweepAsync(LaneMode.Live, Models, writer, judge, 25);

            result.Seen.Should().Be(2);
            writer.Reworded.Should().HaveCount(GarlicSauce.Method.Length, "only the unreserved recipe's method reaches the reword writer, one call a step");
            judge.Read.Should().HaveCount(1, "the judge reads only the unreserved recipe's rewrite");
            var decisions = await rig.Db.LaneDecisions.AsNoTracking().ToListAsync();
            decisions.Single(d => d.Subject == ProseLaneService.Subject(reserved)).Tier.Should().Be(ProseLaneService.RuleOnlyTier);
            decisions.Single(d => d.Subject == ProseLaneService.Subject(open)).Tier.Should().Be(ProseLaneService.Tier);
            decisions.Single(d => d.Subject == ProseLaneService.Subject(reserved)).SignalsJson.Should().Contain(ProseCleanRoom.RuleOnlyStyle);
        }

        private static async Task<long> ReservedAsync(ProseLaneServiceTests.Rig rig, string[]? method = null)
        {
            var id = await ProseLaneServiceTests.SeedAsync(rig.Db, steps: method ?? EggMethod, jsonLd: EggJsonLd(method ?? EggMethod));
            var recipe = await rig.Db.Recipes.SingleAsync(r => r.Id == id);
            recipe.SourceAiReserved = true;
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();
            return id;
        }

        [Fact]
        public async Task The_rule_only_tier_writes_only_while_its_own_tier_is_live_whatever_the_model_tier_does()
        {
            var rig = ProseLaneServiceTests.NewRig();
            var reserved = await ReservedAsync(rig);

            var first = await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            first.Rewritten.Should().Be(0, "the model tier being live does not let the rule-only tier write");
            (await rig.Db.Recipes.AsNoTracking().SingleAsync(r => r.Id == reserved)).ProseWithheld.Should().BeTrue();
            var decision = await rig.Db.LaneDecisions.AsNoTracking().SingleAsync();
            decision.Should().Match<LaneDecisionEntity>(d => d.Tier == ProseLaneService.RuleOnlyTier && d.Mode == LaneMode.Shadow && d.Writes);

            rig.Db.LaneStates.Add(new LaneStateEntity { Lane = ProseLaneService.Lane, Tier = ProseLaneService.RuleOnlyTier, Mode = LaneMode.Live, SignatureHash = Models.Version, Since = System.DateTime.UtcNow, CreatedDate = System.DateTime.UtcNow });
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            var second = await rig.Lane.SweepAsync(LaneMode.Shadow, Models, new FakeWriter(), new FakeJudge(), 25);

            second.Rewritten.Should().Be(1, "a live rule-only tier writes even while the model tier is in shadow");
            (await rig.Db.Recipes.AsNoTracking().SingleAsync(r => r.Id == reserved)).ProseWithheld.Should().BeFalse();
        }

        [Fact]
        public async Task Every_rule_only_rendering_is_audited_by_rule_and_the_tier_is_evaluated_on_that_audit()
        {
            var rig = ProseLaneServiceTests.NewRig();
            await ReservedAsync(rig);
            await rig.Lane.SweepAsync(LaneMode.Live, Models, new FakeWriter(), new FakeJudge(), 25);

            var verdict = await rig.Lane.MeasureRuleOnlyAsync(Models.Version);

            var decision = await rig.Db.LaneDecisions.AsNoTracking().SingleAsync(d => d.Tier == ProseLaneService.RuleOnlyTier);
            decision.AuditOutcome.Should().NotBeNull("every rule-only decision that would write is audited, by rule");
            verdict.Mode.Should().Be(LaneMode.Shadow, "one audited rendering does not qualify the tier");
            (await rig.Frame.StatusAsync()).Should().Contain(s => s.Lane == ProseLaneService.Lane && s.Tier == ProseLaneService.RuleOnlyTier);
        }

        [Fact]
        public void The_rule_audit_flags_a_rendering_that_loses_an_amount_an_ingredient_or_a_step()
        {
            var outcome = ProseCleanRoom.RunRuleOnly(EggInput(EggMethod));
            outcome.Passed.Should().BeTrue(string.Join("; ", outcome.Attempts.SelectMany(a => a.Reasons)));
            var steps = outcome.Rewrite!.Steps;

            ProseCleanRoom.RuleOnlyAudit(EggMethod, steps, EggLines).Should().BeEmpty("the rule rendering reads back as its source");
            ProseCleanRoom.RuleOnlyAudit(EggMethod, steps.Skip(1).ToList(), EggLines).Should().ContainSingle(p => p.Contains("written steps"));
            var amount = steps.Select((text, i) => (text, i)).First(x => System.Text.RegularExpressions.Regex.IsMatch(x.text, @"\d"));
            var dropped = steps.ToList();
            dropped[amount.i] = System.Text.RegularExpressions.Regex.Replace(amount.text, @":[^;.]*", string.Empty);
            ProseCleanRoom.RuleOnlyAudit(EggMethod, dropped, EggLines).Should().Contain(p => p.Contains($"step {amount.i + 1} states"));
        }

        [Fact]
        public void A_rule_rendering_that_keeps_a_source_sentence_in_its_frame_is_refused_though_the_prose_check_passes_it()
        {
            var outcome = ProseCleanRoom.RunRuleOnly(EggInput(EggMethodRenderedInItsFrame));

            outcome.Finding!.Passed.Should().BeTrue("the sentence rule of the prose check reads no copy in it");
            outcome.Passed.Should().BeFalse("its first step is the source's sentence with words dropped, which the strict copy check of new text refuses");
            outcome.FailureClass.Should().Be(ProseReword.CopyReason);
            outcome.Attempts.Single().Reasons.Should().Contain(r => r.StartsWith(ProseReword.CopyReason + ": step 1", System.StringComparison.Ordinal));
            ProseCleanRoom.RunRuleOnly(EggInput(EggMethod)).Passed.Should().BeTrue("a rendering whose every sentence is rebuilt passes");
        }

        [Fact]
        public async Task A_live_rule_only_tier_leaves_withheld_a_recipe_whose_rendering_keeps_a_source_sentence_in_its_frame()
        {
            var rig = ProseLaneServiceTests.NewRig();
            var reserved = await ReservedAsync(rig, EggMethodRenderedInItsFrame);
            rig.Db.LaneStates.Add(new LaneStateEntity { Lane = ProseLaneService.Lane, Tier = ProseLaneService.RuleOnlyTier, Mode = LaneMode.Live, SignatureHash = Models.Version, Since = System.DateTime.UtcNow, CreatedDate = System.DateTime.UtcNow });
            await rig.Db.SaveChangesAsync();
            rig.Db.ChangeTracker.Clear();

            var result = await rig.Lane.SweepAsync(LaneMode.Shadow, Models, new FakeWriter(), new FakeJudge(), 25);

            result.Rewritten.Should().Be(0);
            result.GaveUpByClass.Should().ContainKey(ProseReword.CopyReason);
            var recipe = await rig.Db.Recipes.AsNoTracking().Include(r => r.RecipeSteps).SingleAsync(r => r.Id == reserved);
            recipe.ProseWithheld.Should().BeTrue("a recipe is shown only when every sentence passes the strict copy check");
            recipe.RecipeSteps!.Select(st => st.Description).Should().Equal(EggMethodRenderedInItsFrame);
            (await rig.Db.LaneDecisions.AsNoTracking().SingleAsync()).Decision.Should().Be(ProseLaneService.GaveUpPrefix + ProseReword.CopyReason);
        }

        [Fact]
        public void The_model_free_path_writes_the_rule_rendering_only_when_the_form_rules_and_check_v2_pass()
        {
            var input = new ProseLaneInput(GarlicSauce.Dish, new RewriteFidelity.SourceText("", GarlicSauce.Method, GarlicSauce.Dish), GarlicSauce.Lines, ProseStockIndex.Empty);

            var outcome = ProseCleanRoom.RunRuleOnly(input);

            outcome.RuleSheets.Should().BeTrue();
            outcome.Verdict.Should().BeNull("no judge reads it");
            outcome.Attempts.Should().ContainSingle(a => a.Style == ProseCleanRoom.RuleOnlyStyle);
            if (outcome.Passed)
            {
                outcome.Rewrite!.Steps.Should().HaveCount(GarlicSauce.Method.Length);
                outcome.Finding!.Passed.Should().BeTrue();
            }
            else
            {
                outcome.FailureClass.Should().NotBeNull();
            }

            ProseCleanRoom.RunRuleOnly(new ProseLaneInput("Toast", new RewriteFidelity.SourceText("", System.Array.Empty<string>(), "Toast"), GarlicSauce.Lines, ProseStockIndex.Empty))
                .FailureClass.Should().Be(ProseCleanRoom.NoMethod);
        }
    }
}
