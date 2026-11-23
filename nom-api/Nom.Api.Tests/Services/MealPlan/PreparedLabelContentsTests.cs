using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Api.Controllers;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Staging checkpoint 3, prepared meals and diets: a statement's plain label names resolve by the label rule
    /// (N5) with what they are made of, a claim after a dash is no heading (N4), the 409 for a held meal names a prepared
    /// meal (N6), the diet gate's warm-up computes the probes a request needs and a request during it waits (N2), and the
    /// composite foods, branded foods with a read statement, type-scoped exceptions and admin-named types close the
    /// gaps ck5 left (Goldfish crackers, licorice).
    /// </summary>
    public class PreparedLabelContentsTests
    {
        private const long HouseholdId = 900, PersonId = 90, Gram = 1;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static async Task<ApplicationDbContext> HouseholdAsync(params string[] types)
        {
            var db = NewContext();
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            var id = 64000L;
            foreach (var type in types)
            {
                db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = id, Name = type });
                db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type, RestrictionTypeId = id });
                id++;
            }
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return db;
        }

        private static async Task<long> MealAsync(ApplicationDbContext db, string statement)
        {
            var food = new IngredientEntity { Name = "Bowl " + Guid.NewGuid().ToString("N")[..6], CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 22 };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            await new PreparedContentsService(db).RecordAsync(new[] { new ContentsStatement(food.Id, PreparedContentsService.FdcSource(food.Id.ToString()), statement) }, "fdc-contents-test", apply: true);
            return food.Id;
        }

        private static async Task<PreparedContentsVerdict> JudgeAsync(ApplicationDbContext db, long mealId)
        {
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            return (await PreparedContentsGate.JudgeAsync(db, HouseholdId, set, new[] { mealId }))[mealId];
        }

        [Fact]
        public async Task A_statement_of_plain_label_names_is_fully_known_so_a_clean_meal_is_offered_to_a_celiac_household_and_a_floury_one_is_held()
        {
            using var db = await HouseholdAsync("Celiac Disease");
            var clean = await MealAsync(db, "Cooked rice, chicken breast with rib meat, soybean oil, salt, natural flavor, garlic powder, green onions, pasteurized milk, cream, potato starch, pre-cooked barley");
            var floury = await MealAsync(db, "Rice, enriched flour (niacin, reduced iron), salt");

            var offered = await JudgeAsync(db, clean);
            offered.Held.Should().Be(PreparedContentsHeld.Restricted, "barley is a gluten grain the type names");
            offered.Names.Should().Contain("pre-cooked barley");

            var bowl = await MealAsync(db, "Cooked rice, chicken breast with rib meat, soybean oil, salt, natural flavor, garlic powder, green onions, pasteurized milk, cream, potato starch, chick peas, basmati rice");
            (await JudgeAsync(db, bowl)).Offered.Should().BeTrue("every name resolves by the label rule and none names gluten");

            var held = await JudgeAsync(db, floury);
            held.Held.Should().Be(PreparedContentsHeld.Restricted);
            held.Names.Should().Contain(n => n.StartsWith("enriched flour", StringComparison.Ordinal));

            var row = await db.IngredientContents.FirstAsync(c => c.IngredientId == bowl);
            row.Unresolved.Should().Be(0);
            row.ReaderVersion.Should().Be(PreparedContentsService.ReaderVersion);
            PreparedContentsService.Items(row).Should().Contain(i => i.Name == "soybean oil" && i.Kind == ContentsKinds.Label && i.Label == "soybean");
        }

        [Fact]
        public async Task What_a_label_term_is_made_of_holds_the_meal_where_its_own_words_do_not()
        {
            using var db = await HouseholdAsync("Shellfish Allergy");
            var shrimp = await MealAsync(db, "Litopenaeus vannamei, water, salt, sodium tripolyphosphate");
            (await JudgeAsync(db, shrimp)).Should().BeEquivalentTo(new { Held = PreparedContentsHeld.Restricted }, "Litopenaeus vannamei is the Pacific white shrimp");

            using var custom = NewContext();
            custom.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            custom.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = 65000, Name = "Our dairy rule" });
            custom.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = 65000, IngredientPattern = "%milk%", Severity = 5, Notes = "admin" });
            custom.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Our dairy rule", RestrictionTypeId = 65000 });
            await custom.SaveChangesAsync();
            var whey = await MealAsync(custom, "Rice, sweet dairy whey, salt");
            var verdict = await JudgeAsync(custom, whey);
            verdict.Held.Should().Be(PreparedContentsHeld.Restricted, "whey is a milk product, though the type only names milk");
            verdict.Names.Should().Contain("sweet dairy whey (milk)");
        }

        [Fact]
        public async Task A_term_whose_source_the_label_does_not_state_keeps_the_meal_unknown_only_where_a_restriction_names_a_possible_source()
        {
            using var soy = await HouseholdAsync("Soy Allergy");
            var lecithin = await MealAsync(soy, "Rice, lecithin, salt");
            var held = await JudgeAsync(soy, lecithin);
            held.Held.Should().Be(PreparedContentsHeld.Unresolved);
            held.Names.Should().ContainSingle(n => n.StartsWith("lecithin (may be soy", StringComparison.Ordinal));

            using var fish = await HouseholdAsync("Fish Allergy");
            var same = await MealAsync(fish, "Rice, lecithin, salt");
            (await JudgeAsync(fish, same)).Offered.Should().BeTrue("neither possible source of lecithin is a fish");

            using var vegan = await HouseholdAsync("Vegan");
            (await JudgeAsync(vegan, await MealAsync(vegan, "Rice, mono and diglycerides, salt"))).Held.Should().Be(PreparedContentsHeld.Unresolved, "mono- and diglycerides of unstated source may be animal fat");
            (await JudgeAsync(vegan, await MealAsync(vegan, "Rice, vegetable mono and diglycerides, salt"))).Offered.Should().BeTrue();
        }

        [Fact]
        public async Task A_word_that_may_hide_an_ingredient_is_never_left_out_so_its_name_stays_unknown()
        {
            using var db = await HouseholdAsync("Fish Allergy");
            (await JudgeAsync(db, await MealAsync(db, "Rice, crab flavored seafood, salt"))).Held.Should().Be(PreparedContentsHeld.Unresolved);
            var surimi = await JudgeAsync(db, await MealAsync(db, "Rice, imitation crab, salt"));
            surimi.Held.Should().Be(PreparedContentsHeld.Restricted, "imitation crab is surimi of pollock");
            StatementLabelTerms.Match("breaded chicken").Should().BeNull();
            StatementLabelTerms.Match("vegan cheese").Should().BeNull();
            StatementLabelTerms.Match("white chocolate").Should().BeNull();
            StatementLabelTerms.Match("imitation crab")!.Single().Term.Should().Be("surimi");
        }

        [Fact]
        public void The_table_reads_every_row_with_a_source_and_refuses_a_name_that_stands_for_two_terms()
        {
            StatementLabelTerms.All.Should().HaveCountGreaterThan(200);
            StatementLabelTerms.All.Should().OnlyContain(t => t.Source.Length > 0);
            StatementLabelTerms.Composites.Should().Contain(t => t.Term == "goldfish cracker").And.Contain(t => t.Term == "licorice");
            var text = "term\tkind\tnames\tcontains\tmay\tsource\nmilk\tfood\twhey\tmilk\t-\ta\nwhey\tfood\t-\tmilk\t-\tb\n";
            FluentActions.Invoking(() => StatementLabelTerms.Parse(text)).Should().Throw<System.IO.InvalidDataException>().WithMessage("*whey stands for both*");
        }

        [Fact]
        public void The_409_for_a_held_prepared_meal_names_a_prepared_meal_not_a_locked_recipe()
        {
            var message = MealPlanController.RestrictionViolationMessage("restriction_violation:prepared_held_unconfirmed");
            message.Should().Contain("prepared meal").And.NotContain("recipe").And.NotContain("locked");
            MealPlanController.RestrictionViolationMessage("restriction_violation:locked_restriction").Should().Contain("locked dietary restriction");
        }

        [Fact]
        public async Task A_request_during_the_warm_up_waits_for_the_probe_and_gets_its_value_never_an_empty_one()
        {
            using var db = NewContext();
            var cache = new DietGateCache();
            var release = new TaskCompletionSource();
            var warm = cache.GetAsync(db, "probe", async _ =>
            {
                await release.Task;
                return new HashSet<long> { 7 };
            });
            var request = cache.GetAsync(db, "probe", _ => Task.FromResult(new HashSet<long>()));
            await Task.Delay(50);
            request.IsCompleted.Should().BeFalse("the request waits on the probe the warm-up is computing");
            release.SetResult();
            (await request).Should().Equal(7);
            (await warm).Should().Equal(7);
            cache.Computations.Should().Be(1);
        }

        [Fact]
        public async Task The_warm_up_also_computes_the_severe_probes_so_a_persons_severe_check_after_a_start_does_not_compute_in_line()
        {
            using var db = await HouseholdAsync("Peanut Allergy", "Vegetarian");
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = 67000, Name = "Pancreatitis (ours)" });
            db.Set<RestrictionCriterionEntity>().AddRange(
                new RestrictionCriterionEntity { RestrictionTypeId = 67000, IngredientPattern = "%lard%", Severity = 4, Notes = "admin" },
                new RestrictionCriterionEntity { RestrictionTypeId = 67000, IngredientPattern = "%sugar%", Severity = 2, Notes = "admin" });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Pancreatitis (ours)", RestrictionTypeId = 67000 });
            await db.SaveChangesAsync();
            var cache = new DietGateCache(refreshInBackground: false);
            await DietCriteriaMatcher.WarmAsync(db, cache);
            var warmed = cache.Computations;
            warmed.Should().BeGreaterThan(0);

            var resolver = new HouseholdRestrictionResolver(db, cache);
            await resolver.ResolveSevereForPersonAsync(PersonId);
            await resolver.ResolveAsync(HouseholdId);
            cache.Computations.Should().Be(warmed, "every probe a household or a person's severe set reads was warmed");
        }

        private static async Task<RecipeEntity> LineAsync(ApplicationDbContext db, string line, long? foodId = null)
        {
            var wording = new IngredientEntity { Name = "wording " + Guid.NewGuid().ToString("N")[..6], CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.Add(wording);
            await db.SaveChangesAsync();
            var recipe = new RecipeEntity
            {
                Name = "Line", AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated,
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = foodId ?? wording.Id, MeasurementId = Gram, Quantity = 1m, RawLine = line, SortOrder = 0 } },
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            return recipe;
        }

        [Theory]
        [InlineData("Milk Allergy", "1 cup Goldfish crackers", true)]
        [InlineData("Dairy-Free", "2 cups goldfish crackers, crushed", true)]
        [InlineData("Celiac Disease", "4 oz red licorice, chopped", true)]
        [InlineData("Gluten-Free", "1/2 cup black licorice", true)]
        [InlineData("Gluten Sensitivity", "8 licorice twists", true)]
        [InlineData("Wheat Allergy", "1 cup licorice candy", true)]
        [InlineData("Celiac Disease", "1 tsp licorice root, dried", false)]
        [InlineData("Paleo", "1 tsp licorice root, dried", true)]
        [InlineData("Fish Allergy", "1 tbsp Worcestershire sauce", true)]
        [InlineData("Egg Allergy", "1 cup vanilla custard", true)]
        [InlineData("Peanut Allergy", "1/4 cup satay sauce", true)]
        [InlineData("Peanut Allergy", "1 cup roasted groundnuts", true)]
        [InlineData("Tree Nut Allergy", "2 tbsp basil pesto", true)]
        [InlineData("Milk Allergy", "1/2 cup mascarpone", true)]
        [InlineData("Wheat Allergy", "2 tbsp teriyaki sauce", true)]
        [InlineData("Nightshade Sensitivity", "2 medium sweet potatoes, cubed", false)]
        [InlineData("Jain", "2 medium sweet potatoes, cubed", true)]
        [InlineData("Sugar-Free", "1 cup sugar snap peas", false)]
        [InlineData("Milk Allergy", "1 cup edamame", false)]
        public async Task A_composite_food_named_in_a_line_is_read_by_what_it_is_made_of_and_a_type_scoped_exception_clears_only_its_types(string type, string line, bool flagged)
        {
            using var db = await HouseholdAsync(type);
            var recipe = await LineAsync(db, line);
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            set.RecipeIds.Contains(recipe.Id).Should().Be(flagged, $"{type} on \"{line}\"");
        }

        [Fact]
        public async Task An_admin_type_copied_from_a_default_gets_its_companions_and_any_type_gets_the_composite_foods_its_patterns_restrict()
        {
            using var db = NewContext();
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = 66000, Name = "Strict vegetarian (ours)" });
            foreach (var p in DefaultRestrictionCriteria.ByName["Vegetarian"].Patterns.Where(p => p != "%hamburger%" && p != "%scallopini%" && p != "%worcestershire sauce%" && p != "%worcestershire%"))
                db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = 66000, IngredientPattern = p, Severity = 3, Notes = "copied by an admin" });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Strict vegetarian (ours)", RestrictionTypeId = 66000 });
            await db.SaveChangesAsync();
            DefaultRestrictionCriteria.OriginsOf("Strict vegetarian (ours)", DefaultRestrictionCriteria.ByName["Vegetarian"].Patterns).Should().Contain("Vegetarian");

            (await DefaultRestrictionCriteria.EnsureAsync(db)).Should().BeGreaterThan(0);
            (await DefaultRestrictionCriteria.EnsureAsync(db)).Should().Be(0, "companions are added once");
            var added = await db.Set<RestrictionCriterionEntity>().Where(c => c.RestrictionTypeId == 66000).Select(c => c.IngredientPattern!).ToListAsync();
            added.Should().Contain("%hamburger%").And.Contain("%worcestershire%");

            var hamburger = await LineAsync(db, "1 lb hamburger");
            var worcestershire = await LineAsync(db, "1 tbsp Worcestershire sauce");
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            set.RecipeIds.Should().Contain(new[] { hamburger.Id, worcestershire.Id });
        }

        [Fact]
        public void Every_composite_companion_names_its_composite_and_stands_on_a_pattern_that_names_what_it_is_made_of()
        {
            DefaultRestrictionCriteria.CompositeCompanions.Should().NotBeEmpty();
            foreach (var c in DefaultRestrictionCriteria.CompositeCompanions)
            {
                var composite = StatementLabelTerms.Composites.Single(t => StatementLabelTerms.PatternsOf(t).Contains(c.Companion));
                composite.Contains.Should().Contain(w => DietPatternRule.For(c.Pattern, c.Type).IsMatch("1 cup " + w), $"{c.Type}: {c.Pattern} restricts what {composite.Term} is made of");
                DietPatternRule.For(c.Companion).IsMatch("1 cup " + c.Companion.Trim('%')).Should().BeTrue();
                DefaultRestrictionCriteria.ByName[c.Type].Patterns.Should().Contain(c.Companion);
            }
            DefaultRestrictionCriteria.CompositeCompanions.Should().Contain(c => c.Type == "Milk Allergy" && c.Companion == "%goldfish%")
                .And.Contain(c => c.Type == "Celiac Disease" && c.Companion == "%licorice%");
        }

        [Fact]
        public async Task A_branded_food_a_line_is_linked_to_carries_what_its_read_statement_names()
        {
            using var db = await HouseholdAsync("Milk Allergy");
            var crackers = new IngredientEntity { Name = "Baked snack crackers (Brand)", CurationStatusId = (long)CurationStatusEnum.PendingCuration, FdcDataType = CatalogAdmissionRule.BrandedFood, FdcCategory = "Crackers & Biscotti", FdcId = "999001" };
            db.Ingredients.Add(crackers);
            await db.SaveChangesAsync();
            await new PreparedContentsService(db).RecordAsync(new[] { new ContentsStatement(crackers.Id, PreparedContentsService.FdcSource("999001"), "Enriched wheat flour (wheat flour, niacin), cheddar cheese (cultured milk, salt, enzymes), sunflower oil, salt") }, "fdc-contents-test", apply: true);
            var recipe = await LineAsync(db, "1 cup snack crackers", crackers.Id);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            set.IngredientIds.Should().Contain(crackers.Id, "the label names cheddar cheese");

            using var other = await HouseholdAsync("Peanut Allergy");
            other.Ingredients.Add(new IngredientEntity { Id = crackers.Id, Name = crackers.Name, CurationStatusId = crackers.CurationStatusId, FdcDataType = crackers.FdcDataType, FdcCategory = crackers.FdcCategory });
            await other.SaveChangesAsync();
            await new PreparedContentsService(other).RecordAsync(new[] { new ContentsStatement(crackers.Id, PreparedContentsService.FdcSource("999001"), "Enriched wheat flour, cheddar cheese, salt") }, "fdc-contents-test", apply: true);
            (await new HouseholdRestrictionResolver(other).ResolveAsync(HouseholdId)).IngredientIds.Should().NotContain(crackers.Id);
            recipe.Id.Should().BePositive();
        }

        [Theory]
        [InlineData("Vegetarian", "Cheese (pasteurized milk, cheese cultures, salt, animal rennet), wheat flour, water", PreparedContentsHeld.Restricted, "animal rennet (veal)")]
        [InlineData("Hindu Vegetarian", "Cheese (pasteurized milk, cheese cultures, salt, animal rennet), wheat flour, water", PreparedContentsHeld.Restricted, "animal rennet (veal)")]
        [InlineData("Jain", "Cheese (pasteurized milk, cheese cultures, salt, rennet), rice, water", PreparedContentsHeld.Unresolved, "rennet (may be veal")]
        [InlineData("Vegetarian", "Cheese (pasteurized milk, cheese cultures, salt, rennet), wheat flour, water", PreparedContentsHeld.Unresolved, "rennet (may be veal")]
        [InlineData("Vegetarian", "Cheese (pasteurized milk, cheese cultures, salt, enzymes), wheat flour, water", PreparedContentsHeld.Unresolved, "enzymes (may be veal")]
        [InlineData("Vegetarian", "Cheese (pasteurized milk, cheese cultures, salt, microbial rennet), wheat flour, water", null, null)]
        [InlineData("Vegan", "Wheat flour, water, yeast, salt, L-cysteine", PreparedContentsHeld.Unresolved, "l-cysteine (may be poultry")]
        [InlineData("Vegetarian", "Enriched flour, water, yeast, salt, L-cysteine hydrochloride", PreparedContentsHeld.Unresolved, "l-cysteine hydrochloride (may be poultry")]
        [InlineData("Halal", "Enriched flour, water, yeast, salt, L-cysteine", PreparedContentsHeld.Unresolved, "l-cysteine (may be pork")]
        [InlineData("Celiac Disease", "Rice, water, seasoning, salt", PreparedContentsHeld.Unresolved, "seasoning")]
        [InlineData("Vegetarian", "Rice, water, seasonings, salt", PreparedContentsHeld.Unresolved, "seasonings")]
        [InlineData("Celiac Disease", "Seasoned rice, water, salt", PreparedContentsHeld.Unresolved, "seasoned rice")]
        [InlineData("Celiac Disease", "Rice, seasoning (salt, garlic powder), water", null, null)]
        [InlineData("Celiac Disease", "Grilled seasoned chicken breast meat with rib meat (chicken breast meat with rib meat, water, salt, garlic powder), rice", null, null)]
        [InlineData("Celiac Disease", "Water, miso, onions, salt", PreparedContentsHeld.Unresolved, "miso (may be barley")]
        [InlineData("Soy Allergy", "Water, miso, onions, salt", PreparedContentsHeld.Restricted, "miso")]
        [InlineData("Fish Allergy", "Water, miso, onions, salt", null, null)]
        public async Task A_label_term_of_unstated_animal_or_grain_source_or_an_unnamed_seasoning_is_never_read_as_free_of_it(string type, string statement, string? held, string? name)
        {
            using var db = await HouseholdAsync(type);
            var verdict = await JudgeAsync(db, await MealAsync(db, statement));
            verdict.Held.Should().Be(held, $"{type} on \"{statement}\": {string.Join("; ", verdict.Names)}");
            if (name != null) verdict.Names.Should().Contain(n => n.StartsWith(name, StringComparison.Ordinal));
        }
    }
}
