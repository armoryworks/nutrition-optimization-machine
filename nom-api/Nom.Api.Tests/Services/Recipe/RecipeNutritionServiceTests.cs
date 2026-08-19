using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
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
        private const long Gram = 1, Cup = 11, Piece = 3, Kcal = 16;
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
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal },
                new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = Gram });
            await db.SaveChangesAsync();
        }

        private static async Task<long> SeedRecipeAsync(ApplicationDbContext db, long? servings, decimal qty, long measurementId, decimal? referenceGrams = null)
        {
            var chicken = new IngredientEntity { Name = "Chicken", CurationStatusId = 9003, ReferenceServingGrams = referenceGrams };
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
        public async Task Count_ingredient_needs_a_reference_serving_and_volume_assumes_1g_per_ml()
        {
            using var db = NewContext();
            await SeedReferenceAsync(db);
            var noRef = await SeedRecipeAsync(db, servings: 1, qty: 2m, measurementId: Piece);
            var svc = new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance);
            (await svc.RecalculateAsync(noRef)).Should().Be(0, "a count with no ReferenceServingGrams cannot be converted");

            var withRef = await SeedRecipeAsync(db, servings: 1, qty: 2m, measurementId: Piece, referenceGrams: 150m);
            (await svc.RecalculateAsync(withRef)).Should().Be(2);
            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == withRef && n.NutrientId == Calories)).Amount.Should().Be(495m); // 165 × 3

            var cup = await SeedRecipeAsync(db, servings: 1, qty: 1m, measurementId: Cup);
            (await svc.RecalculateAsync(cup)).Should().Be(2);
            (await db.RecipeNutrition.SingleAsync(n => n.RecipeId == cup && n.NutrientId == Protein)).Amount
                .Should().BeApproximately(73.3423m, 0.001m); // 31 × 236.588 ÷ 100
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
    }
}
