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
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Models.Curation;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// An unsized can, jar or package line is weighed at its food's assumed size only while the
    /// container-sizes lane is live and the size is active: the label counts it, the line is
    /// counted-assumed with the size's text, the label says how many lines are assumed, the era of
    /// an old book picks the period size, and a change to a size, the lane or an era makes the
    /// recipe due for the nutrition sweep.
    /// </summary>
    public class ContainerWeighingTests
    {
        private const long Gram = 1, Kcal = 16, Can = 20, Package = 22, Jar = 23;
        private const long Calories = 5035;
        private const long Operator = 1;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private sealed record Rig(ApplicationDbContext Db, long Beans, long Corn, RecipeNutritionService Nutrition);

        private static async Task<Rig> RigAsync()
        {
            var db = NewContext();
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Can, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true },
                new BaseMeasurementEntity { Id = Package, Name = "Package", Symbol = "pkg", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true },
                new BaseMeasurementEntity { Id = Jar, Name = "Jar", Symbol = "jar", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true },
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.Add(new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal });
            var beans = new IngredientEntity { Name = "Black Beans, canned", Description = "Beans, black, mature seeds, canned, low sodium", FdcCategoryId = 16, CurationStatusId = 9003 };
            var corn = new IngredientEntity { Name = "Corn, canned", Description = "Corn, sweet, yellow, canned, whole kernel, drained solids", FdcCategoryId = 11, CurationStatusId = 9003 };
            db.Ingredients.AddRange(beans, corn);
            await db.SaveChangesAsync();
            db.IngredientNutrients.AddRange(
                new IngredientNutrientEntity { IngredientId = beans.Id, NutrientId = Calories, Amount = 100m, MeasurementId = Kcal },
                new IngredientNutrientEntity { IngredientId = corn.Id, NutrientId = Calories, Amount = 50m, MeasurementId = Kcal });
            db.IngredientContainerSizes.AddRange(
                Size(beans.Id, ContainerKinds.Can, 425m, null, "deterministic:fdc-branded-mode:2025-12-18:canned-black-beans:223/304"),
                Size(corn.Id, ContainerKinds.Can, 432m, 298m, "fdc:169214:portion:3"));
            await db.SaveChangesAsync();
            return new Rig(db, beans.Id, corn.Id, new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance));
        }

        private static IngredientContainerSizeEntity Size(long food, string kind, decimal net, decimal? drained, string source, string status = ContainerSizeStatus.Active) => new()
        {
            IngredientId = food, ContainerKind = kind, Era = ContainerEras.Modern, SizeWord = string.Empty, NetGrams = net, DrainedGrams = drained,
            SizeText = "test", SourceRef = source, Class = "test", SourcesAgreeing = 1, Status = status, CreatedDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        private static async Task LaneAsync(ApplicationDbContext db, string mode, DateTime? since = null)
        {
            var row = await db.LaneStates.FirstOrDefaultAsync(s => s.Lane == AiLanes.ContainerSizes && s.Tier == LaneTiers.Whole);
            if (row == null) db.LaneStates.Add(row = new LaneStateEntity { Lane = AiLanes.ContainerSizes, Tier = LaneTiers.Whole });
            row.Mode = mode;
            row.Since = since ?? DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        private static async Task<long> RecipeAsync(ApplicationDbContext db, string? site, string? attribution, params (long Food, decimal Quantity, long Unit, string Line)[] lines)
        {
            var recipe = new RecipeEntity { Name = "Chili", AuthorId = 1, Servings = 1, SourceSite = site, SourceAttribution = attribution };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            var order = 0;
            foreach (var (food, quantity, unit, line) in lines)
                db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = food, Quantity = quantity, MeasurementId = unit, RawLine = line, SortOrder = order++ });
            await db.SaveChangesAsync();
            return recipe.Id;
        }

        private static async Task<decimal?> KcalAsync(ApplicationDbContext db, long recipeId) =>
            (await db.RecipeNutrition.AsNoTracking().FirstOrDefaultAsync(n => n.RecipeId == recipeId && n.NutrientId == Calories))?.Amount;

        private static async Task<RecipeEntity> LoadAsync(ApplicationDbContext db, long recipeId) =>
            await db.Recipes.AsNoTracking()
                .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Measurement)
                .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Nutrition).ThenInclude(n => n.Nutrient)
                .SingleAsync(r => r.Id == recipeId);

        [Fact]
        public async Task An_unsized_can_counts_at_its_active_size_only_while_the_lane_is_live()
        {
            var rig = await RigAsync();
            var id = await RecipeAsync(rig.Db, "example.com", null, (rig.Beans, 2m, Can, "2 cans black beans"));

            (await rig.Nutrition.RecalculateAsync(id)).Should().Be(0, "no lane row is shadow, so the can stays uncounted");
            await LaneAsync(rig.Db, LaneMode.Shadow);
            (await rig.Nutrition.RecalculateAsync(id)).Should().Be(0);

            await LaneAsync(rig.Db, LaneMode.Live);
            (await rig.Nutrition.RecalculateAsync(id)).Should().Be(1);
            (await KcalAsync(rig.Db, id)).Should().Be(850m, "two cans of 425 g at 100 kcal per 100 g");

            var recipe = await LoadAsync(rig.Db, id);
            var weighing = await ContainerWeighing.ForRecipeAsync(rig.Db, recipe);
            var line = recipe.RecipeIngredients!.Single();
            RecipeNutritionService.StatusFor(line, true, weighing).Should().Be(NutritionLineStatus.CountedAssumed);
            RecipeNutritionService.StatusFor(line, true).Should().Be(NutritionLineStatus.NoConversion, "without the weighing the line has no gram weight of its own");
            RecipeNutritionService.WeighedGramsFor(line, weighing).Should().Be(850m);
            var (rows, source) = RecipeNutritionService.BuildLabel(recipe, new Dictionary<long, HashSet<long>> { [rig.Beans] = new() { Calories } }, weighing);
            (source!.CountedLines, source.AssumedLines).Should().Be((1, 1));
            rows.Single().Partial.Should().BeFalse("an assumed size counts like a stated one; the page marks the label estimated");
            ContainerWeighing.Describe(weighing.Weigh(line)!).Should()
                .Be("1 can ≈ 14.99 oz (425 g): the most common size in USDA branded data (2025-12-18, 223 of 304 products); estimated");

            await LaneAsync(rig.Db, LaneMode.Paused);
            (await ContainerWeighing.ForRecipeAsync(rig.Db, recipe)).Live.Should().BeFalse();
        }

        [Fact]
        public async Task A_shadow_or_paused_size_is_never_weighed()
        {
            var rig = await RigAsync();
            await LaneAsync(rig.Db, LaneMode.Live);
            var size = await rig.Db.IngredientContainerSizes.SingleAsync(s => s.IngredientId == rig.Beans);
            size.Status = ContainerSizeStatus.Shadow;
            await rig.Db.SaveChangesAsync();
            var id = await RecipeAsync(rig.Db, "example.com", null, (rig.Beans, 1m, Can, "1 can black beans"));

            (await rig.Nutrition.RecalculateAsync(id)).Should().Be(0);
            var recipe = await LoadAsync(rig.Db, id);
            RecipeNutritionService.StatusFor(recipe.RecipeIngredients!.Single(), true, await ContainerWeighing.ForRecipeAsync(rig.Db, recipe)).Should().Be(NutritionLineStatus.NoConversion);
        }

        [Fact]
        public async Task An_old_book_weighs_a_vegetable_can_at_the_period_size_and_a_web_recipe_at_the_modern_one()
        {
            var rig = await RigAsync();
            await LaneAsync(rig.Db, LaneMode.Live);
            rig.Db.RecipeSourceEras.Add(new RecipeSourceEraEntity { SourceAttribution = "A Plain Cook Book (Example 1901)", PublishedYear = 1931, Era = ContainerEras.Historic });
            await rig.Db.SaveChangesAsync();
            var web = await RecipeAsync(rig.Db, "example.com", null, (rig.Corn, 1m, Can, "1 can corn"));
            var book = await RecipeAsync(rig.Db, null, "A Plain Cook Book (Example 1901)", (rig.Corn, 1m, Can, "1 can corn"));
            var undated = await RecipeAsync(rig.Db, null, "An Undated Book", (rig.Beans, 1m, Can, "1 can black beans"));

            await rig.Nutrition.RecalculateAsync(web);
            await rig.Nutrition.RecalculateAsync(book);
            await rig.Nutrition.RecalculateAsync(undated);

            (await KcalAsync(rig.Db, web)).Should().Be(149m, "the corn is drained solids: 298 g at 50 kcal per 100 g");
            (await KcalAsync(rig.Db, book)).Should().BeApproximately(156.45m, 0.05m, "No. 303 (453.6 g) is nearer 432 g; 453.6 x 298/432 = 312.9 g");
            (await KcalAsync(rig.Db, undated)).Should().Be(425m, "no period row names legumes, so the modern size");

            var recipe = await LoadAsync(rig.Db, book);
            var weighing = await ContainerWeighing.ForRecipeAsync(rig.Db, recipe);
            weighing.Era.Should().Be(ContainerEras.Historic);
            ContainerWeighing.Describe(weighing.Weigh(recipe.RecipeIngredients!.Single())!).Should()
                .Contain("drained ≈ 313 g").And.Contain("No. 303 or No. 2").And.Contain("nearer today's most common size").And.EndWith("estimated");
            var old = await LoadAsync(rig.Db, undated);
            ContainerWeighing.Describe((await ContainerWeighing.ForRecipeAsync(rig.Db, old)).Weigh(old.RecipeIngredients!.Single())!).Should()
                .Contain("today's size, since no size of the book's time is on record");
        }

        [Fact]
        public async Task A_line_without_a_container_word_is_read_by_its_unit()
        {
            var rig = await RigAsync();
            await LaneAsync(rig.Db, LaneMode.Live);
            var id = await RecipeAsync(rig.Db, null, null, (rig.Beans, 1m, Can, ""), (rig.Beans, 1m, Jar, "black beans"));
            var recipe = await LoadAsync(rig.Db, id);
            var weighing = await ContainerWeighing.ForRecipeAsync(rig.Db, recipe);

            weighing.Era.Should().Be(ContainerEras.Modern, "a recipe written in the app is modern");
            var lines = recipe.RecipeIngredients!.OrderBy(l => l.SortOrder).ToList();
            weighing.GramsFor(lines[0]).Should().Be(425m);
            weighing.Weigh(lines[1])!.Reason.Should().Be(ContainerSizeResolver.NoSize, "the food has a can size, not a jar size");
        }

        [Fact]
        public async Task The_recipe_page_shows_an_assumed_line_with_its_grams_its_size_and_the_label_count()
        {
            var rig = await RigAsync();
            await LaneAsync(rig.Db, LaneMode.Live);
            rig.Db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = 6, Name = "Cook" });
            rig.Db.References.Add(new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" });
            await rig.Db.SaveChangesAsync();
            var id = await RecipeAsync(rig.Db, "example.com", null, (rig.Beans, 1m, Can, "1 can black beans"), (rig.Beans, 100m, Gram, "100 g black beans"));
            var recipe = await rig.Db.Recipes.SingleAsync(r => r.Id == id);
            recipe.AuthorId = 6;
            recipe.CurationStatusId = (long)CurationStatusEnum.NonCurated;
            await rig.Db.SaveChangesAsync();
            await rig.Nutrition.RecalculateAsync(id);
            rig.Db.ChangeTracker.Clear();
            var service = new RecipeOrchestrationService(rig.Db, new HttpContextAccessor(), new Moq.Mock<Nom.Orch.Interfaces.ICurrentUserService>().Object,
                new Moq.Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, rig.Nutrition);

            var model = (await service.GetRecipeAsync(id, personId: 6))!;

            var can = model.Ingredients!.Single(i => i.Notes == "1 can black beans");
            (can.Grams, can.NutritionStatus).Should().Be((425m, NutritionLineStatus.CountedAssumed));
            can.AssumedSize.Should().StartWith("1 can ≈ 14.99 oz (425 g)").And.EndWith("estimated");
            var weighed = model.Ingredients!.Single(i => i.Notes == "100 g black beans");
            (weighed.NutritionStatus, weighed.AssumedSize).Should().Be((NutritionLineStatus.Counted, (string?)null));
            (model.NutritionSource!.CountedLines, model.NutritionSource.AssumedLines).Should().Be((2, 1));
            model.Nutrition!.Single(n => n.Id == Calories).Amount.Should().Be(525m);
        }

        [Fact]
        public async Task A_change_to_a_size_the_lane_or_an_era_makes_a_recipe_with_a_container_line_due()
        {
            var rig = await RigAsync();
            var id = await RecipeAsync(rig.Db, null, "Some Book", (rig.Beans, 1m, Can, "1 can black beans"), (rig.Beans, 100m, Gram, "100 g black beans"));
            await rig.Nutrition.RecalculateAsync(id);
            var calculated = (await rig.Db.RecipeNutrition.AsNoTracking().FirstAsync(n => n.RecipeId == id)).DateCalculated!.Value;
            rig.Db.RecipeIngredients.ToList().ForEach(ri => ri.CreatedDate = calculated.AddMinutes(-10));
            var size = await rig.Db.IngredientContainerSizes.SingleAsync(s => s.IngredientId == rig.Beans);
            size.CreatedDate = calculated.AddMinutes(-10);
            await rig.Db.SaveChangesAsync();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(rig.Db, 0).ToListAsync()).Should().BeEmpty();

            size.LastModifiedDate = calculated.AddMinutes(1);
            await rig.Db.SaveChangesAsync();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(rig.Db, 0).ToListAsync()).Should().Equal(id);
            size.LastModifiedDate = calculated.AddMinutes(-1);
            await rig.Db.SaveChangesAsync();

            await LaneAsync(rig.Db, LaneMode.Live, calculated.AddMinutes(1));
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(rig.Db, 0).ToListAsync()).Should().Equal(id);
            await LaneAsync(rig.Db, LaneMode.Live, calculated.AddMinutes(-1));
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(rig.Db, 0).ToListAsync()).Should().BeEmpty();

            rig.Db.RecipeSourceEras.Add(new RecipeSourceEraEntity { SourceAttribution = "Some Book", PublishedYear = 1931, Era = ContainerEras.Historic, CreatedDate = calculated.AddMinutes(1) });
            await rig.Db.SaveChangesAsync();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(rig.Db, 0).ToListAsync()).Should().Equal(id);
        }

        [Fact]
        public async Task The_search_sort_reads_a_container_line_with_a_weighable_size_as_counted()
        {
            var rig = await RigAsync();
            var id = await RecipeAsync(rig.Db, "example.com", null, (rig.Beans, 1m, Can, "1 can black beans"), (rig.Corn, 1m, Jar, "1 jar corn"));
            var lines = await rig.Db.RecipeIngredients.Include(ri => ri.Measurement).Include(ri => ri.Ingredient).ThenInclude(i => i.IngredientNutrients)
                .Where(ri => ri.RecipeId == id).OrderBy(ri => ri.SortOrder).ToListAsync();

            var off = RecipeNutritionService.IsUncountedLineWith(ContainerWeighing.WeighableSizes(rig.Db)).Compile();
            lines.Select(off).Should().Equal(true, true);
            await LaneAsync(rig.Db, LaneMode.Live);
            var live = RecipeNutritionService.IsUncountedLineWith(ContainerWeighing.WeighableSizes(rig.Db)).Compile();
            lines.Select(live).Should().Equal(false, true);
            lines.Select(RecipeNutritionService.IsUncountedLine.Compile()).Should().Equal(true, true);
        }

        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public string? Sql { get; set; }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Sql = command.CommandText;
                throw new OperationCanceledException("captured");
            }
        }

        private sealed class NoConnection : DbConnectionInterceptor
        {
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private static ApplicationDbContext Postgres(CaptureAndStop capture) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        [Fact]
        public async Task The_sweep_the_search_sort_and_the_lane_check_translate_to_postgres()
        {
            var capture = new CaptureAndStop();
            await using var db = Postgres(capture);

            var due = RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToQueryString();
            due.Should().Contain("\"IngredientContainerSize\"").And.Contain("\"LaneState\"").And.Contain("\"RecipeSourceEra\"");
            var sort = RecipeSearchOrchestrationService.ApplySorting(db.Recipes, new RecipeSearchModel { SortBy = "calories" }, ContainerWeighing.WeighableSizes(db)).ToQueryString();
            sort.Should().Contain("\"IngredientContainerSize\"").And.Contain("\"ContainerKind\"");
            var act = () => ContainerWeighing.LiveAsync(db);
            await act.Should().ThrowAsync<OperationCanceledException>();
            capture.Sql.Should().Contain("\"LaneState\"");
        }
    }
}
