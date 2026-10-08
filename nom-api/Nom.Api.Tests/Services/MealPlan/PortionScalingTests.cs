using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Week-view nutrition totals are the viewer's calorie-scaled portion: one plate is one
    /// serving of each recipe in the cell, scaled to the viewer's daily target × the meal split.
    /// Standalone foods keep their stated quantity.
    /// </summary>
    public class PortionScalingTests
    {
        private const long HouseholdId = 100;
        private const long PersonId = 10;
        private const long Dinner = 1102;

        private static async Task<ApplicationDbContext> SeedAsync(decimal? personCalories, decimal? householdCalories)
        {
            var ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            ctx.Set<HouseholdEntity>().Add(new HouseholdEntity { Id = HouseholdId, Name = "Test Household" });
            ctx.Set<Nom.Data.Person.PersonEntity>().Add(new Nom.Data.Person.PersonEntity { Id = PersonId, Name = "Pat" });
            ctx.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId });
            if (personCalories.HasValue)
                ctx.MacroGoals.Add(new MacroGoalEntity { PersonId = PersonId, CaloriesTarget = personCalories });
            if (householdCalories.HasValue)
                ctx.MacroGoals.Add(new MacroGoalEntity { HouseholdId = HouseholdId, CaloriesTarget = householdCalories });
            await ctx.SaveChangesAsync();
            return ctx;
        }

        private static MealPlanWeekResponseModel Week(params MealPlanEntryModel[] entries) => new()
        {
            Days = new List<MealPlanDayModel>
            {
                new()
                {
                    Cells = new List<MealPlanCellModel>
                    {
                        new()
                        {
                            MealTypeId = Dinner,
                            Entries = entries.ToList(),
                            TotalCalories = entries.Sum(e => e.Calories ?? 0),
                            TotalCarbGrams = entries.Sum(e => e.CarbGrams ?? 0),
                        },
                    },
                },
            },
        };

        private static PortionOrchestrationService Service(ApplicationDbContext ctx) =>
            new(ctx, NullLogger<PortionOrchestrationService>.Instance);

        [Fact]
        public async Task ScalesRecipeCellToHouseholdTarget()
        {
            using var ctx = await SeedAsync(personCalories: null, householdCalories: 1600m);
            var week = Week(
                new MealPlanEntryModel { RecipeId = 1, Calories = 500m, CarbGrams = 60m },
                new MealPlanEntryModel { RecipeId = 2, Calories = 300m, CarbGrams = 20m });

            await Service(ctx).ScaleWeekToPersonAsync(HouseholdId, PersonId, week);

            var cell = week.Days[0].Cells[0];
            cell.PortionPlates.Should().Be(0.7m);
            cell.TotalCalories.Should().Be(560m);
            cell.TotalCarbGrams.Should().Be(56m);
        }

        [Fact]
        public async Task PersonGoalWinsAndStandaloneFoodIsNotScaled()
        {
            using var ctx = await SeedAsync(personCalories: 2000m, householdCalories: 1600m);
            var week = Week(
                new MealPlanEntryModel { RecipeId = 1, Calories = 700m, CarbGrams = 70m },
                new MealPlanEntryModel { IngredientId = 5, Calories = 100m, CarbGrams = 25m });

            await Service(ctx).ScaleWeekToPersonAsync(HouseholdId, PersonId, week);

            var cell = week.Days[0].Cells[0];
            cell.PortionPlates.Should().Be(1m);
            cell.TotalCalories.Should().Be(800m);
            cell.TotalCarbGrams.Should().Be(95m);
        }

        [Fact]
        public async Task LeavesTotalsAloneForNonMember()
        {
            using var ctx = await SeedAsync(personCalories: null, householdCalories: 1600m);
            var week = Week(new MealPlanEntryModel { RecipeId = 1, Calories = 500m, CarbGrams = 60m });

            await Service(ctx).ScaleWeekToPersonAsync(HouseholdId, PersonId + 1, week);

            var cell = week.Days[0].Cells[0];
            cell.PortionPlates.Should().BeNull();
            cell.TotalCalories.Should().Be(500m);
        }
    }
}
