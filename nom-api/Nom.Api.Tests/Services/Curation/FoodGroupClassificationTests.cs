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
    /// Food-group classification of ingredients from their stored USDA category only, never a
    /// name keyword, and manual override.
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
        public async Task AutoClassify_SetsGroupsFromTheStoredCategoryOnly_NeverFromAName()
        {
            using var ctx = NewContext();
            ctx.Ingredients.AddRange(
                new IngredientEntity { Name = "Apples, raw, with skin", FdcDataType = "sr_legacy_food", FdcCategoryId = 9 },
                new IngredientEntity { Name = "Spices, pepper, black", FdcDataType = "sr_legacy_food", FdcCategoryId = 2 },
                new IngredientEntity { Name = "Cheddar Snack Cubes (Acme)", FdcDataType = "branded_food", FdcCategory = "Cheese" },
                new IngredientEntity { Name = "Tomato Ketchup (Acme)", FdcDataType = "branded_food", FdcCategory = "Ketchup, Mustard, BBQ & Cheese Sauce" },
                new IngredientEntity { Name = "Fuji Apple" });
            await ctx.SaveChangesAsync();

            var updated = await NewService(ctx).AutoClassifyFoodGroupsAsync(overwrite: false);

            updated.Should().Be(2);
            GroupOf(ctx, "Apples, raw, with skin").Should().Be((long)FoodGroupEnum.Fruits);
            GroupOf(ctx, "Spices, pepper, black").Should().BeNull("Spices and Herbs gives no group, whatever \"pepper\" reads like");
            GroupOf(ctx, "Cheddar Snack Cubes (Acme)").Should().Be((long)FoodGroupEnum.Dairy);
            GroupOf(ctx, "Tomato Ketchup (Acme)").Should().BeNull("a condiment category gives no group");
            GroupOf(ctx, "Fuji Apple").Should().BeNull("a food with no stored category is never grouped by its name");
        }

        [Fact]
        public async Task AutoClassify_WithoutOverwrite_LeavesAlreadyClassifiedAlone()
        {
            using var ctx = NewContext();
            ctx.Ingredients.Add(new IngredientEntity { Name = "Apples, raw, with skin", FdcDataType = "sr_legacy_food", FdcCategoryId = 9, FoodGroupId = (long)FoodGroupEnum.Dairy });
            await ctx.SaveChangesAsync();

            var updated = await NewService(ctx).AutoClassifyFoodGroupsAsync(overwrite: false);

            updated.Should().Be(0);
            GroupOf(ctx, "Apples, raw, with skin").Should().Be((long)FoodGroupEnum.Dairy);
        }

        [Fact]
        public async Task AutoClassify_WithOverwrite_SetsWhatTheCategoryGives_AndClearsANameDerivedGroup()
        {
            using var ctx = NewContext();
            ctx.Ingredients.AddRange(
                new IngredientEntity { Name = "Apples, raw, with skin", FdcDataType = "sr_legacy_food", FdcCategoryId = 9, FoodGroupId = (long)FoodGroupEnum.Dairy },
                new IngredientEntity { Name = "Pepper", FdcDataType = "sr_legacy_food", FdcCategoryId = 2, FoodGroupId = (long)FoodGroupEnum.Vegetables },
                new IngredientEntity { Name = "Fuji Apple", FoodGroupId = (long)FoodGroupEnum.Dairy });
            await ctx.SaveChangesAsync();

            var updated = await NewService(ctx).AutoClassifyFoodGroupsAsync(overwrite: true);

            updated.Should().Be(2);
            GroupOf(ctx, "Apples, raw, with skin").Should().Be((long)FoodGroupEnum.Fruits);
            GroupOf(ctx, "Pepper").Should().BeNull("black pepper leaves Vegetables");
            GroupOf(ctx, "Fuji Apple").Should().Be((long)FoodGroupEnum.Dairy, "a hand-made food has no category to judge by");
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
