using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Staging checkpoint 8, T15: the retention apply over every recipe failed after ten minutes and left 7,868 lines written.
    /// An apply now commits a batch of recipes at a time, each with its journal stamp and a batch mark under the run's stamp;
    /// a batch that fails twice stops the run with the place to go on from, a resumed run goes on under the same stamp, the
    /// dry run counts what the whole apply then does, and the undo by the run's stamp takes back every batch of a stopped and
    /// resumed run, labels and marks included. The diet store's clearer holds a recipe through the write log on a relational
    /// database instead of removing state rows the build job may have replaced.
    /// </summary>
    public class AlcoholRetentionBatchTests
    {
        private const long Gram = 1, Kcal = 16, Calories = 5035, Alcohol = 5038, PersonId = 70;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static AlcoholRetentionService Service(ApplicationDbContext db) =>
            new(db, new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance), NullLogger<AlcoholRetentionService>.Instance);

        private static async Task<List<long>> SeedAsync(ApplicationDbContext db, int recipes)
        {
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal },
                new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = Gram });
            var wine = new IngredientEntity { Name = "Dry White Wine", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 14 };
            wine.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Calories, Amount = 82m, MeasurementId = Kcal });
            wine.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = 10.3m, MeasurementId = Gram });
            db.Ingredients.Add(wine);
            await db.SaveChangesAsync();
            for (var i = 0; i < recipes; i++)
            {
                var recipe = new RecipeEntity { Name = "Fish in wine " + i, AuthorId = PersonId, Servings = 4, CurationStatusId = (long)CurationStatusEnum.NonCurated };
                recipe.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Cook", Description = i % 2 == 0 ? "Add the wine and simmer 30 minutes." : "Stir the wine into the cold sauce." } };
                recipe.RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = wine.Id, Quantity = 118m, MeasurementId = Gram, RawLine = "1/2 cup dry white wine", SortOrder = 0 } };
                db.Recipes.Add(recipe);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return await db.Recipes.OrderBy(r => r.Id).Select(r => r.Id).ToListAsync();
        }

        private static Task<int> StoredAsync(ApplicationDbContext db) => db.RecipeIngredients.AsNoTracking().CountAsync(ri => ri.AlcoholRetention != null);

        [Fact]
        public async Task An_apply_that_stops_at_a_failing_batch_is_resumed_under_its_stamp_and_its_undo_takes_back_every_batch()
        {
            using var db = NewContext();
            var recipes = await SeedAsync(db, AlcoholRetentionService.Chunk * 2 + 30);
            var nutrition = new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance);
            await nutrition.RecalculateManyAsync(recipes);
            var labels = await db.RecipeNutrition.AsNoTracking().OrderBy(n => n.RecipeId).ThenBy(n => n.NutrientId).Select(n => new { n.RecipeId, n.NutrientId, n.Amount }).ToListAsync();

            var dry = await Service(db).RunAsync(apply: false, maxRecipes: 100000);
            (dry.Batches, dry.Lines, dry.Changed, dry.RecipesTouched, dry.Completed).Should().Be((3, recipes.Count, recipes.Count, recipes.Count, true));
            (await StoredAsync(db)).Should().Be(0);

            var failing = Service(db);
            var attempts = 0;
            failing.BeforeBatchWrite = index =>
            {
                if (index == 1)
                {
                    attempts++;
                    throw new DbUpdateConcurrencyException("the batch's save found a row gone");
                }
                return Task.CompletedTask;
            };
            var stopped = await failing.RunAsync(apply: true, maxRecipes: 100000);
            stopped.Completed.Should().BeFalse();
            attempts.Should().Be(2, "a failed batch is tried once more before the run stops");
            stopped.Error.Should().Contain(nameof(DbUpdateConcurrencyException));
            (stopped.BatchesWritten, stopped.ResumeAfterRecipeId).Should().Be((1, recipes[AlcoholRetentionService.Chunk - 1]),
                "the run says where it stopped: after the last recipe of the batch it committed");
            (await db.AuditLogEntries.CountAsync(a => a.EntityType == AlcoholRetentionService.RunEntityType && a.ChangeType == AlcoholRetentionService.BatchChangeType)).Should().Be(1);

            var resumed = await Service(db).RunAsync(apply: true, maxRecipes: 100000, resume: true);
            resumed.Completed.Should().BeTrue();
            resumed.ResumedRunUtc.Should().Be(stopped.StampUtc, "a resumed run goes on under the stamp of the run it continues");
            resumed.StampUtc.Should().Be(stopped.StampUtc);
            (await StoredAsync(db)).Should().Be(recipes.Count);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == AlcoholRetentionService.ChangeType)).Should().Be(recipes.Count, "every line written once, journaled once");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == AlcoholRetentionService.ChangeType && a.Timestamp < stopped.StampUtc)).Should().Be(0);
            (await db.AuditLogEntries.AnyAsync(a => a.ChangeType == AlcoholRetentionService.DoneChangeType)).Should().BeTrue();
            (await Service(db).RunAsync(apply: true, maxRecipes: 100000, resume: true)).Changed.Should().Be(0, "a finished run is not resumed; a new run finds nothing to change");

            var undoDry = await Service(db).UndoAsync(stopped.StampUtc);
            undoDry.Restored.Should().Be(recipes.Count);
            var undo = await Service(db).UndoAsync(stopped.StampUtc, apply: true);
            (undo.Restored, undo.Skipped, undo.RecipesRecalculated).Should().Be((recipes.Count, 0, recipes.Count));
            (await StoredAsync(db)).Should().Be(0, "the undo by the run's stamp takes back the batches before the stop and after the resume");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == AlcoholRetentionService.ChangeType)).Should().Be(0);
            (await db.AuditLogEntries.CountAsync(a => a.EntityType == AlcoholRetentionService.RunEntityType)).Should().Be(0, "a run taken back leaves no mark a resume would go on with");
            (await db.RecipeNutrition.AsNoTracking().OrderBy(n => n.RecipeId).ThenBy(n => n.NutrientId).Select(n => new { n.RecipeId, n.NutrientId, n.Amount }).ToListAsync())
                .Should().Equal(labels, "every recipe's label is as it was");
        }

        [Fact]
        public async Task A_dry_run_counts_what_the_whole_apply_then_does()
        {
            using var db = NewContext();
            await SeedAsync(db, AlcoholRetentionService.Chunk + 5);

            var dry = await Service(db).RunAsync(apply: false, maxRecipes: 100000, sampleSize: 0);
            var applied = await Service(db).RunAsync(apply: true, maxRecipes: 100000, sampleSize: 0);

            (applied.Batches, applied.Lines, applied.Changed, applied.RecipesTouched, applied.Estimated, applied.UpperBound, applied.RecipesUpperBound)
                .Should().Be((dry.Batches, dry.Lines, dry.Changed, dry.RecipesTouched, dry.Estimated, dry.UpperBound, dry.RecipesUpperBound));
            applied.ByKind.Should().BeEquivalentTo(dry.ByKind);
            (applied.RecipesRecalculated, applied.BatchesWritten, applied.Completed).Should().Be((dry.RecipesTouched, 2, true));
        }

        [Fact]
        public async Task On_a_relational_database_a_write_to_a_recipe_holds_it_through_the_write_log_and_removes_no_state_row()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").AddInterceptors(DietRecipeStateClearer.Shared).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var line = new RecipeIngredientEntity { RecipeId = 41, IngredientId = 7, SortOrder = 0, Quantity = 1m, MeasurementId = Gram, RawLine = "1 cup wine" };
            db.Attach(line);
            line.AlcoholRetention = 0.35m;

            DietRecipeStateClearer.Touched(db).Should().Equal(41L);
            var interception = await DietRecipeStateClearer.Shared.SavingChangesAsync(
                new Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData(null!, (_, _) => string.Empty, db), default);

            db.ChangeTracker.Entries<DietRecipeStateEntity>().Should().BeEmpty("a state row removed by the save fails it when the build job replaced that row first (T15)");
            db.ChangeTracker.Entries<DietGateWriteEntity>().Select(e => (e.State, e.Entity.RecipeId)).Should().Equal((EntityState.Added, (long?)41L));
        }
    }
}
