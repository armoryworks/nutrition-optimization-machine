using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
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
    /// An order-only pass changes positions and nothing else, so every older line undo follows a
    /// line to where the pass put it (staging checkpoint 2, M25: the relink undo skipped 20,737 lines
    /// and the start-up fill undo 3,379 of 3,530 as rebuilt). Each case writes one line operation,
    /// runs the order-only pass, undoes the operation, and then undoes the pass: after the first undo
    /// the recipe holds its first rows at the pass's positions, after the second exactly its first rows.
    /// </summary>
    public class OrderOnlyUndoTests
    {
        private const long Operator = 2;
        private const long Piece = 1, Cup = 2, Teaspoon = 3, Tablespoon = 4, Gram = 5, Quart = 6;
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Admin : ICurrentUserService
        {
            public long? PersonId => Operator;
            public long PersonIdOrSystem => Operator;
            public long RequiredPersonId => Operator;
            public string? UserId => "operator";
            public string RequiredUserId => "operator";
        }

        private sealed record Print(long IngredientId, int SortOrder, decimal Quantity, long MeasurementId, string RawLine, string? LineKind, decimal? Retention, string? Basis);

        private sealed record Case(string Name, string[] Source, (string Food, string RawLine, decimal Quantity, long MeasurementId, string? Kind)[] Rows);

        private sealed class Fixture
        {
            public required ApplicationDbContext Db { get; init; }
            public required long RecipeId { get; init; }
            public required Dictionary<string, long> Foods { get; init; }
            public required string[] Source { get; init; }
        }

        private static async Task<Fixture> SetUpAsync(Case c, params string[] moreFoods)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Teaspoon, Name = "Teaspoon", Symbol = "tsp", MeasurementCategoryId = 2, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Tablespoon, Name = "Tablespoon", Symbol = "tbsp", MeasurementCategoryId = 2, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Quart, Name = "Quart", Symbol = "qt", MeasurementCategoryId = 2, BaseUnitConversionFactor = 1m });
            var foods = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var name in c.Rows.Select(r => r.Food).Concat(moreFoods).Distinct())
            {
                var food = new IngredientEntity
                {
                    Name = name,
                    CurationStatusId = (long)CurationStatusEnum.Curated,
                    IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
                };
                db.Ingredients.Add(food);
                await db.SaveChangesAsync();
                foods[name] = food.Id;
            }

            var record = new ScraperRecipe
            {
                Name = c.Name,
                Ingredients = c.Source.Select(l => new ScraperIngredient { RawLine = l }).ToList(),
                Steps = new List<ScraperStep> { new() { Order = 1, Instruction = "Mix everything well in a large bowl." } },
            };
            var recipe = new RecipeEntity
            {
                Name = c.Name,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                ScrapedAtUtc = Imported,
                LicenseStatus = RecipeLicenseStatus.PublicDomain,
                RecipeIngredients = c.Rows.Select(r => new RecipeIngredientEntity
                {
                    IngredientId = foods[r.Food], SortOrder = 0, Quantity = r.Quantity, MeasurementId = r.MeasurementId, RawLine = r.RawLine,
                    LineKind = r.Kind, CreatedDate = Imported, CreatedByPersonId = Operator,
                }).ToList(),
                RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Mix", Description = "Mix everything well in a large bowl." } },
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            var content = SourceRecord.Content(record);
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity
            {
                RecipeId = recipe.Id, RawJsonLd = content, Format = SourceRecord.Format(record), ContentSha256 = SourceRecord.Sha256(content), FetchedAtUtc = Imported,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture { Db = db, RecipeId = recipe.Id, Foods = foods, Source = c.Source };
        }

        private static readonly Case Cake = new("Cake",
            new[] { "2 cups flour", "1/2 cup sugar", "3 eggs", "1 cup milk" },
            new (string, string, decimal, long, string?)[]
            {
                ("milk", "1 cup milk", 1, Cup, null), ("eggs", "3 eggs", 3, Piece, null), ("sugar", "1/2 cup sugar", 0.5m, Cup, null), ("flour", "2 cups flour", 2, Cup, null),
            });

        private static async Task<List<Print>> PrintAsync(Fixture f) =>
            (await f.Db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking().Where(ri => ri.RecipeId == f.RecipeId && !ri.IsDeleted).ToListAsync())
            .Select(ri => new Print(ri.IngredientId, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine, ri.LineKind, ri.AlcoholRetention, ri.AlcoholRetentionBasis))
            .OrderBy(p => p.SortOrder).ThenBy(p => p.IngredientId).ThenBy(p => p.RawLine, StringComparer.Ordinal)
            .ToList();

        private static List<Print> AtSourcePositions(Fixture f, IEnumerable<Print> rows) =>
            rows.Select(p => p with { SortOrder = Array.IndexOf(f.Source, p.RawLine) })
                .OrderBy(p => p.SortOrder).ThenBy(p => p.IngredientId).ThenBy(p => p.RawLine, StringComparer.Ordinal)
                .ToList();

        private static RecipeReprocessService Reprocess(ApplicationDbContext db) =>
            new(db, new Admin(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(), new Mock<IRecipeNutritionService>().Object,
                NullLogger<RecipeReprocessService>.Instance);

        private static async Task<RecipeReprocessResult> OrderOnlyAsync(Fixture f)
        {
            await Task.Delay(5);
            var result = await Reprocess(f.Db).RebuildAsync(new RecipeReprocessRequestModel { RecipeIds = new List<long> { f.RecipeId } }, apply: true, orderOnly: true);
            f.Db.ChangeTracker.Clear();
            result.Rebuilt.Should().Be(1, "the order-only pass must write for the case to mean anything");
            return result;
        }

        private static async Task UndoOrderOnlyAsync(Fixture f, RecipeReprocessResult order, List<Print> first)
        {
            var back = await Reprocess(f.Db).UndoAsync(null, order.BatchId, apply: true);
            f.Db.ChangeTracker.Clear();
            (back.Restored, back.Skipped).Should().Be((1, 0), "the order-only pass's own undo still finds its rows as written");
            (await PrintAsync(f)).Should().Equal(first);
        }

        private static async Task<AuditLogEntryEntity> JournalAsync(Fixture f, string entityType, string changeType, string? propertyName, string? oldValue, string? newValue)
        {
            var entry = new AuditLogEntryEntity
            {
                EntityType = entityType, EntityId = f.RecipeId, ChangeType = changeType, PropertyName = propertyName,
                OldValue = oldValue, NewValue = newValue, Timestamp = DateTime.UtcNow, ChangedByPersonId = Operator,
            };
            f.Db.AuditLogEntries.Add(entry);
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            return entry;
        }

        private static async Task SetAsync(Fixture f, string food, Action<RecipeIngredientEntity> change)
        {
            var row = await f.Db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == f.RecipeId && ri.IngredientId == f.Foods[food]);
            change(row);
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }

        private static async Task AmountJournalAsync(Fixture f, string entityType, string changeType, string? propertyName, string food, decimal quantity, long measurementId)
        {
            var row = await f.Db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == f.RecipeId && ri.IngredientId == f.Foods[food]);
            await SetAsync(f, food, r => { r.Quantity = quantity; r.MeasurementId = measurementId; });
            await JournalAsync(f, entityType, changeType, propertyName ?? RecipeLineClassificationService.LinePropertyName(row.IngredientId, row.SortOrder),
                PackageLineService.Amount(row.IngredientId, row.SortOrder, row.Quantity, row.MeasurementId),
                PackageLineService.Amount(row.IngredientId, row.SortOrder, quantity, measurementId));
        }

        [Fact]
        public async Task The_start_up_fill_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var f = await SetUpAsync(Cake);
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            await AmountJournalAsync(f, ScrapedRecipeRepairService.LineEntityType, ScrapedRecipeRepairService.FillChangeType, null, "eggs", 4, Piece);
            var order = await OrderOnlyAsync(f);

            var undone = await new ScrapedRecipeRepairService(f.Db, new RecipeVettingService()).UndoFillsAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0), "staging skipped 3,379 of 3,530 fills as rebuilt after the order-only pass (M25)");
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_reread_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var f = await SetUpAsync(Cake);
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            await AmountJournalAsync(f, PackageLineService.RereadEntityType, PackageLineService.RereadChangeType, nameof(RecipeIngredientEntity.MeasurementId), "milk", 0.25m, Quart);
            var order = await OrderOnlyAsync(f);

            var undone = await new PackageLineService(f.Db, new Admin()).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_quantity_revert_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var f = await SetUpAsync(Cake);
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            await AmountJournalAsync(f, AiRepairRevertService.RevertEntityType, AiRepairRevertService.RevertChangeType, nameof(RecipeIngredientEntity.Quantity), "sugar", 0, Piece);
            var order = await OrderOnlyAsync(f);

            var undone = await new AiRepairRevertService(f.Db, new Admin()).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_label_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var noted = Cake with { Rows = Cake.Rows.Select(r => r.Food == "eggs" ? r with { Kind = RecipeIngredientLineKind.Note } : r).ToArray() };
            var f = await SetUpAsync(noted);
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            var eggs = f.Foods["eggs"];
            await SetAsync(f, "eggs", r => r.LineKind = RecipeIngredientLineKind.Ingredient);
            await JournalAsync(f, RecipeLineLabelReviewService.ReviewEntityType, RecipeLineLabelReviewService.ReviewChangeType,
                RecipeLineClassificationService.LinePropertyName(eggs, 0), RecipeIngredientLineKind.Note, $"{RecipeIngredientLineKind.Ingredient} (review): 3 eggs");
            var order = await OrderOnlyAsync(f);

            var undone = await new RecipeLineLabelReviewService(f.Db, new Admin()).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_alcohol_retention_undo_follows_an_order_only_pass_which_keeps_the_factor_and_both_undos_restore_the_first_rows()
        {
            var f = await SetUpAsync(Cake);
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            var milk = f.Foods["milk"];
            await SetAsync(f, "milk", r => { r.AlcoholRetention = 0.25m; r.AlcoholRetentionBasis = "timed:bake:60"; });
            await JournalAsync(f, AlcoholRetentionService.LineEntityType, AlcoholRetentionService.ChangeType, AlcoholRetentionService.PropertyName(milk, 0),
                AlcoholRetentionService.JournalValue(null, null), AlcoholRetentionService.JournalValue(0.25m, "timed:bake:60"));
            var order = await OrderOnlyAsync(f);
            (await PrintAsync(f)).Should().Contain(p => p.IngredientId == milk && p.Retention == 0.25m,
                "an order-only pass rewrote the rows without the retention columns, so it dropped every factor");

            var undone = await new AlcoholRetentionService(f.Db, new Mock<IRecipeNutritionService>().Object, NullLogger<AlcoholRetentionService>.Instance).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_relink_undo_follows_an_order_only_pass_even_for_a_line_the_relink_had_to_reposition_and_both_undos_restore_the_first_rows()
        {
            var c = new Case("Custard", new[] { "1 cup milk", "3 eggs", "2 tbsp milk" },
                new (string, string, decimal, long, string?)[] { ("milk", "1 cup milk", 1, Cup, null), ("eggs", "3 eggs", 3, Piece, null), ("2 tbsp milk", "2 tbsp milk", 2, Tablespoon, null) });
            var f = await SetUpAsync(c);
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            var (junk, milk) = (f.Foods["2 tbsp milk"], f.Foods["milk"]);
            var position = await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, junk, 0, milk, 0, Operator);
            f.Db.ChangeTracker.Clear();
            position.Should().Be(1, "milk already holds a line at position 0, so the relink puts this one at the next free position");
            await JournalAsync(f, LineRelinkService.RelinkEntityType, LineRelinkService.RelinkChangeType, nameof(RecipeIngredientEntity.IngredientId),
                LineRelinkService.Place(junk, 0), LineRelinkService.Place(milk, position!.Value));
            var order = await OrderOnlyAsync(f);

            var undone = await new LineRelinkService(f.Db, new Admin()).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0), "staging skipped 20,737 of C7's relinked lines as rebuilt after the order-only pass (M25)");
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_link_recheck_line_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var f = await SetUpAsync(Cake, "Raw Potato Skins");
            var first = await PrintAsync(f);
            var (eggs, wrong) = (f.Foods["eggs"], f.Foods["Raw Potato Skins"]);
            await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, eggs, 0, wrong, 0, Operator);
            f.Db.ChangeTracker.Clear();
            var entry = await JournalAsync(f, LineRelinkService.RelinkEntityType, LinkRecheckService.LineChangeType, nameof(RecipeIngredientEntity.IngredientId),
                LineRelinkService.Place(eggs, 0), LineRelinkService.Place(wrong, 0));
            var order = await OrderOnlyAsync(f);

            var undone = await new LineRelinkService(f.Db, new Admin()).UndoEntriesAsync(new[] { entry.Id });
            f.Db.ChangeTracker.Clear();

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_alternative_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var c = new Case("Toast", new[] { "2 slices bread", "1 tbsp butter or margarine" },
                new (string, string, decimal, long, string?)[] { ("bread", "2 slices bread", 2, Piece, null), ("margarine", "1 tbsp butter or margarine", 1, Tablespoon, null) });
            var f = await SetUpAsync(c, "butter");
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            var (margarine, butter) = (f.Foods["margarine"], f.Foods["butter"]);
            var position = await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, margarine, 0, butter, 0, Operator);
            f.Db.ChangeTracker.Clear();
            await JournalAsync(f, LineRelinkService.RelinkEntityType, CompoundLineService.AlternativeChangeType, nameof(RecipeIngredientEntity.IngredientId),
                LineRelinkService.Place(margarine, 0), LineRelinkService.Place(butter, position!.Value));
            var order = await OrderOnlyAsync(f);

            var undone = await new LineRelinkService(f.Db, new Admin()).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.AlternativesPutBack, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_unfold_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var c = new Case("Porridge", new[] { "2 cups flour", "2 qts. water", "1 cup water" },
                new (string, string, decimal, long, string?)[] { ("water", "2 qts. water + 1 cup water", 2, Piece, null), ("flour", "2 cups flour", 2, Cup, null) });
            var f = await SetUpAsync(c);
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            (await new LineUnfoldService(f.Db, new Admin()).UnfoldAsync(apply: true)).Folds.Should().Be(1);
            f.Db.ChangeTracker.Clear();
            var order = await OrderOnlyAsync(f);

            var undone = await new LineRelinkService(f.Db, new Admin()).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Refolded, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            (await PrintAsync(f)).Should().Equal(first.Select(p => p with { SortOrder = p.IngredientId == f.Foods["water"] ? 1 : 0 })
                .OrderBy(p => p.SortOrder).ThenBy(p => p.IngredientId).ToList(), "the folded line takes back its text at the place the pass gave its first part");
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_split_undo_follows_an_order_only_pass_and_both_undos_restore_the_first_rows()
        {
            var c = new Case("Soup", new[] { "2 cups stock", "salt to taste", "pepper to taste" },
                new (string, string, decimal, long, string?)[] { ("Salt", "salt and pepper to taste", 0, Gram, null), ("stock", "2 cups stock", 2, Cup, null) });
            var f = await SetUpAsync(c, "Pepper");
            var first = await PrintAsync(f);
            var started = DateTime.UtcNow.AddSeconds(-1);
            (await new CompoundLineService(f.Db, new Admin()).ApplyAsync(CompoundLineSplitter.Rules.ToList())).Split.Should().Be(1);
            f.Db.ChangeTracker.Clear();
            var order = await OrderOnlyAsync(f);

            var undone = await new LineRelinkService(f.Db, new Admin()).UndoAsync(started, apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Unsplit, undone.Skipped, undone.SkippedRebuilt).Should().Be((1, 0, 0));
            var rows = await PrintAsync(f);
            rows.Select(p => (p.IngredientId, p.Quantity, p.MeasurementId, p.RawLine)).Should().BeEquivalentTo(first.Select(p => (p.IngredientId, p.Quantity, p.MeasurementId, p.RawLine)));
            rows.Single(p => p.RawLine == "2 cups stock").SortOrder.Should().Be(0);
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task The_applied_link_undo_after_an_order_only_pass_leaves_the_pass_undoable_and_both_undos_restore_the_first_rows()
        {
            var c = new Case("Stroganoff", new[] { "2 cups milk", "8 oz baby bella mushrooms" },
                new (string, string, decimal, long, string?)[] { ("baby bella mushrooms", "8 oz baby bella mushrooms", 8, Gram, null), ("milk", "2 cups milk", 2, Cup, null) });
            var f = await SetUpAsync(c, "Cremini Mushrooms");
            var first = await PrintAsync(f);
            var (holder, target) = (f.Foods["baby bella mushrooms"], f.Foods["Cremini Mushrooms"]);
            await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, holder, 0, target, 0, Operator);
            var proposal = new Nom.Data.Curation.FoodCatalogProposalEntity
            {
                Action = Nom.Data.Curation.FoodProposalAction.Update, IngredientId = holder, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "baby bella mushrooms",
                ProposedValue = target.ToString(), Source = "ai:fake", Status = Nom.Data.Curation.FoodProposalStatus.Applied,
            };
            f.Db.FoodCatalogProposals.Add(proposal);
            await f.Db.SaveChangesAsync();
            var linked = DateTime.UtcNow;
            f.Db.AuditLogEntries.AddRange(
                LinkApplyJournal.Entry(proposal.Id, LinkApplyJournal.Header,
                    LinkApplyJournal.Write(new LinkApplyJournal.HeaderValue(holder, "baby bella mushrooms", (long)CurationStatusEnum.Curated, "ai:fake", false, target)),
                    target.ToString(), linked, SystemConstants.SystemPersonId),
                LinkApplyJournal.Entry(proposal.Id, LinkApplyJournal.Line, $"{f.RecipeId}:{holder}@0", $"{f.RecipeId}:{target}@0", linked, SystemConstants.SystemPersonId));
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            var order = await OrderOnlyAsync(f);

            var undone = await new LinkApplyUndoService(f.Db, new Admin()).UndoAsync(linked.AddSeconds(-1), apply: true);
            f.Db.ChangeTracker.Clear();

            (undone.Proposals, undone.LinesRestored, undone.SkippedRebuilt).Should().Be((1, 1, 0));
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task An_order_only_write_is_not_a_rebuild_and_a_line_it_did_not_keep_is_skipped_as_moved_not_as_rebuilt()
        {
            var f = await SetUpAsync(Cake);
            var started = DateTime.UtcNow.AddSeconds(-1);
            await AmountJournalAsync(f, ScrapedRecipeRepairService.LineEntityType, ScrapedRecipeRepairService.FillChangeType, null, "eggs", 4, Piece);
            await OrderOnlyAsync(f);
            (await RecipeReprocessService.RebuiltAtAsync(f.Db, new[] { f.RecipeId })).Should().NotContainKey(f.RecipeId);
            await SetAsync(f, "eggs", r => r.Quantity = 6);

            var undone = await new ScrapedRecipeRepairService(f.Db, new RecipeVettingService()).UndoFillsAsync(started, apply: true);

            (undone.Restored, undone.Skipped, undone.SkippedRebuilt).Should().Be((0, 1, 0), "a line changed by hand since is never overwritten");
        }

        [Fact]
        public async Task The_order_only_undo_run_alone_puts_the_positions_back_after_a_later_relink_and_fill_and_every_undo_after_it_still_finds_its_line()
        {
            var f = await SetUpAsync(Cake, "whole milk");
            var first = await PrintAsync(f);
            var order = await OrderOnlyAsync(f);
            var (milk, whole, eggs) = (f.Foods["milk"], f.Foods["whole milk"], f.Foods["eggs"]);
            var milkAt = (await PrintAsync(f)).Single(p => p.IngredientId == milk).SortOrder;
            await Task.Delay(5);
            var later = DateTime.UtcNow.AddMilliseconds(-1);
            (await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, milk, milkAt, whole, milkAt, Operator)).Should().Be(milkAt);
            f.Db.ChangeTracker.Clear();
            await JournalAsync(f, LineRelinkService.RelinkEntityType, LineRelinkService.RelinkChangeType, nameof(RecipeIngredientEntity.IngredientId),
                LineRelinkService.Place(milk, milkAt), LineRelinkService.Place(whole, milkAt));
            await AmountJournalAsync(f, ScrapedRecipeRepairService.LineEntityType, ScrapedRecipeRepairService.FillChangeType, null, "eggs", 4, Piece);

            var back = await Reprocess(f.Db).UndoAsync(null, order.BatchId, apply: true);
            f.Db.ChangeTracker.Clear();

            (back.Restored, back.Skipped, back.PositionsFollowed).Should().Be((1, 0, 1), "staging skipped 22,666 order-only recipes whose links, not positions, changed later (N33)");
            (await PrintAsync(f)).Should().Equal(first.Select(p => p.IngredientId == milk ? p with { IngredientId = whole } : p.IngredientId == eggs ? p with { Quantity = 4 } : p)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.IngredientId).ThenBy(p => p.RawLine, StringComparer.Ordinal).ToList());
            var follow = await f.Db.RecipeReprocessJournals.AsNoTracking().SingleAsync(j => j.UndoneAt == null);
            follow.BatchId.Should().Be(RecipeReprocessService.OrderFollowPrefix + order.Results.Single().JournalId);

            var fills = await new ScrapedRecipeRepairService(f.Db, new RecipeVettingService()).UndoFillsAsync(later, apply: true);
            f.Db.ChangeTracker.Clear();
            var relinks = await new LineRelinkService(f.Db, new Admin()).UndoAsync(later, apply: true);
            f.Db.ChangeTracker.Clear();

            (fills.Restored, fills.Skipped).Should().Be((1, 0));
            (relinks.Restored, relinks.Skipped).Should().Be((1, 0), "the later relink's undo follows its line to the place the order undo gave it");
            (await PrintAsync(f)).Should().Equal(first);

            var again = await Reprocess(f.Db).UndoAsync(null, follow.BatchId, apply: true);
            f.Db.ChangeTracker.Clear();

            (again.Restored, again.Skipped).Should().Be((1, 0), "the undos after it carried what they put back into the order undo's own write");
            (await PrintAsync(f)).Should().Equal(AtSourcePositions(f, first));
            (await f.Db.RecipeReprocessJournals.AsNoTracking().SingleAsync(j => j.Id == order.Results.Single().JournalId)).UndoneAt.Should().BeNull("undoing the order undo sets the pass's positions again, so the pass stands");
            await UndoOrderOnlyAsync(f, order, first);
        }

        [Fact]
        public async Task An_undo_since_never_takes_back_the_order_undo_write_so_running_it_again_leaves_the_pass_undone()
        {
            var f = await SetUpAsync(Cake, "whole milk");
            var first = await PrintAsync(f);
            var earlier = DateTime.UtcNow.AddSeconds(-2);
            var started = DateTime.UtcNow.AddSeconds(-1);
            var order = await OrderOnlyAsync(f);
            var (milk, whole) = (f.Foods["milk"], f.Foods["whole milk"]);
            var milkAt = (await PrintAsync(f)).Single(p => p.IngredientId == milk).SortOrder;
            (await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, milk, milkAt, whole, milkAt, Operator)).Should().Be(milkAt);
            f.Db.ChangeTracker.Clear();
            await JournalAsync(f, LineRelinkService.RelinkEntityType, LineRelinkService.RelinkChangeType, nameof(RecipeIngredientEntity.IngredientId),
                LineRelinkService.Place(milk, milkAt), LineRelinkService.Place(whole, milkAt));

            var back = await Reprocess(f.Db).UndoAsync(started, null, apply: true);
            f.Db.ChangeTracker.Clear();
            (back.Restored, back.PositionsFollowed).Should().Be((1, 1));
            var undone = await PrintAsync(f);
            undone.Should().Equal(first.Select(p => p.IngredientId == milk ? p with { IngredientId = whole } : p)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.IngredientId).ThenBy(p => p.RawLine, StringComparer.Ordinal).ToList());

            foreach (var since in new[] { started, earlier })
            {
                var dry = await Reprocess(f.Db).UndoAsync(since, null);
                var again = await Reprocess(f.Db).UndoAsync(since, null, apply: true);
                f.Db.ChangeTracker.Clear();

                (dry.Entries, dry.Restored, again.Entries, again.Restored).Should().Be((0, 0, 0, 0), "the order undo's write is an undo, and an undo since takes back writes, not undos");
                (await PrintAsync(f)).Should().Equal(undone);
                (await f.Db.RecipeReprocessJournals.AsNoTracking().SingleAsync(j => j.Id == order.Results.Single().JournalId)).UndoneAt.Should().NotBeNull();
            }
        }

        [Theory]
        [InlineData(RecipeReprocessKind.Rebuild)]
        [InlineData(RecipeReprocessKind.Order)]
        public async Task An_order_only_undo_is_not_followed_over_a_later_reprocess_write_that_stands_so_that_write_stays_undoable(string laterKind)
        {
            var f = await SetUpAsync(Cake, "whole milk");
            var order = await OrderOnlyAsync(f);
            var afterPass = await PrintAsync(f);
            var (milk, whole) = (f.Foods["milk"], f.Foods["whole milk"]);
            var milkAt = afterPass.Single(p => p.IngredientId == milk).SortOrder;
            (await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, milk, milkAt, whole, milkAt, Operator)).Should().Be(milkAt);
            f.Db.ChangeTracker.Clear();
            var pass = await f.Db.RecipeReprocessJournals.AsNoTracking().SingleAsync(j => j.Id == order.Results.Single().JournalId);
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var written = System.Text.Json.JsonSerializer.Deserialize<List<RecipeReprocessService.LineRow>>(pass.NewRows!, json)!
                .Select(r => r.IngredientId == milk ? r with { IngredientId = whole } : r).ToList();
            f.Db.RecipeReprocessJournals.Add(new RecipeReprocessJournalEntity
            {
                RecipeId = f.RecipeId, BatchId = "rp-later", Kind = laterKind, RuleVersion = RecipeLineBuilder.Version,
                OldRows = pass.NewRows, NewRows = System.Text.Json.JsonSerializer.Serialize(written, json),
                CreatedDate = DateTime.UtcNow, CreatedByPersonId = Operator,
            });
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();

            var back = await Reprocess(f.Db).UndoAsync(null, order.BatchId, apply: true);
            f.Db.ChangeTracker.Clear();

            (back.Restored, back.Skipped, back.PositionsFollowed).Should().Be((0, 1, 0), "the later write's own undo does not follow lines, so moving them would leave it impossible to undo");
            var later = await Reprocess(f.Db).UndoAsync(null, "rp-later", apply: true);
            f.Db.ChangeTracker.Clear();
            (later.Restored, later.Skipped).Should().Be((1, 0));
            (await PrintAsync(f)).Should().Equal(afterPass);
        }

        [Fact]
        public async Task An_order_only_undo_whose_lines_a_later_step_added_to_still_skips_the_recipe()
        {
            var f = await SetUpAsync(Cake);
            var order = await OrderOnlyAsync(f);
            f.Db.RecipeIngredients.Add(new RecipeIngredientEntity
            {
                RecipeId = f.RecipeId, IngredientId = f.Foods["eggs"], SortOrder = 9, Quantity = 1, MeasurementId = Piece, RawLine = "1 egg yolk",
                CreatedDate = DateTime.UtcNow, CreatedByPersonId = Operator,
            });
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();

            var back = await Reprocess(f.Db).UndoAsync(null, order.BatchId, apply: true);

            (back.Restored, back.Skipped, back.PositionsFollowed).Should().Be((0, 1, 0), "a line added since cannot be given a place the pass found");
        }

        [Fact]
        public async Task The_relink_dry_run_after_an_order_only_pass_reads_only_and_gives_the_counts_of_the_apply()
        {
            var c = new Case("Custard", new[] { "1 cup milk", "3 eggs", "2 tbsp milk" },
                new (string, string, decimal, long, string?)[] { ("milk", "1 cup milk", 1, Cup, null), ("eggs", "3 eggs", 3, Piece, null), ("2 tbsp milk", "2 tbsp milk", 2, Tablespoon, null) });
            var f = await SetUpAsync(c);
            var started = DateTime.UtcNow.AddSeconds(-1);
            var (junk, milk) = (f.Foods["2 tbsp milk"], f.Foods["milk"]);
            var position = await RecipeLineRowWriter.MoveAsync(f.Db, f.RecipeId, junk, 0, milk, 0, Operator);
            f.Db.ChangeTracker.Clear();
            await JournalAsync(f, LineRelinkService.RelinkEntityType, LineRelinkService.RelinkChangeType, nameof(RecipeIngredientEntity.IngredientId),
                LineRelinkService.Place(junk, 0), LineRelinkService.Place(milk, position!.Value));
            await OrderOnlyAsync(f);
            var before = await PrintAsync(f);
            var journals = await f.Db.RecipeReprocessJournals.AsNoTracking().Select(j => j.NewRows).ToListAsync();

            var dry = await new LineRelinkService(f.Db, new Admin()).UndoAsync(started);
            f.Db.ChangeTracker.Clear();

            (await PrintAsync(f)).Should().Equal(before, "a dry run runs on a copy in memory and writes no row (N17)");
            (await f.Db.RecipeReprocessJournals.AsNoTracking().Select(j => j.NewRows).ToListAsync()).Should().Equal(journals);
            (await f.Db.AuditLogEntries.CountAsync(a => a.ChangeType == LineRelinkService.RelinkChangeType)).Should().Be(1);
            var cancelled = () => new LineRelinkService(f.Db, new Admin()).UndoAsync(started, cancellationToken: new System.Threading.CancellationToken(true));
            await cancelled.Should().ThrowAsync<OperationCanceledException>();

            var applied = await new LineRelinkService(f.Db, new Admin()).UndoAsync(started, apply: true);

            dry.Applied.Should().BeFalse();
            (dry.Restored, dry.Skipped, dry.SkippedRebuilt).Should().Be((applied.Restored, applied.Skipped, applied.SkippedRebuilt));
            applied.Restored.Should().Be(1);
        }
    }
}
