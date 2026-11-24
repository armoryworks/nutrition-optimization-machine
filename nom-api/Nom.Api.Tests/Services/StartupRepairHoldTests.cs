using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The start-up repair runs at every API start. It leaves alone the lines of a recipe that a
    /// journaled line operation still holds, so the operation's undo restores every row it wrote
    /// however often the API restarted in between, and it journals every amount it does change.
    /// The compound lines here are the staging rows the repair filled wrongly on 2026-11-20.
    /// </summary>
    public class StartupRepairHoldTests
    {
        private const long Gram = 1, Piece = 3, Pound = 6, Ounce = 7, Cup = 11, Tablespoon = 12, Teaspoon = 13, Pinch = 21;
        private const long Uncurated = 9000, Curated = 9003;
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private sealed record Row(long IngredientId, int SortOrder, decimal Quantity, long MeasurementId, string RawLine, string? LineKind);

        private static async Task<ApplicationDbContext> NewContextAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            BaseMeasurementEntity Unit(long id, string name, long category) =>
                new() { Id = id, Name = name, Symbol = name.ToLowerInvariant(), MeasurementCategoryId = category, BaseUnitConversionFactor = 1m };
            db.Set<BaseMeasurementEntity>().AddRange(
                Unit(Gram, "Gram", 1), Unit(Piece, "Piece", 3), Unit(Pound, "Pound", 1), Unit(Ounce, "Ounce", 1), Unit(Cup, "Cup", 2),
                Unit(Tablespoon, "Tablespoon", 2), Unit(Teaspoon, "Teaspoon", 2), Unit(Pinch, "Pinch", 2));
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<Dictionary<string, long>> FoodsAsync(ApplicationDbContext db, params string[] names)
        {
            var foods = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var curated = !name.Contains(" and ", StringComparison.OrdinalIgnoreCase);
                var food = new IngredientEntity
                {
                    Name = name,
                    CurationStatusId = curated ? Curated : Uncurated,
                    IngredientNutrients = curated ? new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } } : new List<IngredientNutrientEntity>(),
                };
                db.Ingredients.Add(food);
                await db.SaveChangesAsync();
                foods[name] = food.Id;
            }
            return foods;
        }

        private static async Task<long> RecipeAsync(
            ApplicationDbContext db, Dictionary<string, long> foods, string[] steps,
            params (string Food, int SortOrder, decimal Quantity, long MeasurementId, string RawLine, string? LineKind)[] lines)
        {
            var recipe = new RecipeEntity
            {
                Name = "Scraped",
                ScrapedAtUtc = Imported,
                CurationStatusId = Uncurated,
                RecipeSteps = steps.Select((s, i) => new RecipeStepEntity { StepNumber = i + 1, Summary = "Step", Description = s }).ToList(),
                RecipeIngredients = lines.Select(l => new RecipeIngredientEntity
                {
                    IngredientId = foods[l.Food], SortOrder = l.SortOrder, Quantity = l.Quantity, MeasurementId = l.MeasurementId,
                    RawLine = l.RawLine, LineKind = l.LineKind, CreatedDate = Imported,
                }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipe.Id;
        }

        private static async Task<List<Row>> RowsAsync(ApplicationDbContext db, long recipeId) =>
            (await db.RecipeIngredients.AsNoTracking().IgnoreQueryFilters().Where(ri => ri.RecipeId == recipeId && !ri.IsDeleted)
                .OrderBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId).ToListAsync())
            .Select(ri => new Row(ri.IngredientId, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine, ri.LineKind))
            .ToList();

        private static async Task RestartAsync(ApplicationDbContext db, int times = 2)
        {
            for (var i = 0; i < times; i++)
            {
                await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 500);
                db.ChangeTracker.Clear();
            }
        }

        public static IEnumerable<object[]> WrongFills => new[]
        {
            new object[]
            {
                "22136 vinegar 32 Cup", new[] { "Vinegar", "Water", "Brown Sugar", "white mustard seed" }, 0m, Gram, "vinegar and water",
                new[]
                {
                    "Cut tomatoes, cabbage, onions, and cucumbers in small pieces, and salt over night.",
                    "In the morning, drain off the brine; put on vinegar and water, half and half; let stand twenty-four hours; drain again; put in the spices.",
                    "Boil two gallons of vinegar with three pounds of brown sugar; pour over while hot; do this three mornings; then add one-half pound of mustard; stir in when nearly cold.",
                },
                new[] { ("Brown Sugar", 0m, Gram, "brown sugar"), ("white mustard seed", 0.5m, Pound, "half pound of white mustard seed") },
            },
            new object[]
            {
                "92891 water 3 Piece", new[] { "Milk", "Water", "Salt" }, 3m, Piece, "cold milk and water",
                new[]
                {
                    "Cut a pair of chickens into pieces, as for carving; and wash them through two or three waters.",
                    "Then lay them in a large pan, sprinkle them slightly with salt, and fill up the pan with boiling water.",
                    "Pour on cold milk and water (mixed in equal portions) sufficient to cover the chickens well.",
                },
                new[] { ("Salt", 0m, Gram, "salt") },
            },
            new object[]
            {
                "130616 water 1 Piece", new[] { "Salt", "Water", "Mace" }, 1m, Piece, "Salt and water",
                new[]
                {
                    "Wash the salmon roes in salt and water for a minute and dry them well.",
                    "Tie leather over and subject them to the action of a water bath for three hours.",
                },
                new[] { ("Mace", 0.25m, Ounce, "1/4 oz. mace, in finest powder") },
            },
            new object[]
            {
                "119546 Sugar 1 Cup", new[] { "Sugar", "Cinnamon", "Butter" }, 0m, Gram, "Sugar and cinnamon",
                new[]
                {
                    "Place a small amount of apple in the center of each square. Sprinkle lightly with sugar and cinnamon. Dot generously with butter.",
                    "Then pour over them a syrup of 1 cup sugar and ½ cup water that has been heated until all sugar is dissolved.",
                },
                new[] { ("Butter", 0m, Gram, "Butter") },
            },
            new object[]
            {
                "89827 sugar 1 Pinch", new[] { "Salt", "Sugar", "Butter" }, 1m, Pinch, "salt and sugar",
                new[]
                {
                    "Put in a saute pan one dozen peeled shallots and simmer in two ounces of butter until golden yellow.",
                    "Then add one quart of shelled peas, one cup of water, a little salt and a pinch of sugar.",
                },
                new[] { ("Butter", 2m, Ounce, "two ounces of butter") },
            },
        };

        [Theory]
        [MemberData(nameof(WrongFills))]
        public async Task A_split_line_keeps_its_parts_unmeasured_through_restarts_and_its_undo_restores_the_recipe(
            string staging, string[] foodNames, decimal quantity, long measurementId, string line, string[] steps,
            (string Food, decimal Quantity, long MeasurementId, string RawLine)[] others)
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, foodNames.Append(line).ToArray());
            var recipe = await RecipeAsync(db, foods, steps,
                others.Select(o => (o.Food, 0, o.Quantity, o.MeasurementId, o.RawLine, (string?)null))
                    .Prepend((line, 0, quantity, measurementId, line, (string?)null)).ToArray());
            var before = await RowsAsync(db, recipe);
            var started = DateTime.UtcNow;

            var split = await new CompoundLineService(db, new NoUser()).ApplyAsync(CompoundLineSplitter.Rules.ToList());
            db.ChangeTracker.Clear();
            split.Split.Should().Be(1, staging);
            var parts = await RowsAsync(db, recipe);
            await RestartAsync(db);

            (await RowsAsync(db, recipe)).Should().Equal(parts, $"{staging}: no part of a split line takes an amount from the method");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ScrapedRecipeRepairService.FillChangeType)).Should().Be(0);
            var undone = await new LineRelinkService(db, new NoUser()).UndoAsync(started, apply: true);
            db.ChangeTracker.Clear();
            undone.Unsplit.Should().Be(1, staging);
            undone.Skipped.Should().Be(0, staging);
            (await RowsAsync(db, recipe)).Should().Equal(before, staging);
        }

        [Fact]
        public async Task An_unfold_undone_after_restarts_restores_every_row()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Water", "Flour");
            var recipe = await RecipeAsync(db, foods, new[] { "Boil the water and stir in the flour." },
                ("Water", 0, 2m, Piece, "2 qts. water + 1 cup water", null), ("Flour", 0, 2m, Cup, "2 cups flour", null));
            var before = await RowsAsync(db, recipe);
            var started = DateTime.UtcNow;

            var unfolded = await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);
            db.ChangeTracker.Clear();
            unfolded.Folds.Should().Be(1);
            var after = await RowsAsync(db, recipe);
            after.Should().Contain(new Row(foods["Water"], 0, 2m, Piece, "2 qts. water", null), "the unfold keeps the stored amount on the first line");
            await RestartAsync(db);

            (await RowsAsync(db, recipe)).Should().Equal(after, "the start-up re-read of pieces waits until the unfold is undone");
            var undone = await new LineRelinkService(db, new NoUser()).UndoAsync(started, apply: true);
            db.ChangeTracker.Clear();
            undone.Refolded.Should().Be(1);
            undone.Skipped.Should().Be(0);
            (await RowsAsync(db, recipe)).Should().Equal(before);
        }

        [Fact]
        public async Task A_label_review_and_its_reread_undone_after_restarts_restore_every_row()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Milk", "Beans", "Flour");
            var recipe = await RecipeAsync(db, foods, new[] { "Warm the milk." },
                ("Milk", 0, 2m, Piece, "2 qts. milk", RecipeIngredientLineKind.Note),
                ("Beans", 0, 1m, Piece, "1 (15 oz) can beans", RecipeIngredientLineKind.Note),
                ("Flour", 0, 2m, Cup, "2 cups flour", null));
            var before = await RowsAsync(db, recipe);
            var started = DateTime.UtcNow;

            var review = await new RecipeLineLabelReviewService(db, new NoUser()).ReviewAsync(apply: true);
            db.ChangeTracker.Clear();
            review.Changed.Should().Be(2);
            review.Reread.Should().Be(1);
            var after = await RowsAsync(db, recipe);
            await RestartAsync(db);

            (await RowsAsync(db, recipe)).Should().Equal(after, "the start-up re-read of pieces waits until the review is undone");
            var reread = await new PackageLineService(db, new NoUser()).UndoAsync(started, apply: true);
            db.ChangeTracker.Clear();
            var labels = await new RecipeLineLabelReviewService(db, new NoUser()).UndoAsync(started, apply: true);
            db.ChangeTracker.Clear();
            (reread.Restored, reread.Skipped).Should().Be((1, 0));
            (labels.Restored, labels.Skipped).Should().Be((2, 0));
            (await RowsAsync(db, recipe)).Should().Equal(before);
        }

        [Fact]
        public async Task A_recipe_with_no_open_line_journal_is_still_repaired_and_every_change_is_journaled_and_undone_as_a_batch()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Butter", "Milk", "Flour");
            var recipe = await RecipeAsync(db, foods, new[] { "Put two ounces of butter in the saucepan and add the flour." },
                ("Butter", 0, 0m, Gram, "butter", null), ("Milk", 0, 2m, Piece, "2 qts. milk", null), ("Flour", 0, 0m, Gram, "1 cup flour", null));
            var before = await RowsAsync(db, recipe);
            var started = DateTime.UtcNow;

            await RestartAsync(db);

            var rows = await RowsAsync(db, recipe);
            rows.Should().Contain(new Row(foods["Butter"], 0, 2m, Ounce, "butter", null));
            rows.Should().Contain(new Row(foods["Milk"], 0, 8m, Cup, "2 qts. milk", null));
            rows.Should().Contain(new Row(foods["Flour"], 0, 1m, Cup, "1 cup flour", null));
            var journal = await db.AuditLogEntries.Where(a => a.ChangeType == ScrapedRecipeRepairService.FillChangeType).ToListAsync();
            journal.Should().HaveCount(3).And.OnlyContain(a => a.EntityId == recipe && a.EntityType == ScrapedRecipeRepairService.LineEntityType);
            (await db.RecipeIngredients.Where(ri => ri.RecipeId == recipe).ToListAsync()).Should().OnlyContain(ri => ri.LastModifiedDate >= started);

            var service = new ScrapedRecipeRepairService(db, new RecipeVettingService());
            var dryRun = await service.UndoFillsAsync(started);
            (dryRun.Applied, dryRun.Entries, dryRun.Restored, dryRun.Skipped).Should().Be((false, 3, 3, 0));
            (await RowsAsync(db, recipe)).Should().Equal(rows, "a dry run writes nothing");
            var undone = await service.UndoFillsAsync(started, apply: true);
            (undone.Restored, undone.Skipped).Should().Be((3, 0));
            (await RowsAsync(db, recipe)).Should().Equal(before);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Theory]
        [InlineData("RecipeIngredient", "LineRelink", true)]
        [InlineData("RecipeIngredient", "LineReread", true)]
        [InlineData("RecipeIngredient", "AiRepair", true)]
        [InlineData("RecipeIngredient", "AiRepairRevert", true)]
        [InlineData("RecipeIngredient", "LineKindReview", true)]
        [InlineData("FoodCatalogProposal", "LinkApply", true)]
        [InlineData("FoodCatalogProposal", "LinkApplyLeft", false)]
        [InlineData("RecipeIngredient", "LineClassify", false)]
        [InlineData("RecipeIngredient", "StartupFill", false)]
        [InlineData("Recipe", "StepSplit", false)]
        public async Task An_open_line_journal_holds_the_recipe_and_one_no_undo_reads_does_not(string entityType, string changeType, bool holds)
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Butter", "Milk");
            var recipe = await RecipeAsync(db, foods, new[] { "Put two ounces of butter in the saucepan." },
                ("Butter", 0, 0m, Gram, "butter", null), ("Milk", 0, 2m, Piece, "2 qts. milk", null));
            var linked = entityType == LinkApplyJournal.EntityType;
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = entityType,
                EntityId = linked ? 77 : recipe,
                ChangeType = changeType,
                PropertyName = linked ? LinkApplyJournal.Line : "LineKind[1:0]",
                OldValue = linked ? LinkApplyJournal.Place(recipe, 5, 0) : null,
                NewValue = linked ? LinkApplyJournal.Place(recipe, foods["Butter"], 0) : null,
                Timestamp = Imported,
                ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var before = await RowsAsync(db, recipe);

            await RestartAsync(db);

            var after = await RowsAsync(db, recipe);
            if (holds) after.Should().Equal(before);
            else after.Should().Contain(new Row(foods["Butter"], 0, 2m, Ounce, "butter", null)).And.Contain(new Row(foods["Milk"], 0, 8m, Cup, "2 qts. milk", null));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task The_line_classify_lane_leaves_a_held_recipe_for_after_the_undo(bool held)
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Butter", "Flour");
            var recipe = await RecipeAsync(db, foods, new[] { "Rub the butter into the flour." },
                ("Butter", 0, 0m, Gram, "For the pastry:", null), ("Flour", 0, 2m, Cup, "2 cups flour", null));
            if (held)
            {
                db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = LineRelinkService.RelinkEntityType, EntityId = recipe, ChangeType = CompoundLineService.SplitChangeType,
                    PropertyName = "RawLine", Timestamp = Imported, ChangedByPersonId = SystemConstants.SystemPersonId,
                });
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
            var before = await RowsAsync(db, recipe);
            var model = new Moq.Mock<IRecipeClassificationModel>();
            model.SetupGet(m => m.IsConfigured).Returns(false);

            await new RecipeLineClassificationService(db, new RecipeVettingService(), model.Object,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecipeLineClassificationService>.Instance).ClassifyBatchAsync(0, 10);
            db.ChangeTracker.Clear();

            var after = await RowsAsync(db, recipe);
            if (held) after.Should().Equal(before);
            else after.Should().Contain(new Row(foods["Butter"], 0, 0m, Gram, "For the pastry:", RecipeIngredientLineKind.Note));
        }

        [Fact]
        public async Task A_fill_undo_skips_a_line_changed_since()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Flour");
            var recipe = await RecipeAsync(db, foods, new[] { "Sift the flour." }, ("Flour", 0, 0m, Gram, "1 cup flour", null));
            var started = DateTime.UtcNow;
            await RestartAsync(db);
            var row = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == recipe);
            row.Quantity = 2m;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undone = await new ScrapedRecipeRepairService(db, new RecipeVettingService()).UndoFillsAsync(started, apply: true);

            (undone.Restored, undone.Skipped).Should().Be((0, 1));
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == recipe)).Quantity.Should().Be(2m);
        }

        [Theory]
        [InlineData("one of rosemary", "Rosemary", "Stuff the fowl with one rosemary sprig and roast it.")]
        [InlineData("one of thyme", "Thyme", "Stuff the fowl with one thyme sprig and roast it.")]
        public async Task A_count_read_back_as_the_same_count_is_neither_written_nor_journaled(string line, string food, string method)
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, food, "Flour");
            (await db.Ingredients.SingleAsync(i => i.Id == foods[food])).GramsPerItem = 5m;
            await db.SaveChangesAsync();
            var recipe = await RecipeAsync(db, foods, new[] { method }, (food, 0, 1m, Piece, line, null), ("Flour", 1, 0m, Gram, "1 cup flour", null));
            var before = await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe && ri.SortOrder == 0);

            var result = await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 500);

            result.RowsRepaired.Should().Be(1, "only the flour line changes");
            (await db.AuditLogEntries.Where(a => a.ChangeType == ScrapedRecipeRepairService.FillChangeType).Select(a => a.PropertyName).ToListAsync())
                .Should().Equal($"LineKind[{foods["Flour"]}:1]");
            var after = await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe && ri.SortOrder == 0);
            (after.Quantity, after.MeasurementId, after.LastModifiedDate).Should().Be((1m, Piece, before.LastModifiedDate));
        }

        [Fact]
        public async Task A_count_of_a_liquid_its_line_states_is_kept_by_the_line_guard()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Moselle Wine", "Flour");
            var recipe = await RecipeAsync(db, foods, new[] { "Pour in one wine glass of the stock." },
                ("Moselle Wine", 0, 1m, Piece, "one of Moselle wine (or hock)", null), ("Flour", 1, 1m, Cup, "1 cup flour", null));

            await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 500);

            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe && ri.SortOrder == 0)).Quantity
                .Should().Be(1m, "the stored one stands in the line, and the line guard never empties a grounded amount");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ScrapedRecipeRepairService.FillChangeType)).Should().Be(0);
        }

        [Fact]
        public async Task A_count_of_pieces_from_the_method_is_not_taken_for_a_food_with_no_weight_per_item()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Rosemary", "Flour");
            var recipe = await RecipeAsync(db, foods, new[] { "Stuff the fowl with one rosemary sprig and roast it." },
                ("Rosemary", 0, 0m, Gram, "rosemary", null), ("Flour", 1, 1m, Cup, "1 cup flour", null));

            await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 500);

            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe && ri.SortOrder == 0)).Quantity.Should().Be(0m);
        }

        [Theory]
        [InlineData("reference", "Brown Sauce", true)]
        [InlineData("equipment", "Brown Sauce", true)]
        [InlineData("ingredient", "Brown Sauce", true)]
        [InlineData("note", "Brown Sauce", true)]
        [InlineData("note", "For the Brown Sauce:", false)]
        public async Task The_amount_the_method_states_fills_a_line_of_a_kind_that_takes_one_or_a_note_it_measures_and_a_heading_is_held_with_its_reason(string kind, string rawLine, bool filled)
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Brown Sauce", "Pigeons");
            var recipe = await RecipeAsync(db, foods, new[] { "Make two cups of Brown Sauce and pour it over the pigeons." },
                ("Brown Sauce", 0, 0m, Gram, rawLine, kind), ("Pigeons", 1, 4m, Piece, "4 pigeons", null));

            var batch = await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 500);

            var line = await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe && ri.SortOrder == 0);
            (line.Quantity, line.MeasurementId, line.LineKind).Should().Be(filled ? (2m, Cup, kind) : (0m, Gram, kind));
            (batch.GuardReplaced, batch.GuardHeld).Should().Be(filled ? (1, 0) : (0, 1));
            var holds = await db.LineGuardHolds.AsNoTracking().ToListAsync();
            if (filled)
            {
                holds.Should().BeEmpty();
                return;
            }
            batch.GuardHeldByReason.Should().Equal(new Dictionary<string, int> { [LineAmountGuard.Reasons.Kind] = 1 });
            var hold = holds.Single();
            (hold.Origin, hold.Reason, hold.RawLine, hold.StoredKind, hold.StoredQuantity, hold.RebuiltQuantity, hold.RebuiltUnit, hold.RebuiltFromMethod)
                .Should().Be((LineGuardHoldOrigin.StartupFill, LineAmountGuard.Reasons.Kind, rawLine, "note", 0m, 2m, "Cup", true));
        }

        [Fact]
        public async Task A_held_line_is_stored_once_through_restarts_and_released_when_the_guard_no_longer_holds_it()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Brine", "Pork");
            var recipe = await RecipeAsync(db, foods, new[] { "Make a cup of brine and pour it over the pork." },
                ("Brine", 0, 0m, Gram, "For the brine:", "note"), ("Pork", 1, 2m, Pound, "2 lb pork", null));

            await RestartAsync(db, 3);

            var hold = await db.LineGuardHolds.AsNoTracking().SingleAsync();
            (hold.RecipeId, hold.ResolvedAt).Should().Be((recipe, (DateTime?)null));

            var row = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == recipe && ri.SortOrder == 0);
            row.LineKind = "ingredient";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await RestartAsync(db, 1);

            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe && ri.SortOrder == 0)).Quantity.Should().Be(1m);
            var released = await db.LineGuardHolds.AsNoTracking().SingleAsync();
            (released.ResolvedAt != null, released.Resolution).Should().Be((true, LineGuardHolds.Released));
        }

        [Fact]
        public async Task A_hold_follows_its_line_to_a_new_position_and_is_neither_released_nor_made_again()
        {
            using var db = await NewContextAsync();
            var foods = await FoodsAsync(db, "Brine", "Pork");
            var recipe = await RecipeAsync(db, foods, new[] { "Make a cup of brine and pour it over the pork." },
                ("Brine", 0, 0m, Gram, "For the brine:", "note"), ("Pork", 1, 2m, Pound, "2 lb pork", null));
            await RestartAsync(db, 1);
            var held = await db.LineGuardHolds.AsNoTracking().SingleAsync();

            var rows = await db.RecipeIngredients.Where(ri => ri.RecipeId == recipe).ToListAsync();
            db.RecipeIngredients.RemoveRange(rows);
            await db.SaveChangesAsync();
            db.RecipeIngredients.AddRange(rows.Select(r => new RecipeIngredientEntity
            {
                RecipeId = r.RecipeId, IngredientId = r.IngredientId, SortOrder = 1 - r.SortOrder, Quantity = r.Quantity, MeasurementId = r.MeasurementId,
                RawLine = r.RawLine, LineKind = r.LineKind, CreatedDate = r.CreatedDate,
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await RestartAsync(db, 2);

            var holds = await db.LineGuardHolds.AsNoTracking().ToListAsync();
            holds.Should().ContainSingle("staging checkpoint 6 (R22): after the order pass a start released 152 holds and made 152 new rows for the same lines");
            (holds[0].Id, holds[0].SortOrder, holds[0].ResolvedAt).Should().Be((held.Id, 1, (DateTime?)null));
        }
    }
}
