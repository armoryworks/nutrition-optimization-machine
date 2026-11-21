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
using Nom.Orch.Models.Plan;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Plan setup can give a meal to prepared meals (owner answer 2026-11-21, third round, 6). Shuffle then fills that
    /// meal with one curated prepared food of the meal categories, chosen by the same restriction, diet, lock and
    /// nutrient scoring as recipes, varied across the week. A household that has not chosen it gets none, and a
    /// prepared meal counts as one item to buy for each member who eats the meal.
    /// </summary>
    public class PreparedPlanSlotTests
    {
        private const long HouseholdId = 400;
        private const long PersonId = 40;
        private const long SecondPersonId = 41;
        private const long Breakfast = 1100, Lunch = 1101, Dinner = 1102, Snacks = 1103;
        private const long NutAllergyRef = 2012, VegetarianRef = 2004;
        private static readonly DateOnly Monday = new(2026, 10, 12);

        private sealed class FakePolicy : IPolicyEnforcementService
        {
            public bool CuratedOnly { get; init; }
            public List<long> Locked { get; init; } = new();
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(CuratedOnly);
            public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new List<long>());
            public Task<List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(Locked);
        }

        private static readonly NutrientEntity Calories = new() { Id = 1, Name = "Calories" };

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            db.Set<HouseholdEntity>().Add(new HouseholdEntity { Id = HouseholdId, Name = "Prepared kitchen" });
            db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = PersonId, Name = "Cook" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Nutrients.Add(new NutrientEntity { Id = Calories.Id, Name = Calories.Name });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = NutAllergyRef, Name = "Nut Allergy" },
                new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" },
                new ReferenceEntity { Id = Breakfast, Name = "Breakfast" },
                new ReferenceEntity { Id = Lunch, Name = "Lunch" },
                new ReferenceEntity { Id = Dinner, Name = "Dinner" },
                new ReferenceEntity { Id = Snacks, Name = "Snacks" });

            var types = new[] { 3100L, 3101L, 3102L, 3103L, 3104L }
                .ToDictionary(id => id, id => new ReferenceEntity { Id = id, Name = $"Type {id}" });
            db.Set<ReferenceEntity>().AddRange(types.Values);
            var nextId = 5000L;
            foreach (var (mealTypeId, recipeTypeIds) in new[]
            {
                (Breakfast, new[] { 3101L, 3103L }),
                (Lunch, new[] { 3100L, 3101L, 3102L }),
                (Dinner, new[] { 3100L, 3101L, 3102L }),
                (Snacks, new[] { 3104L }),
            })
            {
                foreach (var recipeTypeId in recipeTypeIds)
                {
                    for (var copy = 0; copy < 3; copy++)
                    {
                        var recipe = new RecipeEntity
                        {
                            Id = nextId++,
                            Name = $"Recipe {mealTypeId}/{recipeTypeId} #{copy}",
                            AuthorId = PersonId,
                            Visibility = RecipeVisibilityEnum.Public,
                            CurationStatusId = (long)CurationStatusEnum.Curated,
                            RecipeTypes = new List<ReferenceEntity> { types[recipeTypeId] },
                        };
                        db.Recipes.Add(recipe);
                        db.Set<RecipeCategoryEntity>().Add(new RecipeCategoryEntity { RecipeId = recipe.Id, CategoryId = mealTypeId });
                        db.RecipeNutrition.Add(new RecipeNutritionEntity { RecipeId = recipe.Id, NutrientId = Calories.Id, Amount = 250m });
                    }
                }
            }

            await db.SaveChangesAsync();
            return db;
        }

        private static IngredientEntity Prepared(ApplicationDbContext db, string name, int category = 22, decimal kcalPer100g = 150m,
            decimal? servingGrams = 300m, long status = (long)CurationStatusEnum.Curated)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = status, FdcCategoryId = category, ReferenceServingGrams = servingGrams };
            db.Ingredients.Add(food);
            db.IngredientNutrients.Add(new IngredientNutrientEntity { Ingredient = food, NutrientId = Calories.Id, Amount = kcalPer100g, MeasurementId = 1 });
            return food;
        }

        private static List<IngredientEntity> Entrees(ApplicationDbContext db, int count) =>
            Enumerable.Range(1, count).Select(i => Prepared(db, $"Entree, frozen dinner no. {i}")).ToList();

        private static PortionOrchestrationService Portions(ApplicationDbContext db) =>
            new(db, NullLogger<PortionOrchestrationService>.Instance);

        private static async Task ChooseAsync(ApplicationDbContext db, params long[] mealTypeIds) =>
            await Portions(db).SaveMealPlanOptionsAsync(HouseholdId, new MealPlanOptionsModel { PreparedMealTypeIds = mealTypeIds.ToList() });

        private static async Task<MealPlanShuffleResponseModel> ShuffleWeekAsync(ApplicationDbContext db, FakePolicy? policy = null) =>
            await new MealPlanOrchestrationService(db, policy ?? new FakePolicy()).ShuffleMealPlansAsync(new MealPlanShuffleModel
            {
                HouseholdId = HouseholdId,
                StartDate = Monday,
                EndDate = Monday.AddDays(6),
                ReplaceExisting = true,
            }, PersonId);

        private static async Task<List<MealPlanEntity>> PlannedAsync(ApplicationDbContext db, long mealTypeId) =>
            await db.MealPlans.Where(mp => mp.HouseholdId == HouseholdId && mp.MealTypeId == mealTypeId).OrderBy(mp => mp.Date).ToListAsync();

        [Fact]
        public async Task Plan_setup_stores_the_meals_given_to_prepared_meals_and_none_by_default()
        {
            using var db = await SeedAsync();
            var portions = Portions(db);

            (await portions.GetMealPlanOptionsAsync(HouseholdId)).PreparedMealTypeIds.Should().BeEmpty();

            var saved = await portions.SaveMealPlanOptionsAsync(HouseholdId, new MealPlanOptionsModel { PreparedMealTypeIds = new List<long> { Lunch, Lunch, Snacks } });
            saved.PreparedMealTypeIds.Should().Equal(Lunch, Snacks);

            await portions.SaveMealPlanOptionsAsync(HouseholdId, new MealPlanOptionsModel { IncludeDessert = true });
            var options = await portions.GetMealPlanOptionsAsync(HouseholdId);
            options.IncludeDessert.Should().BeTrue();
            options.PreparedMealTypeIds.Should().Equal(new[] { Lunch, Snacks }, "a save that does not send the choice leaves it as it is");

            await FluentActions.Awaiting(() => portions.SaveMealPlanOptionsAsync(HouseholdId, new MealPlanOptionsModel { PreparedMealTypeIds = new List<long> { 3101 } }))
                .Should().ThrowAsync<ArgumentException>("only the plan's meals can be given to prepared meals");
        }

        [Fact]
        public async Task A_household_that_has_not_chosen_prepared_meals_gets_none_from_shuffle()
        {
            using var db = await SeedAsync();
            Entrees(db, 10);
            await db.SaveChangesAsync();

            var response = await ShuffleWeekAsync(db);

            (await db.MealPlans.CountAsync(mp => mp.IngredientId != null)).Should().Be(0);
            (await PlannedAsync(db, Lunch)).Should().HaveCount(21, "lunch keeps its three recipe courses each day");
            response.PreparedMealsHeld.Should().BeNull();
        }

        [Fact]
        public async Task A_meal_given_to_prepared_meals_gets_one_prepared_meal_a_day_varied_across_the_week()
        {
            using var db = await SeedAsync();
            var entrees = Entrees(db, 10);
            await db.SaveChangesAsync();
            await ChooseAsync(db, Lunch);

            var response = await ShuffleWeekAsync(db);

            var lunches = await PlannedAsync(db, Lunch);
            lunches.Should().HaveCount(7, "a prepared meal is the whole meal, one a day");
            lunches.Should().OnlyContain(l => l.RecipeId == null && l.Quantity == 1m && entrees.Select(e => e.Id).Contains(l.IngredientId!.Value));
            lunches.Select(l => l.IngredientId).Distinct().Should().HaveCount(7, "no prepared meal repeats while unused ones remain");
            lunches.Select(l => l.Title).Should().OnlyContain(t => t!.StartsWith("Entree, frozen dinner"));
            (await PlannedAsync(db, Dinner)).Should().OnlyContain(d => d.RecipeId != null, "only the chosen meal is given to prepared meals");
            (await PlannedAsync(db, Breakfast)).Should().OnlyContain(d => d.RecipeId != null);
            response.PreparedMealsHeld.Should().BeNull();
            response.Week.Days.SelectMany(d => d.Cells).Where(c => c.MealTypeId == Lunch).SelectMany(c => c.Entries)
                .Should().OnlyContain(e => e.IsPrepared);
        }

        [Fact]
        public async Task Shuffle_draws_only_curated_prepared_meals_of_the_meal_categories_never_baby_food_or_another_food()
        {
            using var db = await SeedAsync();
            var burger = Prepared(db, "Fast foods, hamburger", category: 21);
            var restaurant = Prepared(db, "Restaurant, Chinese, fried rice", category: 25);
            var entree = Prepared(db, "Lasagna, cheese, frozen", category: 22);
            Prepared(db, "Babyfood, dinner, vegetables and chicken", category: 3);
            Prepared(db, "Pizza, frozen, pending", category: 22, status: (long)CurationStatusEnum.PendingCuration);
            Prepared(db, "Chicken, broiler, roasted", category: 5);
            await db.SaveChangesAsync();
            await ChooseAsync(db, Lunch);

            await ShuffleWeekAsync(db);

            (await PlannedAsync(db, Lunch)).Select(l => l.IngredientId!.Value).Distinct()
                .Should().BeEquivalentTo(new[] { burger.Id, restaurant.Id, entree.Id }, "only these three are curated meals of categories 21, 22 and 25");
        }

        [Fact]
        public async Task Diets_restrictions_and_locks_keep_their_foods_out_exactly_as_they_keep_recipe_foods_out()
        {
            using var db = await SeedAsync();
            var tenders = Prepared(db, "Fast foods, chicken tenders", category: 21);
            var disliked = Prepared(db, "Macaroni and cheese, frozen entree");
            var locked = Prepared(db, "Restaurant, Mexican, cheese enchilada", category: 25);
            var allowed = Prepared(db, "Lasagna, vegetable, frozen");
            await db.SaveChangesAsync();
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No mac and cheese", IngredientId = disliked.Id, Severity = 2 });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await ChooseAsync(db, Lunch);

            var response = await ShuffleWeekAsync(db, new FakePolicy { Locked = new List<long> { locked.Id } });

            var lunches = await PlannedAsync(db, Lunch);
            lunches.Should().HaveCount(7);
            lunches.Should().OnlyContain(l => l.IngredientId == allowed.Id,
                "the vegetarian diet's %chicken% keeps the tenders out, the direct restriction the macaroni and the lock the enchilada; with one meal left it repeats");
            response.PreparedMealsHeld.Should().BeNull();
        }

        [Fact]
        public async Task An_allergy_or_medical_restriction_holds_prepared_meals_back_and_the_meal_gets_recipes()
        {
            using var db = await SeedAsync();
            Entrees(db, 10);
            await db.SaveChangesAsync();
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Nut Allergy", RestrictionTypeId = NutAllergyRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await ChooseAsync(db, Lunch);

            var response = await ShuffleWeekAsync(db);

            response.PreparedMealsHeld.Should().Be(MealPlanOrchestrationService.PreparedMealsHeldReasons.SevereRestriction,
                "a prepared meal lists no ingredients, so the severe gate cannot show it is free of the allergen");
            (await db.MealPlans.CountAsync(mp => mp.IngredientId != null)).Should().Be(0);
            (await PlannedAsync(db, Lunch)).Should().HaveCount(21).And.OnlyContain(l => l.RecipeId != null);
        }

        [Fact]
        public async Task A_curated_only_member_or_an_empty_pool_falls_back_to_recipes_and_says_why()
        {
            using var db = await SeedAsync();
            Entrees(db, 10);
            await db.SaveChangesAsync();
            await ChooseAsync(db, Lunch);

            var curatedOnly = await ShuffleWeekAsync(db, new FakePolicy { CuratedOnly = true });
            curatedOnly.PreparedMealsHeld.Should().Be(MealPlanOrchestrationService.PreparedMealsHeldReasons.CuratedOnly);
            (await db.MealPlans.CountAsync(mp => mp.IngredientId != null)).Should().Be(0);

            using var empty = await SeedAsync();
            Prepared(empty, "Babyfood, dinner, macaroni and cheese", category: 3);
            await empty.SaveChangesAsync();
            await ChooseAsync(empty, Lunch);

            var none = await ShuffleWeekAsync(empty);
            none.PreparedMealsHeld.Should().Be(MealPlanOrchestrationService.PreparedMealsHeldReasons.NoneAvailable);
            (await PlannedAsync(empty, Lunch)).Should().HaveCount(21).And.OnlyContain(l => l.RecipeId != null);
        }

        [Fact]
        public async Task Prepared_meals_are_scored_against_the_households_calorie_target_like_recipes()
        {
            using var db = await SeedAsync();
            var sensible = Enumerable.Range(1, 8).Select(i => Prepared(db, $"Entree, sensible no. {i}", kcalPer100g: 200m, servingGrams: 300m)).ToList();
            var huge = Enumerable.Range(1, 8).Select(i => Prepared(db, $"Entree, family tray no. {i}", kcalPer100g: 300m, servingGrams: 1000m)).ToList();
            db.MacroGoals.Add(new MacroGoalEntity { HouseholdId = HouseholdId, CaloriesTarget = 2000m });
            await db.SaveChangesAsync();
            await ChooseAsync(db, Lunch);

            await ShuffleWeekAsync(db);

            var lunches = await PlannedAsync(db, Lunch);
            lunches.Should().HaveCount(7);
            lunches.Should().OnlyContain(l => sensible.Select(s => s.Id).Contains(l.IngredientId!.Value),
                "a 600 kcal serving fits a 2,000 kcal day after a 500 kcal breakfast; a 3,000 kcal serving overshoots it");
            huge.Should().NotBeEmpty();
        }

        [Fact]
        public async Task Filling_only_empty_cells_keeps_a_planned_lunch_and_gives_prepared_meals_to_the_rest()
        {
            using var db = await SeedAsync();
            Entrees(db, 10);
            var kept = new MealPlanEntity { HouseholdId = HouseholdId, AuthorId = PersonId, Date = Monday, MealTypeId = Lunch, RecipeId = 5003 };
            db.MealPlans.Add(kept);
            await db.SaveChangesAsync();
            await ChooseAsync(db, Lunch);

            await new MealPlanOrchestrationService(db, new FakePolicy()).ShuffleMealPlansAsync(new MealPlanShuffleModel
            {
                HouseholdId = HouseholdId, StartDate = Monday, EndDate = Monday.AddDays(6), ReplaceExisting = false,
            }, PersonId);

            var lunches = await PlannedAsync(db, Lunch);
            lunches.Where(l => l.Date == Monday).Should().ContainSingle().Which.RecipeId.Should().Be(5003);
            lunches.Where(l => l.Date != Monday).Should().HaveCount(6).And.OnlyContain(l => l.IngredientId != null);
        }

        [Fact]
        public async Task The_week_says_how_many_of_a_prepared_meal_to_buy_for_the_members_who_eat_it()
        {
            using var db = await SeedAsync();
            db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = SecondPersonId, Name = "Partner" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = SecondPersonId, IsActive = true });
            var entree = Prepared(db, "Lasagna, cheese, frozen");
            db.MealPlans.AddRange(
                new MealPlanEntity { HouseholdId = HouseholdId, AuthorId = PersonId, Date = Monday, MealTypeId = Lunch, IngredientId = entree.Id, Quantity = 1m },
                new MealPlanEntity { HouseholdId = HouseholdId, AuthorId = PersonId, Date = Monday.AddDays(1), MealTypeId = Lunch, IngredientId = entree.Id, Quantity = 2m },
                new MealPlanEntity { HouseholdId = HouseholdId, AuthorId = PersonId, Date = Monday, MealTypeId = Dinner, RecipeId = 5010 });
            db.MealPlanExclusions.Add(new MealPlanExclusionEntity { HouseholdId = HouseholdId, PersonId = SecondPersonId, Date = Monday, MealTypeId = Lunch });
            await db.SaveChangesAsync();

            var week = await new MealPlanOrchestrationService(db, new FakePolicy()).GetWeekAsync(HouseholdId, Monday);

            var entries = week.Days.SelectMany(d => d.Cells.SelectMany(c => c.Entries.Select(e => (d.Date, c.MealTypeId, Entry: e)))).ToList();
            entries.Single(e => e.Date == Monday && e.MealTypeId == Lunch).Entry.ServingsToBuy.Should().Be(1m, "the partner skips Monday's lunch");
            entries.Single(e => e.Date == Monday.AddDays(1) && e.MealTypeId == Lunch).Entry.ServingsToBuy.Should().Be(4m, "two each for two members");
            entries.Single(e => e.MealTypeId == Dinner).Entry.ServingsToBuy.Should().BeNull("a recipe is bought by its ingredient lines");
        }
    }
}
