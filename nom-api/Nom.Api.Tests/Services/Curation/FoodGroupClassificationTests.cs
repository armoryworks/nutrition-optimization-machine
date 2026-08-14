using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Curation
{
    /// <summary>
    /// Heuristic food-group classification of ingredients by name keyword,
    /// and manual override.
    /// </summary>
    public class FoodGroupClassificationTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static CurationOrchestrationService NewService(ApplicationDbContext ctx) =>
            new(ctx, NullLogger<CurationOrchestrationService>.Instance);

        private static long? GroupOf(ApplicationDbContext ctx, string name) =>
            ctx.Ingredients.Single(i => i.Name == name).FoodGroupId;

        [Fact]
        public async Task AutoClassify_AssignsGroupsByKeyword_MostSpecificWins()
        {
            using var ctx = NewContext();
            ctx.Ingredients.AddRange(
                new IngredientEntity { Name = "Fuji Apple" },
                new IngredientEntity { Name = "Boneless Chicken Breast" },
                new IngredientEntity { Name = "Creamy Peanut Butter" }, // peanut → Nuts/Seeds, not Dairy(butter)
                new IngredientEntity { Name = "Fresh Spinach" },
                new IngredientEntity { Name = "Unobtainium Widget" });   // no keyword → stays null
            await ctx.SaveChangesAsync();

            var updated = await NewService(ctx).AutoClassifyFoodGroupsAsync(overwrite: false);

            updated.Should().Be(4);
            GroupOf(ctx, "Fuji Apple").Should().Be((long)FoodGroupEnum.Fruits);
            GroupOf(ctx, "Boneless Chicken Breast").Should().Be((long)FoodGroupEnum.ProteinFoods);
            GroupOf(ctx, "Creamy Peanut Butter").Should().Be((long)FoodGroupEnum.NutsSeeds);
            GroupOf(ctx, "Fresh Spinach").Should().Be((long)FoodGroupEnum.Vegetables);
            GroupOf(ctx, "Unobtainium Widget").Should().BeNull();
        }

        [Fact]
        public async Task AutoClassify_WithoutOverwrite_LeavesAlreadyClassifiedAlone()
        {
            using var ctx = NewContext();
            // Deliberately "wrong" existing classification; without overwrite it must be preserved.
            ctx.Ingredients.Add(new IngredientEntity { Name = "Fuji Apple", FoodGroupId = (long)FoodGroupEnum.Dairy });
            await ctx.SaveChangesAsync();

            var updated = await NewService(ctx).AutoClassifyFoodGroupsAsync(overwrite: false);

            updated.Should().Be(0);
            GroupOf(ctx, "Fuji Apple").Should().Be((long)FoodGroupEnum.Dairy);
        }

        [Fact]
        public async Task AutoClassify_WithOverwrite_Reclassifies()
        {
            using var ctx = NewContext();
            ctx.Ingredients.Add(new IngredientEntity { Name = "Fuji Apple", FoodGroupId = (long)FoodGroupEnum.Dairy });
            await ctx.SaveChangesAsync();

            var updated = await NewService(ctx).AutoClassifyFoodGroupsAsync(overwrite: true);

            updated.Should().Be(1);
            GroupOf(ctx, "Fuji Apple").Should().Be((long)FoodGroupEnum.Fruits);
        }

        [Fact]
        public async Task SetIngredientFoodGroup_SetsAndClears()
        {
            using var ctx = NewContext();
            var ing = new IngredientEntity { Name = "Mystery Food" };
            ctx.Ingredients.Add(ing);
            await ctx.SaveChangesAsync();
            var svc = NewService(ctx);

            (await svc.SetIngredientFoodGroupAsync(ing.Id, (long)FoodGroupEnum.Grains)).Should().BeTrue();
            GroupOf(ctx, "Mystery Food").Should().Be((long)FoodGroupEnum.Grains);

            (await svc.SetIngredientFoodGroupAsync(ing.Id, null)).Should().BeTrue();
            GroupOf(ctx, "Mystery Food").Should().BeNull();

            (await svc.SetIngredientFoodGroupAsync(999, (long)FoodGroupEnum.Grains)).Should().BeFalse();
        }
    }
}
