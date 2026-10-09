using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Desserts are their own course, not snacks. Meal plans include a daily dessert only when
    /// the household opts in; the default plan keeps its four slots.
    /// </summary>
    public class DessertSlotTests
    {
        private const long HouseholdId = 21;

        private sealed class FakePolicy : IPolicyEnforcementService
        {
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(false);
            public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new List<long>());
            public Task<List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(new List<long>());
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static PortionOrchestrationService Portions(ApplicationDbContext db) =>
            new(db, NullLogger<PortionOrchestrationService>.Instance);

        [Fact]
        public async Task Desserts_are_off_by_default_and_the_week_has_four_slots()
        {
            using var db = NewContext();

            (await Portions(db).GetMealPlanOptionsAsync(HouseholdId)).IncludeDessert.Should().BeFalse();
            var week = await new MealPlanOrchestrationService(db, new FakePolicy()).GetWeekAsync(HouseholdId, new DateOnly(2026, 10, 12));
            week.Days[0].Cells.Select(c => c.MealTypeId).Should().Equal(1100, 1101, 1102, 1103);
        }

        [Fact]
        public async Task Opting_in_adds_a_dessert_row_and_opting_out_removes_it()
        {
            using var db = NewContext();
            var portions = Portions(db);
            var planner = new MealPlanOrchestrationService(db, new FakePolicy());

            await portions.SaveMealPlanOptionsAsync(HouseholdId, new MealPlanOptionsModel { IncludeDessert = true });
            (await planner.GetWeekAsync(HouseholdId, new DateOnly(2026, 10, 12))).Days[0].Cells
                .Select(c => c.MealTypeId).Should().Equal(1100, 1101, 1102, 1103, MealPlanOrchestrationService.DessertMealTypeId);

            await portions.SaveMealPlanOptionsAsync(HouseholdId, new MealPlanOptionsModel { IncludeDessert = false });
            (await planner.GetWeekAsync(HouseholdId, new DateOnly(2026, 10, 12))).Days[0].Cells.Should().HaveCount(4);
        }

        [Fact]
        public async Task Meal_split_carries_a_dessert_share_that_defaults_to_zero()
        {
            using var db = NewContext();
            var portions = Portions(db);

            var defaults = await portions.GetMealSplitAsync(HouseholdId);
            defaults.DessertPct.Should().Be(0);
            defaults.Total.Should().Be(100);

            var saved = await portions.SaveMealSplitAsync(HouseholdId,
                new MealSplitModel { BreakfastPct = 25, LunchPct = 30, DinnerPct = 35, SnacksPct = 5, DessertPct = 5 });
            saved.DessertPct.Should().Be(5);
            (await portions.GetMealSplitAsync(HouseholdId)).DessertPct.Should().Be(5);

            await FluentActions.Awaiting(() => portions.SaveMealSplitAsync(HouseholdId,
                new MealSplitModel { BreakfastPct = 25, LunchPct = 30, DinnerPct = 35, SnacksPct = 10, DessertPct = 5 }))
                .Should().ThrowAsync<ArgumentException>("the dessert share counts toward the 100%");
        }
    }
}
