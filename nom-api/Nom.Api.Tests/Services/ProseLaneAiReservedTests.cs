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
            writer.Extracted.Should().ContainSingle("only the unreserved recipe's method reaches the extractor");
            writer.Requests.Should().OnlyContain(r => r.Dish == GarlicSauce.Dish);
            judge.Read.Should().HaveCount(1, "the judge reads only the unreserved recipe's rewrite");
            var decisions = await rig.Db.LaneDecisions.AsNoTracking().ToListAsync();
            decisions.Single(d => d.Subject == ProseLaneService.Subject(reserved)).Tier.Should().Be(ProseLaneService.RuleOnlyTier);
            decisions.Single(d => d.Subject == ProseLaneService.Subject(open)).Tier.Should().Be(ProseLaneService.Tier);
            decisions.Single(d => d.Subject == ProseLaneService.Subject(reserved)).SignalsJson.Should().Contain(ProseCleanRoom.RuleOnlyStyle);
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
