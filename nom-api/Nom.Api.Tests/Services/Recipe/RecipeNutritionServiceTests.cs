using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// Recipe nutrition is derived from ingredient per-100 g facts × grams used ÷ servings,
    /// and hand-authored (DateCalculated NULL) labels are never overwritten.
    /// </summary>
    public class RecipeNutritionServiceTests
    {
        private const long Gram = 1, Cup = 11, Piece = 3, Kcal = 16, Ounce = 7, Can = 20;
        private const long Calories = 5035, Protein = 5006;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static async Task SeedReferenceAsync(ApplicationDbContext db)
        {
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 236.588m },
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Ounce, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1, BaseUnitConversionFactor = 28.3495m },
                new BaseMeasurementEntity { Id = Can, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true },
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal },
                new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = Gram });
            await db.SaveChangesAsync();
        }

        private static async Task<long> SeedRecipeAsync(ApplicationDbContext db, long? servings, decimal qty, long measurementId, decimal? referenceGrams = null, decimal? gramsPerMl = null)
        {
            var chicken = new IngredientEntity { Name = "Chicken", CurationStatusId = 9003, ReferenceServingGrams = referenceGrams, GramsPerMilliliter = gramsPerMl };
            db.Ingredients.Add(chicken);
            await db.SaveChangesAsync();
            db.IngredientNutrients.AddRange(
                new IngredientNutrientEntity { IngredientId = chicken.Id, NutrientId = Calories, Amount = 165m, MeasurementId = Kcal },
                new IngredientNutrientEntity { IngredientId = chicken.Id, NutrientId = Protein, Amount = 31m, MeasurementId = Gram });
            var recipe = new RecipeEntity { Name = "Grilled chicken", AuthorId = 1, Servings = servings };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = chicken.Id, Quantity = qty, MeasurementId = measurementId, RawLine = "" });
            await db.SaveChangesAsync();
            return recipe.Id;
        }

        [Fact]
        public async Task Mass_ingredient_scales_per_100g_and_divides_by_servings()
        {
            using var db = NewContext();
            await SeedReferenceAsync(db);
            var recipeId = await SeedRecipeAsync(db, servings: 2, qty: 400m, measurementId: Gram);

            var written = await new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance).RecalculateAsync(recipeId);

            written.Should().Be(2);
            var rows = await db.RecipeNutrition.Where(n => n.RecipeId == recipeId).ToListAsync();
            rows.Single(n => n.NutrientId == Calories).Amount.Should().Be(330m); // 165 × 4 ÷ 2
            rows.Single(n => n.NutrientId == Calories).Unit.Should().Be("kcal");
            rows.Single(n => n.NutrientId == Protein).Amount.Should().Be(62m);
            rows.Should().OnlyContain(n => n.DateCalculated != null);
        }

        [Fact]
        public async Task Count_needs_a_reference_serving_and_volume_needs_a_density()
        {
            using var db = NewContext();
            await SeedReferenceAsync(db);
            var noRef = await SeedRecipeAsync(db, servings: 1, qty: 2m, measurementId: Piece);
            var svc = new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance);
            (await svc.RecalculateAsync(noRef)).Should().Be(0, "a count with no ReferenceServingGrams cannot be converted");

            var withRef = await SeedRecipeAsync(db, servings: 1, qty: 2m, measurementId: Piece, referenceGrams: 150m);
            (await svc.RecalculateAsync(withRef)).Should().Be(2);
            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == withRef && n.NutrientId == Calories)).Amount.Should().Be(495m); // 165 × 3

            var cupNoDensity = await SeedRecipeAsync(db, servings: 1, qty: 1m, measurementId: Cup);
            (await svc.RecalculateAsync(cupNoDensity)).Should().Be(0, "a volume with no GramsPerMilliliter has no mass");

            var cup = await SeedRecipeAsync(db, servings: 1, qty: 1m, measurementId: Cup, gramsPerMl: 0.5m);
            (await svc.RecalculateAsync(cup)).Should().Be(2);
            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == cup && n.NutrientId == Protein)).Amount
                .Should().BeApproximately(36.6711m, 0.001m);
        }

        [Theory]
        [InlineData(3.0, 147.0, 6.0)]
        [InlineData(null, 147.0, 294.0)]
        [InlineData(3.0, null, 6.0)]
        [InlineData(0.0, 147.0, 294.0)]
        public void A_count_weighs_one_item_before_falling_back_to_the_reference_serving(double? gramsPerItem, double? referenceGrams, double expected)
        {
            var piece = new BaseMeasurementEntity { Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m };
            var garlic = new IngredientEntity { Name = "Garlic", GramsPerItem = (decimal?)gramsPerItem, ReferenceServingGrams = (decimal?)referenceGrams };

            RecipeNutritionService.GramsFor(2m, piece, garlic).Should().Be((decimal)expected);
        }

        [Theory]
        [InlineData("Can")]
        [InlineData("Package")]
        public void A_can_or_package_has_no_weight_of_its_own(string unit)
        {
            var container = new BaseMeasurementEntity { Name = unit, Symbol = unit, MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true };
            var tomatoes = new IngredientEntity { Name = "Tomatoes", GramsPerItem = 123m, ReferenceServingGrams = 149m };
            var line = new RecipeIngredientEntity { Quantity = 1m, Measurement = container, Ingredient = tomatoes };

            RecipeNutritionService.GramsFor(1m, container, tomatoes).Should().BeNull("one can of tomatoes is not one tomato and not one serving");
            RecipeNutritionService.StatusFor(line, ingredientHasNutrients: true).Should().Be(NutritionLineStatus.NoConversion);
        }

        [Fact]
        public async Task A_can_counts_by_its_stated_size_and_not_at_all_without_one()
        {
            using var db = NewContext();
            await SeedReferenceAsync(db);
            var svc = new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance);

            var sized = await SeedRecipeAsync(db, servings: 1, qty: 15m, measurementId: Ounce, referenceGrams: 150m);
            (await svc.RecalculateAsync(sized)).Should().Be(2);
            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == sized && n.NutrientId == Calories)).Amount
                .Should().Be(Math.Round(165m * 425.2425m / 100m, 4));

            var unsized = await SeedRecipeAsync(db, servings: 1, qty: 1m, measurementId: Can, referenceGrams: 150m);
            (await svc.RecalculateAsync(unsized)).Should().Be(0, "a can with no stated size is not weighed as one serving");
            (await db.RecipeNutrition.AnyAsync(n => n.RecipeId == unsized)).Should().BeFalse();
        }

        [Fact]
        public async Task Both_lines_of_a_food_count_once_a_joined_row_is_unfolded()
        {
            using var db = NewContext();
            await SeedReferenceAsync(db);
            var sugar = new IngredientEntity { Name = "Sugar", CurationStatusId = 9003, GramsPerMilliliter = 0.85m };
            db.Ingredients.Add(sugar);
            await db.SaveChangesAsync();
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = sugar.Id, NutrientId = Calories, Amount = 387m, MeasurementId = Kcal });
            var recipe = new RecipeEntity { Name = "Sugar cookies", AuthorId = 1, Servings = 1, ScrapedAtUtc = DateTime.UtcNow };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity
            {
                RecipeId = recipe.Id, IngredientId = sugar.Id, Quantity = 1.75m, MeasurementId = Cup,
                RawLine = "1 3/4 cup granulated sugar + 1/3 cup granulated sugar, (for rolling cookies)",
            });
            await db.SaveChangesAsync();
            var nutrition = new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance);
            await nutrition.RecalculateAsync(recipe.Id);
            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == recipe.Id)).Amount
                .Should().BeApproximately(3.87m * 1.75m * 236.588m * 0.85m, 0.01m);

            var currentUser = Mock.Of<ICurrentUserService>(u => u.PersonIdOrSystem == SystemConstants.SystemPersonId);
            (await new LineUnfoldService(db, currentUser).UnfoldAsync(apply: true)).LinesAdded.Should().Be(1);
            await nutrition.RecalculateAsync(recipe.Id);

            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == recipe.Id)).Amount
                .Should().BeApproximately(3.87m * (1.75m + 0.333m) * 236.588m * 0.85m, 0.01m);
        }

        [Fact]
        public async Task Hand_authored_label_is_not_overwritten()
        {
            using var db = NewContext();
            await SeedReferenceAsync(db);
            var recipeId = await SeedRecipeAsync(db, servings: 1, qty: 100m, measurementId: Gram);
            db.RecipeNutrition.Add(new RecipeNutritionEntity { RecipeId = recipeId, NutrientId = Calories, Amount = 999m, Unit = "kcal", DateCalculated = null });
            await db.SaveChangesAsync();

            var written = await new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance).RecalculateAsync(recipeId);

            written.Should().Be(0);
            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == recipeId)).Amount.Should().Be(999m);
        }

        [Theory]
        [InlineData("ingredient", 2, 2L, true, 0.6, "counted")]
        [InlineData(null, 2, 2L, true, null, "no-conversion")]
        [InlineData(null, 2, 2L, false, 0.6, "no-data")]
        [InlineData(null, 0, 2L, false, null, "not-measured")]
        [InlineData("note", 2, 2L, false, null, "not-an-ingredient")]
        [InlineData(null, 100, 1L, true, null, "counted")]
        public void Each_line_reports_whether_it_counts(string? lineKind, double qty, long category, bool hasData, double? gramsPerMl, string expected)
        {
            var line = new RecipeIngredientEntity
            {
                LineKind = lineKind,
                Quantity = (decimal)qty,
                Measurement = new BaseMeasurementEntity { Name = "u", Symbol = "u", MeasurementCategoryId = category, BaseUnitConversionFactor = 1m },
                Ingredient = new IngredientEntity { Name = "Black Beans", GramsPerMilliliter = (decimal?)gramsPerMl }
            };

            RecipeNutritionService.StatusFor(line, hasData).Should().Be(expected);
        }
    }
}
