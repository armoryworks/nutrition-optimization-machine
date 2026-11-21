using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Recipes are built again in place from their stored source with the import's own line
    /// builder: the order of the source comes back, NOM's parser reads every amount, a line keeps
    /// the food it is on unless that food is junk, and guards keep a person's edit, a planned
    /// recipe's foods and an approved recipe that would end worse. Every write is journaled with
    /// the rows before and after and the undo restores them only where nothing changed since.
    /// </summary>
    public class RecipeReprocessTests
    {
        private const long Operator = 2;
        private const long Piece = 1, Cup = 2, Teaspoon = 3, Tablespoon = 4, Gram = 5, Ounce = 6, Can = 7, Pinch = 9;
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string[] Method = { "Mix everything well in a large bowl.", "Bake for twenty minutes until golden." };

        private sealed class Admin : ICurrentUserService
        {
            public long? PersonId => Operator;
            public long PersonIdOrSystem => Operator;
            public long RequiredPersonId => Operator;
            public string? UserId => "operator";
            public string RequiredUserId => "operator";
        }

        private static async Task<ApplicationDbContext> SeededAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3 },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = Teaspoon, Name = "Teaspoon", Symbol = "tsp", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = Tablespoon, Name = "Tablespoon", Symbol = "tbsp", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = Ounce, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = Can, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, IsContainer = true },
                new BaseMeasurementEntity { Id = Pinch, Name = "Pinch", Symbol = "pinch", MeasurementCategoryId = 2 });
            await db.SaveChangesAsync();
            return db;
        }

        private static RecipeReprocessService Service(ApplicationDbContext db, IRecipeScraperClient? scraper = null) =>
            new(db, new Admin(), scraper ?? new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(), new Mock<IRecipeNutritionService>().Object,
                NullLogger<RecipeReprocessService>.Instance);

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name, bool curated = true)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = (long)(curated ? CurationStatusEnum.Curated : CurationStatusEnum.NonCurated) };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private sealed record Row(IngredientEntity Food, string RawLine, decimal Quantity, long MeasurementId, int SortOrder = 0, string? LineKind = null);

        private static ScraperRecipe Record(string name, IEnumerable<string> lines, IEnumerable<string>? steps = null) => new()
        {
            Name = name,
            Ingredients = lines.Select(l => new ScraperIngredient { RawLine = l }).ToList(),
            Steps = (steps ?? Method).Select((s, i) => new ScraperStep { Order = i + 1, Instruction = s }).ToList(),
        };

        private static async Task<RecipeEntity> RecipeAsync(
            ApplicationDbContext db, ScraperRecipe? record, Row[] rows, CurationStatusEnum status = CurationStatusEnum.NonCurated, IEnumerable<string>? steps = null)
        {
            var recipe = new RecipeEntity
            {
                Name = record?.Name ?? "Typed in",
                CurationStatusId = (long)status,
                ScrapedAtUtc = Imported,
                LicenseStatus = RecipeLicenseStatus.PublicDomain,
                RecipeIngredients = rows.Select(r => new RecipeIngredientEntity
                {
                    IngredientId = r.Food.Id, SortOrder = r.SortOrder, Quantity = r.Quantity, MeasurementId = r.MeasurementId, RawLine = r.RawLine,
                    LineKind = r.LineKind, CreatedDate = Imported, CreatedByPersonId = Operator,
                }).ToList(),
                RecipeSteps = (steps ?? Method).Select((s, i) => new RecipeStepEntity { StepNumber = i + 1, Summary = s, Description = s }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            if (record != null)
            {
                var content = SourceRecord.Content(record);
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity
                {
                    RecipeId = recipe.Id, RawJsonLd = content, Format = SourceRecord.Format(record), ContentSha256 = SourceRecord.Sha256(content), FetchedAtUtc = Imported,
                });
                await db.SaveChangesAsync();
            }
            db.ChangeTracker.Clear();
            return recipe;
        }

        private static Task<List<RecipeIngredientEntity>> LinesAsync(ApplicationDbContext db, long recipeId) =>
            db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking().Where(ri => ri.RecipeId == recipeId).OrderBy(ri => ri.SortOrder).ToListAsync();

        private static RecipeReprocessRequestModel For(params long[] ids) => new() { RecipeIds = ids.ToList() };

        [Fact]
        public async Task A_rebuild_puts_the_lines_back_in_source_order_on_the_same_recipe_and_journals_the_rows_before_and_after()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            var eggs = await FoodAsync(db, "eggs");
            var record = Record("Plain Cake", new[] { "2 cups flour", "1 cup sugar", "3 eggs" });
            var recipe = await RecipeAsync(db, record, new[]
            {
                new Row(eggs, "3 eggs", 3, Piece), new Row(sugar, "1 cup sugar", 1, Cup), new Row(flour, "2 cups flour", 2, Cup),
            });

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.Rebuilt.Should().Be(1);
            result.Counts.OrderChanged.Should().Be(2);
            result.Counts.FoodChanged.Should().Be(0);
            var lines = await LinesAsync(db, recipe.Id);
            lines.Select(l => (l.SortOrder, l.IngredientId, l.Quantity, l.MeasurementId)).Should().Equal((0, flour.Id, 2m, Cup), (1, sugar.Id, 1m, Cup), (2, eggs.Id, 3m, Piece));
            lines.Should().OnlyContain(l => l.CreatedDate == Imported && l.LastModifiedByPersonId == SystemConstants.SystemPersonId);
            var journal = await db.RecipeReprocessJournals.SingleAsync();
            journal.RecipeId.Should().Be(recipe.Id);
            journal.Kind.Should().Be(RecipeReprocessKind.Rebuild);
            journal.BatchId.Should().Be(result.BatchId);
            journal.RuleVersion.Should().Be(RecipeLineBuilder.Version);
            journal.OldRows.Should().Contain("3 eggs");
            journal.NewRows.Should().Contain("2 cups flour");
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == RecipeReprocessService.ReprocessChangeType)).NewValue.Should().StartWith($"journal={journal.Id};");
            (await RecipeLineHold.HeldAsync(db, new[] { recipe.Id })).Should().Contain(recipe.Id);
        }

        [Fact]
        public async Task A_dry_run_shows_each_line_before_and_after_and_writes_nothing()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var record = Record("Sweet Flour", new[] { "1/2 cup sugar", "2 cups flour" });
            var recipe = await RecipeAsync(db, record, new[] { new Row(sugar, "1/2 cup sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup) });

            var result = await Service(db).RebuildAsync(For(recipe.Id));

            result.Applied.Should().BeFalse();
            var line = result.Results.Single().Lines[0];
            (line.OldQuantity, line.OldMeasurementId, line.NewQuantity, line.NewMeasurementId).Should().Be((1m, Piece, 0.5m, Cup));
            line.Changes.Should().Equal("amount");
            (await LinesAsync(db, recipe.Id)).Single(l => l.IngredientId == sugar.Id).Quantity.Should().Be(1m);
            (await db.RecipeReprocessJournals.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task A_line_keeps_the_food_a_link_or_relink_put_it_on_but_a_junk_food_named_with_an_amount_gives_way()
        {
            using var db = await SeededAsync();
            await FoodAsync(db, "sugar", curated: false);
            var granulated = await FoodAsync(db, "Sugars, granulated");
            var junk = await FoodAsync(db, "2 cups flour", curated: false);
            var flour = await FoodAsync(db, "flour");
            var record = Record("Sweet Flour", new[] { "1 cup sugar", "2 cups flour" });
            var recipe = await RecipeAsync(db, record, new[] { new Row(granulated, "1 cup sugar", 1, Cup), new Row(junk, "2 cups flour", 2, Cup) });

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.Counts.FoodChanged.Should().Be(1);
            (await LinesAsync(db, recipe.Id)).Select(l => l.IngredientId).Should().Equal(granulated.Id, flour.Id);
        }

        [Fact]
        public async Task An_uncurated_food_gives_way_to_the_curated_food_a_verified_wording_names()
        {
            using var db = await SeededAsync();
            var wording = await FoodAsync(db, "kosher salt", curated: false);
            var salt = await FoodAsync(db, "Salt, table");
            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = "kosher salt", Verdict = IngredientWordingVerdict.Food, IngredientId = salt.Id, Source = "admin:test", Batch = "b1" });
            await db.SaveChangesAsync();
            var water = await FoodAsync(db, "water");
            var record = Record("Salted Water", new[] { "1 teaspoon kosher salt", "2 cups water" });
            var recipe = await RecipeAsync(db, record, new[] { new Row(wording, "1 teaspoon kosher salt", 1, Teaspoon), new Row(water, "2 cups water", 2, Cup, 1) });
            var resolving = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.ResolveOnImportSetting] = "true" }).Build();
            var service = new RecipeReprocessService(db, new Admin(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(),
                new Mock<IRecipeNutritionService>().Object, NullLogger<RecipeReprocessService>.Instance, resolving);

            await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            (await LinesAsync(db, recipe.Id)).Select(l => l.IngredientId).Should().Equal(wording.Id, water.Id);
            db.ChangeTracker.Clear();
            await service.RebuildAsync(For(recipe.Id), apply: true);

            (await LinesAsync(db, recipe.Id)).Select(l => l.IngredientId).Should().Equal(salt.Id, water.Id);
        }

        [Fact]
        public async Task A_line_the_rebuild_puts_on_a_food_through_a_verified_wording_is_journaled_and_the_rebuild_undo_retires_the_entry()
        {
            using var db = await SeededAsync();
            var wording = await FoodAsync(db, "kosher salt", curated: false);
            var salt = await FoodAsync(db, "Salt, table");
            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = "kosher salt", Verdict = IngredientWordingVerdict.Food, IngredientId = salt.Id, Source = "admin:test", Batch = "b1" });
            await db.SaveChangesAsync();
            var record = Record("Salted Water", new[] { "1 teaspoon kosher salt" });
            var recipe = await RecipeAsync(db, record, new[] { new Row(wording, "1 teaspoon kosher salt", 1, Teaspoon) });
            var resolving = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.ResolveOnImportSetting] = "true" }).Build();
            var service = new RecipeReprocessService(db, new Admin(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(),
                new Mock<IRecipeNutritionService>().Object, NullLogger<RecipeReprocessService>.Instance, resolving);

            var applied = await service.RebuildAsync(For(recipe.Id), apply: true);

            var resolved = await db.AuditLogEntries.SingleAsync(a => a.ChangeType == LineRelinkService.WordingResolveChangeType);
            (resolved.EntityId, resolved.OldValue, resolved.NewValue).Should().Be((recipe.Id, "kosher salt", $"{salt.Id}@0"));
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == RecipeReprocessService.ReprocessChangeType)).Timestamp.Should().Be(resolved.Timestamp,
                "the entry is the rebuild's own, so a later retract does not skip it as older than the rebuild");

            db.ChangeTracker.Clear();
            await service.UndoAsync(null, applied.BatchId, apply: true);

            (await LinesAsync(db, recipe.Id)).Select(l => l.IngredientId).Should().Equal(wording.Id);
            (await db.AuditLogEntries.AnyAsync(a => a.ChangeType == LineRelinkService.WordingResolveChangeType)).Should().BeFalse(
                "a retract must not move a line the rebuild undo already put back");
        }

        [Fact]
        public async Task The_rebuild_fills_a_line_from_the_method_and_marks_a_heading_as_a_note()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var salt = await FoodAsync(db, "salt");
            var heading = await FoodAsync(db, "For the brine", curated: false);
            var steps = new[] { "Bring two quarts of water to the boil in a large kettle.", "Stir in the salt and let it cool before using." };
            var record = Record("Brine", new[] { "For the brine:", "water", "1 tablespoon salt" }, steps);
            var recipe = await RecipeAsync(db, record, new[]
            {
                new Row(heading, "For the brine:", 0, Piece), new Row(water, "water", 0, Piece), new Row(salt, "1 tablespoon salt", 1, Tablespoon),
            }, steps: steps);

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.Counts.FilledFromMethod.Should().Be(1);
            var lines = await LinesAsync(db, recipe.Id);
            lines[0].LineKind.Should().Be(RecipeIngredientLineKind.Note);
            (lines[1].IngredientId, lines[1].Quantity, lines[1].MeasurementId).Should().Be((water.Id, 8m, Cup));
        }

        [Fact]
        public async Task The_rebuild_counts_pieces_from_the_method_only_for_a_food_with_a_weight_per_item()
        {
            using var db = await SeededAsync();
            var onions = await FoodAsync(db, "onions");
            var eggs = await FoodAsync(db, "eggs");
            eggs.GramsPerItem = 50m;
            var flour = await FoodAsync(db, "flour");
            await db.SaveChangesAsync();
            var steps = new[] { "Throw in two onions and stir.", "Beat three eggs and fold them into the flour." };
            var record = Record("Onion Cake", new[] { "onions", "eggs", "2 cups flour" }, steps);
            var recipe = await RecipeAsync(db, record, new[]
            {
                new Row(onions, "onions", 0, Piece), new Row(eggs, "eggs", 0, Piece, 1), new Row(flour, "2 cups flour", 2, Cup, 2),
            }, steps: steps);

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.Counts.FilledFromMethod.Should().Be(1);
            var lines = await LinesAsync(db, recipe.Id);
            (lines[0].IngredientId, lines[0].Quantity).Should().Be((onions.Id, 0m));
            (lines[1].IngredientId, lines[1].Quantity, lines[1].MeasurementId).Should().Be((eggs.Id, 3m, Piece));
        }

        [Fact]
        public async Task Rebuilding_a_rebuilt_recipe_changes_nothing()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var record = Record("Sweet Flour", new[] { "2 cups flour", "1/2 cup sugar" });
            var recipe = await RecipeAsync(db, record, new[] { new Row(sugar, "1/2 cup sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup) });
            await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();

            var again = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            again.Unchanged.Should().Be(1);
            (await db.RecipeReprocessJournals.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task A_recipe_the_import_just_created_from_a_record_rebuilds_to_the_same_lines()
        {
            using var db = await SeededAsync();
            await FoodAsync(db, "flour");
            var vetting = new RecipeVettingService();
            var import = new RecipeScrapingService(db, new Admin(), new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object, vetting,
                NullLogger<RecipeScrapingService>.Instance);
            var record = Record("Shortbread", new[] { "For the dough:", "2 cups flour", "1/2 cup sugar", "1 (15 oz) can pumpkin", "a pinch of salt", "butter" });
            record.Ingredients[2].Quantity = 1;
            await import.ImportStagedAsync(new StagedImportRequestModel { SourceAttribution = "Old Book (1900)", Recipes = { record }, PublicDomain = true });
            db.ChangeTracker.Clear();
            var recipe = await db.Recipes.SingleAsync();
            var lines = await LinesAsync(db, recipe.Id);
            lines.Select(l => (l.Quantity, l.MeasurementId, l.LineKind)).Should().Equal(
                (0m, Piece, RecipeIngredientLineKind.Note), (2m, Cup, null), (0.5m, Cup, null), (15m, Ounce, null), (1m, Pinch, null), (0m, Piece, null));

            var result = await Service(db).RebuildAsync(For(recipe.Id));

            result.Unchanged.Should().Be(1, string.Join("; ", result.Results.SelectMany(r => r.Lines).Select(l => $"{l.RawLine}: {string.Join(",", l.Changes)}")));
        }

        [Fact]
        public async Task A_recipe_a_person_edited_is_not_rebuilt()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var recipe = await RecipeAsync(db, Record("Sugar", new[] { "1/2 cup sugar", "1 cup sugar" }), new[] { new Row(sugar, "1/2 cup sugar", 1, Piece) });
            var tracked = await db.Recipes.SingleAsync();
            tracked.Version = 2;
            await db.SaveChangesAsync();

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.HeldByReason.Should().ContainKey(RecipeReprocessHold.PersonEdit);
            (await LinesAsync(db, recipe.Id)).Single().Quantity.Should().Be(1m);
        }

        [Fact]
        public async Task A_recipe_with_no_stored_source_is_never_rebuilt()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var recipe = await RecipeAsync(db, null, new[] { new Row(sugar, "1/2 cup sugar", 1, Piece) });

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.HeldByReason.Should().ContainKey(RecipeReprocessHold.NoSource);
        }

        [Fact]
        public async Task A_planned_recipe_is_rebuilt_only_when_every_line_keeps_its_food()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            var amounts = await RecipeAsync(db, Record("Amounts", new[] { "2 cups flour", "1/2 cup sugar" }),
                new[] { new Row(sugar, "1/2 cup sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup) });
            var junk = await FoodAsync(db, "3 cups flour", curated: false);
            var foods = await RecipeAsync(db, Record("Foods", new[] { "3 cups flour", "1 cup sugar" }),
                new[] { new Row(junk, "3 cups flour", 3, Cup), new Row(sugar, "1 cup sugar", 1, Cup) });
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            db.MealPlans.AddRange(
                new MealPlanEntity { HouseholdId = 1, AuthorId = Operator, MealTypeId = 1, Date = today.AddDays(2), RecipeId = amounts.Id },
                new MealPlanEntity { HouseholdId = 1, AuthorId = Operator, MealTypeId = 1, Date = today.AddDays(2), RecipeId = foods.Id });
            await db.SaveChangesAsync();

            var result = await Service(db).RebuildAsync(For(amounts.Id, foods.Id), apply: true);

            result.Results.Single(r => r.RecipeId == amounts.Id).Outcome.Should().Be(RecipeReprocessOutcome.Rebuilt);
            result.Results.Single(r => r.RecipeId == foods.Id).Reason.Should().Be(RecipeReprocessHold.PlannedFoodChange);
            (await LinesAsync(db, foods.Id)).Select(l => l.IngredientId).Should().Contain(junk.Id);
        }

        [Fact]
        public async Task An_approved_recipe_that_would_lose_an_amount_is_held_and_keeps_its_lines()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            var record = Record("Dough", new[] { "2 cups flour", "water" });
            var recipe = await RecipeAsync(db, record, new[] { new Row(flour, "2 cups flour", 2, Cup), new Row(water, "water", 1, Cup, 1) }, CurationStatusEnum.Curated);

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.HeldByReason.Should().ContainKey(RecipeReprocessHold.ApprovedFewerAmounts);
            (await LinesAsync(db, recipe.Id)).Single(l => l.IngredientId == water.Id).Quantity.Should().Be(1m);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
        }

        [Fact]
        public async Task An_approved_recipe_that_would_newly_fail_the_approval_gate_is_held()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var retired = await FoodAsync(db, "Sugars, granulated");
            var sugar = await FoodAsync(db, "sugar", curated: false);
            var recipe = await RecipeAsync(db, Record("Dough", new[] { "2 cups flour", "1 cup sugar" }),
                new[] { new Row(flour, "2 cups flour", 2, Cup), new Row(retired, "1 cup sugar", 1, Cup, 1) }, CurationStatusEnum.Curated);
            var tracked = await db.Ingredients.SingleAsync(i => i.Id == retired.Id);
            tracked.IsDeleted = true;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.HeldByReason.Should().ContainKey(RecipeReprocessHold.ApprovedGate);
            (await LinesAsync(db, recipe.Id)).Select(l => l.IngredientId).Should().NotContain(sugar.Id);
        }

        [Fact]
        public async Task An_approved_recipe_that_gets_better_is_rebuilt_and_stays_approved()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            var recipe = await RecipeAsync(db, Record("Dough", new[] { "2 cups flour", "1/2 cup sugar" }),
                new[] { new Row(flour, "2 cups flour", 2, Cup), new Row(sugar, "1/2 cup sugar", 1, Piece, 1) }, CurationStatusEnum.Curated);

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.Rebuilt.Should().Be(1);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
        }

        [Fact]
        public async Task A_recipe_with_a_compound_split_is_held_until_the_rebuild_repeats_that_rule()
        {
            using var db = await SeededAsync();
            var salt = await FoodAsync(db, "salt");
            var recipe = await RecipeAsync(db, Record("Seasoning", new[] { "salt and pepper to taste" }), new[] { new Row(salt, "salt to taste", 0, Piece) });
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = recipe.Id, ChangeType = CompoundLineService.SplitChangeType,
                PropertyName = "x", Timestamp = Imported, ChangedByPersonId = Operator,
            });
            await db.SaveChangesAsync();

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.HeldByReason.Should().ContainKey(RecipeReprocessHold.CompoundSplit);
        }

        [Fact]
        public async Task The_undo_restores_the_rows_exactly_and_frees_the_recipe()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var record = Record("Sweet Flour", new[] { "2 cups flour", "1/2 cup sugar" });
            var recipe = await RecipeAsync(db, record, new[] { new Row(sugar, "1/2 cup sugar", 1, Piece, 0, RecipeIngredientLineKind.Ingredient), new Row(flour, "2 cups flour", 2, Cup) });
            var before = (await LinesAsync(db, recipe.Id)).Select(l => (l.IngredientId, l.SortOrder, l.Quantity, l.MeasurementId, l.RawLine, l.LineKind, l.CreatedDate)).ToList();
            var applied = await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();

            var dry = await Service(db).UndoAsync(null, applied.BatchId);
            var undone = await Service(db).UndoAsync(null, applied.BatchId, apply: true);
            db.ChangeTracker.Clear();

            dry.Restored.Should().Be(1);
            (await LinesAsync(db, recipe.Id)).Should().HaveCount(2);
            undone.Restored.Should().Be(1);
            (await LinesAsync(db, recipe.Id)).Select(l => (l.IngredientId, l.SortOrder, l.Quantity, l.MeasurementId, l.RawLine, l.LineKind, l.CreatedDate))
                .Should().BeEquivalentTo(before);
            (await db.RecipeReprocessJournals.SingleAsync()).UndoneAt.Should().NotBeNull();
            (await RecipeLineHold.HeldAsync(db, new[] { recipe.Id })).Should().NotContain(recipe.Id);
            (await Service(db).UndoAsync(null, applied.BatchId, apply: true)).Entries.Should().Be(0);
        }

        [Fact]
        public async Task The_undo_skips_a_recipe_whose_lines_changed_after_the_rebuild()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var recipe = await RecipeAsync(db, Record("Sweet Flour", new[] { "2 cups flour", "1/2 cup sugar" }),
                new[] { new Row(sugar, "1/2 cup sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup) });
            var applied = await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();
            var edited = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == recipe.Id && ri.IngredientId == flour.Id);
            edited.Quantity = 3;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undone = await Service(db).UndoAsync(applied.Stamp, null, apply: true);

            undone.Skipped.Should().Be(1);
            undone.SkippedRecipeIds.Should().Equal(recipe.Id);
            (await LinesAsync(db, recipe.Id)).Single(l => l.IngredientId == flour.Id).Quantity.Should().Be(3m);
        }

        [Fact]
        public async Task An_older_line_undo_skips_a_recipe_rebuilt_after_it_and_works_again_once_the_rebuild_is_undone()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var recipe = await RecipeAsync(db, Record("Sweet Flour", new[] { "2 cups flour", "1/2 cup sugar" }),
                new[] { new Row(flour, "2 cups flour", 2, Cup), new Row(sugar, "1/2 cup sugar", 1, Piece, 1) });
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = recipe.Id, ChangeType = ScrapedRecipeRepairService.FillChangeType,
                PropertyName = RecipeLineClassificationService.LinePropertyName(flour.Id, 0),
                OldValue = PackageLineService.Amount(flour.Id, 0, 0, Piece), NewValue = PackageLineService.Amount(flour.Id, 0, 2, Cup),
                Timestamp = Imported, ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            var rebuild = await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();
            var repair = new ScrapedRecipeRepairService(db, new RecipeVettingService());

            var held = await repair.UndoFillsAsync(Imported, apply: true);
            db.ChangeTracker.Clear();
            await Service(db).UndoAsync(null, rebuild.BatchId, apply: true);
            db.ChangeTracker.Clear();
            var freed = await repair.UndoFillsAsync(Imported, apply: true);

            (held.Restored, held.SkippedRebuilt).Should().Be((0, 1));
            freed.Restored.Should().Be(1);
        }

        [Fact]
        public async Task Order_only_sets_the_order_from_the_source_and_nothing_else_a_fold_taking_its_first_part()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var eggs = await FoodAsync(db, "eggs");
            var record = Record("Cake", new[] { "2 cups flour", "1/2 cup sugar", "3 eggs", "1 tablespoon sugar" });
            var recipe = await RecipeAsync(db, record, new[]
            {
                new Row(eggs, "3 eggs", 3, Piece), new Row(sugar, "1/2 cup sugar + 1 tablespoon sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup),
            });

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true, orderOnly: true);

            result.Rebuilt.Should().Be(1);
            var lines = await LinesAsync(db, recipe.Id);
            lines.Select(l => (l.IngredientId, l.SortOrder, l.Quantity, l.MeasurementId)).Should().Equal((flour.Id, 0, 2m, Cup), (sugar.Id, 1, 1m, Piece), (eggs.Id, 2, 3m, Piece));
            (await db.RecipeReprocessJournals.SingleAsync()).Kind.Should().Be(RecipeReprocessKind.Order);
            (await RecipeLineHold.HeldAsync(db, new[] { recipe.Id })).Should().NotContain(recipe.Id);
            (await RecipeReprocessService.RebuiltAtAsync(db, new[] { recipe.Id })).Should().ContainKey(recipe.Id);
        }

        [Fact]
        public async Task Order_only_holds_a_recipe_with_a_line_its_source_does_not_have()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var recipe = await RecipeAsync(db, Record("Cake", new[] { "2 cups flour", "1/2 cup sugar" }),
                new[] { new Row(sugar, "sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup) });

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true, orderOnly: true);

            result.HeldByReason.Should().ContainKey(RecipeReprocessHold.OrderUnpaired);
        }

        [Fact]
        public async Task A_web_recipe_is_rebuilt_from_its_json_ld_through_the_scrapers_parse_call()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var recipe = await RecipeAsync(db, null, new[] { new Row(sugar, "1/2 cup sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup) });
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = recipe.Id, RawJsonLd = "{\"@type\":\"Recipe\"}", SourceUrl = "https://example.org/r", FetchedAtUtc = Imported });
            await db.SaveChangesAsync();
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            scraper.Setup(s => s.ParseAsync("{\"@type\":\"Recipe\"}", "https://example.org/r", default))
                .ReturnsAsync(new ScraperResult { Success = true, Recipe = Record("Web", new[] { "2 cups flour", "1/2 cup sugar" }) });

            var result = await Service(db, scraper.Object).RebuildAsync(For(recipe.Id), apply: true);

            result.Results.Single().SourceFormat.Should().Be(ScrapedDocumentFormat.JsonLd);
            (await LinesAsync(db, recipe.Id)).Select(l => (l.IngredientId, l.Quantity)).Should().Equal((flour.Id, 2m), (sugar.Id, 0.5m));
            scraper.Verify(s => s.ScrapeAsync(It.IsAny<string>(), default), Times.Never);
        }

        [Fact]
        public async Task A_recipe_whose_source_and_rebuild_are_both_placeholder_text_is_rejected_by_rule_and_the_undo_brings_it_back()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var steps = new[] { "Placeholder text for the method goes here.", "Insert steps here once known." };
            var recipe = await RecipeAsync(db, Record("Lorem", new[] { "2 cups flour" }, steps), new[] { new Row(flour, "2 cups flour", 2, Cup) },
                CurationStatusEnum.RequiresRevision, steps);

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();

            result.Rejected.Should().Be(1);
            var rejected = await db.Recipes.SingleAsync();
            rejected.CurationStatusId.Should().Be((long)CurationStatusEnum.Rejected);
            rejected.VettingIssues.Should().Contain(RecipeReprocessService.RuleRejectIssue);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == RecipeReprocessService.RuleRejectChangeType)).Should().Be(1);

            await Service(db).UndoAsync(null, result.BatchId, RecipeReprocessKind.RuleReject, apply: true);
            db.ChangeTracker.Clear();

            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
        }

        [Fact]
        public async Task A_second_copy_is_retired_keeping_the_referenced_one_and_the_undo_brings_it_back()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var lines = new[] { new Row(flour, "2 cups flour", 2, Cup) };
            var first = await RecipeAsync(db, null, lines);
            var second = await RecipeAsync(db, null, lines);
            foreach (var r in await db.Recipes.ToListAsync())
            {
                r.Name = "Bread";
                r.SourceAttribution = "Old Book (1900)";
            }
            db.MealPlans.Add(new MealPlanEntity { HouseholdId = 1, AuthorId = Operator, MealTypeId = 1, Date = new DateOnly(2026, 1, 1), RecipeId = second.Id });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var dry = await Service(db).RetireDuplicatesAsync(new RecipeDuplicateRequestModel());
            var result = await Service(db).RetireDuplicatesAsync(new RecipeDuplicateRequestModel(), apply: true);
            db.ChangeTracker.Clear();

            dry.RetiredCount.Should().Be(1);
            result.Copies.Single().Should().Match<RecipeDuplicateCopy>(c => c.RecipeId == first.Id && c.KeptRecipeId == second.Id && c.Outcome == RecipeDuplicateResult.Retired);
            (await db.Recipes.Select(r => r.Id).ToListAsync()).Should().Equal(second.Id);

            var undone = await Service(db).UndoAsync(null, result.BatchId, apply: true);
            db.ChangeTracker.Clear();

            undone.RestoredByKind.Should().ContainKey(RecipeReprocessKind.DuplicateRetire);
            (await db.Recipes.CountAsync()).Should().Be(2);
        }

        [Fact]
        public async Task Copies_whose_lines_differ_are_not_retired()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var first = await RecipeAsync(db, null, new[] { new Row(flour, "2 cups flour", 2, Cup) });
            var second = await RecipeAsync(db, null, new[] { new Row(flour, "3 cups flour", 3, Cup) });
            foreach (var r in await db.Recipes.ToListAsync())
            {
                r.Name = "Bread";
                r.SourceAttribution = r.Id == first.Id ? "Book A" : "Book B";
            }
            await db.SaveChangesAsync();

            var byName = await Service(db).RetireDuplicatesAsync(new RecipeDuplicateRequestModel { AnyAttribution = true }, apply: true);
            var byBook = await Service(db).RetireDuplicatesAsync(new RecipeDuplicateRequestModel(), apply: true);

            byName.Copies.Single().Reason.Should().Be(RecipeDuplicateResult.HeldLinesDiffer);
            byBook.Groups.Should().Be(0);
            (await db.Recipes.CountAsync()).Should().Be(2);
            second.Id.Should().BeGreaterThan(first.Id);
        }

        [Fact]
        public async Task The_undo_of_a_rebuild_takes_back_the_link_lines_it_journaled_so_the_recipe_is_free_again()
        {
            using var db = await SeededAsync();
            var wheat = await FoodAsync(db, "Wheat flour, white");
            var holder = await FoodAsync(db, "flour", curated: false);
            var junk = await FoodAsync(db, "2 cups flour", curated: false);
            (await db.Ingredients.SingleAsync(i => i.Id == holder.Id)).IsDeleted = true;
            var proposal = new Nom.Data.Curation.FoodCatalogProposalEntity
            {
                Action = Nom.Data.Curation.FoodProposalAction.Update, IngredientId = holder.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "flour",
                ProposedValue = wheat.Id.ToString(), Source = "ai:fake", Status = Nom.Data.Curation.FoodProposalStatus.Applied,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            db.AuditLogEntries.Add(LinkApplyJournal.Entry(proposal.Id, LinkApplyJournal.Header,
                LinkApplyJournal.Write(new LinkApplyJournal.HeaderValue(holder.Id, "flour", (long)CurationStatusEnum.NonCurated, "ai:fake", true, wheat.Id)),
                wheat.Id.ToString(), Imported, SystemConstants.SystemPersonId));
            await db.SaveChangesAsync();
            var recipe = await RecipeAsync(db, Record("Bread", new[] { "2 cups flour" }), new[] { new Row(junk, "2 cups flour", 2, Cup) });

            var rebuild = await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();
            (await LinesAsync(db, recipe.Id)).Single().IngredientId.Should().Be(wheat.Id);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LinkApplyJournal.ChangeType && a.PropertyName == LinkApplyJournal.Line)).Should().Be(1);

            await Service(db).UndoAsync(null, rebuild.BatchId, apply: true);
            db.ChangeTracker.Clear();

            (await LinesAsync(db, recipe.Id)).Single().IngredientId.Should().Be(junk.Id);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LinkApplyJournal.ChangeType && a.PropertyName == LinkApplyJournal.Line)).Should().Be(0);
            (await RecipeLineHold.HeldAsync(db, new[] { recipe.Id })).Should().NotContain(recipe.Id);
        }

        [Fact]
        public async Task The_undo_leaves_a_recipe_approved_since_the_rebuild_on_the_lines_it_was_approved_with()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            var recipe = await RecipeAsync(db, Record("Dough", new[] { "2 cups flour", "1/2 cup sugar" }),
                new[] { new Row(flour, "2 cups flour", 0, Piece), new Row(sugar, "1/2 cup sugar", 0, Piece, 1) }, CurationStatusEnum.RequiresRevision);
            var rebuild = await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();
            var approved = await db.Recipes.SingleAsync();
            approved.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
            approved.CurationStatusId = (long)CurationStatusEnum.Curated;
            approved.DateCurationCompleted = DateTime.UtcNow;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undone = await Service(db).UndoAsync(null, rebuild.BatchId, apply: true);
            db.ChangeTracker.Clear();

            undone.Skipped.Should().Be(1);
            undone.Restored.Should().Be(0);
            (await LinesAsync(db, recipe.Id)).Select(l => l.Quantity).Should().Equal(2m, 0.5m);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
        }

        [Fact]
        public async Task Recipes_people_typed_in_are_never_grouped_as_duplicates()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var lines = new[] { new Row(flour, "2 cups flour", 2, Cup) };
            await RecipeAsync(db, null, lines);
            await RecipeAsync(db, null, lines);
            foreach (var r in await db.Recipes.ToListAsync())
            {
                r.Name = "Grandma's Pancakes";
                r.ScrapedAtUtc = null;
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var byBook = await Service(db).RetireDuplicatesAsync(new RecipeDuplicateRequestModel(), apply: true);
            var byName = await Service(db).RetireDuplicatesAsync(new RecipeDuplicateRequestModel { AnyAttribution = true }, apply: true);

            (byBook.Groups, byName.Groups).Should().Be((0, 0));
            (await db.Recipes.CountAsync()).Should().Be(2);
        }

        [Fact]
        public async Task An_approved_recipe_that_already_fails_vetting_is_held_when_it_would_fail_a_further_rule()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            await FoodAsync(db, "butter");
            await FoodAsync(db, "cream");
            await FoodAsync(db, "vanilla");
            var steps = new[] { "Mix it all." };
            var recipe = await RecipeAsync(db, Record("Dough", new[] { "2 cups flour", "1 cup sugar", "butter", "cream", "vanilla" }, steps),
                new[] { new Row(flour, "2 cups flour", 2, Cup), new Row(sugar, "1 cup sugar", 1, Cup, 1) }, CurationStatusEnum.Curated, steps);

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);

            result.HeldByReason.Should().ContainKey(RecipeReprocessHold.ApprovedVetting);
            (await LinesAsync(db, recipe.Id)).Should().HaveCount(2);
        }

        [Fact]
        public async Task A_rebuild_stamps_each_recipe_when_it_writes_it_so_a_lane_entry_written_before_that_counts_as_rebuilt_over()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var flour = await FoodAsync(db, "flour");
            var recipe = await RecipeAsync(db, null, new[] { new Row(sugar, "1/2 cup sugar", 1, Piece), new Row(flour, "2 cups flour", 2, Cup) });
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = recipe.Id, RawJsonLd = "{\"@type\":\"Recipe\"}", SourceUrl = "https://example.org/r", FetchedAtUtc = Imported });
            await db.SaveChangesAsync();
            DateTime? laneAt = null;
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            scraper.Setup(s => s.ParseAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync(() =>
            {
                System.Threading.Thread.Sleep(5);
                laneAt = DateTime.UtcNow;
                System.Threading.Thread.Sleep(5);
                return new ScraperResult { Success = true, Recipe = Record("Web", new[] { "2 cups flour", "1/2 cup sugar" }) };
            });

            var result = await Service(db, scraper.Object).RebuildAsync(For(recipe.Id), apply: true);

            result.Rebuilt.Should().Be(1);
            (await RecipeReprocessService.RebuiltAtAsync(db, new[] { recipe.Id }))[recipe.Id].Should().BeAfter(laneAt!.Value);
            (await db.RecipeReprocessJournals.SingleAsync()).CreatedDate.Should().BeAfter(laneAt.Value);
        }

        [Fact]
        public async Task A_rule_rejection_that_also_changes_lines_is_undone_by_its_kind()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var steps = new[] { "Placeholder text for the method goes here.", "Insert steps here once known." };
            var recipe = await RecipeAsync(db, Record("Lorem", new[] { "2 cups flour" }, steps), new[] { new Row(flour, "2 cups flour", 1, Piece) },
                CurationStatusEnum.RequiresRevision, steps);

            var result = await Service(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();
            result.Rejected.Should().Be(1);

            var undone = await Service(db).UndoAsync(null, result.BatchId, RecipeReprocessKind.RuleReject, apply: true);
            db.ChangeTracker.Clear();

            undone.RestoredByKind.Should().ContainKey(RecipeReprocessKind.RuleReject);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
            (await LinesAsync(db, recipe.Id)).Single().Quantity.Should().Be(2m);

            await Service(db).UndoAsync(null, result.BatchId, apply: true);
            db.ChangeTracker.Clear();

            (await LinesAsync(db, recipe.Id)).Single().Quantity.Should().Be(1m);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
        }

        [Fact]
        public async Task A_line_left_on_a_prepared_food_because_its_text_is_that_foods_whole_name_is_reported()
        {
            using var db = await SeededAsync();
            var burger = await FoodAsync(db, "Hamburger");
            (await db.Ingredients.SingleAsync(i => i.Id == burger.Id)).FdcCategoryId = 21;
            var buns = await FoodAsync(db, "buns");
            await db.SaveChangesAsync();
            var recipe = await RecipeAsync(db, Record("Burger Night", new[] { "2 buns", "1 hamburger" }),
                new[] { new Row(burger, "1 hamburger", 1, Piece), new Row(buns, "2 buns", 2, Piece) });

            var result = await Service(db).RebuildAsync(For(recipe.Id));

            var one = result.Results.Single();
            one.Lines.Single(l => l.RawLine == "1 hamburger").Changes.Should().Contain(RecipeReprocessLineDiff.PreparedFood);
            one.Lines.Single(l => l.RawLine == "2 buns").Changes.Should().NotContain(RecipeReprocessLineDiff.PreparedFood);
            result.Counts.OnPreparedFood.Should().Be(1);
        }

        [Fact]
        public void Pairing_takes_the_row_of_the_same_text_first_and_a_fold_for_each_of_its_parts()
        {
            RecipeReprocessService.LineRow Row(string raw, int sort) => new(1, sort, 0, 1, raw, null, null, Imported, null, null, null, false, null, null);
            var fold = Row("1/2 cup sugar + 1 tablespoon sugar", 1);
            var flour = Row("2 cups  Flour", 0);

            var pairs = RecipeReprocessService.Pair(new[] { "2 cups flour", "1/2 cup sugar", "1 tablespoon sugar", "salt" }, new[] { flour, fold });

            pairs.Keys.Should().BeEquivalentTo(new[] { 0, 1, 2 });
            pairs[0].Should().BeSameAs(flour);
            pairs[1].Should().BeSameAs(fold);
            pairs[2].Should().BeSameAs(fold);
        }
    }
}
