using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The search page's filters: "mine only" keeps just the caller's recipes, a time
    /// limit counts prep plus cook and skips recipes with no time, and quickest-first
    /// puts timeless recipes last.
    /// </summary>
    public class RecipeSearchFilterTests
    {
        private const long Me = 7;
        private const long Someone = 22;

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                accessor);
            db.References.Add(new ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "NonCurated" });
            db.Persons.AddRange(
                new PersonEntity { Id = Me, Name = "Me" },
                new PersonEntity { Id = Someone, Name = "Someone" });
            db.Recipes.AddRange(
                Recipe("My quick chicken", Me, prep: 5, cook: 10),
                Recipe("My slow chicken", Me, prep: 20, cook: 60),
                Recipe("My untimed chicken", Me),
                Recipe("My boiled chicken", Me, cook: 25),
                Recipe("Their chicken", Someone, prep: 5, cook: 5, visibility: RecipeVisibilityEnum.Private));
            await db.SaveChangesAsync();
            return db;
        }

        private static RecipeEntity Recipe(string name, long author, long? prep = null, long? cook = null,
            RecipeVisibilityEnum visibility = RecipeVisibilityEnum.Private) =>
            new() { Name = name, AuthorId = author, CurationStatusId = (long)CurationStatusEnum.NonCurated, PrepTimeMinutes = prep, CookTimeMinutes = cook, Visibility = visibility };

        private static Task<RecipeSearchResponseModel> Search(ApplicationDbContext db, RecipeSearchModel model)
        {
            model.Query ??= "chicken";
            model.IncludeIngredients = false;
            return new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance)
                .SearchRecipesAsync(model);
        }

        [Fact]
        public async Task MaxTotalTime_CountsPrepPlusCook_AndSkipsUntimedRecipes()
        {
            await using var db = await SeedAsync();

            var result = await Search(db, new RecipeSearchModel { RequesterPersonId = Me, MaxTotalTime = 30 });

            result.Results.Select(r => r.Name).Should().BeEquivalentTo("My quick chicken", "My boiled chicken");
        }

        [Fact]
        public async Task MineOnly_KeepsTheCallersRecipes_AndReturnsNothingAnonymously()
        {
            await using var db = await SeedAsync();

            (await Search(db, new RecipeSearchModel { RequesterPersonId = Me, MineOnly = true }))
                .Results.Should().OnlyContain(r => r.Name.StartsWith("My ")).And.HaveCount(4);
            (await Search(db, new RecipeSearchModel { MineOnly = true })).TotalCount.Should().Be(0);
        }

        [Fact]
        public async Task TotalTimeSort_PutsQuickestFirst_AndUntimedLast()
        {
            await using var db = await SeedAsync();

            var result = await Search(db, new RecipeSearchModel { RequesterPersonId = Me, MineOnly = true, SortBy = "totalTime" });

            result.Results.Select(r => r.Name).Should().Equal(
                "My quick chicken", "My boiled chicken", "My slow chicken", "My untimed chicken");
        }
    }
}
