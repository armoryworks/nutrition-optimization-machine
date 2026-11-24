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

        [Fact]
        public async Task Update_ChangingAnAmount_StampsTheEditor_AndAnUnchangedAmountKeepsItsStamp()
        {
            await using var db = NewContext();
            var id = await SeedAsync(db);
            var user = new Mock<ICurrentUserService>();
            user.Setup(u => u.PersonId).Returns(42);
            var service = new RecipeOrchestrationService(db, new HttpContextAccessor(), user.Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);
            async Task StampSystemAsync()
            {
                foreach (var row in await db.RecipeIngredients.ToListAsync()) row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }

            await StampSystemAsync();
            await service.UpdateRecipeAsync(id, Request(id, 6,
                new() { new() { IngredientId = 10, Quantity = 3, MeasurementId = 1 } },
                new() { new() { Order = 1, Description = "Mix" } }));
            db.ChangeTracker.Clear();

            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == id && ri.IngredientId == 10)).LastModifiedByPersonId.Should().Be(42,
                "the start-up repair leaves an amount a person set, which it can tell only by the person's stamp (review CK4-PM-2)");

            await StampSystemAsync();
            await service.UpdateRecipeAsync(id, Request(id, 8,
                new() { new() { IngredientId = 10, Quantity = 3, MeasurementId = 1 } },
                new() { new() { Order = 1, Description = "Mix" } }));
            db.ChangeTracker.Clear();

            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == id && ri.IngredientId == 10)).LastModifiedByPersonId.Should().Be(SystemConstants.SystemPersonId);
        }

        private static async Task<long> SeedScrapedAsync(ApplicationDbContext db, string longStep)
        {
            var recipe = new RecipeEntity { Name = "Roasted Potatoes", AuthorId = 6, Servings = 4, ScrapedAtUtc = DateTime.UtcNow };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 10, SortOrder = 0, Quantity = 2, MeasurementId = 1, RawLine = "2 lb potatoes" });
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = 11, SortOrder = 1, Quantity = 2, MeasurementId = 2, RawLine = "2 tbsp olive oil" });
            db.RecipeSteps.Add(new RecipeStepEntity { RecipeId = recipe.Id, StepNumber = 1, Summary = "Toss the potatoes in the oil", Description = longStep });
            db.RecipeSteps.Add(new RecipeStepEntity { RecipeId = recipe.Id, StepNumber = 2, Summary = "Roast until golden", Description = "Roast for forty minutes." });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipe.Id;
        }

        private static UpdateRecipeRequest Scraped(long id, decimal oil, string longStep, string secondStep = "Roast for forty minutes.", decimal potatoes = 2) =>
            new()
            {
                Id = id, Name = "Roasted Potatoes", Servings = 4,
                Ingredients = new() { new() { IngredientId = 10, Quantity = potatoes, MeasurementId = 1 }, new() { IngredientId = 11, Quantity = oil, MeasurementId = 2 } },
                Steps = new() { new() { Order = 1, Description = longStep }, new() { Order = 2, Description = secondStep } },
            };

        [Fact]
        public async Task Update_OfARecipeWithAStepOver255Characters_LeavesUnchangedStepsAndCutsAChangedStepsSummaryTo255()
        {
            await using var db = NewContext();
            var longStep = string.Concat(Enumerable.Repeat("Toss the potatoes with the oil, salt and pepper until every piece is coated. ", 6));
            var id = await SeedScrapedAsync(db, longStep);

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 3, longStep));
            db.ChangeTracker.Clear();

            var steps = await db.RecipeSteps.Where(rs => rs.RecipeId == id).OrderBy(rs => rs.StepNumber).ToListAsync();
            steps.Select(rs => rs.Summary).Should().Equal(new[] { "Toss the potatoes in the oil", "Roast until golden" },
                "an edit of a line leaves every step it did not change alone (staging checkpoint 6, R4)");

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 3, longStep + " Then spread them out.", "Roast for forty-five minutes."));
            db.ChangeTracker.Clear();

            steps = await db.RecipeSteps.Where(rs => rs.RecipeId == id).OrderBy(rs => rs.StepNumber).ToListAsync();
            steps[0].Summary.Length.Should().BeLessThanOrEqualTo(255, "RecipeStep.Summary is varchar(255), so a longer one fails the save with 22001");
            steps[0].Description.Should().EndWith("Then spread them out.");
            steps[1].Summary.Should().Be("Roast for forty-five minutes.");
        }

        [Fact]
        public async Task Update_ThatChangesNothing_OrAnEditAndItsRevert_KeepsTheScrapedRecipesVersion()
        {
            await using var db = NewContext();
            var id = await SeedScrapedAsync(db, "Toss the potatoes in the oil.");
            async Task<long> VersionAsync()
            {
                db.ChangeTracker.Clear();
                return (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == id)).Version;
            }

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 2, "Toss the potatoes in the oil."));
            (await VersionAsync()).Should().Be(1, "a save that changes nothing is no person's edit");

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 3, "Toss the potatoes in the oil."));
            (await VersionAsync()).Should().Be(2);
            (await db.AuditLogEntries.CountAsync(a => a.EntityId == id && a.ChangeType == RecipeOrchestrationService.EditBaseChangeType)).Should().Be(1);

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 2, "Toss the potatoes in the oil."));
            (await VersionAsync()).Should().Be(1, "an edit and its revert leave the recipe a scraped one, in the rebuild and order sets (staging checkpoint 6, R10)");

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 3, "Toss the potatoes in the oil."));
            (await VersionAsync()).Should().Be(2);
            var potatoes = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == id && ri.IngredientId == 10);
            potatoes.Quantity = 3;
            await db.SaveChangesAsync();
            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 2, "Toss the potatoes in the oil.", potatoes: 3));
            (await VersionAsync()).Should().BeGreaterThan(1, "the recipe is not what it was before the person's edit, so the edit stands");
        }

        [Fact]
        public async Task Update_AnEditOfStepTextsAndItsRevert_PutsBackEveryStepsSummaryWithTheVersion()
        {
            await using var db = NewContext();
            var id = await SeedScrapedAsync(db, "Toss the potatoes in the oil.");
            async Task<List<string>> SummariesAsync()
            {
                db.ChangeTracker.Clear();
                return await db.RecipeSteps.AsNoTracking().Where(rs => rs.RecipeId == id).OrderBy(rs => rs.StepNumber).Select(rs => rs.Summary).ToListAsync();
            }

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 2, "Coat the potatoes with the oil."));
            (await SummariesAsync()).Should().Equal("Coat the potatoes with the oil.", "Roast until golden");
            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 2, "Coat the potatoes with the oil.", "Roast for an hour."));
            (await SummariesAsync()).Should().Equal("Coat the potatoes with the oil.", "Roast for an hour.");

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 2, "Toss the potatoes in the oil."));

            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == id)).Version.Should().Be(1);
            (await SummariesAsync()).Should().Equal(new[] { "Toss the potatoes in the oil", "Roast until golden" },
                "a revert that puts the version back puts back the summaries the edit replaced (review CK11L-2)");
        }

        [Fact]
        public async Task Update_WithAStepWhose255thCharacterStartsAnEmoji_CutsTheSummaryBeforeTheEmoji()
        {
            await using var db = NewContext();
            var id = await SeedScrapedAsync(db, "Toss the potatoes in the oil.");
            var step = new string('a', 254) + "\U0001F954 and roast them.";

            await NewService(db).UpdateRecipeAsync(id, Scraped(id, 2, step));
            db.ChangeTracker.Clear();

            var summary = (await db.RecipeSteps.SingleAsync(rs => rs.RecipeId == id && rs.StepNumber == 1)).Summary;
            summary.Should().Be(new string('a', 254));
            FluentActions.Invoking(() => new System.Text.UTF8Encoding(false, true).GetBytes(summary)).Should().NotThrow(
                "Npgsql writes text with a strict UTF-8 encoder, so half an emoji fails the save (review CK11L-3)");
        }
    }
}
