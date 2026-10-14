using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Shuffle steers toward the goals the eater saved on their profile, not only the household's.
    /// Portions scale every plate to the person's calories, so calories always land; what shuffle
    /// controls is each plate's macro mix, and a realistic, fat-heavy pool must still come out
    /// near the person's fat and carb goals.
    /// </summary>
    public class ShuffleMacroGoalTests
    {
        private const long HouseholdId = 300;
        private const long PersonId = 30;
        private const long SecondPersonId = 31;
        private static readonly DateOnly Monday = new(2026, 10, 12);

        private static readonly MacroGoalEntity LizGoal = new()
        {
            PersonId = PersonId, CaloriesTarget = 1900m, ProteinGramsTarget = 95m, CarbGramsTarget = 230m, FatGramsTarget = 65m,
        };

        private static readonly MacroGoalEntity KetoHouseholdGoal = new()
        {
            HouseholdId = HouseholdId, CaloriesTarget = 1900m, ProteinGramsTarget = 140m, CarbGramsTarget = 70m, FatGramsTarget = 125m,
        };

        private sealed class FakePolicy : IPolicyEnforcementService
        {
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(false);
            public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new List<long>());
            public Task<List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(new List<long>());
        }

        private static readonly (long MealTypeId, long[] RecipeTypeIds)[] Meals =
        {
            (1100L, new[] { 3101L, 3103L }),
            (1101L, new[] { 3100L, 3101L, 3102L }),
            (1102L, new[] { 3100L, 3101L, 3102L }),
            (1103L, new[] { 3104L }),
        };

        private static readonly (string Kind, decimal ProteinPct, decimal CarbPct, decimal FatPct)[] Kinds =
        {
            ("rich", 30m, 20m, 50m),
            ("creamy", 25m, 30m, 45m),
            ("hearty", 35m, 25m, 40m),
            ("balanced", 20m, 50m, 30m),
            ("grain", 15m, 60m, 25m),
        };

        private static async Task<ApplicationDbContext> SeedAsync(params MacroGoalEntity[] goals) => await SeedAsync(0, goals);

        private static async Task<ApplicationDbContext> SeedAsync(int unknownPerSlot, params MacroGoalEntity[] goals)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            db.Set<HouseholdEntity>().Add(new HouseholdEntity { Id = HouseholdId, Name = "Liz's kitchen" });
            db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = PersonId, Name = "Liz" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId });

            var calories = new NutrientEntity { Id = 1, Name = "Calories" };
            var protein = new NutrientEntity { Id = 2, Name = "Protein" };
            var carbs = new NutrientEntity { Id = 3, Name = "Total Carbohydrates" };
            var fat = new NutrientEntity { Id = 4, Name = "Fat" };
            db.Nutrients.AddRange(calories, protein, carbs, fat);

            var types = new[] { 3100L, 3101L, 3102L, 3103L, 3104L }
                .ToDictionary(id => id, id => new ReferenceEntity { Id = id, Name = $"Type {id}" });
            db.Set<ReferenceEntity>().AddRange(types.Values);
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" });
            db.Set<ReferenceEntity>().AddRange(Meals.Select(m => new ReferenceEntity { Id = m.MealTypeId, Name = $"Meal {m.MealTypeId}" }));

            var nextId = 1000L;
            foreach (var (mealTypeId, recipeTypeIds) in Meals)
            {
                foreach (var recipeTypeId in recipeTypeIds)
                {
                    for (var copy = 0; copy < 3; copy++)
                    {
                        foreach (var (kind, proteinPct, carbPct, fatPct) in Kinds)
                        {
                            var kcal = 300m + 100m * copy;
                            var recipe = new RecipeEntity
                            {
                                Id = nextId++,
                                Name = $"{kind} {mealTypeId}/{recipeTypeId} #{copy}",
                                AuthorId = PersonId,
                                Visibility = RecipeVisibilityEnum.Public,
                                CurationStatusId = (long)CurationStatusEnum.Curated,
                                RecipeTypes = new List<ReferenceEntity> { types[recipeTypeId] },
                            };
                            db.Recipes.Add(recipe);
                            db.Set<RecipeCategoryEntity>().Add(new RecipeCategoryEntity { RecipeId = recipe.Id, CategoryId = mealTypeId });
                            db.RecipeNutrition.AddRange(
                                new RecipeNutritionEntity { RecipeId = recipe.Id, NutrientId = calories.Id, Nutrient = calories, Amount = kcal },
                                new RecipeNutritionEntity { RecipeId = recipe.Id, NutrientId = protein.Id, Nutrient = protein, Amount = kcal * proteinPct / 100m / 4m },
                                new RecipeNutritionEntity { RecipeId = recipe.Id, NutrientId = carbs.Id, Nutrient = carbs, Amount = kcal * carbPct / 100m / 4m },
                                new RecipeNutritionEntity { RecipeId = recipe.Id, NutrientId = fat.Id, Nutrient = fat, Amount = kcal * fatPct / 100m / 9m });
                        }
                    }
                }
            }

            foreach (var (mealTypeId, recipeTypeIds) in Meals)
                foreach (var recipeTypeId in recipeTypeIds)
                    for (var u = 0; u < unknownPerSlot; u++)
                    {
                        var recipe = new RecipeEntity
                        {
                            Id = nextId++,
                            Name = $"unknown {mealTypeId}/{recipeTypeId} #{u}",
                            AuthorId = PersonId,
                            Visibility = RecipeVisibilityEnum.Public,
                            CurationStatusId = (long)CurationStatusEnum.Curated,
                            RecipeTypes = new List<ReferenceEntity> { types[recipeTypeId] },
                        };
                        db.Recipes.Add(recipe);
                        db.Set<RecipeCategoryEntity>().Add(new RecipeCategoryEntity { RecipeId = recipe.Id, CategoryId = mealTypeId });
                    }

            db.MacroGoals.AddRange(goals.Select(g => new MacroGoalEntity
            {
                PersonId = g.PersonId,
                HouseholdId = g.HouseholdId,
                CaloriesTarget = g.CaloriesTarget,
                ProteinGramsTarget = g.ProteinGramsTarget,
                CarbGramsTarget = g.CarbGramsTarget,
                FatGramsTarget = g.FatGramsTarget,
            }));
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<List<(decimal Calories, decimal Carbs, decimal Fat)>> ShuffleWeekForLizAsync(ApplicationDbContext db)
        {
            var planner = new MealPlanOrchestrationService(db, new FakePolicy());
            await planner.ShuffleMealPlansAsync(new MealPlanShuffleModel
            {
                HouseholdId = HouseholdId,
                StartDate = Monday,
                EndDate = Monday.AddDays(6),
                ReplaceExisting = true,
            }, PersonId);

            var week = await planner.GetWeekAsync(HouseholdId, Monday);
            await new PortionOrchestrationService(db, NullLogger<PortionOrchestrationService>.Instance)
                .ScaleWeekToPersonAsync(HouseholdId, PersonId, week);

            return week.Days.Select(d => (
                d.Cells.Sum(c => c.TotalCalories ?? 0m),
                d.Cells.Sum(c => c.TotalCarbGrams ?? 0m),
                d.Cells.Sum(c => c.TotalFatGrams ?? 0m))).ToList();
        }

        private static decimal PoolMeanFatPerKcal() => Kinds.Average(k => k.FatPct) / 100m / 9m;

        [Fact]
        public async Task Personal_goal_steers_a_fat_heavy_pool_to_the_persons_fat_and_carb_goals()
        {
            using var db = await SeedAsync(LizGoal);

            var days = await ShuffleWeekForLizAsync(db);

            days.Should().HaveCount(7);
            days.Should().OnlyContain(d => Math.Abs(d.Calories - 1900m) < 10m, "portions scale every plate to her calories");
            var blindFat = 1900m * PoolMeanFatPerKcal();
            blindFat.Should().BeGreaterThan(65m * 1.2m, "an unsteered pick from this pool runs well over her fat goal");
            days.Should().OnlyContain(d => d.Fat <= 65m * 1.1m && d.Fat >= 65m * 0.85m,
                "fat lands near her goal every day: " + string.Join(", ", days.Select(d => Math.Round(d.Fat))));
            days.Should().OnlyContain(d => d.Carbs >= 230m * 0.85m && d.Carbs <= 230m * 1.15m,
                "carbs land near her goal every day: " + string.Join(", ", days.Select(d => Math.Round(d.Carbs))));
        }

        [Fact]
        public async Task Recipes_without_nutrition_data_do_not_crowd_out_ones_that_steer_the_mix()
        {
            using var db = await SeedAsync(15, LizGoal);

            var days = await ShuffleWeekForLizAsync(db);

            var placed = await db.MealPlans.Include(m => m.Recipe).Where(m => m.RecipeId != null).ToListAsync();
            placed.Count(m => m.Recipe!.Name.StartsWith("unknown")).Should().BeLessThan(placed.Count / 5,
                "half the pool has no nutrition data, and an unknown plate must not outscore a known one on the mix");
            days.Average(d => d.Fat).Should().BeLessThan(1900m * PoolMeanFatPerKcal(),
                "the known half of the pool still steers fat below an unsteered pick");
        }

        [Fact]
        public async Task Personal_goal_wins_over_the_household_goal()
        {
            using var db = await SeedAsync(LizGoal, KetoHouseholdGoal);

            var days = await ShuffleWeekForLizAsync(db);

            days.Average(d => d.Fat).Should().BeLessThan(65m * 1.1m);
            days.Average(d => d.Carbs).Should().BeGreaterThan(230m * 0.85m);
        }

        [Fact]
        public async Task Household_goal_still_steers_a_member_without_a_goal_of_their_own()
        {
            using var db = await SeedAsync(KetoHouseholdGoal);

            var days = await ShuffleWeekForLizAsync(db);

            days.Average(d => d.Fat).Should().BeGreaterThan(1900m * PoolMeanFatPerKcal(),
                "the household's high-fat goal pulls the plan above the pool's average fat");
            days.Average(d => d.Carbs).Should().BeLessThan(140m);
        }

        [Fact]
        public void Members_share_one_plate_so_the_combined_mix_is_their_goals_over_their_calories()
        {
            var combined = PlanningMacroGoal.Combine(
                new MacroGoalEntity?[]
                {
                    new() { PersonId = PersonId, CaloriesTarget = 1500m, FatGramsTarget = 50m },
                    new() { PersonId = SecondPersonId, CaloriesTarget = 2500m },
                },
                new MacroGoalEntity { HouseholdId = HouseholdId, CaloriesTarget = 2000m, FatGramsTarget = 90m, CarbGramsTarget = 250m });

            combined.CaloriesTarget.Should().Be(2000m);
            combined.FatGramsTarget.Should().Be(70m);
            combined.FatPerKcal.Should().Be(140m / 4000m);
            combined.CarbPerKcal.Should().Be(500m / 4000m);
            combined.ProteinPerKcal.Should().BeNull();
        }

        [Fact]
        public void Solo_member_without_goals_falls_back_to_the_household_and_then_to_nothing()
        {
            PlanningMacroGoal.Combine(new MacroGoalEntity?[] { null }, KetoHouseholdGoal)
                .FatGramsTarget.Should().Be(125m);

            var none = PlanningMacroGoal.Combine(new MacroGoalEntity?[] { null }, null);
            none.HasAnyTarget.Should().BeFalse();
            none.FatPerKcal.Should().BeNull();
        }
    }
}
