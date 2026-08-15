using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Nutrition shown for a standalone whole food in a meal-plan slot. Stored amounts are
    /// per 100 g; one unit of Quantity is one reference serving when the food has one, else
    /// the 100 g basis.
    /// </summary>
    public class StandaloneNutritionTests
    {
        private const long HouseholdId = 100;
        private const long Author = 10;
        private const long Snacks = 1103;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private sealed class FakePolicy : IPolicyEnforcementService
        {
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(false);
            public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new List<long>());
            public Task<List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(new List<long>());
        }

        /// <summary>Seeds one standalone food (per-100g calories) scheduled into a snack slot.</summary>
        private static async Task<ApplicationDbContext> SeedAsync(
            decimal kcalPer100g, decimal? referenceServingGrams, decimal? quantity)
        {
            var ctx = NewContext();

            // MealPlan's Household/Author/MealType navigations are required and each principal
            // carries a soft-delete query filter, so the row is filtered out unless they exist.
            ctx.Set<HouseholdEntity>().Add(new HouseholdEntity { Id = HouseholdId, Name = "Test Household" });
            ctx.Set<Nom.Data.Person.PersonEntity>().Add(new Nom.Data.Person.PersonEntity { Id = Author, Name = "Tester" });
            ctx.Set<Nom.Data.Reference.ReferenceEntity>().Add(
                new Nom.Data.Reference.ReferenceEntity { Id = Snacks, Name = "Snacks" });

            var calories = new NutrientEntity { Name = "Calories" };
            ctx.Set<NutrientEntity>().Add(calories);
            await ctx.SaveChangesAsync();

            var food = new IngredientEntity
            {
                Name = "Test Food",
                CurationStatusId = 9003,
                ReferenceServingGrams = referenceServingGrams,
                IngredientNutrients = new List<IngredientNutrientEntity>
                {
                    new() { NutrientId = calories.Id, Nutrient = calories, Amount = kcalPer100g },
                },
            };
            ctx.Ingredients.Add(food);
            await ctx.SaveChangesAsync();

            ctx.MealPlans.Add(new MealPlanEntity
            {
                HouseholdId = HouseholdId,
                AuthorId = Author,
                Date = new DateOnly(2026, 8, 17), // Monday
                MealTypeId = Snacks,
                IngredientId = food.Id,
                Quantity = quantity,
            });
            await ctx.SaveChangesAsync();
            return ctx;
        }

        private static async Task<decimal?> CaloriesShownAsync(ApplicationDbContext ctx)
        {
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var week = await svc.GetWeekAsync(HouseholdId, new DateOnly(2026, 8, 17));
            var entry = week.Days.SelectMany(d => d.Cells).SelectMany(c => c.Entries).Single();
            return entry.Calories;
        }

        [Fact]
        public async Task ScalesByReferenceServing_WhenFoodHasOne()
        {
            // Milk 2%: 50 kcal/100g, one cup = 227 g → 113.5 kcal.
            using var ctx = await SeedAsync(kcalPer100g: 50m, referenceServingGrams: 227m, quantity: 1m);
            (await CaloriesShownAsync(ctx)).Should().BeApproximately(113.5m, 0.01m);
        }

        [Fact]
        public async Task MultipliesByQuantity()
        {
            using var ctx = await SeedAsync(kcalPer100g: 50m, referenceServingGrams: 227m, quantity: 2m);
            (await CaloriesShownAsync(ctx)).Should().BeApproximately(227m, 0.01m);
        }

        [Fact]
        public async Task FallsBackTo100gBasis_WhenNoReferenceServing()
        {
            // No reference portion → quantity counts 100 g units.
            using var ctx = await SeedAsync(kcalPer100g: 89m, referenceServingGrams: null, quantity: 1m);
            (await CaloriesShownAsync(ctx)).Should().BeApproximately(89m, 0.01m);
        }

        [Fact]
        public async Task TreatsZeroReferenceServing_As100gBasis()
        {
            // A 0 g reference would zero out nutrition — guard falls back to the 100 g basis.
            using var ctx = await SeedAsync(kcalPer100g: 89m, referenceServingGrams: 0m, quantity: 1m);
            (await CaloriesShownAsync(ctx)).Should().BeApproximately(89m, 0.01m);
        }

        [Fact]
        public async Task DefaultsQuantityToOne_WhenNull()
        {
            using var ctx = await SeedAsync(kcalPer100g: 40m, referenceServingGrams: 150m, quantity: null);
            (await CaloriesShownAsync(ctx)).Should().BeApproximately(60m, 0.01m);
        }
    }
}
