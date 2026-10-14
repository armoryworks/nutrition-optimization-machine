using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// Ingredients keep the order the author entered them in, and the same ingredient
    /// may appear on more than one line. (UA-113)
    /// </summary>
    public class RecipeIngredientOrderTests
    {
        private static ApplicationDbContext NewContext()
        {
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            return new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                accessor);
        }

        private static RecipeOrchestrationService NewService(ApplicationDbContext db) =>
            new(db,
                new HttpContextAccessor(),
                new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object,
                new Mock<IRecipeNutritionService>().Object);

        private static async Task<long> SeedAuthorAsync(ApplicationDbContext db)
        {
            var person = new PersonEntity { Name = "Cook" };
            db.Persons.Add(person);
            await db.SaveChangesAsync();
            return person.Id;
        }

        private static Task<List<RecipeIngredientEntity>> LinesAsync(ApplicationDbContext db, long recipeId) =>
            db.RecipeIngredients.Where(ri => ri.RecipeId == recipeId).OrderBy(ri => ri.SortOrder).ToListAsync();

        [Fact]
        public async Task Create_KeepsEntryOrder_AndAllowsTheSameIngredientTwice()
        {
            await using var db = NewContext();
            var authorId = await SeedAuthorAsync(db);

            var created = await NewService(db).CreateRecipeAsync(new RecipeCreateModel
            {
                Name = "Marinated chicken",
                Ingredients = new()
                {
                    new() { IngredientId = 30, Quantity = 1, MeasurementId = 1 },
                    new() { IngredientId = 20, Quantity = 2, MeasurementId = 1 },
                    new() { IngredientId = 30, Quantity = 3, MeasurementId = 1 }
                },
                Steps = new() { new() { Order = 1, Description = "Marinate" } }
            }, authorId);
            db.ChangeTracker.Clear();

            var lines = await LinesAsync(db, created.Id);
            lines.Select(ri => (ri.IngredientId, ri.Quantity)).Should().Equal((30L, 1m), (20L, 2m), (30L, 3m));
            (await db.RecipeSteps.CountAsync(rs => rs.RecipeId == created.Id)).Should().Be(1);
        }

        [Fact]
        public async Task Create_WithAnUnselectedIngredient_SavesNothing()
        {
            await using var db = NewContext();
            var authorId = await SeedAuthorAsync(db);

            var act = () => NewService(db).CreateRecipeAsync(new RecipeCreateModel
            {
                Name = "Half typed",
                Ingredients = new()
                {
                    new() { IngredientId = 30, Quantity = 1, MeasurementId = 1 },
                    new() { IngredientId = 0, Quantity = 1, MeasurementId = 1 }
                }
            }, authorId);

            await act.Should().ThrowAsync<ArgumentException>();
            db.ChangeTracker.Clear();
            (await db.Recipes.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Update_ReorderingAndRepeating_FollowsTheNewOrder_AndKeepsRawLines()
        {
            await using var db = NewContext();
            var recipe = new RecipeEntity { Name = "Eggs", AuthorId = 6, Servings = 2 };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 10, Quantity = 2, MeasurementId = 1, RawLine = "2 eggs" });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 11, Quantity = 1, MeasurementId = 1, RawLine = "1 pinch salt" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await NewService(db).UpdateRecipeAsync(recipe.Id, new UpdateRecipeRequest
            {
                Id = recipe.Id,
                Name = "Eggs",
                Servings = 2,
                Ingredients = new()
                {
                    new() { IngredientId = 11, Quantity = 1, MeasurementId = 1 },
                    new() { IngredientId = 10, Quantity = 2, MeasurementId = 1 },
                    new() { IngredientId = 11, Quantity = 1, MeasurementId = 1 }
                },
                Steps = new()
            });
            db.ChangeTracker.Clear();

            var lines = await LinesAsync(db, recipe.Id);
            lines.Select(ri => ri.IngredientId).Should().Equal(11L, 10L, 11L);
            lines.Select(ri => ri.RawLine).Should().Equal("1 pinch salt", "2 eggs", string.Empty);
        }

        [Fact]
        public async Task Update_OfAScrapedRecipe_KeepsEachLinesClassification()
        {
            await using var db = NewContext();
            var recipe = new RecipeEntity { Name = "Scraped soup", AuthorId = 6, Servings = 4 };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 10, Quantity = 1, MeasurementId = 1, RawLine = "1 onion" });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 11, Quantity = 0, MeasurementId = 1, RawLine = "see note below", LineKind = RecipeIngredientLineKind.Note });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 12, Quantity = 0, MeasurementId = 1, RawLine = "a large stockpot", LineKind = RecipeIngredientLineKind.Equipment });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await NewService(db).UpdateRecipeAsync(recipe.Id, new UpdateRecipeRequest
            {
                Id = recipe.Id,
                Name = "Scraped soup",
                Servings = 4,
                Ingredients = new()
                {
                    new() { IngredientId = 10, Quantity = 1, MeasurementId = 1 },
                    new() { IngredientId = 11, Quantity = 0, MeasurementId = 1 },
                    new() { IngredientId = 12, Quantity = 0, MeasurementId = 1 }
                },
                Steps = new()
            });
            db.ChangeTracker.Clear();

            var lines = await LinesAsync(db, recipe.Id);
            lines.Select(ri => (ri.IngredientId, ri.SortOrder, ri.LineKind)).Should().Equal(
                (10L, 0, (string?)null), (11L, 1, RecipeIngredientLineKind.Note), (12L, 2, RecipeIngredientLineKind.Equipment));
            lines.Select(ri => ri.RawLine).Should().Equal("1 onion", "see note below", "a large stockpot");
        }

        [Fact]
        public async Task Get_CarriesEachLinesFoodGroup_ForTheShoppingAisle()
        {
            await using var db = NewContext();
            db.Persons.Add(new PersonEntity { Id = 6, Name = "Cook" });
            db.References.Add(new ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" });
            db.References.Add(new ReferenceEntity { Id = (long)FoodGroupEnum.Legumes, Name = "Legumes" });
            db.Set<BaseMeasurementEntity>().Add(new BaseMeasurementEntity { Id = 1, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 });
            db.Ingredients.Add(new IngredientEntity { Id = 10, Name = "Black Beans", FoodGroupId = (long)FoodGroupEnum.Legumes });
            db.Ingredients.Add(new IngredientEntity { Id = 11, Name = "Mystery spice blend" });
            var recipe = new RecipeEntity { Name = "Bean bowl", AuthorId = 6, Servings = 2, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 10, Quantity = 1, MeasurementId = 1, SortOrder = 0 });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 11, Quantity = 1, MeasurementId = 1, SortOrder = 1 });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var model = await NewService(db).GetRecipeAsync(recipe.Id, personId: 6);

            model!.Ingredients!.Select(i => (i.Name, i.FoodGroupName)).Should().Equal(
                ("Black Beans", "Legumes"), ("Mystery spice blend", (string?)null));
        }
    }
}
