using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Sweet-or-snack recipes are re-filed by added-sugar share once their ingredients carry USDA
    /// data; recipes without enough data keep what they had.
    /// </summary>
    public class CourseClassificationTests
    {
        private const long Kcal = 5035, Sugars = 5036;

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Nutrients.AddRange(new NutrientEntity { Id = Kcal, Name = "Calories" }, new NutrientEntity { Id = Sugars, Name = "Total Sugars" });
            db.Set<BaseMeasurementEntity>().Add(new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = CourseClassificationService.SnackType, Name = "Snack" },
                new ReferenceEntity { Id = CourseClassificationService.DessertType, Name = "Dessert" },
                new ReferenceEntity { Id = CourseClassificationService.SnacksCategory, Name = "Snacks" },
                new ReferenceEntity { Id = CourseClassificationService.DessertsCategory, Name = "Desserts" });
            await db.SaveChangesAsync();
            return db;
        }

        private static IngredientEntity Food(ApplicationDbContext db, string name, decimal kcal, decimal sugars)
        {
            var food = new IngredientEntity { Name = name, FdcId = Guid.NewGuid().ToString("N")[..8] };
            food.IngredientNutrients = new List<IngredientNutrientEntity>
            {
                new() { NutrientId = Kcal, Amount = kcal, MeasurementId = 1 },
                new() { NutrientId = Sugars, Amount = sugars, MeasurementId = 1 },
            };
            db.Ingredients.Add(food);
            return food;
        }

        private static async Task<RecipeEntity> RecipeAsync(ApplicationDbContext db, string name, long categoryId, params (IngredientEntity Food, decimal Grams)[] lines)
        {
            await db.SaveChangesAsync();
            var recipe = new RecipeEntity { Name = name };
            recipe.RecipeCategories = new List<RecipeCategoryEntity> { new() { CategoryId = categoryId } };
            recipe.RecipeIngredients = lines.Select(l => new RecipeIngredientEntity { IngredientId = l.Food.Id, Quantity = l.Grams, MeasurementId = 1 }).ToList();
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            return recipe;
        }

        private static async Task<(long[] Types, long[] Categories)> CourseOf(ApplicationDbContext db, long recipeId)
        {
            var r = await db.Recipes.Include(x => x.RecipeTypes).Include(x => x.RecipeCategories).SingleAsync(x => x.Id == recipeId);
            return (r.RecipeTypes!.Select(t => t.Id).OrderBy(i => i).ToArray(),
                r.RecipeCategories!.Where(c => !c.IsDeleted).Select(c => c.CategoryId).OrderBy(i => i).ToArray());
        }

        [Fact]
        public async Task Cookies_filed_as_snacks_move_to_desserts_and_banana_cream_stays_a_snack()
        {
            using var db = await SeedAsync();
            var flour = Food(db, "Wheat flour, white, all-purpose", 364, 0.27m);
            var butter = Food(db, "Butter, without salt", 717, 0.06m);
            var sugar = Food(db, "Sugars, granulated", 387, 99.8m);
            var chips = Food(db, "Candies, semisweet chocolate", 480, 54.5m);
            var banana = Food(db, "Bananas, raw", 89, 12.2m);
            var cookies = await RecipeAsync(db, "Cookies", CourseClassificationService.SnacksCategory, (flour, 280), (butter, 227), (sugar, 300), (chips, 340));
            var nice = await RecipeAsync(db, "Banana Nice Cream", CourseClassificationService.DessertsCategory, (banana, 240));

            var result = await new CourseClassificationService(db, NullLogger<CourseClassificationService>.Instance).ClassifyAsync();

            result.Classified.Should().Be(2);
            var cookieCourse = await CourseOf(db, cookies.Id);
            cookieCourse.Types.Should().Equal(CourseClassificationService.DessertType);
            cookieCourse.Categories.Should().Equal(CourseClassificationService.DessertsCategory);
            var niceCourse = await CourseOf(db, nice.Id);
            niceCourse.Types.Should().Equal(CourseClassificationService.SnackType);
            niceCourse.Categories.Should().Equal(CourseClassificationService.SnacksCategory);
        }

        [Fact]
        public async Task Recipes_without_enough_data_are_left_alone()
        {
            using var db = await SeedAsync();
            var unknown = new IngredientEntity { Name = "mystery crumble" };
            db.Ingredients.Add(unknown);
            var recipe = await RecipeAsync(db, "Crumble", CourseClassificationService.SnacksCategory, (unknown, 200));

            var result = await new CourseClassificationService(db, NullLogger<CourseClassificationService>.Instance).ClassifyAsync();

            result.Undetermined.Should().Be(1);
            (await CourseOf(db, recipe.Id)).Categories.Should().Equal(CourseClassificationService.SnacksCategory);
        }
    }
}
