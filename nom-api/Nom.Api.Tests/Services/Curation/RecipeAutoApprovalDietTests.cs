using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Curation
{
    /// <summary>
    /// Plan item ck21, approval follows understanding: the auto-approval lane leaves a recipe waiting, counted by its reason,
    /// while the stored diet gate has not decided it, a severe type holds it as unknown, or the second reader's pair has not
    /// read it; a recipe already approved is never taken back.
    /// </summary>
    public class RecipeAutoApprovalDietTests
    {
        private const long CeliacRef = 60800, PersonId = 81;

        private static RecipeEntity Recipe(long id, string name, long rice, long milk, long status = (long)CurationStatusEnum.NonCurated) => new()
        {
            Id = id,
            Name = name,
            Visibility = RecipeVisibilityEnum.Public,
            CurationStatusId = status,
            LicenseStatus = RecipeLicenseStatus.PublicDomain,
            ScrapedAtUtc = DateTime.UtcNow,
            AuthorId = SystemConstants.SystemPersonId,
            RecipeIngredients = new List<RecipeIngredientEntity>
            {
                new() { IngredientId = rice, RawLine = "1 cup rice", Quantity = 1m, MeasurementId = 1, SortOrder = 0 },
                new() { IngredientId = milk, RawLine = "1 quart milk", Quantity = 1m, MeasurementId = 1, SortOrder = 1 },
            },
            RecipeSteps = new List<RecipeStepEntity>
            {
                new() { StepNumber = 1, Summary = "Boil", Description = "Boil the rice in the milk until soft." },
                new() { StepNumber = 2, Summary = "Bake", Description = "Bake in a slow oven for an hour." },
            },
        };

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            db.References.AddRange(
                new ReferenceEntity { Id = 9100, Name = "Recipe" },
                new ReferenceEntity { Id = 9199, Name = "Approval" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.PendingCuration, Name = "Pending Curation" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" },
                new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Celiac Disease", RestrictionTypeId = CeliacRef });
            var rice = new IngredientEntity { Id = 501, Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            var milk = new IngredientEntity { Id = 502, Name = "Milk", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(rice, milk);
            db.Recipes.AddRange(
                Recipe(10, "Rice Pudding", rice.Id, milk.Id),
                Recipe(11, "Baked Rice", rice.Id, milk.Id),
                Recipe(12, "Rice Custard", rice.Id, milk.Id),
                Recipe(13, "Rice Cream", rice.Id, milk.Id),
                Recipe(14, "Approved Rice", rice.Id, milk.Id, (long)CurationStatusEnum.Curated));
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            db.ChangeTracker.Clear();
            return db;
        }

        private static void ReadByPair(ApplicationDbContext db, long recipeId, string name)
        {
            var text = DietSecondGate.TextSignature(name, new[] { "1 cup rice", "1 quart milk" }, new[] { "Boil the rice in the milk until soft.", "Bake in a slow oven for an hour." });
            foreach (var model in DietSecondGate.Pair)
                db.RecipeDietReadings.Add(new RecipeDietReadingEntity
                {
                    RecipeId = recipeId, Model = model, PromptSignature = OllamaDietSecondReader.Signature, TextSignature = text, StoreVersion = DietSecondGate.Version,
                    Readable = true, Complete = true, Verdicts = "fmfffffffff", ReadAt = DateTime.UtcNow,
                });
        }

        private static RecipeAutoCurationService Service(ApplicationDbContext db, Dictionary<string, string?>? settings = null) =>
            new(db, new RecipeVettingService(), NullLogger<RecipeAutoCurationService>.Instance, new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()).Build());

        [Fact]
        public async Task A_recipe_waits_until_the_gate_has_decided_it_no_severe_type_holds_it_as_unknown_and_both_readers_have_read_it()
        {
            using var db = await SeedAsync();
            await new DietGateStoreBuilder().RunAsync(db);
            ReadByPair(db, 11, "Baked Rice");
            ReadByPair(db, 12, "Rice Custard");
            ReadByPair(db, 13, "Rice Cream");
            ReadByPair(db, 14, "Approved Rice");
            var celiac = await db.DietGateSets.Where(s => s.RestrictionTypeId == CeliacRef && s.Severe).Select(s => s.Id).ToListAsync();
            celiac.Should().NotBeEmpty("Celiac Disease is a severe type");
            foreach (var set in celiac)
            {
                db.DietRecipeFlags.Add(new DietRecipeFlagEntity { SetId = set, RecipeId = 12, Verdict = DietVerdict.Unknown, Reason = DietFlagReason.UnknownWord });
                db.DietRecipeFlags.Add(new DietRecipeFlagEntity { SetId = set, RecipeId = 14, Verdict = DietVerdict.Unknown, Reason = DietFlagReason.UnknownWord });
            }
            db.DietRecipeStates.RemoveRange(await db.DietRecipeStates.Where(s => s.RecipeId == 13).ToListAsync());
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await Service(db).AutoApproveBatchAsync(0, 50);

            result.Approved.Should().Be(1);
            (await db.Recipes.Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated).Select(r => r.Id).OrderBy(id => id).ToListAsync())
                .Should().Equal(new long[] { 11, 14 }, "only the understood and read recipe is approved, and the approved recipe a severe type cannot read stays approved");
            result.Refusals.Should().Contain(DietApprovalHolds.UnreadKey, 1).And.Contain(DietApprovalHolds.UnknownKey, 1).And.Contain(DietApprovalHolds.NotDecidedKey, 1);
        }

        [Fact]
        public async Task The_operator_can_take_either_condition_off_and_nothing_waits_where_the_stored_gate_is_not_in_use()
        {
            using (var db = await SeedAsync())
            {
                await new DietGateStoreBuilder().RunAsync(db);
                var result = await Service(db, new() { ["Curation:AutoApprove:RequireSecondReading"] = "false" }).AutoApproveBatchAsync(0, 50);
                result.Approved.Should().Be(4, "without the second reading every recipe the gate decided and understands is approved");
            }

            using (var db = await SeedAsync())
            {
                var result = await Service(db).AutoApproveBatchAsync(0, 50);
                result.Approved.Should().Be(4, "an in-memory database without an active build does not use the stored gate");
            }
        }

        [Fact]
        public async Task Nothing_is_approved_while_the_active_build_builds_a_severe_set_whose_flags_are_not_yet_written()
        {
            using var db = await SeedAsync();
            await new DietGateStoreBuilder().RunAsync(db);
            foreach (var (id, name) in new[] { (10L, "Rice Pudding"), (11L, "Baked Rice"), (12L, "Rice Custard"), (13L, "Rice Cream") }) ReadByPair(db, id, name);
            var celiac = await db.DietGateSets.Where(s => s.RestrictionTypeId == CeliacRef && s.Severe).ToListAsync();
            celiac.Should().NotBeEmpty();
            foreach (var set in celiac) set.State = DietGateStore.Building;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await Service(db).AutoApproveBatchAsync(0, 50);

            result.Approved.Should().Be(0, "a severe set being built has decided nothing, so no recipe is known to be understood by its type");
            result.Refusals.Should().Contain(DietApprovalHolds.NotDecidedKey, 4);
        }

        [Fact]
        public async Task The_dry_run_of_the_waiting_reasons_counts_every_candidate_by_reason_and_writes_nothing()
        {
            using var db = await SeedAsync();
            await new DietGateStoreBuilder().RunAsync(db);
            ReadByPair(db, 11, "Baked Rice");
            ReadByPair(db, 12, "Rice Custard");
            var celiac = await db.DietGateSets.Where(s => s.RestrictionTypeId == CeliacRef && s.Severe).Select(s => s.Id).ToListAsync();
            foreach (var set in celiac) db.DietRecipeFlags.Add(new DietRecipeFlagEntity { SetId = set, RecipeId = 12, Verdict = DietVerdict.Unknown, Reason = DietFlagReason.UnknownWord });
            var unmeasured = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == 13 && ri.SortOrder == 1);
            unmeasured.Quantity = 0m;
            unmeasured.RawLine = "milk";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var report = await Service(db).WaitingAsync(sample: 5);

            report.Candidates.Should().Be(4, "the approved recipe is no candidate");
            report.Refused.Should().Be(1);
            report.RefusedBy.Values.Sum().Should().Be(1, "the recipe with an unmeasured line is refused by the policy");
            report.Passing.Should().Be(3);
            report.WaitingBy.Should().Contain(DietApprovalHolds.UnknownKey, 1).And.Contain(DietApprovalHolds.UnreadKey, 1);
            report.WouldApprove.Should().Be(1);
            report.Samples[RecipeAutoCurationService.WouldApproveKey].Should().Equal(11L);
            report.Samples[DietApprovalHolds.UnreadKey].Should().Equal(10L);
            report.RequireSecondReading.Should().BeTrue();
            (await db.Recipes.CountAsync(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)).Should().Be(1, "a dry run approves nothing");
        }
    }
}
