using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
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
    /// The two corrections of what the model lanes already wrote on recipe lines: labels that take
    /// a real ingredient off the label, and piece counts filled from a quote that states no count.
    /// Both report before they write, journal each change and undo as a batch, and neither changes
    /// a recipe's curation status.
    /// </summary>
    public class LineCorrectionTests
    {
        private const long Gram = 1, Piece = 3;
        private const long Curated = (long)CurationStatusEnum.Curated;
        private const long NonCurated = (long)CurationStatusEnum.NonCurated;

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m });
            db.SaveChanges();
            return db;
        }

        private static IngredientEntity Food(long id, string name, bool curated = true, decimal? gramsPerItem = null) => new()
        {
            Id = id,
            Name = name,
            CurationStatusId = curated ? Curated : NonCurated,
            GramsPerItem = gramsPerItem,
            IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
        };

        private sealed record Row(long FoodId, string Line, decimal Quantity = 0, string? Kind = null, long MeasurementId = Gram);

        private static RecipeEntity Recipe(long id, long status, params Row[] rows) => new()
        {
            Id = id,
            Name = $"Recipe {id}",
            CurationStatusId = status,
            ScrapedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            RecipeIngredients = rows.Select((r, i) => new RecipeIngredientEntity
            {
                IngredientId = r.FoodId, SortOrder = i, RawLine = r.Line, Quantity = r.Quantity, MeasurementId = r.MeasurementId, LineKind = r.Kind,
            }).ToList(),
        };

        private static async Task<ApplicationDbContext> LabelsAsync()
        {
            var db = NewContext();
            db.Ingredients.AddRange(
                Food(1, "Eggs"), Food(2, "Puff Paste"), Food(3, "Chicken Stock"), Food(4, "Water"), Food(5, "Pudding Cloth", curated: false));
            db.Recipes.AddRange(
                Recipe(10, Curated,
                    new Row(1, "whites 3 eggs beaten stiff", 3m, RecipeIngredientLineKind.Instruction, Piece),
                    new Row(2, "puff paste", 0m, RecipeIngredientLineKind.Reference)),
                Recipe(11, NonCurated,
                    new Row(3, "chicken-stock", 0m, RecipeIngredientLineKind.Reference),
                    new Row(4, "boiling water", 0m, RecipeIngredientLineKind.Equipment),
                    new Row(5, "a pudding cloth", 0m, RecipeIngredientLineKind.Equipment)),
                Recipe(12, Curated,
                    new Row(1, "2 eggs", 2m, null, Piece),
                    new Row(3, "chicken-stock", 0m, RecipeIngredientLineKind.Reference)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return db;
        }

        private static RecipeLineLabelReviewService Labels(ApplicationDbContext db) => new(db, new NoUser());

        [Fact]
        public async Task The_label_review_counts_both_rules_and_writes_nothing()
        {
            using var db = await LabelsAsync();

            var result = await Labels(db).ReviewAsync();

            result.Applied.Should().BeFalse();
            result.LinesExamined.Should().Be(6);
            result.WithAmount.Should().Be(1);
            result.NamingCuratedFood.Should().Be(1);
            result.ShortOnCuratedFood.Should().Be(3, "chicken-stock twice and boiling water sit on curated foods; the pudding cloth's food is not curated");
            (result.Certain.Lines, result.Certain.Recipes, result.Certain.ApprovedRecipes, result.Certain.ApprovedStopPassing).Should().Be((2, 1, 1, 1),
                "recipe 10 then has a puff paste line with no amount");
            (result.WithShortLines.Lines, result.WithShortLines.Recipes, result.WithShortLines.ApprovedRecipes, result.WithShortLines.ApprovedStopPassing).Should().Be((5, 3, 2, 2));
            result.RecipesByStatus.Should().BeEquivalentTo(new Dictionary<string, int> { ["Curated"] = 1 });
            result.Samples.Select(s => (s.Line, s.Reason)).Should().Equal(
                ("whites 3 eggs beaten stiff", RecipeLineKindGuard.QuantityReason), ("puff paste", RecipeLineKindGuard.CuratedNameReason));
            result.Remaining.Should().Be(2);

            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
            (await db.RecipeIngredients.CountAsync(ri => ri.LineKind == RecipeIngredientLineKind.Ingredient)).Should().Be(0);
        }

        [Fact]
        public async Task Applying_the_certain_cases_journals_each_line_and_leaves_the_recipe_status_alone()
        {
            using var db = await LabelsAsync();

            var result = await Labels(db).ReviewAsync(apply: true);

            (result.Changed, result.Skipped, result.Remaining).Should().Be((2, 0, 0));
            var lines = await db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == 10).OrderBy(ri => ri.SortOrder).ToListAsync();
            lines.Select(l => l.LineKind).Should().Equal(RecipeIngredientLineKind.Ingredient, RecipeIngredientLineKind.Ingredient);
            lines.Should().OnlyContain(l => l.LastModifiedDate != null);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == 10)).CurationStatusId.Should().Be(Curated);
            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == 11 && ri.IngredientId == 3)).LineKind.Should().Be(RecipeIngredientLineKind.Reference);

            var journal = await db.AuditLogEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
            journal.Select(a => (a.ChangeType, a.EntityId, a.PropertyName, a.OldValue)).Should().Equal(
                (RecipeLineLabelReviewService.ReviewChangeType, 10L, "LineKind[1:0]", RecipeIngredientLineKind.Instruction),
                (RecipeLineLabelReviewService.ReviewChangeType, 10L, "LineKind[2:1]", RecipeIngredientLineKind.Reference));
            journal[1].NewValue.Should().Be("ingredient (names a curated food): puff paste");

            (await Labels(db).ReviewAsync()).Certain.Lines.Should().Be(0);
        }

        [Fact]
        public async Task The_wider_rule_is_applied_only_when_asked_for_and_the_batch_undoes()
        {
            using var db = await LabelsAsync();
            var since = DateTime.UtcNow.AddSeconds(-1);

            var result = await Labels(db).ReviewAsync(apply: true, shortLinesOnCuratedFoods: true);

            result.Changed.Should().Be(5);
            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == 11 && ri.IngredientId == 5)).LineKind.Should().Be(RecipeIngredientLineKind.Equipment);

            var edited = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == 11 && ri.IngredientId == 4);
            edited.LineKind = RecipeIngredientLineKind.Note;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var labelled = await db.RecipeIngredients.AsNoTracking().OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).Select(ri => ri.LineKind).ToListAsync();

            var dryRun = await Labels(db).UndoAsync(since);

            (dryRun.Applied, dryRun.Restored, dryRun.Skipped).Should().Be((false, 4, 1));
            (await db.RecipeIngredients.AsNoTracking().OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).Select(ri => ri.LineKind).ToListAsync())
                .Should().Equal(labelled, "a dry run writes nothing");
            (await db.AuditLogEntries.CountAsync()).Should().Be(5);

            var undo = await Labels(db).UndoAsync(since, apply: true);

            (undo.Applied, undo.Restored, undo.Skipped).Should().Be((true, 4, 1), "a line labelled again since is left as it is");
            var kinds = await db.RecipeIngredients.AsNoTracking().OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).Select(ri => ri.LineKind).ToListAsync();
            kinds.Should().Equal(
                RecipeIngredientLineKind.Instruction, RecipeIngredientLineKind.Reference,
                RecipeIngredientLineKind.Reference, RecipeIngredientLineKind.Note, RecipeIngredientLineKind.Equipment,
                null, RecipeIngredientLineKind.Reference);
            (await db.AuditLogEntries.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task A_heading_a_note_or_a_utensil_list_keeps_its_label()
        {
            using var db = NewContext();
            db.Ingredients.AddRange(Food(1, "Meatballs"), Food(2, "Baked Potatoes"), Food(3, "Dripping"), Food(4, "Piping Tips", curated: false));
            db.Recipes.Add(Recipe(40, Curated,
                new Row(4, "Piping tips: Wilton #125, Wilton #21, small round tip", 1m, RecipeIngredientLineKind.Equipment, Piece),
                new Row(1, "MEATBALLS", 0m, RecipeIngredientLineKind.Note),
                new Row(2, "or 6 baked potatoes", 0m, RecipeIngredientLineKind.Note),
                new Row(3, "tablespoonful of dripping from the top of the soup", 0m, RecipeIngredientLineKind.Reference),
                new Row(3, "dripping", 0m, RecipeIngredientLineKind.Reference)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await Labels(db).ReviewAsync();

            result.Certain.Lines.Should().Be(1);
            result.Samples.Select(s => s.Line).Should().Equal("dripping");
        }

        [Fact]
        public async Task A_can_line_returned_to_ingredient_is_read_by_its_stated_size_in_the_same_apply()
        {
            using var db = NewContext();
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 7, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1, BaseUnitConversionFactor = 28.3495m },
                new BaseMeasurementEntity { Id = 20, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true });
            db.Ingredients.Add(Food(1, "Rotel Tomatoes"));
            db.Recipes.Add(Recipe(50, NonCurated,
                new Row(1, "1 can (10 ounce) Rotel tomatoes", 1m, RecipeIngredientLineKind.Reference, Piece)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await Labels(db).ReviewAsync(apply: true);

            (result.Changed, result.Reread).Should().Be((1, 1));
            var line = await db.RecipeIngredients.AsNoTracking().SingleAsync();
            (line.LineKind, line.Quantity, line.MeasurementId).Should().Be((RecipeIngredientLineKind.Ingredient, 10m, 7L));
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == PackageLineService.RereadChangeType)).Should().Be(1);
        }

        [Fact]
        public async Task The_label_undo_leaves_a_row_that_now_holds_another_line()
        {
            using var db = await LabelsAsync();
            var since = DateTime.UtcNow.AddSeconds(-1);
            await Labels(db).ReviewAsync(apply: true);
            var unfolded = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == 10 && ri.IngredientId == 2);
            unfolded.RawLine = "puff paste, rolled";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var undo = await Labels(db).UndoAsync(since, apply: true);

            (undo.Restored, undo.Skipped).Should().Be((1, 1));
            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == 10 && ri.IngredientId == 2)).LineKind.Should().Be(RecipeIngredientLineKind.Ingredient);
            (await db.AuditLogEntries.SingleAsync()).PropertyName.Should().Be("LineKind[2:1]", "the entry it could not take back stays");
        }

        private static AuditLogEntryEntity Fill(long recipeId, string place, string quote, decimal quantity = 1m) => new()
        {
            EntityType = "RecipeIngredient",
            EntityId = recipeId,
            ChangeType = RecipeAiRepairService.RepairChangeType,
            PropertyName = $"Quantity[{place}]",
            OldValue = "0.0000 (measurement 1)",
            NewValue = $"{quantity} Piece from \"{quote}\"",
            Timestamp = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            ChangedByPersonId = SystemConstants.SystemPersonId,
        };

        private static async Task<ApplicationDbContext> FillsAsync()
        {
            var db = NewContext();
            db.Ingredients.AddRange(
                Food(1, "Lard"), Food(2, "Red Wine"), Food(3, "Eggs", gramsPerItem: 50m), Food(4, "Neck Chops"), Food(5, "Cabbage"), Food(6, "Ground Ginger"));
            db.Recipes.AddRange(
                Recipe(20, Curated,
                    new Row(1, "lard", 1m, null, Piece),
                    new Row(2, "red wine", 1m, null, Piece),
                    new Row(3, "eggs", 1m, null, Piece)),
                Recipe(21, NonCurated,
                    new Row(4, "neck chops", 6m, null, Piece),
                    new Row(5, "cabbage", 1m, null, Piece),
                    new Row(5, "cabbage for the sauce", 1m, null, Piece),
                    new Row(6, "ginger", 1m, null, Piece)));
            db.AuditLogEntries.AddRange(
                Fill(20, "1", "a big teaspoonful of lard"),
                Fill(20, "2:1", "a glassful of good red wine"),
                Fill(20, "3:2", "an egg"),
                Fill(21, "4:0", "six neck chops", 6m),
                Fill(21, "5", "a large cabbage"),
                Fill(21, "6:3", "one scruple of ginger"),
                Fill(21, "6:7", "a pinch of ginger"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return db;
        }

        private static AiRepairRevertService Quantities(ApplicationDbContext db) => new(db, new NoUser());

        [Fact]
        public async Task The_quantity_revert_reports_what_it_would_put_back_and_writes_nothing()
        {
            using var db = await FillsAsync();

            var result = await Quantities(db).RevertAsync();

            result.FillsExamined.Should().Be(7);
            result.Grounded.Should().Be(2, "six chops state a count and an egg has an item weight");
            result.Ambiguous.Should().Be(1, "the cabbage fill has no position and two cabbage lines hold one piece");
            result.LineChanged.Should().Be(1, "no line of ginger sits at position 7");
            result.Ungrounded.Should().Be(3);
            result.ByReason.Should().BeEquivalentTo(new Dictionary<string, int> { [RecipeRepairGrounding.UnreadMeasureRefusal] = 3 });
            (result.Recipes, result.ApprovedRecipes, result.ApprovedStopPassing).Should().Be((2, 1, 1));
            result.RecipesByStatus.Should().BeEquivalentTo(new Dictionary<string, int> { ["Curated"] = 1, ["NonCurated"] = 1 });
            result.Samples.Select(s => s.Quote).Should().BeEquivalentTo("a big teaspoonful of lard", "a glassful of good red wine", "one scruple of ginger");

            (await db.AuditLogEntries.CountAsync()).Should().Be(7);
            (await db.RecipeIngredients.CountAsync(ri => ri.Quantity == 0)).Should().Be(0);
        }

        [Fact]
        public async Task A_fill_whose_line_has_moved_to_another_food_is_found_on_it_and_reverted()
        {
            using var db = await FillsAsync();
            var wine = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == 20 && ri.IngredientId == 2);
            db.RecipeIngredients.Remove(wine);
            db.Ingredients.Add(Food(7, "Wine, table, red"));
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = 20, IngredientId = 7, SortOrder = 1, RawLine = "red wine", Quantity = 1m, MeasurementId = Piece });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await Quantities(db).RevertAsync(apply: true);

            result.FoundMoved.Should().Be(1);
            result.LineChanged.Should().Be(1, "only the ginger fill at a position no ginger line holds is lost");
            result.Changed.Should().Be(3);
            var moved = await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == 20 && ri.IngredientId == 7);
            (moved.Quantity, moved.MeasurementId).Should().Be((0m, Gram));
        }

        [Fact]
        public async Task A_single_countable_item_keeps_its_count_and_a_chosen_reason_reverts_alone()
        {
            using var db = NewContext();
            db.Ingredients.AddRange(Food(1, "Bay Leaf"), Food(2, "Mutton"), Food(3, "Butter"), Food(4, "Red Wine"));
            db.Recipes.Add(Recipe(30, NonCurated,
                new Row(1, "a bay leaf", 1m, null, Piece),
                new Row(2, "a leg of mutton", 1m, null, Piece),
                new Row(3, "a little butter", 1m, null, Piece),
                new Row(4, "a glass of red wine", 1m, null, Piece)));
            db.AuditLogEntries.AddRange(
                Fill(30, "1:0", "a bay leaf"),
                Fill(30, "2:1", "a leg of mutton"),
                Fill(30, "3:2", "a little butter"),
                Fill(30, "4:3", "a glass of red wine"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var dry = await Quantities(db).RevertAsync();

            dry.Grounded.Should().Be(2);
            dry.GroundedByReason.Should().BeEquivalentTo(new Dictionary<string, int> { [RecipeRepairGrounding.OneCountableItem] = 2 });
            dry.ByReason.Should().BeEquivalentTo(new Dictionary<string, int>
            {
                [RecipeRepairGrounding.VagueAmountRefusal] = 1,
                [RecipeRepairGrounding.UnreadMeasureRefusal] = 1,
            });
            dry.ReasonMeanings.Keys.Should().Contain(RecipeRepairGrounding.OneCountableItem);

            var applied = await Quantities(db).RevertAsync(apply: true, reasons: new[] { RecipeRepairGrounding.VagueAmountRefusal });

            (applied.Changed, applied.NotChosen, applied.Remaining).Should().Be((1, 1, 0));
            applied.Reasons.Should().Equal(RecipeRepairGrounding.VagueAmountRefusal);
            var lines = await db.RecipeIngredients.AsNoTracking().OrderBy(ri => ri.SortOrder).Select(ri => ri.Quantity).ToListAsync();
            lines.Should().Equal(1m, 1m, 0m, 1m);
        }

        [Fact]
        public async Task Applying_the_revert_restores_the_journaled_amount_and_the_batch_undoes()
        {
            using var db = await FillsAsync();
            var since = DateTime.UtcNow.AddSeconds(-1);

            var result = await Quantities(db).RevertAsync(apply: true);

            (result.Changed, result.Skipped, result.Remaining).Should().Be((3, 0, 0));
            var lard = await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == 20 && ri.IngredientId == 1);
            (lard.Quantity, lard.MeasurementId).Should().Be((0m, Gram));
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == 20)).CurationStatusId.Should().Be(Curated);
            var dryRun = await Quantities(db).UndoAsync(since);
            (dryRun.Applied, dryRun.Restored, dryRun.Skipped).Should().Be((false, 3, 0));
            (await db.RecipeIngredients.CountAsync(ri => ri.Quantity == 0)).Should().Be(3, "a dry run writes nothing");
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == AiRepairRevertService.RevertChangeType).OrderBy(a => a.Id).ToListAsync();
            journal.Select(a => (a.EntityId, a.OldValue, a.NewValue)).Should().BeEquivalentTo(new[]
            {
                (20L, "1@0|1|3", "1@0|0|1"), (20L, "2@1|1|3", "2@1|0|1"), (21L, "6@3|1|3", "6@3|0|1"),
            });
            (await Quantities(db).RevertAsync()).Ungrounded.Should().Be(0);

            var undo = await Quantities(db).UndoAsync(since, apply: true);

            (undo.Restored, undo.Skipped).Should().Be((3, 0));
            (await db.RecipeIngredients.CountAsync(ri => ri.Quantity == 0)).Should().Be(0);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == AiRepairRevertService.RevertChangeType)).Should().Be(0);
        }

        [Fact]
        public async Task A_fill_whose_quote_reads_as_a_spoon_or_cup_takes_that_unit_and_the_batch_undoes()
        {
            const long Tablespoon = 11, Teaspoon = 12, Cup = 13;
            using var db = NewContext();
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Tablespoon, Name = "Tablespoon", Symbol = "tbsp", MeasurementCategoryId = 2, BaseUnitConversionFactor = 15m },
                new BaseMeasurementEntity { Id = Teaspoon, Name = "Teaspoon", Symbol = "tsp", MeasurementCategoryId = 2, BaseUnitConversionFactor = 5m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "c", MeasurementCategoryId = 2, BaseUnitConversionFactor = 240m });
            db.Ingredients.AddRange(Food(1, "Flour"), Food(2, "Sugar"), Food(3, "Lard"), Food(4, "Milk"), Food(5, "Red Wine"));
            db.Recipes.Add(Recipe(97615, NonCurated,
                new Row(1, "flour", 1m, null, Piece),
                new Row(2, "sugar", 1m, null, Piece),
                new Row(3, "lard", 1m, null, Piece),
                new Row(4, "milk", 1m, null, Piece),
                new Row(5, "red wine", 1m, null, Piece)));
            db.AuditLogEntries.AddRange(
                Fill(97615, "1:0", "1 heaped tablespoonful"),
                Fill(97615, "2:1", "1 level teaspoonful"),
                Fill(97615, "3:2", "a tbp of lard and butter"),
                Fill(97615, "4:3", "A large teacupful"),
                Fill(97615, "5:4", "a glassful of good red wine"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var since = DateTime.UtcNow.AddSeconds(-1);

            var dry = await Quantities(db).RevertAsync();

            (dry.Ungrounded, dry.Reread).Should().Be((5, 4));
            dry.Samples.Select(s => (s.Quote, s.RestoredQuantity, s.RestoredUnit)).Should().BeEquivalentTo(new[]
            {
                ("1 heaped tablespoonful", 1m, (string?)"Tablespoon"), ("1 level teaspoonful", 1m, "Teaspoon"), ("a tbp of lard and butter", 1m, "Tablespoon"),
                ("A large teacupful", 0.5m, "Cup"), ("a glassful of good red wine", 0m, null),
            });

            (await Quantities(db).RevertAsync(apply: true)).Changed.Should().Be(5);
            var lines = await db.RecipeIngredients.AsNoTracking().OrderBy(ri => ri.SortOrder).Select(ri => new { ri.Quantity, ri.MeasurementId }).ToListAsync();
            lines.Select(l => (l.Quantity, l.MeasurementId)).Should().Equal((1m, Tablespoon), (1m, Teaspoon), (1m, Tablespoon), (0.5m, Cup), (0m, Gram));

            (await Quantities(db).UndoAsync(since, apply: true)).Restored.Should().Be(5);
            (await db.RecipeIngredients.AsNoTracking().Select(ri => ri.MeasurementId).Distinct().ToListAsync()).Should().Equal(Piece);
        }

        [Fact]
        public async Task A_dry_run_of_an_undo_follows_a_line_changed_twice_as_the_undo_does()
        {
            using var db = NewContext();
            db.Ingredients.Add(Food(1, "Lard"));
            db.Recipes.Add(Recipe(60, NonCurated, new Row(1, "lard", 3m, null, Piece)));
            AuditLogEntryEntity Reverted(string was, string became) => new()
            {
                EntityType = AiRepairRevertService.RevertEntityType, EntityId = 60, ChangeType = AiRepairRevertService.RevertChangeType,
                PropertyName = "Quantity", OldValue = was, NewValue = became, Timestamp = DateTime.UtcNow, ChangedByPersonId = SystemConstants.SystemPersonId,
            };
            db.AuditLogEntries.AddRange(Reverted("1@0|1|3", "1@0|2|3"), Reverted("1@0|2|3", "1@0|3|3"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var since = DateTime.UtcNow.AddMinutes(-1);

            var dryRun = await Quantities(db).UndoAsync(since);

            (dryRun.Restored, dryRun.Skipped).Should().Be((2, 0));
            (await db.RecipeIngredients.AsNoTracking().SingleAsync()).Quantity.Should().Be(3m);
            (await db.AuditLogEntries.CountAsync()).Should().Be(2);
            var undo = await Quantities(db).UndoAsync(since, apply: true);
            (undo.Restored, undo.Skipped).Should().Be((2, 0));
            (await db.RecipeIngredients.AsNoTracking().SingleAsync()).Quantity.Should().Be(1m);
        }
    }
}
