using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Recipe;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The auto-approval sweep is skipped while nothing it decides on has changed, so the
    /// fingerprint must move with every change that can turn a refusal into an approval.
    /// </summary>
    public class AutoApproveChangeSignalTests
    {
        [Fact]
        public async Task The_auto_approval_fingerprint_changes_when_an_ingredient_is_curated_and_not_otherwise()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Ingredients.Add(new IngredientEntity { Id = 1, Name = "flour", CurationStatusId = (long)CurationStatusEnum.NonCurated });
            await db.SaveChangesAsync();
            var before = await AutoApproveChangeSignal.FingerprintAsync(db, default);
            (await AutoApproveChangeSignal.FingerprintAsync(db, default)).Should().Be(before);

            (await db.Ingredients.SingleAsync()).CurationStatusId = (long)CurationStatusEnum.Curated;
            await db.SaveChangesAsync();

            (await AutoApproveChangeSignal.FingerprintAsync(db, default)).Should().NotBe(before);
        }

        [Fact]
        public async Task The_fingerprint_changes_when_the_second_diet_reader_reads_a_recipe_or_the_diet_gate_runs_a_cycle()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var before = await AutoApproveChangeSignal.FingerprintAsync(db, default);

            db.RecipeDietReadings.Add(new Nom.Data.Plan.RecipeDietReadingEntity { RecipeId = 1, Model = "phi4:latest", PromptSignature = "p", TextSignature = "t", StoreVersion = "s", Verdicts = "fffffffffff", Readable = true, ReadAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var read = await AutoApproveChangeSignal.FingerprintAsync(db, default);
            read.Should().NotBe(before, "plan item ck21: a recipe waits for the second reader, so a reading can turn a refusal into an approval");

            db.DietGateBuilds.Add(new Nom.Data.Plan.DietGateBuildEntity { Version = "v", State = Nom.Orch.Services.Support.DietGateStore.Active, StartedAt = DateTime.UtcNow, LastCycleAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            (await AutoApproveChangeSignal.FingerprintAsync(db, default)).Should().NotBe(read, "a recipe waits for the diet gate to decide it");
        }

        [Fact]
        public async Task An_idle_diet_gate_cycle_leaves_the_fingerprint_alone_while_a_decision_a_write_or_a_set_being_built_moves_it()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var build = new Nom.Data.Plan.DietGateBuildEntity { Version = "v", State = Nom.Orch.Services.Support.DietGateStore.Active, StartedAt = DateTime.UtcNow, LastCycleAt = DateTime.UtcNow };
            db.DietGateBuilds.Add(build);
            await db.SaveChangesAsync();
            db.DietRecipeStates.Add(new Nom.Data.Plan.DietRecipeStateEntity { BuildId = build.Id, RecipeId = 1, Fingerprint = 7, ComputedAt = DateTime.UtcNow.AddMinutes(-5) });
            await db.SaveChangesAsync();
            var before = await AutoApproveChangeSignal.FingerprintAsync(db, default);

            build.LastCycleAt = DateTime.UtcNow.AddMinutes(15);
            await db.SaveChangesAsync();
            (await AutoApproveChangeSignal.FingerprintAsync(db, default)).Should().Be(before, "the gate's backstop cycle runs every 15 minutes on an idle database and decides nothing");

            db.DietGateWrites.Add(new Nom.Data.Plan.DietGateWriteEntity { RecipeId = 1, WrittenAt = DateTime.UtcNow, Tx = 9 });
            await db.SaveChangesAsync();
            var written = await AutoApproveChangeSignal.FingerprintAsync(db, default);
            written.Should().NotBe(before, "a logged write takes the recipe's decision back");

            db.DietRecipeStates.Add(new Nom.Data.Plan.DietRecipeStateEntity { BuildId = build.Id, RecipeId = 2, Fingerprint = 8, ComputedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var decided = await AutoApproveChangeSignal.FingerprintAsync(db, default);
            decided.Should().NotBe(written, "a recipe the gate decides may now be approved");

            db.DietGateSets.Add(new Nom.Data.Plan.DietGateSetEntity { BuildId = build.Id, CriteriaKey = "k", RestrictionTypeId = 1, Severe = true, Criteria = "c", State = Nom.Orch.Services.Support.DietGateStore.Building });
            await db.SaveChangesAsync();
            (await AutoApproveChangeSignal.FingerprintAsync(db, default)).Should().NotBe(decided, "a set being built holds every recipe from approval until it is complete");
        }
    }
}
