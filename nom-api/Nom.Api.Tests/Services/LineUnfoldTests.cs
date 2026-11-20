using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// A recipe row that holds several source lines joined with " + " becomes one row per line,
    /// and the undo sweep joins it again. Lines here are real staging rows.
    /// </summary>
    public class LineUnfoldTests
    {
        private const long Gram = 1, Piece = 3, Pound = 6, Ounce = 7, Cup = 11, Tablespoon = 12, Teaspoon = 13, Clove = 20;
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private sealed record Seeded(long RecipeId, Dictionary<string, long> Foods);

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
                Unit(Tablespoon, "Tablespoon", 2), Unit(Teaspoon, "Teaspoon", 2), Unit(Clove, "Clove", 3));
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<Seeded> SeedAsync(
            ApplicationDbContext db, string[]? source, params (string Food, int SortOrder, decimal Quantity, long MeasurementId, string? LineKind, string RawLine)[] lines)
        {
            var foods = new Dictionary<string, long>();
            foreach (var name in lines.Select(l => l.Food).Distinct())
            {
                var food = await db.Ingredients.FirstOrDefaultAsync(i => i.Name == name);
                if (food == null)
                {
                    food = new IngredientEntity { Name = name, CurationStatusId = 9003 };
                    db.Ingredients.Add(food);
                    await db.SaveChangesAsync();
                }

                foods[name] = food.Id;
            }

            var recipe = new RecipeEntity
            {
                Name = "Scraped",
                ScrapedAtUtc = Imported,
                RecipeIngredients = lines.Select(l => new RecipeIngredientEntity
                {
                    IngredientId = foods[l.Food], SortOrder = l.SortOrder, Quantity = l.Quantity, MeasurementId = l.MeasurementId,
                    LineKind = l.LineKind, RawLine = l.RawLine, CreatedDate = Imported,
                }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            if (source != null)
            {
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity
                {
                    RecipeId = recipe.Id,
                    RawJsonLd = System.Text.Json.JsonSerializer.Serialize(new { recipeIngredient = source }),
                });
                await db.SaveChangesAsync();
            }

            return new Seeded(recipe.Id, foods);
        }

        private static Task<List<RecipeIngredientEntity>> LinesAsync(ApplicationDbContext db, long recipeId) =>
            db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId)
                .OrderBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId).ToListAsync();

        [Fact]
        public async Task A_dry_run_writes_nothing_and_reports_what_the_apply_then_does()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null,
                ("Sugar", 0, 1.75m, Cup, null, "1 3/4 cup granulated sugar + 1/3 cup granulated sugar, (for rolling cookies)"),
                ("Butter", 0, 1m, Ounce, null, "butter + butter"),
                ("Olive Oil", 0, 2m, Tablespoon, null, "2 tablespoons extra-virgin olive oil + Extra-virgin olive oil (for drizzling)"));
            var service = new LineUnfoldService(db, new NoUser());

            var dryRun = await service.UnfoldAsync();

            dryRun.Applied.Should().BeFalse();
            (await LinesAsync(db, seeded.RecipeId)).Should().HaveCount(3).And.OnlyContain(l => l.RawLine.Contains(" + ") && l.LastModifiedDate == null);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
            dryRun.Samples.Should().HaveCount(3);
            dryRun.Samples.Single(s => s.IngredientName == "Sugar").Lines.Should().BeEquivalentTo(new[]
            {
                new LineUnfoldLine { RawLine = "1 3/4 cup granulated sugar", Quantity = 1.75m, Measurement = "Cup" },
                new LineUnfoldLine { RawLine = "1/3 cup granulated sugar, (for rolling cookies)", Quantity = 0.333m, Measurement = "Cup" },
            }, o => o.WithStrictOrdering());

            var applied = await service.UnfoldAsync(apply: true);

            applied.Applied.Should().BeTrue();
            applied.Should().BeEquivalentTo(dryRun, o => o.Excluding(r => r.Applied));
            applied.RowsExamined.Should().Be(3);
            applied.Folds.Should().Be(3);
            applied.LinesAdded.Should().Be(2);
            applied.Collapsed.Should().Be(1);
            applied.DuplicatesDropped.Should().Be(1);
            applied.UnmeasuredAdded.Should().Be(1);
            applied.Remaining.Should().Be(0);
        }

        [Fact]
        public async Task Each_line_gets_its_own_row_and_the_first_keeps_the_place_and_the_amount()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db,
                new[] { "1 3/4 cup granulated sugar", "1/3 cup granulated sugar, (for rolling cookies)", "2 large eggs" },
                ("Sugar", 0, 1.75m, Cup, null, "1 3/4 cup granulated sugar + 1/3 cup granulated sugar, (for rolling cookies)"),
                ("Eggs", 0, 2m, Piece, null, "2 large eggs"));
            var started = DateTime.UtcNow;

            await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);

            var lines = await LinesAsync(db, seeded.RecipeId);
            lines.Should().HaveCount(3);
            var sugar = lines.Where(l => l.IngredientId == seeded.Foods["Sugar"]).ToList();
            sugar[0].Should().Match<RecipeIngredientEntity>(l => l.SortOrder == 0 && l.Quantity == 1.75m && l.MeasurementId == Cup
                && l.RawLine == "1 3/4 cup granulated sugar" && l.CreatedDate == Imported && l.LastModifiedDate >= started
                && l.LastModifiedByPersonId == SystemConstants.SystemPersonId);
            sugar[1].Should().Match<RecipeIngredientEntity>(l => l.SortOrder == 1 && l.Quantity == 0.333m && l.MeasurementId == Cup
                && l.RawLine == "1/3 cup granulated sugar, (for rolling cookies)" && l.LineKind == null && l.CreatedDate >= started
                && l.CreatedByPersonId == SystemConstants.SystemPersonId);
            lines.Single(l => l.IngredientId == seeded.Foods["Eggs"]).LastModifiedDate.Should().BeNull();

            var entry = await db.AuditLogEntries.SingleAsync();
            entry.EntityType.Should().Be(LineRelinkService.RelinkEntityType);
            entry.EntityId.Should().Be(seeded.RecipeId);
            entry.ChangeType.Should().Be(LineUnfoldService.UnfoldChangeType);
            entry.OldValue.Should().Be($$"""{"at":"{{seeded.Foods["Sugar"]}}@0","raw":"1 3/4 cup granulated sugar + 1/3 cup granulated sugar, (for rolling cookies)","qty":1.75,"m":11}""");
            entry.NewValue.Should().Be("""{"added":[1],"qty":1.75,"m":11,"lines":[{"at":1,"qty":0.333,"m":11}]}""");
        }

        [Fact]
        public async Task An_amount_that_belongs_to_a_later_line_goes_with_it()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null,
                ("Flour", 0, 8m, Cup, null, "flour + 2 quarts flour"),
                ("Butter", 0, 1m, Ounce, null, "butter + butter"),
                ("Oil", 0, 2m, Tablespoon, RecipeIngredientLineKind.Ingredient, "2 tbsp oil + oil (for drizzling)"),
                ("Garlic", 0, 3m, Gram, null, "3 cloves garlic (unpeeled) + 2 cloves garlic (minced)"));

            var result = await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);

            result.QuantitiesReassigned.Should().Be(1);
            result.Collapsed.Should().Be(1);
            result.LinesAdded.Should().Be(3);
            result.UnmeasuredAdded.Should().Be(1);
            var lines = await LinesAsync(db, seeded.RecipeId);
            (string, decimal, long, string?)[] Of(string food) => lines.Where(l => l.IngredientId == seeded.Foods[food])
                .Select(l => (l.RawLine, l.Quantity, l.MeasurementId, l.LineKind)).ToArray();
            Of("Flour").Should().Equal(("flour", 0m, Cup, null), ("2 quarts flour", 8m, Cup, null));
            Of("Butter").Should().Equal(("butter", 1m, Ounce, null));
            Of("Oil").Should().Equal(
                ("2 tbsp oil", 2m, Tablespoon, RecipeIngredientLineKind.Ingredient),
                ("oil (for drizzling)", 0m, Gram, RecipeIngredientLineKind.Ingredient));
            Of("Garlic").Should().Equal(("3 cloves garlic (unpeeled)", 3m, Gram, null), ("2 cloves garlic (minced)", 2m, Clove, null));
            lines.Select(l => l.SortOrder).Should().Equal(0, 0, 0, 0, 1, 2, 3);
        }

        [Fact]
        public async Task A_row_that_is_not_a_fold_is_left_alone_and_a_second_run_finds_nothing()
        {
            using var db = await NewContextAsync();
            var backed = await SeedAsync(db,
                new[] { "1 cup + 2 Tablespoons (225g) granulated sugar", "3 large eggs", "1 large egg + 1 teaspoon water, (lightly beaten for egg wash)", "1 cup flour" },
                ("Sugar", 0, 1m, Cup, null, "1 cup + 2 Tablespoons (225g) granulated sugar"),
                ("Eggs", 0, 3m, Piece, null, "3  large eggs + 1  large egg + 1 teaspoon water, (lightly beaten for egg wash)"),
                ("Flour", 0, 1m, Cup, null, "1 cup flour + 2 cups bread flour"));
            var cookbook = await SeedAsync(db, null,
                ("Milk", 0, 0.75m, Cup, null, "3/4 c + 2 T milk"),
                ("Powdered Sugar", 0, 0m, Gram, RecipeIngredientLineKind.Artefact, "powdered sugar + 8 tablespoonfuls powdered sugar"),
                ("Stock", 0, 0m, Gram, RecipeIngredientLineKind.Reference, "good stock + 1 pint good stock"));
            var unlisted = await SeedAsync(db, Array.Empty<string>(), ("Salt", 0, 1m, Teaspoon, null, "1 tsp salt + 2 tsp salt"));
            var member = new RecipeEntity
            {
                Name = "Written by a member",
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { IngredientId = backed.Foods["Sugar"], Quantity = 1m, MeasurementId = Cup, RawLine = "white + brown, mixed" },
                },
            };
            db.Recipes.Add(member);
            await db.SaveChangesAsync();
            var service = new LineUnfoldService(db, new NoUser());

            var first = await service.UnfoldAsync(apply: true);

            first.RowsExamined.Should().Be(7);
            first.Folds.Should().Be(1);
            first.LinesAdded.Should().Be(1);
            first.SkippedSingleSourceLine.Should().Be(1);
            first.SkippedAmbiguous.Should().Be(3, "the flour row's second part is no line of the source, the cookbook milk row has a part that is only an amount, and the salt row's stored source lists no lines");
            first.SkippedOtherKind.Should().Be(2);
            (await LinesAsync(db, backed.RecipeId)).Select(l => l.RawLine).Should().BeEquivalentTo(new[]
            {
                "1 cup + 2 Tablespoons (225g) granulated sugar",
                "3  large eggs",
                "1  large egg + 1 teaspoon water, (lightly beaten for egg wash)",
                "1 cup flour + 2 cups bread flour",
            });
            (await LinesAsync(db, cookbook.RecipeId)).Should().HaveCount(3).And.OnlyContain(l => l.RawLine.Contains(" + "));
            (await LinesAsync(db, unlisted.RecipeId)).Single().RawLine.Should().Be("1 tsp salt + 2 tsp salt");
            (await LinesAsync(db, member.Id)).Single().RawLine.Should().Be("white + brown, mixed");

            var second = await service.UnfoldAsync(apply: true);

            second.Folds.Should().Be(0);
            second.SkippedSingleSourceLine.Should().Be(2, "the egg wash line now stands on its own and is one line of the source");
            (await db.AuditLogEntries.CountAsync()).Should().Be(1);
            (await LinesAsync(db, backed.RecipeId)).Should().HaveCount(4);
        }

        [Fact]
        public async Task Added_lines_go_after_the_last_line_of_the_recipe_in_source_order()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null,
                ("Salt", 2, 0.5m, Teaspoon, null, "½ teaspoon sea salt + ½ teaspoon sea salt + ¼ teaspoon sea salt"),
                ("Water", 5, 1m, Cup, null, "1 cup water"),
                ("Butter", 4, 1m, Tablespoon, null, "1 tablespoonful of butter + 1/4 pound of butter"));

            var result = await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);

            result.LinesAdded.Should().Be(3);
            result.DuplicatesDropped.Should().Be(0, "a repeated line that states an amount is a real second line");
            (await LinesAsync(db, seeded.RecipeId)).Select(l => (l.SortOrder, l.RawLine, l.Quantity, l.MeasurementId)).Should().Equal(
                (2, "½ teaspoon sea salt", 0.5m, Teaspoon),
                (4, "1 tablespoonful of butter", 1m, Tablespoon),
                (5, "1 cup water", 1m, Cup),
                (6, "½ teaspoon sea salt", 0.5m, Teaspoon),
                (7, "¼ teaspoon sea salt", 0.25m, Teaspoon),
                (8, "1/4 pound of butter", 0.25m, Pound));
        }

        [Fact]
        public async Task A_call_takes_apart_no_more_rows_than_asked_and_says_how_many_are_left()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null,
                ("Sugar", 0, 1m, Cup, null, "1 cup sugar + 2 cups sugar"),
                ("Flour", 0, 1m, Cup, null, "1 cup flour + 2 cups flour"),
                ("Butter", 0, 1m, Cup, null, "1 cup butter + 2 cups butter"));
            var service = new LineUnfoldService(db, new NoUser());

            var first = await service.UnfoldAsync(apply: true, maxRows: 2, sampleSize: 1);

            first.Folds.Should().Be(2);
            first.Remaining.Should().Be(1);
            first.Samples.Should().HaveCount(1);
            (await LinesAsync(db, seeded.RecipeId)).Should().HaveCount(5);

            var second = await service.UnfoldAsync(apply: true, maxRows: 2);

            second.Folds.Should().Be(1);
            second.Remaining.Should().Be(0);
            (await LinesAsync(db, seeded.RecipeId)).Select(l => l.SortOrder).Should().Equal(0, 0, 0, 1, 2, 3);
        }

        [Fact]
        public async Task A_row_too_long_for_the_journal_is_left_alone()
        {
            using var db = await NewContextAsync();
            var longLine = "1 cup sugar + " + new string('s', 4000);
            var seeded = await SeedAsync(db, null, ("Sugar", 0, 1m, Cup, null, longLine));

            var result = await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);

            result.Folds.Should().Be(0);
            result.SkippedAmbiguous.Should().Be(1);
            (await LinesAsync(db, seeded.RecipeId)).Single().RawLine.Should().Be(longLine);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task The_undo_sweep_leaves_a_row_whose_added_line_was_given_another_amount_in_the_editor()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null,
                ("Water", 0, 1m, Cup, null, "1 cup water"),
                ("Sugar", 1, 0.25m, Cup, null, "1/4 cup sugar + 1/4 cup sugar, for dusting"));
            var started = DateTime.UtcNow;
            await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);
            var corrected = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == seeded.RecipeId && ri.SortOrder == 2);
            corrected.Quantity = 0.333m;
            corrected.LastModifiedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undone = await new LineRelinkService(db, new NoUser()).UndoAsync(started);

            undone.Refolded.Should().Be(0);
            undone.Skipped.Should().Be(1);
            (await LinesAsync(db, seeded.RecipeId)).Select(l => (l.RawLine, l.Quantity)).Should().Equal(
                ("1 cup water", 1m), ("1/4 cup sugar", 0.25m), ("1/4 cup sugar, for dusting", 0.333m));
            (await db.AuditLogEntries.CountAsync()).Should().Be(1, "the entry stays for the operator to see");
        }

        [Fact]
        public async Task The_undo_sweep_follows_a_row_whose_food_was_merged_into_another_since()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null, ("sugar, granulated", 0, 1m, Cup, null, "1 cup sugar + 2 cups sugar"));
            var usda = new IngredientEntity { Name = "Sugars, granulated", CurationStatusId = 9003 };
            db.Ingredients.Add(usda);
            await db.SaveChangesAsync();
            var started = DateTime.UtcNow;
            await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);
            var source = await db.Ingredients.SingleAsync(i => i.Id == seeded.Foods["sugar, granulated"]);
            foreach (var line in await db.RecipeIngredients.Where(ri => ri.IngredientId == source.Id).ToListAsync())
            {
                db.RecipeIngredients.Remove(line);
                db.RecipeIngredients.Add(new RecipeIngredientEntity
                {
                    RecipeId = line.RecipeId, IngredientId = usda.Id, SortOrder = line.SortOrder, Quantity = line.Quantity,
                    MeasurementId = line.MeasurementId, RawLine = line.RawLine, LineKind = line.LineKind, CreatedDate = line.CreatedDate,
                });
            }
            source.IsDeleted = true;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undone = await new LineRelinkService(db, new NoUser()).UndoAsync(started);

            undone.Refolded.Should().Be(1);
            (await LinesAsync(db, seeded.RecipeId)).Select(l => (l.IngredientId, l.RawLine, l.Quantity)).Should().Equal((usda.Id, "1 cup sugar + 2 cups sugar", 1m));
        }

        [Fact]
        public async Task A_later_line_that_names_another_food_is_added_without_an_amount_and_the_startup_reread_leaves_it()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null,
                ("Honey", 0, 2m, Tablespoon, null, "2 tablespoons honey + 2 tablespoons butter (optional, see FAQ notes) + 2 tablespoons butter"),
                ("Tomatoes", 0, 2m, Piece, null, "2 tomatoes + 1 tomato, sliced"));
            var service = new LineUnfoldService(db, new NoUser());

            var dryRun = await service.UnfoldAsync();
            await service.UnfoldAsync(apply: true);
            db.ChangeTracker.Clear();
            await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 50);

            dryRun.UnnamedUnmeasured.Should().Be(2);
            var lines = await LinesAsync(db, seeded.RecipeId);
            lines.Where(l => l.IngredientId == seeded.Foods["Honey"]).Select(l => (l.RawLine, l.Quantity)).Should().Equal(
                ("2 tablespoons honey", 2m), ("2 tablespoons butter (optional, see FAQ notes)", 0m), ("2 tablespoons butter", 0m));
            lines.Where(l => l.IngredientId == seeded.Foods["Tomatoes"]).Select(l => (l.RawLine, l.Quantity)).Should().Equal(
                ("2 tomatoes", 2m), ("1 tomato, sliced", 1m));
        }

        [Fact]
        public async Task The_startup_reread_leaves_the_lines_an_unfold_leaves_without_an_amount_at_zero()
        {
            using var db = await NewContextAsync();
            var flour = await SeedAsync(db, null, ("Flour", 0, 2m, Pound, null, "flour + two pounds of flour"));
            var salt = await SeedAsync(db, null, ("Salt", 0, 1m, Pound, null, "salt + a pound of salt"));
            var butter = await SeedAsync(db, null, ("Butter", 0, 0.25m, Pound, null, "a quarter of a pound of butter + a little butter"));
            void Method(long recipeId, string text) =>
                db.RecipeSteps.Add(new RecipeStepEntity { RecipeId = recipeId, StepNumber = 1, Summary = "Method", Description = text });
            Method(flour.RecipeId, "Mix together two pounds of flour, a teaspoonful of soda and cold water to a stiff paste.");
            Method(salt.RecipeId, "Then stew a gallon of ripe tomatoes with a pound of salt for two hours.");
            Method(butter.RecipeId, "I then add a quarter of a pound of butter, a teaspoonful of salt, mixed with a little butter, stir in the rice.");
            await db.SaveChangesAsync();
            await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);
            db.ChangeTracker.Clear();

            await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 50);

            async Task<decimal> TotalAsync(Seeded seeded, string food) =>
                (await LinesAsync(db, seeded.RecipeId)).Where(l => l.IngredientId == seeded.Foods[food]).Sum(l => l.Quantity);
            (await LinesAsync(db, flour.RecipeId)).Should().HaveCount(2);
            (await TotalAsync(flour, "Flour")).Should().Be(2m, "the method's two pounds is the line that states it, not a second two pounds");
            (await TotalAsync(salt, "Salt")).Should().Be(1m);
            (await TotalAsync(butter, "Butter")).Should().Be(0.25m);
        }

        [Fact]
        public async Task The_undo_sweep_joins_the_row_again_and_removes_the_added_lines()
        {
            using var db = await NewContextAsync();
            var seeded = await SeedAsync(db, null,
                ("Flour", 0, 8m, Cup, null, "flour + 2 quarts flour"),
                ("Sugar", 0, 1.75m, Cup, null, "1 3/4 cup granulated sugar + 1/3 cup granulated sugar + sugar, for dusting"),
                ("Butter", 0, 1m, Ounce, null, "butter + butter"));
            var started = DateTime.UtcNow;
            await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);
            (await LinesAsync(db, seeded.RecipeId)).Should().HaveCount(6);

            var undone = await new LineRelinkService(db, new NoUser()).UndoAsync(started);

            undone.Refolded.Should().Be(3);
            undone.Skipped.Should().Be(0);
            (await LinesAsync(db, seeded.RecipeId)).Select(l => (l.SortOrder, l.RawLine, l.Quantity, l.MeasurementId)).Should().BeEquivalentTo(new[]
            {
                (0, "flour + 2 quarts flour", 8m, Cup),
                (0, "1 3/4 cup granulated sugar + 1/3 cup granulated sugar + sugar, for dusting", 1.75m, Cup),
                (0, "butter + butter", 1m, Ounce),
            });
            (await db.RecipeIngredients.IgnoreQueryFilters().CountAsync(ri => ri.RecipeId == seeded.RecipeId)).Should().Be(3);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();

            var again = await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);
            again.Folds.Should().Be(3);
            again.LinesAdded.Should().Be(3);
        }

        [Fact]
        public async Task The_undo_sweep_leaves_a_row_whose_lines_were_edited_or_removed_since()
        {
            using var db = await NewContextAsync();
            var edited = await SeedAsync(db, null, ("Sugar", 0, 1m, Cup, null, "1 cup sugar + 2 cups sugar"));
            var removed = await SeedAsync(db, null, ("Flour", 0, 1m, Cup, null, "1 cup flour + 2 cups flour"));
            var reworded = await SeedAsync(db, null, ("Butter", 0, 1m, Cup, null, "1 cup butter + 2 cups butter"));
            var remeasured = await SeedAsync(db, null, ("Milk", 0, 2m, Cup, null, "milk + 2 cups milk"));
            var started = DateTime.UtcNow;
            await new LineUnfoldService(db, new NoUser()).UnfoldAsync(apply: true);

            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == edited.RecipeId && ri.SortOrder == 1)).RawLine = "3 cups brown sugar";
            db.RecipeIngredients.Remove(await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == removed.RecipeId && ri.SortOrder == 1));
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == reworded.RecipeId && ri.SortOrder == 0)).RawLine = "1 cup soft butter";
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == remeasured.RecipeId && ri.SortOrder == 0)).Quantity = 0.5m;
            await db.SaveChangesAsync();

            var undone = await new LineRelinkService(db, new NoUser()).UndoAsync(started);

            undone.Refolded.Should().Be(1);
            undone.Skipped.Should().Be(3);
            (await LinesAsync(db, edited.RecipeId)).Select(l => l.RawLine).Should().Equal("1 cup sugar", "3 cups brown sugar");
            (await LinesAsync(db, removed.RecipeId)).Select(l => l.RawLine).Should().Equal("1 cup flour");
            (await LinesAsync(db, reworded.RecipeId)).Select(l => l.RawLine).Should().Equal("1 cup soft butter", "2 cups butter");
            (await LinesAsync(db, remeasured.RecipeId)).Single().Should().Match<RecipeIngredientEntity>(
                l => l.RawLine == "milk + 2 cups milk" && l.Quantity == 0.5m, "an amount changed since the unfold is not overwritten");
            (await db.AuditLogEntries.Select(a => a.EntityId).ToListAsync()).Should().BeEquivalentTo(new[] { edited.RecipeId, removed.RecipeId, reworded.RecipeId });
        }
    }
}
