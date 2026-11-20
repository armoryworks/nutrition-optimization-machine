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
    /// Compound recipe lines become one row per food or move to their first alternative, rule by
    /// rule, journaled, and the relink undo puts them back. Lines here are real staging rows.
    /// </summary>
    public class CompoundLineServiceTests
    {
        private const long Gram = 1, Piece = 3, Cup = 11, Tablespoon = 12, Teaspoon = 13;
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

        private sealed class Kitchen
        {
            public ApplicationDbContext Db { get; init; } = default!;
            public Dictionary<string, long> Foods { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private static async Task<Kitchen> NewKitchenAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            BaseMeasurementEntity Unit(long id, string name, long category) =>
                new() { Id = id, Name = name, Symbol = name.ToLowerInvariant(), MeasurementCategoryId = category, BaseUnitConversionFactor = 1m };
            db.Set<BaseMeasurementEntity>().AddRange(
                Unit(Gram, "Gram", 1), Unit(Piece, "Piece", 3), Unit(Cup, "Cup", 2), Unit(Tablespoon, "Tablespoon", 2), Unit(Teaspoon, "Teaspoon", 2));

            var kitchen = new Kitchen { Db = db };
            IngredientEntity Food(string name, long status, params string[] usda) => new()
            {
                Name = name,
                CurationStatusId = status,
                IngredientNutrients = status == Curated ? new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } } : new List<IngredientNutrientEntity>(),
                Aliases = usda.Select(u => new IngredientAliasEntity { AliasName = u, SourceContext = IngredientLinkService.UsdaDescriptionContext }).ToList(),
            };
            var foods = new[]
            {
                Food("Salt", Curated, "Salt, table"), Food("Pepper", Curated, "Spices, pepper, black"), Food("Flour", Curated),
                Food("Butter", Curated), Food("Lard", Curated), Food("butter or margarine", Uncurated), Food("Cayenne Pepper", Curated),
            };
            db.Ingredients.AddRange(foods);
            await db.SaveChangesAsync();
            foreach (var food in foods) kitchen.Foods[food.Name] = food.Id;
            return kitchen;
        }

        private static async Task<long> RecipeAsync(Kitchen kitchen, bool approved, params (string Food, int SortOrder, decimal Quantity, long MeasurementId, string RawLine)[] lines)
        {
            var recipe = new RecipeEntity
            {
                Name = "Scraped",
                ScrapedAtUtc = Imported,
                CurationStatusId = approved ? Curated : Uncurated,
                RecipeIngredients = lines.Select(l => new RecipeIngredientEntity
                {
                    IngredientId = kitchen.Foods[l.Food], SortOrder = l.SortOrder, Quantity = l.Quantity, MeasurementId = l.MeasurementId,
                    RawLine = l.RawLine, CreatedDate = Imported,
                }).ToList(),
            };
            kitchen.Db.Recipes.Add(recipe);
            await kitchen.Db.SaveChangesAsync();
            return recipe.Id;
        }

        private static Task<List<(string Food, int SortOrder, decimal Quantity, long MeasurementId, string RawLine)>> LinesAsync(Kitchen kitchen, long recipeId) =>
            kitchen.Db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId)
                .OrderBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new { ri.Ingredient.Name, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine })
                .ToListAsync()
                .ContinueWith(t => t.Result.Select(r => (r.Name, r.SortOrder, r.Quantity, r.MeasurementId, r.RawLine)).ToList());

        private static async Task<(Kitchen Kitchen, long Plain, long Collides, long Alternative, long CuratedRows)> SeedAsync()
        {
            var kitchen = await NewKitchenAsync();
            var plain = await RecipeAsync(kitchen, approved: true,
                ("Salt", 0, 0m, Gram, "salt and pepper to taste"), ("Flour", 0, 2m, Cup, "2 cups flour"));
            var collides = await RecipeAsync(kitchen, approved: false,
                ("Salt", 0, 1m, Teaspoon, "salt and pepper"), ("Pepper", 0, 1m, Teaspoon, "1 tsp pepper"), ("Flour", 1, 1m, Cup, "1 cup flour"));
            var alternative = await RecipeAsync(kitchen, approved: false,
                ("butter or margarine", 0, 0.5m, Cup, "1/2 cup butter or margarine"), ("Cayenne Pepper", 0, 0m, Gram, "a little pepper and salt"));
            var curatedRows = await RecipeAsync(kitchen, approved: false,
                ("Flour", 0, 0.5m, Cup, "1/2 cup butter or margarine"), ("Lard", 0, 0.75m, Cup, "3/4 cup butter or lard"),
                ("Butter", 1, 0.75m, Cup, "3/4 cup butter or lard"));
            return (kitchen, plain, collides, alternative, curatedRows);
        }

        [Fact]
        public async Task A_preview_counts_each_rule_and_writes_nothing()
        {
            var (kitchen, plain, _, _, _) = await SeedAsync();
            var before = await LinesAsync(kitchen, plain);
            var service = new CompoundLineService(kitchen.Db, new NoUser());

            var preview = await service.PreviewAsync();

            preview.LinesExamined.Should().Be(7);
            preview.Lines.Should().Be(4);
            preview.Rules.Should().BeEquivalentTo(new[]
            {
                new { Rule = CompoundLineSplitter.AndSaltPepper, Lines = 3, OnUncurated = 0, OnCurated = 3, OnApprovedRecipes = 1, RowMoves = 1, RowsAdded = 3, Repositioned = 1 },
                new { Rule = CompoundLineSplitter.OrFirstNamed, Lines = 1, OnUncurated = 1, OnCurated = 0, OnApprovedRecipes = 0, RowMoves = 1, RowsAdded = 0, Repositioned = 0 },
            }, o => o.ExcludingMissingMembers());
            preview.LeftAlone.Should().BeEquivalentTo(new Dictionary<string, int>
            {
                [CompoundLineService.LeaveCuratedRow] = 1,
                [CompoundLineService.LeaveNamesOwnFood] = 1,
                [CompoundLineService.LeaveAlreadyFirst] = 1,
            });
            preview.Samples.Should().Contain(s => s.Rule == CompoundLineSplitter.AndSaltPepper && s.Parts.Select(p => p.RawLine).SequenceEqual(new[] { "salt to taste", "pepper to taste" }));
            (await service.PreviewAsync(new[] { CompoundLineSplitter.OrFirstNamed })).Lines.Should().Be(1);
            (await LinesAsync(kitchen, plain)).Should().Equal(before);
            (await kitchen.Db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Applying_one_rule_splits_its_lines_alone_and_a_second_apply_finds_nothing()
        {
            var (kitchen, plain, collides, alternative, _) = await SeedAsync();
            var service = new CompoundLineService(kitchen.Db, new NoUser());

            var applied = await service.ApplyAsync(new[] { CompoundLineSplitter.AndSaltPepper });

            applied.Should().BeEquivalentTo(new { Split = 3, Moved = 0, RowsAdded = 3, Repositioned = 1, Skipped = 0, Remaining = 0 });
            (await LinesAsync(kitchen, plain)).Should().Equal(
                ("Salt", 0, 0m, Gram, "salt to taste"), ("Pepper", 0, 0m, Gram, "pepper to taste"), ("Flour", 0, 2m, Cup, "2 cups flour"));
            (await LinesAsync(kitchen, collides)).Should().Equal(
                ("Salt", 0, 0m, Teaspoon, "salt"), ("Pepper", 0, 1m, Teaspoon, "1 tsp pepper"), ("Flour", 1, 1m, Cup, "1 cup flour"),
                ("Pepper", 2, 0m, Teaspoon, "pepper"));
            (await LinesAsync(kitchen, alternative)).Should().Equal(
                ("Salt", 0, 0m, Gram, "a little salt"), ("Pepper", 0, 0m, Gram, "a little pepper"),
                ("butter or margarine", 0, 0.5m, Cup, "1/2 cup butter or margarine"));
            var journal = await kitchen.Db.AuditLogEntries.Where(a => a.EntityId == plain).SingleAsync();
            journal.ChangeType.Should().Be(CompoundLineService.SplitChangeType);
            journal.OldValue.Should().Be($$"""{"i":{{kitchen.Foods["Salt"]}},"s":0,"q":0,"m":1,"r":"salt and pepper to taste"}""");

            var again = await service.ApplyAsync(new[] { CompoundLineSplitter.AndSaltPepper });
            again.Should().BeEquivalentTo(new { Split = 0, Moved = 0, Remaining = 0 });
            (await service.ApplyAsync(Array.Empty<string>())).Split.Should().Be(0);
        }

        [Fact]
        public async Task A_line_with_alternatives_moves_off_its_wording_row_but_a_curated_row_stays()
        {
            var (kitchen, _, _, alternative, curatedRows) = await SeedAsync();
            var service = new CompoundLineService(kitchen.Db, new NoUser());
            var curatedBefore = await LinesAsync(kitchen, curatedRows);

            var applied = await service.ApplyAsync(new[] { CompoundLineSplitter.OrFirstNamed, CompoundLineSplitter.OrIndependent });

            applied.Should().BeEquivalentTo(new { Split = 0, Moved = 1, RowsAdded = 0, Skipped = 0 });
            (await LinesAsync(kitchen, alternative)).Should().Contain(("Butter", 0, 0.5m, Cup, "1/2 cup butter or margarine"));
            (await LinesAsync(kitchen, curatedRows)).Should().Equal(curatedBefore);
            var journal = await kitchen.Db.AuditLogEntries.SingleAsync();
            journal.ChangeType.Should().Be(CompoundLineService.AlternativeChangeType);
            journal.OldValue.Should().Be(LineRelinkService.Place(kitchen.Foods["butter or margarine"], 0));
            journal.NewValue.Should().Be(LineRelinkService.Place(kitchen.Foods["Butter"], 0));
        }

        [Fact]
        public async Task The_relink_undo_puts_every_split_and_move_back()
        {
            var (kitchen, plain, collides, alternative, curatedRows) = await SeedAsync();
            var recipes = new[] { plain, collides, alternative, curatedRows };
            var before = new List<object>();
            foreach (var id in recipes) before.Add(await LinesAsync(kitchen, id));
            var started = DateTime.UtcNow;
            await new CompoundLineService(kitchen.Db, new NoUser()).ApplyAsync(CompoundLineSplitter.Rules.ToList());
            kitchen.Db.ChangeTracker.Clear();

            var undone = await new LineRelinkService(kitchen.Db, new NoUser()).UndoAsync(started, apply: true);

            undone.Unsplit.Should().Be(3);
            undone.AlternativesPutBack.Should().Be(1);
            undone.Skipped.Should().Be(0);
            var after = new List<object>();
            foreach (var id in recipes) after.Add(await LinesAsync(kitchen, id));
            after.Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
            (await kitchen.Db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task The_undo_skips_a_split_whose_rows_were_edited_since()
        {
            var (kitchen, plain, _, _, _) = await SeedAsync();
            var started = DateTime.UtcNow;
            await new CompoundLineService(kitchen.Db, new NoUser()).ApplyAsync(new[] { CompoundLineSplitter.AndSaltPepper });
            kitchen.Db.ChangeTracker.Clear();
            var pepper = await kitchen.Db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == plain && ri.IngredientId == kitchen.Foods["Pepper"]);
            pepper.RawLine = "1/4 tsp pepper";
            pepper.Quantity = 0.25m;
            await kitchen.Db.SaveChangesAsync();
            kitchen.Db.ChangeTracker.Clear();

            var undone = await new LineRelinkService(kitchen.Db, new NoUser()).UndoAsync(started, apply: true);

            undone.Unsplit.Should().Be(2);
            undone.Skipped.Should().Be(1);
            (await LinesAsync(kitchen, plain)).Should().Contain(("Salt", 0, 0m, Gram, "salt to taste"));
            (await kitchen.Db.AuditLogEntries.CountAsync(a => a.EntityId == plain)).Should().Be(1);
        }
    }
}
