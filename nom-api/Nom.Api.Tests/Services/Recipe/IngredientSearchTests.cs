using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The ingredient picker offers curated foods and the caller's own entries; pending
    /// imports and other users' import residue stay hidden, and an exact name match leads.
    /// </summary>
    public class IngredientSearchTests
    {
        private const long Me = 7;
        private const long Someone = 22;

        private static ApplicationDbContext NewContext()
        {
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            return new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                accessor);
        }

        private static RecipeOrchestrationService NewService(ApplicationDbContext db)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(c => c.PersonId).Returns(Me);
            return new(db,
                new HttpContextAccessor(),
                currentUser.Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object,
                new Mock<IRecipeNutritionService>().Object);
        }

        private static IngredientEntity Food(string name, CurationStatusEnum status, bool? wholeFood = null, long? createdBy = null) =>
            new() { Name = name, CurationStatusId = (long)status, IsWholeFood = wholeFood, CreatedByPersonId = createdBy };

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = NewContext();
            db.Ingredients.AddRange(
                Food("SALTED BUTTER, SALTED (Safeway, Inc.)", CurationStatusEnum.PendingCuration, true),
                Food("SALT & VINEGAR KETTLE CHIPS (Old Dutch Foods Inc)", CurationStatusEnum.PendingCuration, true),
                Food("Peanut butter, smooth style, with salt", CurationStatusEnum.Curated, true),
                Food("Salt", CurationStatusEnum.Curated),
                Food("yellow onion (diced", CurationStatusEnum.NonCurated, createdBy: Someone),
                Food("Yellow Onion", CurationStatusEnum.Curated),
                Food("Sumac", CurationStatusEnum.NonCurated, createdBy: Someone),
                Food("salt flakes (smoked", CurationStatusEnum.NonCurated, createdBy: Me));
            await db.SaveChangesAsync();
            return db;
        }

        [Fact]
        public async Task Search_PutsExactCuratedMatchFirst_AndHidesPendingImports()
        {
            await using var db = await SeedAsync();

            var names = (await NewService(db).SearchIngredientsAsync("salt")).Select(r => r.Name).ToList();

            names.First().Should().Be("Salt");
            names.Should().NotContain(n => n.Contains("Inc"));
            names.Should().Contain("Peanut butter, smooth style, with salt");
        }

        [Fact]
        public async Task Search_HidesOtherUsersResidue_ButKeepsTheCallersOwn()
        {
            await using var db = await SeedAsync();
            var service = NewService(db);

            (await service.SearchIngredientsAsync("yellow onion")).Select(r => r.Name)
                .Should().Equal("Yellow Onion");
            (await service.SearchIngredientsAsync("salt flakes")).Select(r => r.Name)
                .Should().Equal("salt flakes (smoked");
        }

        [Fact]
        public async Task Search_FindsAnotherUsersEntry_OnlyByItsExactName()
        {
            await using var db = await SeedAsync();
            var service = NewService(db);

            (await service.SearchIngredientsAsync("suma")).Should().BeEmpty();
            (await service.SearchIngredientsAsync("sumac")).Select(r => r.Name).Should().Equal("Sumac");
        }
    }
}
