using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The diet gate by data (alcohol design A9): No Alcohol, Halal and Whole30 read alcohol from the
    /// food's stored alcohol (nutrient 5038) and, for a food with no alcohol figure, from the alcohol
    /// sense of its name through word-bounded patterns. Bread Crumbs, Ginger Root, Crumbled Feta and
    /// the wine vinegars no longer match; Vanilla Extract is left to a note (owner answer 6); a wine,
    /// a spirit and an unlinked "glass of sherry" still do.
    /// </summary>
    public class DietGateByDataTests
    {
        private const long HouseholdId = 7, PersonId = 70, NoAlcoholRef = 60913, HalalRef = 60914, CeliacRef = 60800, Alcohol = 5038, Gram = 1;
        private const long Curated = (long)CurationStatusEnum.Curated, Pending = (long)CurationStatusEnum.PendingCuration;

        private static readonly string[] OldAlcoholPatterns = { "%wine%", "%beer%", "%vodka%", "%rum%", "%whisk%", "%bourbon%", "%brandy%", "%liqueur%", "%sake%", "%tequila%", "%gin%", "%sherry%", "%vermouth%", "%cider%" };

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private sealed record Foods(Dictionary<string, long> Ids);

        private static async Task<Foods> SeedAsync(ApplicationDbContext db, bool withNutrient = true)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" },
                new ReferenceEntity { Id = HalalRef, Name = "Halal" },
                new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" });
            if (withNutrient) db.Nutrients.Add(new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = Gram });
            var foods = new (string Name, long Status, decimal? Alcohol, int? Category)[]
            {
                ("Bread Crumbs", Curated, 0m, 18),
                ("Ginger Root", Curated, null, 11),
                ("Crumbled Feta", Curated, null, 1),
                ("Red Wine Vinegar", Curated, 0m, 2),
                ("Apple Cider Vinegar", Curated, 0m, 2),
                ("Vanilla Extract", Curated, 34.4m, 2),
                ("Dry White Wine", Curated, 10.3m, 14),
                ("Coffee Cream Liqueur", Curated, 10.9m, 14),
                ("Beer Salami", Curated, 0m, 7),
                ("glass of sherry", Pending, null, null),
                ("brandy", Pending, null, null),
                ("rum extract", Pending, null, null),
                ("oyster liquor", Pending, null, null),
                ("ginger beer", Pending, null, null),
                ("sweet cider", Pending, null, null),
                ("Gingerbread", Curated, null, 18),
            };
            var ids = new Dictionary<string, long>();
            foreach (var (name, status, alcohol, category) in foods)
            {
                var food = new IngredientEntity { Name = name, CurationStatusId = status, FdcCategoryId = category };
                if (alcohol is { } grams) food.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = grams, MeasurementId = Gram });
                db.Ingredients.Add(food);
                await db.SaveChangesAsync();
                ids[name] = food.Id;
            }
            return new Foods(ids);
        }

        private static async Task<RestrictedIngredientSet> ResolveAsync(ApplicationDbContext db, long typeId, string name)
        {
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = name, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
        }

        [Fact]
        public async Task No_alcohol_flags_drinks_by_data_and_by_the_sense_of_unlinked_names_and_nothing_else()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");

            set.IngredientIds.Should().Contain(new[] { foods.Ids["Dry White Wine"], foods.Ids["Coffee Cream Liqueur"], foods.Ids["glass of sherry"], foods.Ids["brandy"] });
            set.IngredientIds.Should().NotContain(new[]
            {
                foods.Ids["Bread Crumbs"], foods.Ids["Ginger Root"], foods.Ids["Crumbled Feta"], foods.Ids["Red Wine Vinegar"], foods.Ids["Apple Cider Vinegar"],
                foods.Ids["Vanilla Extract"], foods.Ids["Beer Salami"], foods.Ids["rum extract"], foods.Ids["oyster liquor"], foods.Ids["ginger beer"],
                foods.Ids["sweet cider"], foods.Ids["Gingerbread"],
            });
            set.HasSevere.Should().BeFalse("No Alcohol is a diet, severity 3");
        }

        [Fact]
        public async Task Halal_keeps_its_pork_patterns_and_reads_alcohol_by_data()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            var bacon = new IngredientEntity { Name = "Bacon", CurationStatusId = Curated };
            db.Ingredients.Add(bacon);
            await db.SaveChangesAsync();

            var set = await ResolveAsync(db, HalalRef, "Halal");

            set.IngredientIds.Should().Contain(bacon.Id).And.Contain(foods.Ids["Dry White Wine"]).And.Contain(foods.Ids["brandy"]);
            set.IngredientIds.Should().NotContain(foods.Ids["Bread Crumbs"]).And.NotContain(foods.Ids["Red Wine Vinegar"]).And.NotContain(foods.Ids["Ginger Root"]);
        }

        [Fact]
        public async Task Without_an_alcohol_nutrient_the_word_patterns_alone_still_flag_every_drink_name()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db, withNutrient: false);

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");

            set.IngredientIds.Should().Contain(new[] { foods.Ids["Dry White Wine"], foods.Ids["Coffee Cream Liqueur"], foods.Ids["glass of sherry"], foods.Ids["brandy"] });
            set.IngredientIds.Should().NotContain(new[] { foods.Ids["Bread Crumbs"], foods.Ids["Ginger Root"], foods.Ids["Red Wine Vinegar"], foods.Ids["Vanilla Extract"] });
        }

        [Fact]
        public async Task Ensure_replaces_the_untouched_alcohol_name_patterns_and_leaves_an_edited_type_alone()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var now = DateTime.UtcNow;
            foreach (var pattern in OldAlcoholPatterns)
                db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = NoAlcoholRef, IngredientPattern = pattern, Severity = 3, Notes = DefaultRestrictionCriteria.DefaultNote, CreatedDate = now });
            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = HalalRef, IngredientPattern = "%rum%", Severity = 3, Notes = "admin" });
            await db.SaveChangesAsync();

            await DefaultRestrictionCriteria.EnsureAsync(db);
            var second = await DefaultRestrictionCriteria.EnsureAsync(db);

            var noAlcohol = await db.Set<RestrictionCriterionEntity>().Where(c => c.RestrictionTypeId == NoAlcoholRef).ToListAsync();
            noAlcohol.Select(c => c.IngredientPattern).Should().NotContain(OldAlcoholPatterns).And.Contain(@"\mrum\M").And.Contain(@"\mgin\M");
            noAlcohol.Should().ContainSingle(c => c.NutrientId == Alcohol && c.IngredientPattern == null && c.MaxAmountPerServing == null);
            second.Should().Be(0);
            (await db.Set<RestrictionCriterionEntity>().CountAsync(c => c.RestrictionTypeId == HalalRef)).Should().Be(1, "an admin-edited type is left alone");
        }

        [Fact]
        public async Task A_recipe_with_a_drink_line_is_flagged_for_no_alcohol_and_a_recipe_with_only_crumbs_ginger_and_vinegar_is_not()
        {
            using var db = NewContext();
            var foods = await SeedAsync(db);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No Alcohol", RestrictionTypeId = NoAlcoholRef });
            var plain = Recipe("Breaded fish", foods.Ids["Bread Crumbs"], foods.Ids["Ginger Root"], foods.Ids["Red Wine Vinegar"], foods.Ids["Vanilla Extract"]);
            var trifle = Recipe("Trifle", foods.Ids["glass of sherry"], foods.Ids["Bread Crumbs"]);
            var fish = Recipe("Fish in white wine", foods.Ids["Dry White Wine"]);
            db.Recipes.AddRange(plain, trifle, fish);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var service = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);

            (await service.GetDietMatchesAsync(plain.Id, PersonId)).Should().BeEmpty();
            (await service.GetDietMatchesAsync(trifle.Id, PersonId)).Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new { RestrictionName = "No Alcohol", IngredientName = "glass of sherry", Severity = (int?)3, Notes = (string?)null });
            (await service.GetDietMatchesAsync(fish.Id, PersonId)).Select(m => m.IngredientName).Should().Equal("Dry White Wine");
        }

        [Fact]
        public void A_word_pattern_is_matched_on_word_bounds()
        {
            DietCriteriaMatcher.IsWordPattern(@"\mrum\M").Should().BeTrue();
            DietCriteriaMatcher.IsWordPattern("%rum%").Should().BeFalse();
            DietCriteriaMatcher.WordRegex(@"\mrum\M").IsMatch("Bread Crumbs").Should().BeFalse();
            DietCriteriaMatcher.WordRegex(@"\mrum\M").IsMatch("Dark Rum").Should().BeTrue();
            DietCriteriaMatcher.WordRegex(@"\mgin\M").IsMatch("Ginger Root").Should().BeFalse();
            DietCriteriaMatcher.WordRegex(@"\mwhiske?y\M").IsMatch("Scotch whisky").Should().BeTrue();
        }

        [Fact]
        public void The_diet_gate_queries_translate_to_postgres_word_matches_and_a_nutrient_filter()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var scope = new List<long> { 1, 2 };

            DietCriteriaMatcher.WordNameRows(db, AlcoholLineContext.DrinkNamePattern, scope).ToQueryString().Should().Contain("\"Name\" ~*").And.NotContain("\"AliasName\"");
            DietCriteriaMatcher.WordAliasRows(db, @"\mrum\M", null).ToQueryString().Should().Contain("\"AliasName\" ~*");
            DietCriteriaMatcher.NutrientRows(db, Alcohol, scope).ToQueryString().Should().Contain("\"Amount\" >").And.Contain("\"FdcCategoryId\"");
            db.Set<RestrictionCriterionEntity>().Where(c => c.IngredientPattern != null && !c.IngredientPattern.StartsWith(DietCriteriaMatcher.WordMarker)).ToQueryString()
                .Should().Contain("LIKE");
        }

        private static RecipeEntity Recipe(string name, params long[] foods)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var food in foods)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, SortOrder = order++ });
            return recipe;
        }
    }
}
