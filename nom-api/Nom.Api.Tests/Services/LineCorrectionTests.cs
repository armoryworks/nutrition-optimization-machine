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

            var undo = await Labels(db).UndoAsync(since);

            (undo.Restored, undo.Skipped).Should().Be((4, 1), "a line labelled again since is left as it is");
            var kinds = await db.RecipeIngredients.AsNoTracking().OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).Select(ri => ri.LineKind).ToListAsync();
            kinds.Should().Equal(
                RecipeIngredientLineKind.Instruction, RecipeIngredientLineKind.Reference,
                RecipeIngredientLineKind.Reference, RecipeIngredientLineKind.Note, RecipeIngredientLineKind.Equipment,
                null, RecipeIngredientLineKind.Reference);
            (await db.AuditLogEntries.CountAsync()).Should().Be(1);
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
        public async Task Applying_the_revert_restores_the_journaled_amount_and_the_batch_undoes()
        {
            using var db = await FillsAsync();
            var since = DateTime.UtcNow.AddSeconds(-1);

            var result = await Quantities(db).RevertAsync(apply: true);

            (result.Changed, result.Skipped, result.Remaining).Should().Be((3, 0, 0));
            var lard = await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == 20 && ri.IngredientId == 1);
            (lard.Quantity, lard.MeasurementId).Should().Be((0m, Gram));
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == 20)).CurationStatusId.Should().Be(Curated);
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.ChangeType == AiRepairRevertService.RevertChangeType).OrderBy(a => a.Id).ToListAsync();
            journal.Select(a => (a.EntityId, a.OldValue, a.NewValue)).Should().BeEquivalentTo(new[]
            {
                (20L, "1@0|1|3", "1@0|0|1"), (20L, "2@1|1|3", "2@1|0|1"), (21L, "6@3|1|3", "6@3|0|1"),
            });
            (await Quantities(db).RevertAsync()).Ungrounded.Should().Be(0);

            var undo = await Quantities(db).UndoAsync(since);

            (undo.Restored, undo.Skipped).Should().Be((3, 0));
            (await db.RecipeIngredients.CountAsync(ri => ri.Quantity == 0)).Should().Be(0);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == AiRepairRevertService.RevertChangeType)).Should().Be(0);
        }
    }
}
