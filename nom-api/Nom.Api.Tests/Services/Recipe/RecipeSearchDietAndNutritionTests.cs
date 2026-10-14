using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// Recipe search hides recipes that conflict with the requester's allergy/medical restrictions
    /// (on by default when they have one, before paging so counts stay right), and sorts by protein
    /// or calories per serving with incomplete labels kept off the top.
    /// </summary>
    public class RecipeSearchDietAndNutritionTests
    {
        private const long Me = 7;
        private const long CeliacRef = 60800, VegetarianRef = 2004;
        private const long Gram = 1, Cup = 2;
        private const long ProteinId = 5006, CaloriesId = 5035;

        private sealed record Catalog(long Flour, long Chicken, long Rice, long Beans);

        private static async Task<(ApplicationDbContext db, Catalog catalog)> SeedAsync()
        {
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                accessor);
            db.Persons.Add(new PersonEntity { Id = Me, Name = "Me" });
            db.References.AddRange(
                new ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "NonCurated" },
                new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" },
                new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 240m });
            db.Nutrients.AddRange(
                new NutrientEntity { Id = ProteinId, Name = "Protein" },
                new NutrientEntity { Id = CaloriesId, Name = "Calories" });

            var flour = Food("All-Purpose Wheat Flour", withData: true);
            var chicken = Food("Chicken Breast", withData: true);
            var rice = Food("White Rice", withData: true);
            var beans = Food("Black Beans", withData: false);
            db.Ingredients.AddRange(flour, chicken, rice, beans);
            await db.SaveChangesAsync();
            return (db, new Catalog(flour.Id, chicken.Id, rice.Id, beans.Id));
        }

        private static IngredientEntity Food(string name, bool withData)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = (long)CurationStatusEnum.Curated };
            if (withData)
                food.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = ProteinId, Amount = 10m });
            return food;
        }

        private static RecipeEntity Recipe(string name, params (long ingredientId, long measurementId)[] lines)
        {
            var recipe = new RecipeEntity
            {
                Name = name,
                AuthorId = Me,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                Visibility = RecipeVisibilityEnum.Private,
                RecipeIngredients = new List<RecipeIngredientEntity>()
            };
            var order = 0;
            foreach (var (ingredientId, measurementId) in lines)
            {
                recipe.RecipeIngredients!.Add(new RecipeIngredientEntity
                {
                    IngredientId = ingredientId,
                    MeasurementId = measurementId,
                    Quantity = 100m,
                    SortOrder = order++
                });
            }
            return recipe;
        }

        private static void Label(RecipeEntity recipe, decimal protein, decimal calories, bool derived = true)
        {
            recipe.Nutrition ??= new List<RecipeNutritionEntity>();
            var calculated = derived ? DateTime.UtcNow : (DateTime?)null;
            recipe.Nutrition.Add(new RecipeNutritionEntity { NutrientId = ProteinId, Amount = protein, DateCalculated = calculated });
            recipe.Nutrition.Add(new RecipeNutritionEntity { NutrientId = CaloriesId, Amount = calories, DateCalculated = calculated });
        }

        private static async Task AddCeliacAsync(ApplicationDbContext db)
        {
            db.Restrictions.Add(new RestrictionEntity { PersonId = Me, Name = "Celiac Disease", RestrictionTypeId = CeliacRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
        }

        private static Task<RecipeSearchResponseModel> Search(ApplicationDbContext db, RecipeSearchModel model)
        {
            model.RequesterPersonId ??= Me;
            model.IncludeIngredients = false;
            return new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance)
                .SearchRecipesAsync(model);
        }

        private static async Task SeedChickenRecipesAsync(ApplicationDbContext db, Catalog c)
        {
            db.Recipes.AddRange(
                Recipe("Breaded chicken", (c.Chicken, Gram), (c.Flour, Gram)),
                Recipe("Chicken and rice", (c.Chicken, Gram), (c.Rice, Gram)),
                Recipe("Chicken pot pie", (c.Chicken, Gram), (c.Flour, Gram)),
                Recipe("Grilled chicken", (c.Chicken, Gram)));
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Severe_restriction_hides_conflicting_recipes_by_default_and_counts_after_hiding()
        {
            var (db, c) = await SeedAsync();
            await using var _ = db;
            await SeedChickenRecipesAsync(db, c);
            await AddCeliacAsync(db);

            var response = await Search(db, new RecipeSearchModel { Query = "chicken", PageSize = 1 });

            response.DietConflictsHidden.Should().BeTrue();
            response.TotalCount.Should().Be(2, "the two flour recipes conflict with celiac disease");
            response.TotalPages.Should().Be(2);
            var second = await Search(db, new RecipeSearchModel { Query = "chicken", PageSize = 1, Page = 2 });
            response.Results.Concat(second.Results).Select(r => r.Name)
                .Should().BeEquivalentTo("Chicken and rice", "Grilled chicken");
        }

        [Fact]
        public async Task Turning_the_filter_off_shows_conflicting_recipes_again()
        {
            var (db, c) = await SeedAsync();
            await using var _ = db;
            await SeedChickenRecipesAsync(db, c);
            await AddCeliacAsync(db);

            var response = await Search(db, new RecipeSearchModel { Query = "chicken", HideDietConflicts = false });

            response.DietConflictsHidden.Should().BeFalse();
            response.TotalCount.Should().Be(4);
        }

        [Fact]
        public async Task Without_a_severe_restriction_nothing_is_hidden_by_default()
        {
            var (db, c) = await SeedAsync();
            await using var _ = db;
            await SeedChickenRecipesAsync(db, c);
            db.Restrictions.Add(new RestrictionEntity { PersonId = Me, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            db.Restrictions.Add(new RestrictionEntity { PersonId = Me, Name = "No rice", IngredientId = c.Rice, Severity = 2 });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var response = await Search(db, new RecipeSearchModel { Query = "chicken" });

            response.DietConflictsHidden.Should().BeFalse();
            response.TotalCount.Should().Be(4);
        }

        [Fact]
        public async Task A_direct_severe_ingredient_restriction_counts()
        {
            var (db, c) = await SeedAsync();
            await using var _ = db;
            await SeedChickenRecipesAsync(db, c);
            db.Restrictions.Add(new RestrictionEntity { PersonId = Me, Name = "Rice allergy", IngredientId = c.Rice, Severity = 5 });
            await db.SaveChangesAsync();

            var response = await Search(db, new RecipeSearchModel { Query = "chicken" });

            response.DietConflictsHidden.Should().BeTrue();
            response.Results.Select(r => r.Name).Should().NotContain("Chicken and rice").And.HaveCount(3);
        }

        [Fact]
        public async Task Anonymous_callers_are_never_filtered()
        {
            var (db, c) = await SeedAsync();
            await using var _ = db;
            await AddCeliacAsync(db);

            var response = await new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance)
                .SearchRecipesAsync(new RecipeSearchModel { Query = "chicken", HideDietConflicts = true, IncludeIngredients = false });

            response.DietConflictsHidden.Should().BeFalse();
        }

        private static async Task SeedLabelledAsync(ApplicationDbContext db, Catalog c)
        {
            var lean = Recipe("Lean chicken", (c.Chicken, Gram));
            Label(lean, protein: 30m, calories: 200m);
            var bowl = Recipe("Chicken bean bowl", (c.Chicken, Gram), (c.Beans, Gram));
            Label(bowl, protein: 50m, calories: 150m);
            var rice = Recipe("Chicken fried rice", (c.Chicken, Gram), (c.Rice, Cup));
            Label(rice, protein: 45m, calories: 120m);
            var seeded = Recipe("Chicken stew", (c.Chicken, Gram), (c.Beans, Gram));
            Label(seeded, protein: 40m, calories: 300m, derived: false);
            var unlabelled = Recipe("Chicken soup", (c.Chicken, Gram));
            db.Recipes.AddRange(lean, bowl, rice, seeded, unlabelled);
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Most_protein_ranks_complete_labels_first()
        {
            var (db, c) = await SeedAsync();
            await using var _ = db;
            await SeedLabelledAsync(db, c);

            var names = (await Search(db, new RecipeSearchModel { Query = "chicken", SortBy = "protein" }))
                .Results.Select(r => r.Name).ToList();

            names.Should().Equal(
                "Chicken stew",
                "Lean chicken",
                "Chicken bean bowl",
                "Chicken fried rice",
                "Chicken soup");
        }

        [Fact]
        public async Task Fewest_calories_ranks_complete_labels_first()
        {
            var (db, c) = await SeedAsync();
            await using var _ = db;
            await SeedLabelledAsync(db, c);

            var names = (await Search(db, new RecipeSearchModel { Query = "chicken", SortBy = "calories" }))
                .Results.Select(r => r.Name).ToList();

            names.Should().Equal(
                "Lean chicken",
                "Chicken stew",
                "Chicken fried rice",
                "Chicken bean bowl",
                "Chicken soup");
        }

        [Theory]
        [InlineData(null, 100, Gram, true, null, null, NutritionLineStatus.Counted)]
        [InlineData("note", 100, Gram, false, null, null, NutritionLineStatus.NotAnIngredient)]
        [InlineData(null, 0, Gram, false, null, null, NutritionLineStatus.NotMeasured)]
        [InlineData("ingredient", 100, Gram, false, null, null, NutritionLineStatus.NoData)]
        [InlineData(null, 1, Cup, true, null, null, NutritionLineStatus.NoConversion)]
        [InlineData(null, 1, Cup, true, 0.9, null, NutritionLineStatus.Counted)]
        [InlineData(null, 2, 3L, true, null, null, NutritionLineStatus.NoConversion)]
        [InlineData(null, 2, 3L, true, null, 50.0, NutritionLineStatus.Counted)]
        public void Uncounted_line_predicate_agrees_with_the_line_status(
            string? lineKind, int quantity, long measurementId, bool hasData, double? gramsPerMl, double? servingGrams, string status)
        {
            var categories = new Dictionary<long, long> { [Gram] = 1, [Cup] = 2, [3L] = 3 };
            var ingredient = Food("Food", hasData);
            ingredient.GramsPerMilliliter = (decimal?)gramsPerMl;
            ingredient.ReferenceServingGrams = (decimal?)servingGrams;
            var line = new RecipeIngredientEntity
            {
                LineKind = lineKind,
                Quantity = quantity,
                Ingredient = ingredient,
                Measurement = new BaseMeasurementEntity { Id = measurementId, Name = "Unit", Symbol = "u", MeasurementCategoryId = categories[measurementId], BaseUnitConversionFactor = 1m }
            };

            RecipeNutritionService.StatusFor(line, hasData).Should().Be(status);
            RecipeNutritionService.IsUncountedLine.Compile()(line).Should().Be(
                status is NutritionLineStatus.NoData or NutritionLineStatus.NoConversion);
        }
    }
}
