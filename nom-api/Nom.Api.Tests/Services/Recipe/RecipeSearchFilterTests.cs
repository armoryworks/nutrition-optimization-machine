using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The search results page narrows by course, kitchen tool, total time and what the household's
    /// kitchen can make, and orders results by relevance, rating, speed or ingredient count.
    /// </summary>
    public class RecipeSearchFilterTests
    {
        private const long HouseholdId = 5;
        private const long Entree = 3101, Dessert = 3105;
        private const long Toast = 1, AirFriedWings = 2, SousVideSteak = 3, ChocolateCake = 4, ToastedSandwich = 5;

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Persons.Add(new PersonEntity { Id = 99, Name = "Author" });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" },
                new ReferenceEntity { Id = Entree, Name = "Entree" },
                new ReferenceEntity { Id = Dessert, Name = "Dessert" });
            await db.SaveChangesAsync();

            var entree = db.Set<ReferenceEntity>().Single(r => r.Id == Entree);
            var dessert = db.Set<ReferenceEntity>().Single(r => r.Id == Dessert);

            db.Recipes.AddRange(
                Recipe(Toast, "Toast", prep: 1, cook: 3, types: entree),
                Recipe(AirFriedWings, "Air Fried Wings", prep: 10, cook: 25, types: entree),
                Recipe(SousVideSteak, "Sous Vide Steak", prep: 5, cook: 120, types: entree),
                Recipe(ChocolateCake, "Chocolate Cake", prep: 20, cook: 35, types: dessert),
                Recipe(ToastedSandwich, "Cheese Sandwich, Toasted", prep: null, cook: null, types: entree));

            db.RecipeTools.AddRange(
                new RecipeToolEntity { RecipeId = AirFriedWings, ToolId = KitchenToolCatalog.AirFryer },
                new RecipeToolEntity { RecipeId = SousVideSteak, ToolId = KitchenToolCatalog.SousVide },
                new RecipeToolEntity { RecipeId = ChocolateCake, ToolId = KitchenToolCatalog.Oven },
                new RecipeToolEntity { RecipeId = ChocolateCake, ToolId = KitchenToolCatalog.StandMixer, Source = "ai:test" });

            db.Set<RecipeStepEntity>().Add(new RecipeStepEntity { RecipeId = Toast, StepNumber = 1, Description = "Toast the bread in a toaster oven." });

            db.Set<RecipeIngredientEntity>().AddRange(
                Enumerable.Range(0, 3).Select(i => new RecipeIngredientEntity { RecipeId = ChocolateCake, IngredientId = 100 + i })
                    .Concat(Enumerable.Range(0, 2).Select(i => new RecipeIngredientEntity { RecipeId = AirFriedWings, IngredientId = 200 + i }))
                    .Append(new RecipeIngredientEntity { RecipeId = Toast, IngredientId = 300 }));

            db.RecipeRatings.AddRange(
                new RecipeRatingEntity { RecipeId = ChocolateCake, RaterId = 99, Rating = 5 },
                new RecipeRatingEntity { RecipeId = ChocolateCake, RaterId = 98, Rating = 4 },
                new RecipeRatingEntity { RecipeId = AirFriedWings, RaterId = 99, Rating = 3 });

            await db.SaveChangesAsync();
            return db;
        }

        private static RecipeEntity Recipe(long id, string name, long? prep, long? cook, params ReferenceEntity[] types) => new()
        {
            Id = id,
            Name = name,
            PrepTimeMinutes = prep,
            CookTimeMinutes = cook,
            AuthorId = 99,
            Visibility = RecipeVisibilityEnum.Public,
            CurationStatusId = (long)CurationStatusEnum.Curated,
            RecipeTypes = types.ToList(),
        };

        private static async Task<List<long>> SearchIds(ApplicationDbContext db, RecipeSearchModel model)
        {
            model.IncludeIngredients = false;
            model.PageSize = 50;
            var service = new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance);
            var response = await service.SearchRecipesAsync(model);
            return response.Results.Select(r => (long)r.Id).ToList();
        }

        [Fact]
        public async Task Tool_filter_matches_recipes_using_any_selected_tool()
        {
            using var db = await SeedAsync();
            var ids = await SearchIds(db, new RecipeSearchModel { ToolIds = new() { KitchenToolCatalog.AirFryer, KitchenToolCatalog.SousVide } });
            ids.Should().BeEquivalentTo(new[] { AirFriedWings, SousVideSteak });
        }

        [Fact]
        public async Task Course_filter_matches_the_recipe_type()
        {
            using var db = await SeedAsync();
            (await SearchIds(db, new RecipeSearchModel { RecipeTypeIds = new() { Dessert } }))
                .Should().Equal(ChocolateCake);
        }

        [Fact]
        public async Task Max_total_time_counts_a_missing_part_as_zero_and_skips_recipes_with_no_time()
        {
            using var db = await SeedAsync();
            (await SearchIds(db, new RecipeSearchModel { MaxTotalTime = 40 }))
                .Should().BeEquivalentTo(new[] { Toast, AirFriedWings });
        }

        [Fact]
        public async Task Cookable_filter_drops_recipes_needing_a_tool_the_kitchen_lacks()
        {
            using var db = await SeedAsync();
            var ids = await SearchIds(db, new RecipeSearchModel { CookableForHouseholdId = HouseholdId });
            ids.Should().Contain(new[] { Toast, AirFriedWings, ChocolateCake }).And.NotContain(SousVideSteak);
        }

        [Fact]
        public async Task Cookable_filter_honours_the_households_answers_and_harder_substitutes()
        {
            using var db = await SeedAsync();
            db.HouseholdTools.AddRange(
                new HouseholdToolEntity { HouseholdId = HouseholdId, ToolId = KitchenToolCatalog.Oven, IsAvailable = false },
                new HouseholdToolEntity { HouseholdId = HouseholdId, ToolId = KitchenToolCatalog.AirFryer, IsAvailable = true },
                new HouseholdToolEntity { HouseholdId = HouseholdId, ToolId = KitchenToolCatalog.Thermometer, IsAvailable = true });
            await db.SaveChangesAsync();

            var ids = await SearchIds(db, new RecipeSearchModel { CookableForHouseholdId = HouseholdId });
            ids.Should().Contain(new[] { AirFriedWings, ChocolateCake, SousVideSteak }).And.NotContain(Toast);
        }

        [Fact]
        public async Task Cookable_filter_combines_with_the_other_filters()
        {
            using var db = await SeedAsync();
            (await SearchIds(db, new RecipeSearchModel { CookableForHouseholdId = HouseholdId, RecipeTypeIds = new() { Dessert } }))
                .Should().Equal(ChocolateCake);
        }

        [Fact]
        public async Task Relevance_puts_name_matches_that_start_with_the_term_first()
        {
            using var db = await SeedAsync();
            (await SearchIds(db, new RecipeSearchModel { Query = "toast", SortBy = "relevance" }))
                .Should().Equal(Toast, ToastedSandwich);
        }

        [Fact]
        public async Task Rating_sort_puts_unrated_recipes_last()
        {
            using var db = await SeedAsync();
            var ids = await SearchIds(db, new RecipeSearchModel { SortBy = "rating", SortDirection = "desc" });
            ids.Take(2).Should().Equal(ChocolateCake, AirFriedWings);
            ids.Should().HaveCount(5);
        }

        [Fact]
        public async Task Quickest_sort_orders_by_total_time_with_unknown_times_last()
        {
            using var db = await SeedAsync();
            (await SearchIds(db, new RecipeSearchModel { SortBy = "quickest" }))
                .Should().Equal(Toast, AirFriedWings, ChocolateCake, SousVideSteak, ToastedSandwich);
        }

        [Fact]
        public async Task Ingredients_sort_orders_by_count_with_empty_recipes_last()
        {
            using var db = await SeedAsync();
            var ids = await SearchIds(db, new RecipeSearchModel { SortBy = "ingredients" });
            ids.Take(3).Should().Equal(Toast, AirFriedWings, ChocolateCake);
            ids.Skip(3).Should().BeEquivalentTo(new[] { SousVideSteak, ToastedSandwich });
        }

        [Fact]
        public async Task Filter_options_list_courses_and_the_tool_catalog()
        {
            using var db = await SeedAsync();
            db.Set<ReferenceGroupEntity>().Add(new ReferenceGroupEntity
            {
                Id = (long)ReferenceDiscriminatorEnum.RecipeType,
                Name = "Recipe Type",
                References = db.Set<ReferenceEntity>().Where(r => r.Id == Entree || r.Id == Dessert).ToList(),
            });
            await db.SaveChangesAsync();

            var service = new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance);
            var options = await service.GetFilterOptionsAsync();

            options.Courses.Select(c => c.Name).Should().Equal("Entree", "Dessert");
            options.KitchenTools.Should().Contain(t => t.Id == KitchenToolCatalog.AirFryer && t.Group == KitchenToolCatalog.CategoryAppliances);
            options.KitchenTools.Should().HaveCount(KitchenToolCatalog.ById.Count);
        }
    }
}
