using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The recipe label shows FDA %Daily Values only for totals it can vouch for, marks a nutrient
    /// some counted ingredients lack, says where its numbers come from, and is recalculated when an
    /// ingredient gains nutrient values.
    /// </summary>
    public class RecipeNutritionLabelTests
    {
        private const long Sodium = 5004, Fat = 5000, Protein = 5006;

        [Theory]
        [InlineData("Sodium", 1150, "mg", 50)]
        [InlineData("Sodium", 1.15, "g", 50)]
        [InlineData("Fat", 7.8, "g", 10)]
        [InlineData("Total Carbohydrates", 27.5, "g", 10)]
        [InlineData("Vitamin D", 2, "µg", 10)]
        [InlineData("Vitamin D", 2, "mcg", 10)]
        [InlineData("Potassium", 470, "mg", 10)]
        [InlineData("Calcium", 130, "mg", 10)]
        [InlineData("Iron", 1.8, "mg", 10)]
        [InlineData("Dietary Fiber", 7, "g", 25)]
        [InlineData("Added Sugars", 5, "g", 10)]
        [InlineData("Saturated Fat", 2, "g", 10)]
        [InlineData("Cholesterol", 30, "mg", 10)]
        public void Daily_values_follow_21_CFR_101_9(string name, double amount, string unit, int expected)
        {
            FdaDailyValues.PercentOf(name, (decimal)amount, unit).Should().Be(expected);
        }

        [Theory]
        [InlineData("Trans Fat", "g")]
        [InlineData("Total Sugars", "g")]
        [InlineData("Protein", "g")]
        [InlineData("Sodium", "kcal")]
        [InlineData("Sodium", "")]
        public void Nutrients_without_a_daily_value_or_a_known_unit_get_none(string name, string unit)
        {
            FdaDailyValues.PercentOf(name, 10m, unit).Should().BeNull();
        }

        private static RecipeIngredientEntity Line(long ingredientId, string? fdcId, decimal grams = 100m) => new()
        {
            IngredientId = ingredientId,
            Quantity = grams,
            Measurement = new BaseMeasurementEntity { Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m },
            Ingredient = new IngredientEntity { Id = ingredientId, Name = "Food " + ingredientId, FdcId = fdcId },
        };

        private static RecipeNutritionEntity Row(long nutrientId, string name, decimal amount, string unit, bool derived = true) => new()
        {
            NutrientId = nutrientId,
            Nutrient = new NutrientEntity { Id = nutrientId, Name = name },
            Amount = amount,
            Unit = unit,
            DateCalculated = derived ? DateTime.UtcNow : null,
        };

        [Fact]
        public void A_nutrient_some_counted_foods_lack_is_partial_and_gets_no_daily_value()
        {
            var recipe = new RecipeEntity
            {
                RecipeIngredients = new List<RecipeIngredientEntity> { Line(1, "171287"), Line(2, "2345") },
                Nutrition = new List<RecipeNutritionEntity> { Row(Fat, "Fat", 15.6m, "g"), Row(Sodium, "Sodium", 460m, "mg") },
            };
            var has = new Dictionary<long, HashSet<long>> { [1] = new() { Fat, Sodium }, [2] = new() { Fat } };

            var (rows, source) = RecipeNutritionService.BuildLabel(recipe, has);

            rows.Single(r => r.Id == Fat).Should().Match<Nom.Orch.Models.Recipe.RecipeNutritionSearchModel>(r => !r.Partial && r.DailyValuePercent == 20m);
            rows.Single(r => r.Id == Sodium).Should().Match<Nom.Orch.Models.Recipe.RecipeNutritionSearchModel>(r => r.Partial && r.DailyValuePercent == null);
            source!.Kind.Should().Be("usda");
            source.UsdaLines.Should().Be(2);
        }

        [Fact]
        public void An_uncounted_ingredient_withholds_every_daily_value()
        {
            var recipe = new RecipeEntity
            {
                RecipeIngredients = new List<RecipeIngredientEntity> { Line(1, "171287"), Line(2, null) },
                Nutrition = new List<RecipeNutritionEntity> { Row(Fat, "Fat", 15.6m, "g") },
            };
            var has = new Dictionary<long, HashSet<long>> { [1] = new() { Fat } };

            var (rows, source) = RecipeNutritionService.BuildLabel(recipe, has);

            rows.Single().DailyValuePercent.Should().BeNull("the totals are known to be low");
            rows.Single().Partial.Should().BeFalse();
            source!.Kind.Should().Be("usda", "only counted lines decide the source");
        }

        [Fact]
        public void Source_says_mixed_catalog_or_recipe()
        {
            var has = new Dictionary<long, HashSet<long>> { [1] = new() { Protein }, [2] = new() { Protein } };
            RecipeEntity Recipe(bool derived, params RecipeIngredientEntity[] lines) => new()
            {
                RecipeIngredients = lines.ToList(),
                Nutrition = new List<RecipeNutritionEntity> { Row(Protein, "Protein", 20m, "g", derived) },
            };

            var mixed = RecipeNutritionService.BuildLabel(Recipe(true, Line(1, "171287"), Line(2, null)), has).Source!;
            mixed.Should().Match<Nom.Orch.Models.Recipe.RecipeNutritionSourceModel>(s => s.Kind == "mixed" && s.UsdaLines == 1 && s.CountedLines == 2);
            RecipeNutritionService.BuildLabel(Recipe(true, Line(1, null)), has).Source!.Kind.Should().Be("catalog");

            var (authoredRows, authored) = RecipeNutritionService.BuildLabel(Recipe(false, Line(1, null), Line(3, null)), has);
            authored!.Kind.Should().Be("recipe");
            authoredRows.Single().Partial.Should().BeFalse("a hand-authored label is taken as entered");
        }

        [Fact]
        public void No_label_has_no_rows_and_no_source()
        {
            var (rows, source) = RecipeNutritionService.BuildLabel(new RecipeEntity(), new Dictionary<long, HashSet<long>>());
            rows.Should().BeEmpty();
            source.Should().BeNull();
        }

        [Fact]
        public async Task A_derived_label_is_due_again_when_a_line_food_gains_a_nutrient()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var calculated = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var egg = new IngredientEntity { Name = "Egg", CurationStatusId = 9003 };
            db.Ingredients.Add(egg);
            var recipe = new RecipeEntity { Name = "Omelette", AuthorId = 1 };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = egg.Id, Quantity = 100m, MeasurementId = 1, RawLine = "", CreatedDate = calculated.AddDays(-1) });
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = egg.Id, NutrientId = Fat, Amount = 9.5m, MeasurementId = 1, CreatedDate = calculated.AddDays(-1) });
            db.RecipeNutrition.Add(new RecipeNutritionEntity { RecipeId = recipe.Id, NutrientId = Fat, Amount = 9.5m, Unit = "g", DateCalculated = calculated });
            await db.SaveChangesAsync();

            (await RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToListAsync()).Should().BeEmpty();

            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = egg.Id, NutrientId = Sodium, Amount = 142m, MeasurementId = 8, CreatedDate = calculated.AddHours(1) });
            await db.SaveChangesAsync();

            (await RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToListAsync()).Should().Equal(recipe.Id);
        }
    }
}
