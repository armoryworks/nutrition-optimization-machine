using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Staging checkpoint 5, what the diet gate knows: the wheat and gluten types read the foods of the categories that carry
    /// wheat and the classic composites (Q5), the sulfite types read the sulfiting agents a statement names (Q7) and the
    /// fortified wines (Q13), a vegetable oil whose bracket names its oils is decided by them (Q8), and the over-flags of
    /// Q19 are named exceptions or a USDA description read in another order of speech.
    /// </summary>
    public class DietCoverageTests
    {
        private const long HouseholdId = 910, PersonId = 91;

        private static bool TypeNames(string type, string line) =>
            DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Any(p => DietPatternRule.For(p, type).IsMatch(line));

        private static bool TypeNamesFood(string type, string name, string description) =>
            DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p))
                .Any(p => DietPatternRule.For(p, type).IsMatch(name) || DietPatternRule.For(p, type).IsDescriptionMatch(description));

        [Theory]
        [InlineData("Celiac Disease", "1 box angel food cake mix")]
        [InlineData("Wheat Allergy", "2 slices of stale sponge cake")]
        [InlineData("Wheat Allergy", "1 French baguette")]
        [InlineData("Celiac Disease", "2 cups Bisquick")]
        [InlineData("Wheat Allergy", "8 oz uncooked fettuccine")]
        [InlineData("Wheat Allergy", "1/2 cup graham meal")]
        [InlineData("Celiac Disease", "12 lady fingers")]
        [InlineData("Wheat Allergy", "1 lb rigatoni")]
        [InlineData("Celiac Disease", "4 oz vermicelli")]
        [InlineData("Wheat Allergy", "1 cup broken macaroni")]
        [InlineData("Wheat Allergy", "1 package frozen pizza rolls")]
        [InlineData("Celiac Disease", "3 cups panettone, cubed")]
        [InlineData("Egg Allergy", "1 cup Allemande sauce")]
        [InlineData("Egg Allergy", "2 cups sponge cake crumbs")]
        [InlineData("Sulfite Sensitivity", "4 maraschino cherries, sliced")]
        [InlineData("Sulfites Sensitivity", "sodium metabisulfite")]
        [InlineData("Sulfite Sensitivity", "1/2 cup dry sherry")]
        [InlineData("Sulfite Sensitivity", "1/2 cup ruby port")]
        [InlineData("Sulfite Sensitivity", "2 tbsp Shaoxing")]
        [InlineData("Tree Nut Allergy", "1 cup chopped nuts")]
        [InlineData("Peanut Allergy", "1 cup chopped nuts")]
        [InlineData("Fish Allergy", "1 lb carp fillets")]
        [InlineData("Shellfish Allergy", "1 dozen escargots")]
        [InlineData("Sesame Allergy", "2 tbsp benne seeds")]
        [InlineData("Soy Allergy", "1 cup textured vegetable protein")]
        [InlineData("Milk Allergy", "1 cup half-and-half")]
        [InlineData("Wheat Allergy", "1 slice apple pie")]
        [InlineData("Celiac Disease", "2 chicken pot pies")]
        [InlineData("Wheat Allergy", "1 baked 9-inch piecrust")]
        [InlineData("Fish Allergy", "1 lb ling fillets")]
        [InlineData("Fish Allergy", "2 lb northern pike")]
        [InlineData("Fish Allergy", "1 lb smoked eel")]
        [InlineData("Fish Allergy", "1 lb shad roe")]
        [InlineData("Vegetarian", "1 lb smoked eel")]
        [InlineData("Vegetarian", "2 lb northern pike")]
        [InlineData("Vegetarian", "6 chicken drumsticks")]
        [InlineData("Sesame Allergy", "2 tbsp til seeds")]
        [InlineData("Wheat Allergy", "1 package egg roll wrappers")]
        [InlineData("Wheat Allergy", "3 cups bread stuffing")]
        [InlineData("Celiac Disease", "1 box stuffing mix")]
        [InlineData("Wheat Allergy", "4 cups stuffing")]
        public void A_severe_type_reads_the_vocabulary_of_its_class(string type, string line)
        {
            TypeNames(type, line).Should().BeTrue($"{type} restricts what \"{line}\" names (diet-vocabulary.tsv, label-terms.tsv)");
        }

        [Theory]
        [InlineData("Celiac Disease", "1 cup sorghum flour")]
        [InlineData("Wheat Allergy", "10 corn tortillas")]
        [InlineData("Milk Allergy", "1/2 cup apple butter")]
        [InlineData("Egg Allergy", "1 tbsp flaxseed meal (to make a flax egg)")]
        [InlineData("Halal", "4 slices meatless bacon")]
        [InlineData("Celiac Disease", "1 spaghetti squash")]
        [InlineData("Celiac Disease", "1 jar spaghetti sauce")]
        [InlineData("Wheat Allergy", "1 bunch parsley")]
        [InlineData("Wheat Allergy", "1 cake compressed yeast")]
        [InlineData("Peanut Allergy", "1/2 cup pine nuts")]
        [InlineData("Fish Allergy", "6 chicken drumsticks")]
        [InlineData("Sesame Allergy", "1 cup lentils")]
        [InlineData("Sulfite Sensitivity", "1 lb portobello mushrooms")]
        [InlineData("Sulfite Sensitivity", "2 tbsp unsulphured molasses")]
        [InlineData("Wheat Allergy", "1 (2-inch) piece fresh ginger")]
        [InlineData("Celiac Disease", "2 chicken breasts, cut into 1-inch pieces")]
        [InlineData("Grain-Free", "1 piece of kombu")]
        [InlineData("Paleo", "2 lb beef chuck, cut into pieces")]
        [InlineData("Vegetarian", "12 oz linguine")]
        [InlineData("Vegan", "1/4 cup lingonberry jam")]
        [InlineData("Plant-Based", "12 oz linguini")]
        [InlineData("Vegetarian", "8 pikelets")]
        [InlineData("Hindu Vegetarian", "2 drumsticks (moringa), cut")]
        [InlineData("Jain", "2 drumsticks (moringa), cut")]
        [InlineData("Vegan", "1 cup sourdough bread, San Francisco style")]
        [InlineData("Fish Allergy", "3 cloves garlic (unpeeled)")]
        [InlineData("Vegetarian", "1 lb unpeeled red potatoes")]
        [InlineData("Jain", "1 cup unpeeled chestnuts")]
        [InlineData("Sesame Allergy", "8 oz Tillamook sharp cheddar, shredded")]
        [InlineData("Sesame Allergy", "simmer till thick")]
        [InlineData("Shellfish Allergy", "1 package egg roll wrappers")]
        [InlineData("Halal", "1 package egg roll wrappers")]
        [InlineData("Kosher", "20 egg roll wrappers")]
        [InlineData("Wheat Allergy", "4 poblano peppers, for stuffing")]
        [InlineData("Celiac Disease", "8 large tomatoes, for stuffing")]
        public void A_look_alike_stays_clear(string type, string line)
        {
            TypeNames(type, line).Should().BeFalse($"\"{line}\" holds nothing {type} restricts");
        }

        [Theory]
        [InlineData("Wheat Allergy", "Corn Tortillas", "Tortillas, ready-to-bake or -fry, corn", false)]
        [InlineData("Wheat Allergy", "Flour Tortillas", "Tortillas, ready-to-bake or -fry, flour, refrigerated", true)]
        [InlineData("Milk Allergy", "Apple Butter", "Fruit butters, apple", false)]
        [InlineData("Celiac Disease", "Broccoli Florets", "Broccoli, flower clusters, raw", false)]
        [InlineData("Celiac Disease", "Sorghum Flour", "Flour, sorghum", false)]
        [InlineData("Halal", "Meatless Bacon", "Bacon, meatless", false)]
        [InlineData("Soy Allergy", "Meatless Bacon", "Bacon, meatless", true)]
        [InlineData("Celiac Disease", "Spaghetti Squash", "Squash, winter, spaghetti, raw", false)]
        [InlineData("Wheat Allergy", "Pizza Rolls", "Pizza rolls, frozen, unprepared", true)]
        [InlineData("Sulfite Sensitivity", "Maraschino Cherries", "Maraschino cherries, canned, drained", true)]
        public void A_food_is_read_by_its_name_and_its_usda_description_in_each_order_of_speech(string type, string name, string description, bool flagged)
        {
            TypeNamesFood(type, name, description).Should().Be(flagged);
        }

        [Fact]
        public void Each_later_part_of_a_description_is_read_before_its_first_and_the_other_parts_stay_in_order()
        {
            DietPatternRule.SpokenOrders("Squash, winter, spaghetti, raw").Should().Equal(
                "winter Squash, spaghetti, raw", "spaghetti Squash, winter, raw", "raw Squash, winter, spaghetti");
            DietPatternRule.SpokenOrders("Flour, almond").Should().Equal("almond Flour");
            DietPatternRule.SpokenOrders("Honey").Should().BeEmpty();
            DietPatternRule.SpokenOrders("Flour, almond").First().Should().Be(DietPatternRule.SpokenOrder("Flour, almond"));
        }

        [Fact]
        public void A_take_back_of_a_fortified_wine_off_a_wine_vinegar_keeps_the_sulfite_flag_by_its_own_words()
        {
            var guard = RecheckDietGuard.For();
            var vinegar = new GateFood(9, "Red Wine Vinegar");
            guard.Lowered(vinegar, "sherry", new[] { "1/2 cup sherry" }).Should().NotContain("Sulfite Sensitivity", "sherry is a wine the sulfite types hold by %sherry% (staging checkpoint 5, Q13)");
            guard.Lowered(vinegar, "madeira", new[] { "1/4 cup Madeira" }).Should().NotContain("Sulfite Sensitivity");
            guard.Lowered(vinegar, "port", new[] { "1/2 cup port" }).Should().NotContain("Sulfite Sensitivity");
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static async Task<ApplicationDbContext> HouseholdAsync(string type)
        {
            var db = NewContext();
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = 66000, Name = type });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = type, RestrictionTypeId = 66000 });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return db;
        }

        private static async Task<PreparedContentsVerdict> JudgeAsync(ApplicationDbContext db, string statement)
        {
            var food = new IngredientEntity { Name = "Meal " + Guid.NewGuid().ToString("N")[..6], CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 22 };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            await new PreparedContentsService(db).RecordAsync(new[] { new ContentsStatement(food.Id, PreparedContentsService.FdcSource(food.Id.ToString()), statement) }, "fdc-contents-test", apply: true);
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
            return (await PreparedContentsGate.JudgeAsync(db, HouseholdId, set, new[] { food.Id }))[food.Id];
        }

        [Theory]
        [InlineData("Sulfite Sensitivity", "Potatoes, water, salt, sodium bisulfite")]
        [InlineData("Sulfites Sensitivity", "Shrimp, water, salt, sodium metabisulfite as a preservative")]
        [InlineData("Sulfite Sensitivity", "Apples, sugar, freshness preserved with sodium bisulfite")]
        [InlineData("Sulfite Sensitivity", "Apricots, sugar, potassium metabisulfite")]
        [InlineData("Sulfite Sensitivity", "Coconut, sugar, sulfur dioxide")]
        public async Task A_sulfite_type_holds_a_prepared_meal_whose_statement_names_a_sulfiting_agent(string type, string statement)
        {
            using var db = await HouseholdAsync(type);
            (await JudgeAsync(db, statement)).Held.Should().Be(PreparedContentsHeld.Restricted, "the sulfiting agents a label must name (21 CFR 182) are sulfites (staging checkpoint 5, Q7)");
        }

        [Fact]
        public async Task A_sulfite_type_offers_a_meal_whose_statement_names_no_sulfite()
        {
            using var db = await HouseholdAsync("Sulfite Sensitivity");
            (await JudgeAsync(db, "Potatoes, water, salt")).Offered.Should().BeTrue();
        }

        [Fact]
        public async Task A_vegetable_oil_whose_bracket_names_its_oils_is_decided_by_them_and_a_bare_one_stays_unknown()
        {
            using var peanut = await HouseholdAsync("Peanut Allergy");
            (await JudgeAsync(peanut, "Rice, vegetable oil (soybean and/or canola oil), salt")).Offered.Should().BeTrue("the bracket names every oil, none of them peanut (staging checkpoint 5, Q8)");
            (await JudgeAsync(peanut, "Rice, vegetable oil (contains one or more of the following: soybean oil, canola oil), salt")).Offered.Should().BeTrue();
            var bare = await JudgeAsync(peanut, "Rice, vegetable oil, salt");
            bare.Held.Should().Be(PreparedContentsHeld.Unresolved, "a vegetable oil that names no source may be peanut oil (P4)");

            using var soy = await HouseholdAsync("Soy Allergy");
            (await JudgeAsync(soy, "Rice, vegetable oil (soybean and/or canola oil), salt")).Held.Should().Be(PreparedContentsHeld.Restricted, "the bracket names soybean oil");
        }

        [Theory]
        [InlineData("Potatoes, vegetable oil (non-hydrogenated), salt")]
        [InlineData("Rice, vegetable oil (preserved with TBHQ), salt")]
        [InlineData("Rice, vegetable oil (high oleic), salt")]
        [InlineData("Rice, vegetable oil (expeller pressed), salt")]
        [InlineData("Rice, vegetable oil (TBHQ), salt")]
        [InlineData("Chicken, vegetable oil (canola oil), seasoning (salt, vegetable oil, spices)")]
        public async Task A_vegetable_oil_whose_bracket_does_not_name_every_source_stays_unknown(string statement)
        {
            using var peanut = await HouseholdAsync("Peanut Allergy");
            (await JudgeAsync(peanut, statement)).Offered.Should().BeFalse("only a bracket that names the oils decides what a vegetable oil is made of; any other word, or another bare vegetable oil, leaves its source unstated");
        }

        [Theory]
        [InlineData("Shellfish Allergy")]
        [InlineData("Halal")]
        [InlineData("Kosher")]
        public async Task Egg_roll_wrappers_are_flour_and_egg_and_a_filled_egg_roll_may_hold_pork_or_shrimp(string type)
        {
            using var db = await HouseholdAsync(type);
            (await JudgeAsync(db, "Cabbage, egg roll wrappers, salt")).Offered.Should().BeTrue("egg roll wrappers are wheat flour and egg (SR: Wonton wrappers, includes egg roll wrappers)");
            (await JudgeAsync(db, "Cabbage, egg rolls, salt")).Offered.Should().BeFalse("a filled egg roll may hold pork or shrimp");
        }

        [Theory]
        [InlineData("Rice, vegetable oil (canola oil, preserved with TBHQ), salt")]
        [InlineData("Rice, vegetable oil (canola oil, TBHQ), salt")]
        [InlineData("Chicken, vegetable oil (canola oil), seasoning (salt, sunflower oil, spices)")]
        [InlineData("Rice, vegetable oil (high oleic sunflower oil), salt")]
        [InlineData("Rice, vegetable shortening (palm oil, soybean oil), salt")]
        public async Task A_vegetable_oil_whose_bracket_names_its_oils_beside_a_qualifier_is_decided_by_them(string statement)
        {
            using var peanut = await HouseholdAsync("Peanut Allergy");
            (await JudgeAsync(peanut, statement)).Offered.Should().BeTrue();
        }
    }
}
