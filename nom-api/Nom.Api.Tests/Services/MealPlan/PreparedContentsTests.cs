using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Models.Plan;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// A household with a diet, an allergy or an avoided food gets a prepared meal when, and only when, the meal's
    /// contents are known from an authoritative statement (USDA FDC's for a branded food, or the household's own
    /// confirmed label): the statement was read, every named ingredient resolved to a curated food or was recognised,
    /// and nothing in it, its Contains line or (for allergies and medical restrictions) its warnings is restricted.
    /// Anything unknown keeps the meal out.
    /// </summary>
    public class PreparedContentsTests
    {
        private const long HouseholdId = 500;
        private const long OtherHouseholdId = 501;
        private const long PersonId = 50;
        private const long OtherPersonId = 51;
        private const long Breakfast = 1100, Lunch = 1101, Dinner = 1102, Snacks = 1103;
        private const long CeliacRef = 2210, LactoseRef = 2211, VegetarianRef = 2212, FishAllergyRef = 2213, NoAlcoholRef = 2214, PeanutAllergyRef = 2215;
        private static readonly DateOnly Monday = new(2026, 10, 12);

        private sealed class FakePolicy : IPolicyEnforcementService
        {
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(false);
            public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new List<long>());
            public Task<List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(new List<long>());
        }

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            db.Set<HouseholdEntity>().AddRange(new HouseholdEntity { Id = HouseholdId, Name = "Contents kitchen" }, new HouseholdEntity { Id = OtherHouseholdId, Name = "Next door" });
            db.Persons.AddRange(new Nom.Data.Person.PersonEntity { Id = PersonId, Name = "Cook" }, new Nom.Data.Person.PersonEntity { Id = OtherPersonId, Name = "Neighbour" });
            db.HouseholdMembers.AddRange(
                new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true },
                new HouseholdMemberEntity { HouseholdId = OtherHouseholdId, PersonId = OtherPersonId, IsActive = true });
            db.Nutrients.AddRange(new NutrientEntity { Id = 1, Name = "Calories" }, new NutrientEntity { Id = 2, Name = DietCriteriaMatcher.AlcoholNutrientName });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" },
                new ReferenceEntity { Id = LactoseRef, Name = "Lactose Intolerance" },
                new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" },
                new ReferenceEntity { Id = FishAllergyRef, Name = "Fish Allergy" },
                new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" },
                new ReferenceEntity { Id = PeanutAllergyRef, Name = "Peanut Allergy" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" },
                new ReferenceEntity { Id = Breakfast, Name = "Breakfast" },
                new ReferenceEntity { Id = Lunch, Name = "Lunch" },
                new ReferenceEntity { Id = Dinner, Name = "Dinner" },
                new ReferenceEntity { Id = Snacks, Name = "Snacks" });

            var types = new[] { 3100L, 3101L, 3102L, 3103L, 3104L }.ToDictionary(id => id, id => new ReferenceEntity { Id = id, Name = $"Type {id}" });
            db.Set<ReferenceEntity>().AddRange(types.Values);
            var nextId = 6000L;
            foreach (var (mealTypeId, recipeTypeIds) in new[]
            {
                (Breakfast, new[] { 3101L, 3103L }), (Lunch, new[] { 3100L, 3101L, 3102L }), (Dinner, new[] { 3100L, 3101L, 3102L }), (Snacks, new[] { 3104L }),
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
                        db.RecipeNutrition.Add(new RecipeNutritionEntity { RecipeId = recipe.Id, NutrientId = 1, Amount = 250m });
                    }
                }
            }

            foreach (var name in new[] { "Rice", "Chicken", "Black beans", "Wheat flour", "Milk", "Cheese", "Pollock", "Tomatoes", "Onions", "White wine" })
                db.Ingredients.Add(new IngredientEntity { Name = name, CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 11 });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new PortionOrchestrationService(db, NullLogger<PortionOrchestrationService>.Instance)
                .SaveMealPlanOptionsAsync(HouseholdId, new MealPlanOptionsModel { PreparedMealTypeIds = new List<long> { Lunch } });
            return db;
        }

        private static IngredientEntity Meal(ApplicationDbContext db, string name, long status = (long)CurationStatusEnum.Curated)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = status, FdcCategoryId = 22, ReferenceServingGrams = 300m };
            db.Ingredients.Add(food);
            db.IngredientNutrients.Add(new IngredientNutrientEntity { Ingredient = food, NutrientId = 1, Amount = 150m, MeasurementId = 1 });
            return food;
        }

        private static async Task<IngredientEntity> MealWithStatementAsync(ApplicationDbContext db, string name, string statement, string fdcId)
        {
            var food = Meal(db, name);
            await db.SaveChangesAsync();
            await new PreparedContentsService(db).RecordAsync(new[] { new ContentsStatement(food.Id, PreparedContentsService.FdcSource(fdcId), statement) }, "fdc-contents-test", apply: true);
            return food;
        }

        private static async Task RestrictAsync(ApplicationDbContext db, long typeId, long personId = PersonId)
        {
            db.Restrictions.Add(new RestrictionEntity { PersonId = personId, Name = "Restriction", RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
        }

        private static async Task<MealPlanShuffleResponseModel> ShuffleWeekAsync(ApplicationDbContext db) =>
            await new MealPlanOrchestrationService(db, new FakePolicy()).ShuffleMealPlansAsync(new MealPlanShuffleModel
            {
                HouseholdId = HouseholdId, StartDate = Monday, EndDate = Monday.AddDays(6), ReplaceExisting = true,
            }, PersonId);

        private static async Task<List<long?>> LunchFoodsAsync(ApplicationDbContext db) =>
            await db.MealPlans.Where(mp => mp.HouseholdId == HouseholdId && mp.MealTypeId == Lunch).Select(mp => mp.IngredientId).ToListAsync();

        private static async Task<Dictionary<long, PreparedMealCheckModel>> CheckAsync(ApplicationDbContext db, long householdId, params long[] ids) =>
            (await new MealPlanOrchestrationService(db, new FakePolicy()).CheckPreparedMealsAsync(householdId, ids)).ToDictionary(c => c.IngredientId);

        [Fact]
        public async Task A_celiac_household_gets_only_the_prepared_meal_whose_read_contents_hold_no_gluten()
        {
            using var db = await SeedAsync();
            var safe = await MealWithStatementAsync(db, "Chicken and rice bowl, frozen", "Ingredients: Rice, chicken, water, salt, xanthan gum.", "3000001");
            var pasta = await MealWithStatementAsync(db, "Chicken alfredo bowl, frozen", "Ingredients: Pasta (wheat flour, water), chicken, salt.", "3000002");
            var unlisted = Meal(db, "Restaurant, Chinese, fried rice");
            var unreadable = await MealWithStatementAsync(db, "Rice casserole, frozen", "Ingredients: Rice (water, rice, chicken, salt", "3000003");
            await db.SaveChangesAsync();
            await RestrictAsync(db, CeliacRef);

            var response = await ShuffleWeekAsync(db);

            var lunches = await LunchFoodsAsync(db);
            lunches.Should().HaveCount(7).And.OnlyContain(id => id == safe.Id,
                "Celiac Disease (severity 4) is judged by contents: only the bowl's statement was read, every ingredient known, and none gluten");
            response.PreparedMealsHeld.Should().BeNull();

            var checks = await CheckAsync(db, HouseholdId, safe.Id, pasta.Id, unlisted.Id, unreadable.Id);
            checks[safe.Id].Held.Should().BeNull();
            checks[pasta.Id].Held.Should().Be(PreparedContentsHeld.Restricted);
            checks[pasta.Id].Names.Should().Contain("wheat flour");
            checks[unlisted.Id].Held.Should().Be(PreparedContentsHeld.None);
            checks[unreadable.Id].Held.Should().Be(PreparedContentsHeld.Unread);
        }

        [Fact]
        public async Task A_celiac_household_whose_prepared_meals_all_name_gluten_or_are_unknown_gets_recipes_and_the_severe_reason()
        {
            using var db = await SeedAsync();
            await MealWithStatementAsync(db, "Chicken pasta, frozen", "Ingredients: Pasta (wheat flour, water), chicken, salt.", "3000002");
            await MealWithStatementAsync(db, "Barley soup, frozen", "Ingredients: Water, barley, onions, salt.", "3000004");
            Meal(db, "Restaurant, Chinese, fried rice");
            await db.SaveChangesAsync();
            await RestrictAsync(db, CeliacRef);

            var response = await ShuffleWeekAsync(db);

            response.PreparedMealsHeld.Should().Be(MealPlanOrchestrationService.PreparedMealsHeldReasons.SevereRestriction);
            (await LunchFoodsAsync(db)).Should().HaveCount(21).And.OnlyContain(id => id == null, "lunch falls back to its three recipe courses a day");
        }

        [Fact]
        public async Task Lactose_intolerance_holds_a_meal_that_declares_milk_or_warns_of_it_and_offers_one_that_does_neither()
        {
            using var db = await SeedAsync();
            var clean = await MealWithStatementAsync(db, "Bean bowl, frozen", "Ingredients: Black beans, rice, tomatoes, salt.", "3000010");
            var declared = await MealWithStatementAsync(db, "Rice bowl, frozen", "Ingredients: Rice, chicken, natural flavor. Contains: milk.", "3000011");
            var warned = await MealWithStatementAsync(db, "Chicken rice, frozen", "Ingredients: Rice, chicken, salt. May contain milk.", "3000012");
            await RestrictAsync(db, LactoseRef);

            var checks = await CheckAsync(db, HouseholdId, clean.Id, declared.Id, warned.Id);

            checks[clean.Id].Held.Should().BeNull();
            checks[declared.Id].Held.Should().Be(PreparedContentsHeld.Restricted, "a declared allergen is contained");
            checks[declared.Id].Names.Should().Contain("contains milk");
            checks[warned.Id].Held.Should().Be(PreparedContentsHeld.Restricted, "a warning counts as contained for a severity 4 restriction");
            checks[warned.Id].Names.Should().Contain("may contain milk");

            await ShuffleWeekAsync(db);
            (await LunchFoodsAsync(db)).Should().OnlyContain(id => id == clean.Id);
        }

        [Fact]
        public async Task A_warning_holds_a_meal_for_an_allergy_but_not_for_a_diet()
        {
            using var db = await SeedAsync();
            var bowl = await MealWithStatementAsync(db, "Bean bowl, frozen", "Ingredients: Black beans, rice, onions, salt. Made in a facility that also processes fish.", "3000020");
            await RestrictAsync(db, VegetarianRef);

            (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id].Held.Should().BeNull("a vegetarian diet (severity 3) is judged by what the meal contains, not by a shared facility");

            await RestrictAsync(db, FishAllergyRef);
            var check = (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id];
            check.Held.Should().Be(PreparedContentsHeld.Restricted, "for a fish allergy the warning counts as contained");
            check.Names.Should().Contain("may contain fish");
        }

        [Fact]
        public async Task An_ingredient_neither_resolved_nor_recognised_keeps_the_meal_out_for_a_restricted_household_only()
        {
            using var db = await SeedAsync();
            var bowl = await MealWithStatementAsync(db, "Rice bowl, frozen", "Ingredients: Rice, chicken, house seasoning, salt.", "3000030");

            (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id].Held.Should().BeNull("a household with no restriction is not judged by contents");

            await RestrictAsync(db, PeanutAllergyRef);
            var check = (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id];
            check.Held.Should().Be(PreparedContentsHeld.Unresolved);
            check.Names.Should().Equal("house seasoning");
        }

        [Fact]
        public async Task A_food_that_was_resolved_but_is_no_longer_curated_makes_the_contents_unknown()
        {
            using var db = await SeedAsync();
            var bowl = await MealWithStatementAsync(db, "Chicken rice bowl, frozen", "Ingredients: Rice, chicken, salt.", "3000031");
            await RestrictAsync(db, PeanutAllergyRef);
            (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id].Held.Should().BeNull();

            var chicken = await db.Ingredients.SingleAsync(i => i.Name == "Chicken");
            chicken.CurationStatusId = (long)CurationStatusEnum.PendingCuration;
            await db.SaveChangesAsync();

            var check = (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id];
            check.Held.Should().Be(PreparedContentsHeld.Unresolved);
            check.Names.Should().Equal("chicken");
        }

        [Fact]
        public async Task A_drink_in_the_contents_holds_the_meal_for_no_alcohol_because_its_amount_is_not_given()
        {
            using var db = await SeedAsync();
            var risotto = await MealWithStatementAsync(db, "Risotto, frozen", "Ingredients: Rice, white wine, cheese (milk, salt), onions.", "3000040");
            var vinegar = await MealWithStatementAsync(db, "Rice salad, chilled", "Ingredients: Rice, red wine vinegar, onions.", "3000041");
            await RestrictAsync(db, NoAlcoholRef);

            var checks = await CheckAsync(db, HouseholdId, risotto.Id, vinegar.Id);
            checks[risotto.Id].Held.Should().Be(PreparedContentsHeld.Restricted);
            checks[risotto.Id].Names.Should().Contain("white wine");
            checks[vinegar.Id].Held.Should().NotBe(PreparedContentsHeld.Restricted, "a wine vinegar is no drink");
        }

        [Fact]
        public async Task A_households_confirmed_label_decides_for_that_household_only_and_a_restricted_statement_holds_either_way()
        {
            using var db = await SeedAsync();
            var entree = Meal(db, "Lasagna, vegetable, frozen");
            await db.SaveChangesAsync();
            await RestrictAsync(db, PeanutAllergyRef);
            await RestrictAsync(db, PeanutAllergyRef, OtherPersonId);
            var label = new IngredientVerificationEntity
            {
                IngredientId = entree.Id, HouseholdId = HouseholdId, Source = IngredientVerificationService.UserLabelSource(PersonId),
                Kind = IngredientVerificationKind.Label, Status = IngredientVerificationStatus.Pending, Confirmed = true,
                IngredientStatement = "Ingredients: Rice, black beans, tomatoes, salt.",
            };
            db.IngredientVerifications.Add(label);
            await db.SaveChangesAsync();

            var row = await new PreparedContentsService(db).RecordLabelAsync(label);

            row.Should().NotBeNull();
            row!.Source.Should().Be($"label:user:{PersonId}");
            row.HouseholdId.Should().Be(HouseholdId);
            (await CheckAsync(db, HouseholdId, entree.Id))[entree.Id].Held.Should().BeNull();
            (await CheckAsync(db, OtherHouseholdId, entree.Id))[entree.Id].Held.Should().Be(PreparedContentsHeld.None, "another household's label is that household's evidence");
            (await db.IngredientNutrients.CountAsync(n => n.IngredientId == entree.Id)).Should().Be(1, "label evidence is never written to the nutrient table");

            await new PreparedContentsService(db).RecordAsync(new[] { new ContentsStatement(entree.Id, PreparedContentsService.FdcSource("3000050"), "Ingredients: Rice, peanuts, salt.") }, "fdc-contents-later", apply: true);
            (await CheckAsync(db, HouseholdId, entree.Id))[entree.Id].Held.Should().Be(PreparedContentsHeld.Restricted, "a restricted ingredient in any statement holds the meal");
        }

        [Fact]
        public async Task A_label_statement_confirmed_through_the_shopping_flow_is_read_for_a_prepared_food_and_the_household_is_asked_for_it()
        {
            using var db = await SeedAsync();
            var entree = Meal(db, "Lasagna, vegetable, frozen");
            var oats = new IngredientEntity { Name = "Rolled oats", CurationStatusId = (long)CurationStatusEnum.Curated, FdcId = "173904" };
            db.Ingredients.Add(oats);
            await db.SaveChangesAsync();
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = oats.Id, NutrientId = 1, Amount = 379m, MeasurementId = 1 });
            await db.SaveChangesAsync();
            var ocr = new Mock<ITesseractOcrService>();
            ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync("Nutrition Facts\nCalories 300\nINGREDIENTS: RICE, BLACK BEANS, SALT. CONTAINS: NONE");
            var service = new IngredientVerificationService(db, ocr.Object, new Mock<IBarcodeDecodeService>().Object, new Mock<IMediaStorageService>().Object,
                new Mock<IVerificationLookupLane>().Object, new ScanRateLimiter(), NullLogger<IngredientVerificationService>.Instance);

            (await service.NeedsVerificationAsync(HouseholdId, new[] { entree.Id })).Single().ShouldAsk.Should().BeFalse("a household with no restriction needs no contents");
            await RestrictAsync(db, PeanutAllergyRef);
            var needs = (await service.NeedsVerificationAsync(HouseholdId, new[] { entree.Id, oats.Id })).ToDictionary(n => n.IngredientId);
            needs[entree.Id].ShouldAsk.Should().BeTrue("a curated prepared meal with unknown contents is asked about for a household judged by contents");
            needs[entree.Id].AskContents.Should().BeTrue();
            needs[oats.Id].ShouldAsk.Should().BeFalse("an ordinary verified food is not");

            var scan = await service.ScanLabelAsync(entree.Id, HouseholdId, PersonId, TinyPng());
            scan!.IngredientStatement.Should().StartWith("INGREDIENTS: RICE, BLACK BEANS, SALT.");
            await service.ConfirmAsync(scan.VerificationId, PersonId, new IngredientVerificationConfirmModel { Calories = 300, IngredientStatement = "Ingredients: Rice, black beans, salt." });

            var row = await db.IngredientContents.SingleAsync(c => c.IngredientId == entree.Id);
            row.HouseholdId.Should().Be(HouseholdId);
            row.VerificationId.Should().Be(scan.VerificationId);
            row.Outcome.Should().Be(IngredientContentsOutcome.Read);
            row.Unresolved.Should().Be(0);
            (await CheckAsync(db, HouseholdId, entree.Id))[entree.Id].Held.Should().BeNull();
            (await service.NeedsVerificationAsync(HouseholdId, new[] { entree.Id })).Single().ShouldAsk.Should().BeFalse("its contents are known now");
        }

        [Fact]
        public async Task A_pass_writes_a_row_only_when_the_statement_changed_and_its_undo_brings_the_superseded_row_back()
        {
            using var db = await SeedAsync();
            var bowl = await MealWithStatementAsync(db, "Chicken rice bowl, frozen", "Ingredients: Rice, chicken, salt.", "3000060");
            var contents = new PreparedContentsService(db);

            var same = await contents.RecordAsync(new[] { new ContentsStatement(bowl.Id, PreparedContentsService.FdcSource("3000060"), "Ingredients: Rice, chicken, salt.") }, "pass-2", apply: true);
            same.Written.Should().Be(0);
            same.Unchanged.Should().Be(1);

            var dry = await contents.RecordAsync(new[] { new ContentsStatement(bowl.Id, PreparedContentsService.FdcSource("3000060"), "Ingredients: Rice, chicken, peanuts.") }, "pass-3", apply: false);
            dry.Written.Should().Be(1);
            (await db.IngredientContents.CountAsync()).Should().Be(1, "a dry run writes nothing");

            await contents.RecordAsync(new[] { new ContentsStatement(bowl.Id, PreparedContentsService.FdcSource("3000060"), "Ingredients: Rice, chicken, peanuts.") }, "pass-3", apply: true);
            await RestrictAsync(db, PeanutAllergyRef);
            (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id].Held.Should().Be(PreparedContentsHeld.Restricted, "the newest statement is current");

            (await contents.UndoAsync("pass-3", apply: true)).Should().Be(1);
            (await CheckAsync(db, HouseholdId, bowl.Id))[bowl.Id].Held.Should().BeNull("the undo brings the earlier statement back");
        }

        [Fact]
        public async Task A_branded_whole_meal_is_a_prepared_food_drawn_only_once_the_branded_lift_has_curated_it()
        {
            using var db = await SeedAsync();
            var lifted = new IngredientEntity { Name = "Chicken rice bowl (Acme)", FdcId = "3100001", FdcDataType = CatalogAdmissionRule.BrandedFood, FdcCategory = "Frozen Dinners & Entrees", CurationStatusId = (long)CurationStatusEnum.Curated, ReferenceServingGrams = 280m };
            var waiting = new IngredientEntity { Name = "Beef rice bowl (Acme)", FdcId = "3100002", FdcDataType = CatalogAdmissionRule.BrandedFood, FdcCategory = "Frozen Dinners & Entrees", CurationStatusId = (long)CurationStatusEnum.PendingCuration, AdmissionReason = CatalogAdmissionRule.AwaitingSecondSource, ReferenceServingGrams = 280m };
            var snack = new IngredientEntity { Name = "Granola bar (Acme)", FdcId = "3100003", FdcDataType = CatalogAdmissionRule.BrandedFood, FdcCategory = "Snack, Energy & Granola Bars", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(lifted, waiting, snack);
            await db.SaveChangesAsync();

            PreparedFoods.IsPrepared(lifted).Should().BeTrue();
            PreparedFoods.IsPrepared(snack).Should().BeFalse();
            (await db.Ingredients.Where(PreparedFoods.NotPrepared).AnyAsync(i => i.Id == lifted.Id)).Should().BeFalse("a branded meal is never a recipe line's food");

            await new PreparedContentsService(db).RecordAsync(new[]
            {
                new ContentsStatement(lifted.Id, PreparedContentsService.FdcSource(lifted.FdcId), "Ingredients: Rice, chicken, salt."),
                new ContentsStatement(waiting.Id, PreparedContentsService.FdcSource(waiting.FdcId), "Ingredients: Rice, salt."),
            }, "fdc-contents-test", apply: true);
            await RestrictAsync(db, PeanutAllergyRef);

            await ShuffleWeekAsync(db);
            (await LunchFoodsAsync(db)).Should().HaveCount(7).And.OnlyContain(id => id == lifted.Id, "the pending branded meal has read contents but still waits for the branded lift");
        }

        [Fact]
        public void The_pipeline_counts_each_curated_meal_once_by_its_best_current_statement()
        {
            var facts = new List<PipelineStatusService.ContentsFact>
            {
                new() { Id = 1, IngredientId = 10, HouseholdId = null, Read = true, Unresolved = 0 },
                new() { Id = 2, IngredientId = 11, HouseholdId = null, Read = true, Unresolved = 2 },
                new() { Id = 3, IngredientId = 11, HouseholdId = 7, Read = true, Unresolved = 0 },
                new() { Id = 4, IngredientId = 12, HouseholdId = null, Read = true, Unresolved = 1 },
                new() { Id = 5, IngredientId = 13, HouseholdId = null, Read = true, Unresolved = 0 },
                new() { Id = 6, IngredientId = 13, HouseholdId = null, Read = false, Unresolved = 0 },
            };

            var counts = PipelineStatusService.PreparedContents(10, facts);

            counts.Meals.Should().Be(10);
            counts.Read.Should().Be(2, "meal 10 by FDC and meal 11 by a household's label");
            counts.ReadUnresolved.Should().Be(1);
            counts.Unread.Should().Be(1, "meal 13's newest FDC statement supersedes the older read one");
            counts.None.Should().Be(6);
            counts.FromFdc.Should().Be(4);
            counts.FromLabels.Should().Be(1);
        }

        private static byte[] TinyPng()
        {
            using var bitmap = new SkiaSharp.SKBitmap(8, 8);
            using (var canvas = new SkiaSharp.SKCanvas(bitmap)) canvas.Clear(SkiaSharp.SKColors.White);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
