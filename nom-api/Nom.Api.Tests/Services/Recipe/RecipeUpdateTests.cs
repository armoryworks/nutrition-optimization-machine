using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// Saving an edit must update ingredient and step rows in place, since both
    /// are soft-deleted under composite keys that a remove-and-re-add collides with.
    /// </summary>
    public class RecipeUpdateTests
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

        private static async Task<long> SeedAsync(ApplicationDbContext db)
        {
            var recipe = new RecipeEntity { Name = "Eggs", AuthorId = 6, Servings = 6 };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 10, Quantity = 2, MeasurementId = 1, RawLine = "2 eggs" });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 11, Quantity = 1, MeasurementId = 1 });
            db.RecipeSteps.Add(new RecipeStepEntity { RecipeId = recipe.Id, StepNumber = 1, Summary = "Mix", Description = "Mix" });
            db.RecipeSteps.Add(new RecipeStepEntity { RecipeId = recipe.Id, StepNumber = 2, Summary = "Serve", Description = "Serve" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipe.Id;
        }

        private static UpdateRecipeRequest Request(long id, long servings, List<RecipeIngredientModel> ingredients, List<RecipeStepModel> steps) =>
            new() { Id = id, Name = "Eggs", Servings = servings, Ingredients = ingredients, Steps = steps };

        [Fact]
        public async Task Update_KeepingIngredientsAndSteps_UpdatesInPlace()
        {
            await using var db = NewContext();
            var id = await SeedAsync(db);

            await NewService(db).UpdateRecipeAsync(id, Request(id, 8,
                new() { new() { IngredientId = 10, Quantity = 3, MeasurementId = 2 } },
                new() { new() { Order = 1, Description = "Whisk" } }));
            db.ChangeTracker.Clear();

            (await db.Recipes.SingleAsync(r => r.Id == id)).Servings.Should().Be(8);
            var ingredients = await db.RecipeIngredients.Where(ri => ri.RecipeId == id).ToListAsync();
            ingredients.Should().ContainSingle();
            ingredients[0].Quantity.Should().Be(3);
            ingredients[0].MeasurementId.Should().Be(2);
            ingredients[0].RawLine.Should().Be("2 eggs");
            var steps = await db.RecipeSteps.Where(rs => rs.RecipeId == id).ToListAsync();
            steps.Should().ContainSingle().Which.Description.Should().Be("Whisk");
        }

        [Fact]
        public async Task Update_ReAddingRemovedIngredientAndStep_RevivesSoftDeletedRows()
        {
            await using var db = NewContext();
            var id = await SeedAsync(db);
            var service = NewService(db);

            await service.UpdateRecipeAsync(id, Request(id, 6,
                new() { new() { IngredientId = 10, Quantity = 2, MeasurementId = 1 } },
                new() { new() { Order = 1, Description = "Mix" } }));
            db.ChangeTracker.Clear();

            await service.UpdateRecipeAsync(id, Request(id, 6,
                new()
                {
                    new() { IngredientId = 10, Quantity = 2, MeasurementId = 1 },
                    new() { IngredientId = 11, Quantity = 4, MeasurementId = 1 }
                },
                new()
                {
                    new() { Order = 1, Description = "Mix" },
                    new() { Order = 2, Description = "Plate" }
                }));
            db.ChangeTracker.Clear();

            var ingredients = await db.RecipeIngredients.Where(ri => ri.RecipeId == id).ToListAsync();
            ingredients.Select(ri => ri.IngredientId).Should().BeEquivalentTo(new[] { 10L, 11L });
            ingredients.Single(ri => ri.IngredientId == 11).Quantity.Should().Be(4);
            var steps = await db.RecipeSteps.Where(rs => rs.RecipeId == id).OrderBy(rs => rs.StepNumber).ToListAsync();
            steps.Select(rs => rs.Description).Should().Equal("Mix", "Plate");
        }
    }
}
