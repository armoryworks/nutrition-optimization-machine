using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Staging checkpoint 9, U11 and U1: the w01 unload with its lines failed on the 30 s command timeout as one save, and a
    /// verified-wording batch write held 82 % of recipes for two minutes. A load and an unload now write in batches of
    /// <see cref="VerifiedWordingService.Chunk"/> rows, each in its own transaction with its own journal time, an unload
    /// stopped part way goes on when it is run again, and a write holds only the recipes whose lines on a model-picked food
    /// name the wording the write proves or unproves. The same at staging size, with the trigger and the 30 s timeout, is
    /// <c>DietGateStorePostgresTests.A_wording_batch_of_staging_size_...</c>.
    /// </summary>
    public class VerifiedWordingBatchTests
    {
        private const string Batch = "w01";
        private static readonly int Rows = VerifiedWordingService.Chunk + 20;

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static DbContextOptions<ApplicationDbContext> Options() =>
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        private static VerifiedWordingService Wordings(ApplicationDbContext db) => new(db, new NoUser());

        private static LineRelinkService Relink(ApplicationDbContext db) => new(db, new NoUser());

        private static async Task<IngredientEntity> SeedAsync(ApplicationDbContext db)
        {
            var juice = new IngredientEntity
            {
                Name = "Apple Juice", FdcId = "173933", CurationStatusId = (long)CurationStatusEnum.Curated,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            db.Ingredients.Add(juice);
            var sources = Enumerable.Range(0, Rows).Select(i => new IngredientEntity { Name = $"cider {i}", CurationStatusId = (long)CurationStatusEnum.NonCurated }).ToList();
            db.Ingredients.AddRange(sources);
            await db.SaveChangesAsync();
            db.Recipes.AddRange(sources.Select((s, i) => new RecipeEntity
            {
                Name = $"Mulled cider {i}", ScrapedAtUtc = DateTime.UtcNow,
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = s.Id, SortOrder = 0, Quantity = 1, MeasurementId = 1, RawLine = $"1 quart cider {i}" } },
            }));
            await db.SaveChangesAsync();
            return juice;
        }

        private static List<VerifiedWordingRow> RowsFor(IngredientEntity food) =>
            Enumerable.Range(0, Rows).Select(i => new VerifiedWordingRow { Row = $"r{i}", Wording = $"cider {i}", Verdict = IngredientWordingVerdict.Food, FoodId = food.Id, FoodName = food.Name }).ToList();

        private static Task<string> LinesAsync(ApplicationDbContext db) =>
            db.RecipeIngredients.AsNoTracking().OrderBy(l => l.RecipeId).ThenBy(l => l.SortOrder)
                .Select(l => $"{l.RecipeId}:{l.IngredientId}:{l.SortOrder}:{l.RawLine}").ToListAsync().ContinueWith(t => string.Join("|", t.Result));

        [Fact]
        public async Task A_load_and_an_unload_of_more_rows_than_a_batch_write_in_batches_and_an_unload_stopped_part_way_goes_on_when_run_again()
        {
            var options = Options();
            IngredientEntity juice;
            string before;
            await using (var db = new ApplicationDbContext(options))
            {
                juice = await SeedAsync(db);
                before = await LinesAsync(db);
                var dry = await Wordings(db).LoadAsync(RowsFor(juice), Batch);
                var loaded = await Wordings(db).LoadAsync(RowsFor(juice), Batch, apply: true);
                (dry.Batches, loaded.Batches, loaded.BatchesWritten, loaded.Added).Should().Be((2, 2, 2, Rows));
                (await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch)).Moved.Should().Be(Rows);
            }

            await using (var db = new ApplicationDbContext(options))
            {
                var dry = await Wordings(db).UnloadAsync(Batch, withLines: true);
                (dry.Batches, dry.Removed, dry.Lines!.LineMoves).Should().Be((2, Rows, Rows));
                var stopping = Wordings(db);
                stopping.BeforeBatchSave = index => index == 1 ? throw new TimeoutException("Timeout during reading attempt") : Task.CompletedTask;
                var stop = async () => await stopping.UnloadAsync(Batch, apply: true);
                await stop.Should().ThrowAsync<TimeoutException>();
            }

            await using (var db = new ApplicationDbContext(options))
            {
                (await db.IngredientWordings.CountAsync(w => w.Batch == Batch)).Should().Be(Rows - VerifiedWordingService.Chunk, "the first batch was committed with its own journal entries; the failed one wrote nothing");
                var entries = await db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == VerifiedWordingService.WordingEntityType && a.ChangeType == VerifiedWordingService.VerifyChangeType && a.NewValue == null)
                    .ToListAsync();
                entries.Should().HaveCount(VerifiedWordingService.Chunk);
                entries.Select(a => a.Timestamp).Distinct().Should().ContainSingle("a batch's entries carry the batch's own time");

                var resumed = await Wordings(db).UnloadAsync(Batch, apply: true);
                (resumed.Batches, resumed.BatchesWritten, resumed.Removed, resumed.Completed).Should().Be((1, 1, Rows - VerifiedWordingService.Chunk, true), "the same unload again goes on with the rows still in the batch");
                (await db.IngredientWordings.CountAsync(w => w.Batch == Batch)).Should().Be(0);
            }
        }

        [Fact]
        public async Task An_unload_with_its_lines_in_batches_and_a_reload_leave_every_line_where_it_was()
        {
            await using var db = new ApplicationDbContext(Options());
            var juice = await SeedAsync(db);
            (await Wordings(db).LoadAsync(RowsFor(juice), Batch, apply: true)).Added.Should().Be(Rows);
            (await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch)).Moved.Should().Be(Rows);
            var linked = await LinesAsync(db);

            var unloaded = await Wordings(db).UnloadAsync(Batch, apply: true, withLines: true);
            (unloaded.BatchesWritten, unloaded.Removed, unloaded.Lines!.Undone!.Restored).Should().Be((2, Rows, Rows), "each batch takes back its own wordings' lines");
            (await Wordings(db).LoadAsync(RowsFor(juice), Batch, apply: true)).Added.Should().Be(Rows);
            (await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch)).Moved.Should().Be(Rows);

            (await LinesAsync(db)).Should().Be(linked);
        }

        [Fact]
        public async Task A_food_row_holds_only_the_recipes_whose_lines_on_its_model_picked_food_name_the_picks_wording()
        {
            await using var db = new ApplicationDbContext(Options());
            var salt = new IngredientEntity { Name = "Salt", CurationStatusId = (long)CurationStatusEnum.Curated };
            var pepper = new IngredientEntity { Name = "Black Pepper", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(salt, pepper);
            await db.SaveChangesAsync();
            RecipeEntity Recipe(string name, IngredientEntity food, string line) => new()
            {
                Name = name, RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = food.Id, SortOrder = 0, Quantity = 1, MeasurementId = 1, RawLine = line } },
            };
            var named = Recipe("Brine", salt, "2 tbsp. Kosher Salt");
            var plain = Recipe("Soup", salt, "1 tsp salt");
            var elsewhere = Recipe("Rub", pepper, "1 tsp kosher salt");
            db.Recipes.AddRange(named, plain, elsewhere);
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "Kosher Salt", ProposedValue = salt.Id.ToString(),
                Source = "ai:qwen", Status = FoodProposalStatus.Applied, CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            (await DietGateWordingHolds.RecipesAsync(db, new[] { new DietGateWordingHolds.Written("kosher salt", IngredientWordingVerdict.Food, salt.Id, false) }))
                .Should().Equal(new[] { named.Id }, "staging checkpoint 9, U1: the w02 load held every recipe with a line on Salt; only the line the pick put there by its wording reads differently");
            (await DietGateWordingHolds.RecipesAsync(db, new[] { new DietGateWordingHolds.Written("kosher salt", IngredientWordingVerdict.Food, salt.Id, true) }))
                .Should().BeEmpty("a deleted row is read by nothing");
            (await DietGateWordingHolds.RecipesAsync(db, new[] { new DietGateWordingHolds.Written("sea salt", IngredientWordingVerdict.Food, salt.Id, false) }))
                .Should().BeEmpty("no pick of that wording");
            (await DietGateWordingHolds.RecipesAsync(db, new[] { new DietGateWordingHolds.Written("kosher salt", IngredientWordingVerdict.Food, pepper.Id, false) }))
                .Should().BeEmpty("no pick on that food");
            (await DietGateWordingHolds.RecipesAsync(db, new[] { new DietGateWordingHolds.Written("kosher salt", IngredientWordingVerdict.None, null, false) }))
                .Should().BeEmpty();

            var unproven = await DietGateStoreBuilder.UnprovenAsync(db, default);
            unproven.Should().Contain((salt.Id, "kosher salt"));
            var links = DietGateStoreBuilder.UnprovenOf(unproven);
            DietGateStoreBuilder.OnUnprovenLink(named.RecipeIngredients.Single().RawLine, links[salt.Id]).Should().BeTrue("the line the hold finds is the one the reading reads by its words");
            DietGateStoreBuilder.OnUnprovenLink(plain.RecipeIngredients.Single().RawLine, links[salt.Id]).Should().BeFalse();
        }
    }
}
