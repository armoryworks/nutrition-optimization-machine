using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Standalone whole-food scheduling, food-group requirement CRUD, and the
    /// food-group top-up that guarantees a household's minimum during shuffle.
    /// </summary>
    public class FoodGroupTests
    {
        private const long Author = 10;
        private const long HouseholdId = 100;
        private const long Vegetables = (long)FoodGroupEnum.Vegetables; // 3200
        private const long Dinner = 1102;
        private const long Snacks = 1103;
        private const long Curated = 9003; // CurationStatusEnum.Curated

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        /// <summary>Permissive policy; optionally reports locked ingredient ids.</summary>
        private sealed class FakePolicy : IPolicyEnforcementService
        {
            public List<long> Locked { get; init; } = new();
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(false);
            public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new List<long>());
            public Task<List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(Locked);
        }

        private static void AddActiveMember(ApplicationDbContext ctx) =>
            ctx.HouseholdMembers.Add(new HouseholdMemberEntity { PersonId = Author, HouseholdId = HouseholdId, IsActive = true });

        private static IngredientEntity Veg(ApplicationDbContext ctx, string name)
        {
            var ing = new IngredientEntity { Name = name, FoodGroupId = Vegetables, CurationStatusId = Curated };
            ctx.Ingredients.Add(ing);
            return ing;
        }

        // ---- Standalone scheduling ----

        [Fact]
        public async Task Create_WithIngredient_SchedulesStandaloneFood_DefaultsQuantityToOne()
        {
            using var ctx = NewContext();
            AddActiveMember(ctx);
            var apple = Veg(ctx, "Apple");
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());

            var res = await svc.CreateMealPlanAsync(new MealPlanCreateModel
            {
                HouseholdId = HouseholdId,
                Date = new DateOnly(2026, 8, 14),
                MealTypeId = Snacks,
                IngredientId = apple.Id,
            }, Author);

            var saved = await ctx.MealPlans.FindAsync(res.Id);
            saved!.IngredientId.Should().Be(apple.Id);
            saved.RecipeId.Should().BeNull();
            saved.Quantity.Should().Be(1m);
        }

        [Fact]
        public async Task Create_WithBothRecipeAndIngredient_Throws()
        {
            using var ctx = NewContext();
            AddActiveMember(ctx);
            var apple = Veg(ctx, "Apple");
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());

            var act = () => svc.CreateMealPlanAsync(new MealPlanCreateModel
            {
                HouseholdId = HouseholdId,
                Date = new DateOnly(2026, 8, 14),
                MealTypeId = Snacks,
                RecipeId = 5,
                IngredientId = apple.Id,
            }, Author);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Create_WithMissingIngredient_Throws()
        {
            using var ctx = NewContext();
            AddActiveMember(ctx);
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());

            var act = () => svc.CreateMealPlanAsync(new MealPlanCreateModel
            {
                HouseholdId = HouseholdId,
                Date = new DateOnly(2026, 8, 14),
                MealTypeId = Snacks,
                IngredientId = 999,
            }, Author);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("ingredient_not_found");
        }

        [Fact]
        public async Task Create_WithLockedIngredient_Throws()
        {
            using var ctx = NewContext();
            AddActiveMember(ctx);
            var beet = Veg(ctx, "Beet");
            await ctx.SaveChangesAsync();
            var policy = new FakePolicy();
            policy.Locked.Add(beet.Id);
            var svc = new MealPlanOrchestrationService(ctx, policy);

            var act = () => svc.CreateMealPlanAsync(new MealPlanCreateModel
            {
                HouseholdId = HouseholdId,
                Date = new DateOnly(2026, 8, 14),
                MealTypeId = Snacks,
                IngredientId = beet.Id,
            }, Author);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("locked_restriction");
        }

        // ---- Food-group rule CRUD ----

        [Fact]
        public async Task Upsert_CreatesThenUpdatesInPlace()
        {
            using var ctx = NewContext();
            ctx.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = Vegetables, Name = "Vegetables" });
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());

            var created = await svc.UpsertFoodGroupRuleAsync(new FoodGroupRuleUpsertModel
            {
                HouseholdId = HouseholdId,
                FoodGroupId = Vegetables,
                MinServings = 2,
                Timeframe = "PerDay",
            });
            created.FoodGroupName.Should().Be("Vegetables");
            created.MinServings.Should().Be(2);

            var updated = await svc.UpsertFoodGroupRuleAsync(new FoodGroupRuleUpsertModel
            {
                HouseholdId = HouseholdId,
                FoodGroupId = Vegetables,
                MinServings = 4,
                Timeframe = "PerDay",
            });

            updated.Id.Should().Be(created.Id); // same row, not a duplicate
            (await ctx.FoodGroupRules.CountAsync()).Should().Be(1);
            updated.MinServings.Should().Be(4);
        }

        [Fact]
        public async Task Upsert_WithNonPositiveServings_Throws()
        {
            using var ctx = NewContext();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var act = () => svc.UpsertFoodGroupRuleAsync(new FoodGroupRuleUpsertModel
            {
                HouseholdId = HouseholdId, FoodGroupId = Vegetables, MinServings = 0, Timeframe = "PerDay",
            });
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Delete_RemovesRule()
        {
            using var ctx = NewContext();
            ctx.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = Vegetables, Name = "Vegetables" });
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var rule = await svc.UpsertFoodGroupRuleAsync(new FoodGroupRuleUpsertModel
            {
                HouseholdId = HouseholdId, FoodGroupId = Vegetables, MinServings = 2, Timeframe = "PerDay",
            });

            (await svc.DeleteFoodGroupRuleAsync(rule.Id, HouseholdId + 1)).Should().BeFalse("another household's id must not delete it");
            (await svc.DeleteFoodGroupRuleAsync(rule.Id, HouseholdId)).Should().BeTrue();
            (await ctx.FoodGroupRules.CountAsync()).Should().Be(0);
        }

        // ---- Food-group top-up (the guarantee) ----

        [Fact]
        public async Task TopUp_AddsStandaloneWholeFoods_ToMeetPerDayMinimum()
        {
            using var ctx = NewContext();
            var v1 = Veg(ctx, "Carrot");
            var v2 = Veg(ctx, "Spinach");
            ctx.FoodGroupRules.Add(new FoodGroupRuleEntity
            {
                HouseholdId = HouseholdId, FoodGroupId = Vegetables, MinServings = 2,
                Timeframe = FoodGroupRuleTimeframe.PerDay, IsActive = true,
            });
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var date = new DateOnly(2026, 8, 14);

            var additions = await svc.ApplyFoodGroupRulesAsync(
                HouseholdId, Author, date, date, new List<MealPlanEntity>(), new List<long>(), DateTime.UtcNow);

            additions.Should().HaveCount(2);
            additions.Should().OnlyContain(a => a.IngredientId == v1.Id || a.IngredientId == v2.Id);
            additions.Should().OnlyContain(a => a.MealTypeId == Snacks && a.Quantity == 1m);
        }

        [Fact]
        public async Task TopUp_CountsRecipeContainingGroup_AsAServing()
        {
            using var ctx = NewContext();
            var vegIngredient = Veg(ctx, "Broccoli");
            Veg(ctx, "Kale");
            var recipe = new RecipeEntity
            {
                Name = "Stir Fry",
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { Ingredient = vegIngredient, Quantity = 1 },
                },
            };
            ctx.Recipes.Add(recipe);
            ctx.FoodGroupRules.Add(new FoodGroupRuleEntity
            {
                HouseholdId = HouseholdId, FoodGroupId = Vegetables, MinServings = 2,
                Timeframe = FoodGroupRuleTimeframe.PerDay, IsActive = true,
            });
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var date = new DateOnly(2026, 8, 14);

            // A placed recipe supplying vegetables covers 1 of the 2 required servings.
            var placed = new List<MealPlanEntity>
            {
                new() { HouseholdId = HouseholdId, AuthorId = Author, Date = date, MealTypeId = Dinner, RecipeId = recipe.Id },
            };

            var additions = await svc.ApplyFoodGroupRulesAsync(
                HouseholdId, Author, date, date, placed, new List<long>(), DateTime.UtcNow);

            // Recipe supplied 1 of the 2 required servings, so exactly one whole-food top-up,
            // and it is a curated vegetable from the candidate pool.
            additions.Should().HaveCount(1);
            var addedFoodGroup = ctx.Ingredients.Single(i => i.Id == additions[0].IngredientId).FoodGroupId;
            addedFoodGroup.Should().Be(Vegetables);
        }

        [Fact]
        public async Task TopUp_SkipsRestrictedCandidates()
        {
            using var ctx = NewContext();
            var restricted = Veg(ctx, "Celery");
            ctx.FoodGroupRules.Add(new FoodGroupRuleEntity
            {
                HouseholdId = HouseholdId, FoodGroupId = Vegetables, MinServings = 1,
                Timeframe = FoodGroupRuleTimeframe.PerDay, IsActive = true,
            });
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var date = new DateOnly(2026, 8, 14);

            // The only vegetable candidate is restricted → nothing safe to add.
            var additions = await svc.ApplyFoodGroupRulesAsync(
                HouseholdId, Author, date, date, new List<MealPlanEntity>(),
                new List<long> { restricted.Id }, DateTime.UtcNow);

            additions.Should().BeEmpty();
        }

        [Fact]
        public async Task TopUp_NeverSchedulesAnAlcoholicDrinkOrARecipeOnlyIngredient()
        {
            using var ctx = NewContext();
            const long Beverages = (long)FoodGroupEnum.Beverages;
            var alcohol = new Nom.Data.Nutrient.NutrientEntity { Name = "Alcohol" };
            ctx.Set<Nom.Data.Nutrient.NutrientEntity>().Add(alcohol);
            var vodka = new IngredientEntity { Name = "Alcoholic beverage, distilled, vodka, 80 proof", FoodGroupId = Beverages, CurationStatusId = Curated, IsWholeFood = true };
            var stock = new IngredientEntity { Name = "Beverages, coffee, instant, powder", FoodGroupId = Beverages, CurationStatusId = Curated, IsWholeFood = false };
            var kombucha = new IngredientEntity { Name = "Kombucha", FoodGroupId = Beverages, CurationStatusId = Curated };
            var milk = new IngredientEntity { Name = "Beverages, almond milk, unsweetened", FoodGroupId = Beverages, CurationStatusId = Curated, IsWholeFood = true };
            ctx.Ingredients.AddRange(vodka, stock, kombucha, milk);
            await ctx.SaveChangesAsync();
            ctx.Set<Nom.Data.Nutrient.IngredientNutrientEntity>().AddRange(
                new Nom.Data.Nutrient.IngredientNutrientEntity { IngredientId = vodka.Id, NutrientId = alcohol.Id, Amount = 33.4m },
                new Nom.Data.Nutrient.IngredientNutrientEntity { IngredientId = kombucha.Id, NutrientId = alcohol.Id, Amount = 0.5m });
            ctx.FoodGroupRules.Add(new FoodGroupRuleEntity
            {
                HouseholdId = HouseholdId, FoodGroupId = Beverages, MinServings = 4,
                Timeframe = FoodGroupRuleTimeframe.PerDay, IsActive = true,
            });
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var date = new DateOnly(2026, 8, 14);

            var additions = await svc.ApplyFoodGroupRulesAsync(
                HouseholdId, Author, date, date, new List<MealPlanEntity>(), new List<long>(), DateTime.UtcNow);

            additions.Select(a => a.IngredientId).Should().NotContain(new long?[] { vodka.Id, stock.Id },
                "a spirit admitted to Beverages and a recipe-only ingredient are never a standalone serving");
            additions.Select(a => a.IngredientId).Distinct().Should().BeEquivalentTo(new long?[] { kombucha.Id, milk.Id },
                "0.5 g of alcohol per 100 g or less is not a drink's alcohol, and an unknown edibility is not excluded");
        }

        [Fact]
        public async Task A_prepared_food_is_never_a_top_up_and_never_counts_toward_a_minimum()
        {
            using var ctx = NewContext();
            var babyCarrots = new IngredientEntity { Name = "Babyfood, vegetables, carrots, strained", FoodGroupId = Vegetables, CurationStatusId = Curated, IsWholeFood = true, FdcCategoryId = 3 };
            var carrot = new IngredientEntity { Name = "Carrots, raw", FoodGroupId = Vegetables, CurationStatusId = Curated, IsWholeFood = true, FdcCategoryId = 11 };
            ctx.Ingredients.AddRange(babyCarrots, carrot);
            var recipe = new RecipeEntity { Name = "Puree", CurationStatusId = Curated };
            ctx.Recipes.Add(recipe);
            await ctx.SaveChangesAsync();
            ctx.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = babyCarrots.Id, SortOrder = 0, Quantity = 1, MeasurementId = 1 });
            ctx.FoodGroupRules.Add(new FoodGroupRuleEntity
            {
                HouseholdId = HouseholdId, FoodGroupId = Vegetables, MinServings = 2,
                Timeframe = FoodGroupRuleTimeframe.PerDay, IsActive = true,
            });
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var date = new DateOnly(2026, 8, 14);
            var placed = new List<MealPlanEntity>
            {
                new() { HouseholdId = HouseholdId, Date = date, MealTypeId = Dinner, RecipeId = recipe.Id },
                new() { HouseholdId = HouseholdId, Date = date, MealTypeId = Snacks, IngredientId = babyCarrots.Id, Quantity = 1m },
            };

            var additions = await svc.ApplyFoodGroupRulesAsync(HouseholdId, Author, date, date, placed, new List<long>(), DateTime.UtcNow);

            additions.Should().HaveCount(2, "neither the recipe's line on a prepared food nor the planned prepared food is a Vegetables serving");
            additions.Should().OnlyContain(a => a.IngredientId == carrot.Id, "a prepared food is never scheduled as a top-up");
        }

        [Fact]
        public async Task A_household_plans_a_prepared_food_as_a_food_of_its_own_and_sees_it_marked_prepared()
        {
            using var ctx = NewContext();
            AddActiveMember(ctx);
            ctx.Set<HouseholdEntity>().Add(new HouseholdEntity { Id = HouseholdId, Name = "Test Household" });
            ctx.Set<Nom.Data.Person.PersonEntity>().Add(new Nom.Data.Person.PersonEntity { Id = Author, Name = "Tester" });
            ctx.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = Dinner, Name = "Dinner" });
            var burger = new IngredientEntity { Name = "McDONALD'S, Hamburger", CurationStatusId = Curated, FdcCategoryId = 21 };
            ctx.Ingredients.Add(burger);
            await ctx.SaveChangesAsync();
            var svc = new MealPlanOrchestrationService(ctx, new FakePolicy());
            var date = new DateOnly(2026, 8, 14);

            var res = await svc.CreateMealPlanAsync(new MealPlanCreateModel
            {
                HouseholdId = HouseholdId, Date = date, MealTypeId = Dinner, IngredientId = burger.Id,
            }, Author);

            (await ctx.MealPlans.FindAsync(res.Id))!.IngredientId.Should().Be(burger.Id);
            var week = await svc.GetWeekAsync(HouseholdId, date);
            week.Days.SelectMany(d => d.Cells).SelectMany(c => c.Entries).Single(e => e.IngredientId == burger.Id).IsPrepared.Should().BeTrue();
        }
    }
}
