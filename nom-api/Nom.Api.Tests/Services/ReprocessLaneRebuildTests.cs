using System;
using System.Collections.Generic;
using System.Data.Common;
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
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Import.Golden;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// What the reprocess lane needs of the rebuild and of the lanes around it: a newer extraction
    /// of a book replaces the steps as well as the lines, journaled and undone; the line-classify lane
    /// labels a rebuilt recipe's rows and the rebuild's undo still takes it back; the start-up repair
    /// leaves a rebuilt recipe alone; the lane's queries translate to Postgres.
    /// </summary>
    public class ReprocessLaneRebuildTests
    {
        private const long Piece = 1, Cup = 2, Tablespoon = 3;
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Operator : ICurrentUserService
        {
            public long? PersonId => 2;
            public long PersonIdOrSystem => 2;
            public long RequiredPersonId => 2;
            public string? UserId => "operator";
            public string RequiredUserId => "operator";
        }

        private static async Task<ApplicationDbContext> SeededAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3 },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = Tablespoon, Name = "Tablespoon", Symbol = "tbsp", MeasurementCategoryId = 2 });
            await db.SaveChangesAsync();
            return db;
        }

        private static RecipeReprocessService Reprocess(ApplicationDbContext db) =>
            new(db, new Operator(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(), new Mock<IRecipeNutritionService>().Object,
                NullLogger<RecipeReprocessService>.Instance);

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private static async Task<RecipeEntity> BookRecipeAsync(ApplicationDbContext db, string name, (IngredientEntity Food, string Raw, decimal Quantity, long Unit)[] rows,
            string[] steps, (string[] Lines, string[] Steps)[] versions, CurationStatusEnum status = CurationStatusEnum.NonCurated)
        {
            var recipe = new RecipeEntity
            {
                Name = name, CurationStatusId = (long)status, ScrapedAtUtc = Imported, LicenseStatus = RecipeLicenseStatus.PublicDomain, SourceAttribution = "A book",
                RecipeIngredients = rows.Select((r, i) => new RecipeIngredientEntity
                {
                    IngredientId = r.Food.Id, SortOrder = i, Quantity = r.Quantity, MeasurementId = r.Unit, RawLine = r.Raw, CreatedDate = Imported,
                }).ToList(),
                RecipeSteps = steps.Select((s, i) => new RecipeStepEntity { StepNumber = i + 1, Summary = s, Description = s }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            for (var v = 0; v < versions.Length; v++)
            {
                var record = new ScraperRecipe
                {
                    Name = name,
                    Ingredients = versions[v].Lines.Select(l => new ScraperIngredient { RawLine = l }).ToList(),
                    Steps = versions[v].Steps.Select((s, i) => new ScraperStep { Order = i + 1, Instruction = s }).ToList(),
                };
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity
                {
                    RecipeId = recipe.Id, RawJsonLd = SourceRecord.Content(record), Format = ScrapedDocumentFormat.StagedCookbook, Version = v + 1, FetchedAtUtc = Imported,
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipe;
        }

        private static Task<List<RecipeStepEntity>> StepsAsync(ApplicationDbContext db, long recipeId) =>
            db.RecipeSteps.AsNoTracking().Where(s => s.RecipeId == recipeId).OrderBy(s => s.StepNumber).ToListAsync();

        private static RecipeReprocessRequestModel For(long id) => new() { RecipeIds = new List<long> { id } };

        [Fact]
        public async Task A_newer_extraction_of_a_book_replaces_the_steps_too_and_the_undo_puts_them_back()
        {
            using var db = await SeededAsync();
            var butter = await FoodAsync(db, "butter");
            var flour = await FoodAsync(db, "flour");
            var old = new[] { "Burro e farina, mescolare.", "Cuocere." };
            var english = new[] { "Rub the butter into the flour.", "Bake for twenty minutes.", "Serve warm." };
            var recipe = await BookRecipeAsync(db, "Pasta frolla",
                new[] { (butter, "Burro - 100g", 30m, Piece), (flour, "Farina - 200g", 200m, Piece) }, old,
                new[] { (new[] { "Burro - 100g", "Farina - 200g" }, old), (new[] { "100 grams of butter", "200 grams of flour" }, english) });

            var preview = await Reprocess(db).RebuildAsync(For(recipe.Id));
            preview.Results.Single().StepsReplaced.Should().BeTrue();
            (await StepsAsync(db, recipe.Id)).Select(s => s.Description).Should().Equal(old);

            var applied = await Reprocess(db).RebuildAsync(For(recipe.Id), apply: true);

            applied.Rebuilt.Should().Be(1);
            var steps = await StepsAsync(db, recipe.Id);
            steps.Select(s => s.Description).Should().Equal(english);
            steps.Should().OnlyContain(s => s.LastModifiedByPersonId == SystemConstants.SystemPersonId);
            var journal = await db.RecipeReprocessJournals.SingleAsync();
            journal.OldSteps.Should().Contain("Cuocere.");
            journal.NewSteps.Should().Contain("Serve warm.");

            (await Reprocess(db).RebuildAsync(For(recipe.Id))).Unchanged.Should().Be(1, "the rebuilt steps equal the source");
            var undo = await Reprocess(db).UndoAsync(null, applied.BatchId, apply: true);

            undo.Restored.Should().Be(1);
            (await StepsAsync(db, recipe.Id)).Select(s => s.Description).Should().Equal(old);
            (await db.RecipeSteps.IgnoreQueryFilters().CountAsync(s => s.RecipeId == recipe.Id)).Should().Be(2, "the step the rebuild added is gone again");
        }

        [Fact]
        public async Task The_first_extraction_never_replaces_steps_and_a_step_a_person_edited_holds_the_recipe()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var steps = new[] { "Sift the flour.", "Bake." };
            var first = await BookRecipeAsync(db, "One", new[] { (flour, "2 cups flour", 2m, Cup) }, steps,
                new[] { (new[] { "2 cups flour" }, new[] { "Sift the flour twice.", "Bake." }) });
            var edited = await BookRecipeAsync(db, "Two", new[] { (flour, "2 cups flour", 2m, Cup) }, steps,
                new[] { (new[] { "2 cups flour" }, steps), (new[] { "2 cups flour" }, new[] { "Sift the flour twice.", "Bake." }) });
            var step = await db.RecipeSteps.FirstAsync(s => s.RecipeId == edited.Id);
            step.LastModifiedByPersonId = 2;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await Reprocess(db).RebuildAsync(For(first.Id))).Results.Single().Should().Match<RecipeReprocessRecipeResult>(r => r.Outcome == RecipeReprocessOutcome.Unchanged && !r.StepsReplaced);
            (await Reprocess(db).RebuildAsync(For(edited.Id))).Results.Single().Reason.Should().Be(RecipeReprocessHold.PersonStepEdit);
        }

        [Fact]
        public async Task The_undo_still_restores_a_rebuilt_recipe_whose_unlabelled_rows_the_line_classify_lane_labelled_since()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var sauce = await FoodAsync(db, "white sauce");
            var recipe = await BookRecipeAsync(db, "Fish", new[] { (water, "2 tablespns. water", 2m, Piece), (sauce, "white sauce", 0m, Piece) },
                new[] { "Mix.", "Bake." }, new[] { (new[] { "2 tablespns. water", "white sauce" }, new[] { "Mix.", "Bake." }) });
            var applied = await Reprocess(db).RebuildAsync(For(recipe.Id), apply: true);
            applied.Rebuilt.Should().Be(1);

            var model = new Mock<IRecipeClassificationModel>();
            model.SetupGet(m => m.IsConfigured).Returns(true);
            model.SetupGet(m => m.ModelName).Returns("qwen2.5:14b-instruct");
            model.Setup(m => m.ClassifyLinesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, string> { [0] = RecipeIngredientLineKind.Ingredient });
            var classify = new RecipeLineClassificationService(db, new RecipeVettingService(), model.Object, NullLogger<RecipeLineClassificationService>.Instance);
            var batch = await classify.ClassifyBatchAsync(0, 10);

            batch.Should().Match<LineClassifyBatchResult>(b => b.ModelClassified == 1, "a full rebuild no longer holds the recipe from the line-classify lane");
            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe.Id && ri.IngredientId == sauce.Id)).LineKind.Should().Be(RecipeIngredientLineKind.Ingredient);
            RecipeReprocessService.StandsAsWritten(
                    new[] { Row(sauce.Id, 1, "white sauce", 0, "reference") }, new[] { Row(sauce.Id, 1, "white sauce", 0, null) })
                .Should().BeTrue();
            RecipeReprocessService.StandsAsWritten(
                    new[] { Row(sauce.Id, 1, "white sauce", 0, "note") }, new[] { Row(sauce.Id, 1, "white sauce", 0, "reference") })
                .Should().BeFalse("a kind the write set is not the classify lane's to change");

            var undo = await Reprocess(db).UndoAsync(null, applied.BatchId, apply: true);
            undo.Restored.Should().Be(1);
            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == recipe.Id && ri.IngredientId == water.Id)).MeasurementId.Should().Be(Piece);
        }

        private static RecipeReprocessService.LineRow Row(long food, int sortOrder, string raw, decimal quantity, string? kind) =>
            new(food, sortOrder, quantity, Piece, raw, kind, null, Imported, null, null, null, false, null, null);

        [Fact]
        public async Task The_start_up_repair_leaves_a_rebuilt_recipe_alone()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            var recipe = await BookRecipeAsync(db, "Fish &amp; Chips", new[] { (water, "2 tablespns. water", 2m, Piece), (flour, "1 cup flour", 1m, Cup) },
                new[] { "Mix.", "Bake." }, new[] { (new[] { "2 tablespns. water", "1 cup flour" }, new[] { "Mix.", "Bake." }) });
            await Reprocess(db).RebuildAsync(For(recipe.Id), apply: true);
            db.ChangeTracker.Clear();

            var result = await new ScrapedRecipeRepairService(db, new RecipeVettingService()).RepairBatchAsync(0, 10);

            result.RecipesRebuilt.Should().Be(1);
            result.RecipesHeld.Should().Be(0);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipe.Id)).Name.Should().Be("Fish &amp; Chips", "the repair does not touch a rebuilt recipe at all");
        }

        [Fact]
        public void Every_judged_line_gives_the_line_judge_a_better_and_a_worse_case()
        {
            var cases = ModelLanes.JudgeCases(Golden.GoldenSet.Folder).ToList();

            cases.Should().NotBeEmpty();
            cases.Select(c => c.Id).Should().OnlyHaveUniqueItems();
            cases.Count(c => c.Expected == LineJudgement.Better).Should().Be(cases.Count(c => c.Expected == LineJudgement.Worse));
            cases.Where(c => c.Expected == LineJudgement.Worse).Should().Contain(c => c.RuleFlags, "the rule audit confirms some known-worse rows");
            cases.Where(c => c.Expected == LineJudgement.Better).Count(c => c.RuleFlags).Should().BeLessThan(cases.Count / 10, "the rule audit seldom fails a right row");
            var score = ModelLanes.Score(ModelLanes.LineJudge, "m", "h", "s", cases.Select(c => c.Outcome(c.Expected)).ToList());
            score.Score.Should().Be(1);
            score.AuditKnownBad.Should().Be(cases.Count / 2);
            score.AuditFlaggedBad.Should().Be(cases.Count(c => c.Expected == LineJudgement.Worse && c.RuleFlags));
            score.AuditFlaggedGood.Should().Be(0);
        }

        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public List<string> Sql { get; } = new();

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Sql.Add(command.CommandText);
                throw new OperationCanceledException("captured");
            }
        }

        private sealed class NoConnection : DbConnectionInterceptor
        {
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(InterceptionResult.Suppress());
        }

        [Fact]
        public async Task The_detector_and_lane_queries_translate_to_postgres()
        {
            var capture = new CaptureAndStop();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

            await FluentActions.Awaiting(() => ReprocessDetectorService.UnreadQuery(db, 0, RecipeLineBuilder.Version).Take(10).ToListAsync()).Should().ThrowAsync<OperationCanceledException>();
            await FluentActions.Awaiting(() => ReprocessLaneService.PendingQuery(db, ReprocessTier.Rebuild, live: true).Take(10).ToListAsync()).Should().ThrowAsync<OperationCanceledException>();
            var lane = new ReprocessLaneService(db, new Mock<IRecipeReprocessService>().Object, new Mock<ILaneFrameService>().Object,
                new Mock<IReprocessLineJudge>().Object, NullLogger<ReprocessLaneService>.Instance);
            await FluentActions.Awaiting(() => lane.StatusAsync(true)).Should().ThrowAsync<OperationCanceledException>();
            await FluentActions.Awaiting(() => RecipeReprocessService.RebuiltRecipesAsync(db, new long[] { 1, 2 })).Should().ThrowAsync<OperationCanceledException>();

            capture.Sql.Should().HaveCount(4);
            capture.Sql[0].Should().Contain("max(").And.Contain("ReprocessCandidate");
            capture.Sql[1].Should().Contain("& ").And.Contain("ORDER BY");
            capture.Sql[2].Should().Contain("GROUP BY");
        }
    }
}
