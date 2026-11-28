using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Plan item ck23, U5: a recipe people judged to hide a food a restriction type keeps out while the rules show it is
    /// flagged by the stored gate for every set of that type as "Judged to contain", whatever the rules do, and a judged hold
    /// never clears anything. The holds are loaded with a dry run, journaled under the load's stamp and undone by it.
    /// </summary>
    public class DietJudgedHoldTests
    {
        private const long PersonId = 81, HouseholdId = 8, CeliacRef = 60800, VegetarianRef = 60850, WheatRef = 60706, GlutenSensitivityRef = 60749, Gram = 1;

        private static ApplicationDbContext NewContext(string? name = null, params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).AddInterceptors(interceptors).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private sealed class JournalFails : SaveChangesInterceptor
        {
            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (eventData.Context!.ChangeTracker.Entries<AuditLogEntryEntity>().Any(e => e.State == EntityState.Added))
                    throw new OperationCanceledException("the request was cancelled while the journal was written");
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        private sealed record Kitchen(long Bread, long Pilaf, long Salad, long Gone);

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.Curated, Visibility = RecipeVisibilityEnum.Public, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private static async Task<Kitchen> SeedAsync(ApplicationDbContext db, bool wheat = false)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().AddRange(new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" }, new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            if (wheat)
                db.Set<ReferenceEntity>().AddRange(new ReferenceEntity { Id = WheatRef, Name = "Wheat Allergy" }, new ReferenceEntity { Id = GlutenSensitivityRef, Name = "Gluten Sensitivity" });
            var flour = new IngredientEntity { Name = "Wheat Flour", CurationStatusId = (long)CurationStatusEnum.Curated };
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            var water = new IngredientEntity { Name = "Water", CurationStatusId = (long)CurationStatusEnum.Curated };
            var lettuce = new IngredientEntity { Name = "Lettuce", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(flour, rice, water, lettuce);
            await db.SaveChangesAsync();
            var bread = Recipe("Bread", (flour.Id, "3 cups wheat flour"), (water.Id, "1 cup water"));
            var pilaf = Recipe("Pilaf", (rice.Id, "1 cup rice"), (water.Id, "2 cups water"));
            var salad = Recipe("Salad", (lettuce.Id, "1 head lettuce"));
            var gone = Recipe("Gone", (rice.Id, "1 cup rice"));
            gone.IsDeleted = true;
            db.Recipes.AddRange(bread, pilaf, salad, gone);
            db.Restrictions.AddRange(
                new RestrictionEntity { PersonId = PersonId, Name = "Celiac Disease", RestrictionTypeId = CeliacRef },
                new RestrictionEntity { PersonId = PersonId, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            if (wheat) db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Wheat Allergy", RestrictionTypeId = WheatRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            db.ChangeTracker.Clear();
            return new Kitchen(bread.Id, pilaf.Id, salad.Id, gone.Id);
        }

        private static DietJudgedHoldService Service(ApplicationDbContext db) => new(db, NullLogger<DietJudgedHoldService>.Instance);

        private static DietJudgedHoldInput Row(long recipe, string type, string food = "toast") =>
            new() { RecipeId = recipe, Type = type, Food = food, JudgedBy = "agent-ck23-test", JudgedAt = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc), Evidence = "ck23-test.judged.tsv" };

        private static async Task<List<(int Severity, DietRecipeFlagEntity Flag)>> FlagsAsync(ApplicationDbContext db, long typeId, long recipeId)
        {
            var sets = await db.DietGateSets.Where(s => s.RestrictionTypeId == typeId).ToDictionaryAsync(s => s.Id, s => s.MinimumSeverity);
            var ids = sets.Keys.ToList();
            return (await db.DietRecipeFlags.Where(f => ids.Contains(f.SetId) && f.RecipeId == recipeId).ToListAsync()).Select(f => (sets[f.SetId], f)).ToList();
        }

        [Fact]
        public async Task A_judged_hold_flags_its_recipe_for_every_set_of_its_type_only_where_the_rules_show_it_and_is_loaded_with_a_dry_run()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var rows = new[]
            {
                Row(k.Pilaf, "celiac disease", "toast"), Row(k.Bread, "Celiac Disease", "flour"), Row(k.Bread, "Vegetarian", "lard"), Row(k.Salad, "Nightshade Free"), Row(k.Gone, "Celiac Disease"),
                Row(k.Pilaf, "Celiac Disease"), Row(k.Salad, "Celiac Disease", ""), new DietJudgedHoldInput { RecipeId = k.Salad, Type = "Celiac Disease", Food = "bread" },
            };

            var dry = await Service(db).LoadAsync(rows, apply: false, PersonId);
            dry.Added.Should().Be(3);
            dry.Refused.Should().HaveCount(5);
            dry.Refused.Should().Contain(r => r.Contains("no restriction type")).And.Contain(r => r.Contains("no live recipe")).And.Contain(r => r.Contains("given twice"))
                .And.Contain(r => r.Contains("short word")).And.Contain(r => r.Contains("evidence file"));
            (await db.RecipeDietJudgedHolds.CountAsync()).Should().Be(0, "a dry run writes nothing");

            var loaded = await Service(db).LoadAsync(rows, apply: true, PersonId);
            loaded.Added.Should().Be(3);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == DietJudgedHoldService.LoadChange && a.Timestamp == loaded.Stamp)).Should().Be(3);
            (await Service(db).LoadAsync(rows, apply: true, PersonId)).Should().Match<DietJudgedHoldLoadReport>(r => r.Added == 0 && r.Unchanged == 3, "loading the same file twice changes nothing");

            await new DietGateStoreBuilder().RunAsync(db);
            var pilaf = await FlagsAsync(db, CeliacRef, k.Pilaf);
            var celiacSets = await db.DietGateSets.CountAsync(s => s.RestrictionTypeId == CeliacRef);
            celiacSets.Should().BePositive();
            pilaf.Should().HaveCount(celiacSets, "every set of the type holds it").And
                .OnlyContain(f => f.Flag.Verdict == DietVerdict.Contains && f.Flag.Reason == DietFlagReason.Judged);
            (await FlagsAsync(db, CeliacRef, k.Bread)).Should().NotBeEmpty().And.OnlyContain(f => f.Flag.Reason == DietFlagReason.Food, "the rules' own flag stands; a judged hold only adds a hold");
            (await FlagsAsync(db, VegetarianRef, k.Pilaf)).Should().BeEmpty("a judged hold is of its type only");
            (await FlagsAsync(db, VegetarianRef, k.Bread)).Should().NotBeEmpty().And.OnlyContain(f => f.Flag.Verdict == DietVerdict.Contains && f.Flag.Reason == DietFlagReason.Judged,
                "a diet of choice is held too: the judged recipe holds its food whatever the severity");
            (await FlagsAsync(db, CeliacRef, k.Salad)).Should().BeEmpty();

            var counts = await DietGateStoreReports.CountsAsync(db);
            counts.Types.Where(t => t.TypeId == CeliacRef).Should().OnlyContain(t => t.RecipesJudged == 1);
            var list = await Service(db).ListAsync();
            list.Holds.Should().Be(3);
            list.ByType["Celiac Disease"].Should().Be(2);

            var page = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);
            (await page.GetDietMatchesAsync(k.Pilaf, PersonId)).Should().ContainSingle(m => m.IngredientName == "Judged to contain" && m.Notes!.Contains("\"toast\""));
            (await page.GetDietMatchesAsync(k.Bread, PersonId)).Should().Contain(m => m.RestrictionType == "Vegetarian" && m.IngredientName == "Judged to contain" && m.Notes!.Contains("\"lard\""));
        }

        [Fact]
        public async Task A_hold_loaded_after_the_build_is_decided_by_the_cycle_over_the_changes_and_its_undo_releases_only_what_the_load_added()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            (await FlagsAsync(db, CeliacRef, k.Pilaf)).Should().BeEmpty();

            var first = await Service(db).LoadAsync(new[] { Row(k.Pilaf, "Celiac Disease") }, apply: true, PersonId);
            await Task.Delay(5);
            var second = await Service(db).LoadAsync(new[] { Row(k.Salad, "Celiac Disease", "croutons") }, apply: true, PersonId);
            db.DietGateWrites.AddRange(new DietGateWriteEntity { RecipeId = k.Pilaf, WrittenAt = DateTime.UtcNow }, new DietGateWriteEntity { RecipeId = k.Salad, WrittenAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            (await builder.RunChangesAsync(db)).RecipesDecided.Should().Be(2, "the table's triggers log the written recipes; only they are read again");
            (await FlagsAsync(db, CeliacRef, k.Pilaf)).Should().OnlyContain(f => f.Flag.Reason == DietFlagReason.Judged).And.NotBeEmpty();

            var dryUndo = await Service(db).UndoAsync(first.Stamp, apply: false, PersonId);
            dryUndo.Undone.Should().Be(1);
            (await db.RecipeDietJudgedHolds.CountAsync()).Should().Be(2);
            (await Service(db).UndoAsync(first.Stamp, apply: true, PersonId)).Undone.Should().Be(1);
            (await db.RecipeDietJudgedHolds.Select(h => h.RecipeId).ToListAsync()).Should().Equal(k.Salad);
            (await Service(db).UndoAsync(first.Stamp, apply: true, PersonId)).Should().Match<DietJudgedHoldUndoReport>(u => u.Undone == 0 && u.Skipped == 1);
            db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = k.Pilaf, WrittenAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await builder.RunChangesAsync(db);
            (await FlagsAsync(db, CeliacRef, k.Pilaf)).Should().BeEmpty("the undo released the hold the first load added");
            (await FlagsAsync(db, CeliacRef, k.Salad)).Should().NotBeEmpty("the second load's hold stands");
            second.Added.Should().Be(1);
        }

        [Fact]
        public async Task A_wheat_food_judged_for_Wheat_Allergy_is_held_for_the_gluten_types_too_and_a_gluten_food_is_not_held_for_wheat()
        {
            using var db = NewContext();
            var k = await SeedAsync(db, wheat: true);

            var loaded = await Service(db).LoadAsync(new[] { Row(k.Pilaf, "Wheat Allergy", "grain granules"), Row(k.Salad, "Celiac Disease", "barley"), Row(k.Bread, "Wheat Allergy", "flour"), Row(k.Bread, "Celiac Disease", "flour") },
                apply: true, PersonId);
            loaded.Should().Match<DietJudgedHoldLoadReport>(r => r.Added == 8 && r.Implied == 4 && r.Refused.Count == 0, "ck23 review: 26393 judged for Wheat Allergy is a Celiac miss as well");
            var holds = (await db.RecipeDietJudgedHolds.ToListAsync()).Select(h => (h.RecipeId, h.RestrictionTypeId)).ToHashSet();
            holds.Should().BeEquivalentTo(new[]
            {
                (k.Pilaf, WheatRef), (k.Pilaf, CeliacRef), (k.Pilaf, GlutenSensitivityRef), (k.Salad, CeliacRef), (k.Salad, GlutenSensitivityRef),
                (k.Bread, WheatRef), (k.Bread, CeliacRef), (k.Bread, GlutenSensitivityRef),
            }, "gluten also comes from barley and rye, so nothing implies Wheat Allergy");
            (await db.RecipeDietJudgedHolds.SingleAsync(h => h.RecipeId == k.Pilaf && h.RestrictionTypeId == CeliacRef)).Should()
                .Match<RecipeDietJudgedHoldEntity>(h => h.Food == "grain granules" && h.JudgedBy == "agent-ck23-test" && h.Evidence == "ck23-test.judged.tsv; implied by Wheat Allergy");
            (await db.RecipeDietJudgedHolds.SingleAsync(h => h.RecipeId == k.Bread && h.RestrictionTypeId == CeliacRef)).Evidence.Should().Be("ck23-test.judged.tsv", "a type given in the file is not an implied one");

            await new DietGateStoreBuilder().RunAsync(db);
            (await FlagsAsync(db, CeliacRef, k.Pilaf)).Should().NotBeEmpty().And.OnlyContain(f => f.Flag.Verdict == DietVerdict.Contains && f.Flag.Reason == DietFlagReason.Judged);

            (await Service(db).UndoAsync(loaded.Stamp, apply: true, PersonId)).Undone.Should().Be(8);
            (await db.RecipeDietJudgedHolds.CountAsync()).Should().Be(0, "the implied holds are journaled under the load's stamp like the others");
        }

        [Fact]
        public async Task The_holds_and_their_journal_are_written_in_one_save_so_a_load_cut_off_while_journaling_stores_no_hold_the_undo_cannot_find()
        {
            var name = Guid.NewGuid().ToString();
            using (var seed = NewContext(name))
            {
                var k = await SeedAsync(seed);
                using var db = NewContext(name, new JournalFails());
                var load = () => Service(db).LoadAsync(new[] { Row(k.Pilaf, "Celiac Disease") }, apply: true, PersonId);
                await load.Should().ThrowAsync<OperationCanceledException>();
            }
            using var after = NewContext(name);
            (await after.RecipeDietJudgedHolds.CountAsync()).Should().Be(0, "ck23 review: a hold stored without its journal entry is one the undo by stamp never takes back");
            (await after.AuditLogEntries.CountAsync()).Should().Be(0);
        }
    }
}
