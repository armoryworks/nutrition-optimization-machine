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
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Staging checkpoint 4, the unsafe and over-flagging side of the diet gate: "free from" and "free of" something else is
    /// no "-free" label (P12), "flower" is the old spelling of flour (P12), cream tartar and USDA descriptions in reverse
    /// order are not dairy or wheat (P18), Reese's and a nut butter that names no nut are held for Peanut Allergy (P3),
    /// marshmallows for the gelatin types (P17), a bare "vegetable oil" in a statement keeps what it may be (P4), and the
    /// link re-check never lowers a recipe's diet flags by taking lines back to a wording whose contents are not known (P19).
    /// </summary>
    public class DietTermsTests
    {
        private static DietPatternRule Rule(string pattern, string? type = null) => DietPatternRule.For(pattern, type);

        private static bool TypeNames(string type, string line) =>
            DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Any(p => Rule(p, type).IsMatch(line));

        [Theory]
        [InlineData("%bread%", "3 thin slices bread free from crusts", true)]
        [InlineData("%bread%", "1 loaf day-old bread free of its crust", true)]
        [InlineData("%bread%", "1 loaf bread, free from crust", true)]
        [InlineData("%fish%", "1 lb boiled fish free of bones", true)]
        [InlineData("%fish%", "2 fillets fish free-of skin", true)]
        [InlineData("%flour%", "1 cup flour free of lumps", true)]
        [InlineData("%milk%", "1 pint milk free from cream", true)]
        [InlineData("%sugar%", "1 lb sugar free of lumps", true)]
        [InlineData("%wheat%", "1 cup wheat free from chaff", true)]
        [InlineData("%egg%", "6 eggs free-range", true)]
        [InlineData("%egg%", "1 egg free range", true)]
        [InlineData("%peanut%", "1 cup peanut-free chocolate chips", false)]
        [InlineData("%peanut%", "1 cup peanut free chocolate chips", false)]
        [InlineData("%egg%", "8 oz egg-free noodles", false)]
        [InlineData("%wheat%", "1 cup wheat-free flour blend", false)]
        [InlineData("%sugar%", "1 can sugar free soda", false)]
        [InlineData("%milk%", "1 cup milk-free yogurt", false)]
        [InlineData("%flour%", "a flourless cake", false)]
        [InlineData("%wheat%", "wheat free", false)]
        [InlineData("%wheat%", "wheat-free, of course", false)]
        [InlineData("%egg%", "egg free often", false)]
        public void Free_from_or_free_of_something_else_is_no_label_that_the_food_is_absent(string pattern, string line, bool names)
        {
            Rule(pattern).IsMatch(line).Should().Be(names);
        }

        [Fact]
        public void Every_named_exception_still_blanks_its_phrase_before_free_from_and_the_term_before_it_still_counts()
        {
            foreach (var e in DietPatternRule.All)
            {
                var type = e.Types?.First();
                var rule = Rule("%" + e.Term + "%", type);
                rule.IsMatch("1 cup " + e.Term + " free from lumps").Should().BeTrue($"\"{e.Term} free from lumps\" names {e.Term}");
                rule.IsMatch("1 cup " + e.Term + " free of lumps").Should().BeTrue($"\"{e.Term} free of lumps\" names {e.Term}");
                rule.IsMatch("1 cup " + e.Term + "-free").Should().BeFalse($"\"{e.Term}-free\" is a label that {e.Term} is absent");
                if (rule.IsMatch(e.Phrase)) continue;
                rule.IsMatch("1 cup " + e.Phrase + " free from lumps").Should().BeFalse($"{e.Phrase} is still its exception of {e.Term} before \"free from\"");
                rule.IsMatch("1 cup " + e.Phrase + ", 1 cup " + e.Term + " free of lumps").Should().BeTrue($"the {e.Term} after {e.Phrase} still counts");
            }
        }

        [Fact]
        public void The_postgres_expression_reads_free_from_and_free_of_as_the_dotnet_one_does()
        {
            var rule = Rule("%bread%");
            rule.Expression(DietRegexDialect.Postgres).Should().Contain("(?![[:space:](-]+(?:from|of)(?![[:alpha:]]))");
            rule.Expression(DietRegexDialect.DotNet).Should().Contain(@"(?![\s(-]+(?:from|of)(?!\p{L}))");
        }

        [Theory]
        [InlineData("Celiac Disease", "1 lb of fine flower, sifted", true)]
        [InlineData("Wheat Allergy", "2 cups flower", true)]
        [InlineData("Gluten-Free", "a little flower to dredge", true)]
        [InlineData("Celiac Disease", "1 head cauliflower", false)]
        [InlineData("Celiac Disease", "2 cups cauliflower florets", false)]
        [InlineData("Celiac Disease", "1/4 cup sunflower seeds", false)]
        [InlineData("Celiac Disease", "2 tbsp safflower oil", false)]
        [InlineData("Celiac Disease", "1 tsp orange-flower water", false)]
        [InlineData("Celiac Disease", "1 tsp rose flower water", false)]
        [InlineData("Celiac Disease", "2 cups broccoli flowerets", false)]
        [InlineData("Celiac Disease", "edible flowers, to garnish", false)]
        [InlineData("Celiac Disease", "12 squash flowers", false)]
        [InlineData("Celiac Disease", "2 tbsp elderflower cordial", false)]
        [InlineData("Milk Allergy", "1 cup flower", false)]
        public void Flower_is_read_as_the_old_spelling_of_flour_by_the_types_that_restrict_flour_and_never_as_a_blossom(string type, string line, bool names)
        {
            TypeNames(type, line).Should().Be(names);
        }

        [Theory]
        [InlineData("1 tsp cream tartar", false)]
        [InlineData("1 tsp cream-tartar", false)]
        [InlineData("cream tartars", false)]
        [InlineData("1/2 tsp cream of tartar", false)]
        [InlineData("1/2 cup cream, 1 tsp tartar", true)]
        [InlineData("1 cup heavy cream", true)]
        public void Cream_tartar_in_the_older_spelling_is_cream_of_tartar_and_no_dairy(string line, bool names)
        {
            Rule("%cream%").IsMatch(line).Should().Be(names);
        }

        [Theory]
        [InlineData("%flour%", "Flour, almond", false)]
        [InlineData("%flour%", "Flour, coconut", false)]
        [InlineData("%flour%", "Flour, buckwheat", false)]
        [InlineData("%flour%", "Flour, wheat, all-purpose, enriched", true)]
        [InlineData("%flour%", "Flour, rye, dark", true)]
        [InlineData("%goat%", "Cheese, goat, hard type", false)]
        [InlineData("%cheese%", "Cheese, goat, hard type", true)]
        [InlineData("%milk%", "Milk, coconut", false)]
        [InlineData("%milk%", "Milk, whole, 3.25% milkfat", true)]
        [InlineData("%cream%", "Leavening agents, cream of tartar", false)]
        [InlineData("%oyster%", "Mushrooms, oyster, raw", false)]
        public void A_usda_description_is_read_in_the_order_of_speech_too_so_a_reversed_exception_clears_it(string pattern, string description, bool names)
        {
            var rule = Rule(pattern);
            rule.IsDescriptionMatch(description).Should().Be(names);
            rule.IsAliasMatch(description, IngredientLinkService.UsdaDescriptionContext).Should().Be(names);
        }

        [Fact]
        public void Only_a_usda_description_is_read_in_the_order_of_speech_never_a_wording_or_a_line()
        {
            var flour = Rule("%flour%");
            flour.IsAliasMatch("flour, almond", "deterministic:recheck:name-key").Should().BeTrue("a list on a recipe line or a wording alias is two foods, as the exceptions read it");
            flour.IsMatch("2 cups flour, almond extract").Should().BeTrue();
            flour.IsDescriptionMatch(null).Should().BeFalse();
            DietPatternRule.SpokenOrder("Flour, almond").Should().Be("almond Flour");
            DietPatternRule.SpokenOrder("Cheese, goat, hard type").Should().Be("goat Cheese, hard type");
            DietPatternRule.SpokenOrder("Almond Flour").Should().Be("Almond Flour");
            DietPatternRule.SpokenOrder("Flour,").Should().Be("Flour,");
        }

        [Theory]
        [InlineData("Peanut Allergy", "1 cup Reese's Pieces candies", true)]
        [InlineData("Peanut Allergy", "12 Reese's Peanut Butter Cups, chopped", true)]
        [InlineData("Peanut Allergy", "3 tbsp nut butter of choice", true)]
        [InlineData("Peanut Allergy", "2 tbsp almond butter", false)]
        [InlineData("Peanut Allergy", "2 tbsp coconut butter", false)]
        [InlineData("Milk Allergy", "1 cup Reese's Pieces candies", true)]
        [InlineData("Halal", "2 cups mini marshmallows", true)]
        [InlineData("Halal", "1 jar marshmallow creme", true)]
        [InlineData("Vegetarian", "2 cups miniature marshmallows", true)]
        [InlineData("Kosher", "10 large marshmallows", true)]
        [InlineData("Egg Allergy", "1 jar marshmallow creme", true)]
        [InlineData("Egg Allergy", "10 large marshmallows", false)]
        [InlineData("Milk Allergy", "1 cup marshmallow cream", false)]
        [InlineData("Egg Allergy", "1 cup marshmallow cream", true)]
        [InlineData("Carnivore", "20 oz cheese tortellini", true)]
        [InlineData("Carnivore", "6 zwiebacks", true)]
        [InlineData("Wheat Allergy", "8 oz spaghetti", true)]
        [InlineData("Wheat Allergy", "1 small spaghetti squash", false)]
        [InlineData("Wheat Allergy", "20 oz cheese tortellini", true)]
        [InlineData("Celiac Disease", "20 oz cheese tortellini", true)]
        [InlineData("Egg Allergy", "1 lb fresh tortellini", true)]
        [InlineData("Celiac Disease", "1 cup crushed zwieback", true)]
        [InlineData("Celiac Disease", "1 lb frozen potstickers", true)]
        [InlineData("Halal", "1 lb frozen potstickers", true)]
        [InlineData("Wheat Allergy", "1 lb frozen ravioli", true)]
        [InlineData("Corn Allergy", "1 box frozen taquitos", true)]
        [InlineData("Nightshade Sensitivity", "2 cups Spanish rice", true)]
        [InlineData("Wheat Allergy", "1 lb pizza dough", true)]
        [InlineData("Vegetarian", "12 frozen wontons", true)]
        [InlineData("Vegetarian", "24 wonton wrappers", false)]
        [InlineData("Vegetarian", "24 wonton skins", false)]
        [InlineData("Wheat Allergy", "24 wonton wrappers", true)]
        [InlineData("Egg Allergy", "24 wonton wrappers", true)]
        public void A_composite_food_is_restricted_by_every_type_that_restricts_what_it_is_made_of(string type, string line, bool names)
        {
            TypeNames(type, line).Should().Be(names);
        }

        [Theory]
        [InlineData("marshmallows", "marshmallow")]
        [InlineData("mini marshmallows", "marshmallow")]
        [InlineData("marshmallow creme", "marshmallow creme")]
        [InlineData("cheese tortellini", "tortellini")]
        [InlineData("frozen potstickers", "potsticker")]
        [InlineData("wontons", "wonton")]
        [InlineData("wonton wrappers", "wonton wrapper")]
        [InlineData("zwieback", "zwieback")]
        [InlineData("reese's pieces", "reese's")]
        [InlineData("taquitos", "taquito")]
        public void A_composite_name_reads_as_its_term(string name, string term)
        {
            StatementLabelTerms.Match(name)!.Select(t => t.Term).Should().Equal(term);
        }

        [Fact]
        public void A_name_the_label_table_reads_is_a_label_item_first_and_keeps_the_catalog_food_it_also_resolves_to()
        {
            var resolver = new Dictionary<string, long>(StringComparer.Ordinal) { ["vegetable oil"] = 42, ["romanesco"] = 43, ["koshihikari rice"] = 44 };
            var oil = PreparedContentsService.Resolve(new StatementIngredient("vegetable oil", null, false), resolver);
            oil.Kind.Should().Be(ContentsKinds.Label);
            oil.FoodId.Should().Be(42);
            oil.LabelTerms()!.Single().May.Should().Contain("peanut");

            var catalogOnly = PreparedContentsService.Resolve(new StatementIngredient("romanesco", null, false), resolver);
            catalogOnly.Should().Be(new ContentsItem("romanesco", null, ContentsKinds.Food, 43));
        }

        [Fact]
        public async Task A_bare_vegetable_oil_that_the_catalog_also_names_keeps_a_meal_unknown_for_peanut_allergy()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 910, PersonId = 91, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = 66000, Name = "Peanut Allergy" });
            db.Restrictions.Add(new RestrictionEntity { PersonId = 91, Name = "Peanut Allergy", RestrictionTypeId = 66000 });
            db.Ingredients.Add(new IngredientEntity { Name = "Vegetable Oil", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 4 });
            var meal = new IngredientEntity { Name = "Eggplant parmigiana bowl", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 22 };
            db.Ingredients.Add(meal);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new PreparedContentsService(db).RecordAsync(new[] { new ContentsStatement(meal.Id, PreparedContentsService.FdcSource("283237"), "Rice, vegetable oil, salt") }, "fdc-contents-test", apply: true);

            var row = await db.IngredientContents.SingleAsync(c => c.IngredientId == meal.Id);
            PreparedContentsService.Items(row).Should().Contain(i => i.Name == "vegetable oil" && i.Kind == ContentsKinds.Label && i.FoodId != null);
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(910);
            var verdict = (await PreparedContentsGate.JudgeAsync(db, 910, set, new[] { meal.Id }))[meal.Id];
            verdict.Held.Should().Be(PreparedContentsHeld.Unresolved, "a vegetable oil of unstated source may be peanut oil (owner-reported decision: unstated oil sources stay on the safe side)");
            verdict.Names.Should().Contain(n => n.StartsWith("vegetable oil (may be peanut", StringComparison.Ordinal));
        }

        [Fact]
        public void The_re_check_never_takes_lines_back_to_a_wording_of_unknown_contents_where_a_diet_flag_would_drop()
        {
            var guard = RecheckDietGuard.For();
            var hamburger = new GateFood(277965, "Plain Fast Food Hamburger", "Fast foods, hamburger; single, regular patty; plain");
            var patty = guard.Lowered(hamburger, "patty", new[] { "patty" });
            patty.Should().Contain(new[] { "Vegetarian", "Vegan" }, "a patty of unknown contents leaving a hamburger would lower the meat types' flag");
            RecheckDietGuard.Reason(patty).Should().StartWith(RecheckDietGuard.HeldReason + " (");

            var biscuit = new GateFood(277584, "Fast food, biscuit");
            guard.Lowered(biscuit, "the food", new[] { "1 tablespoonful of the food" }).Should().Contain("Wheat Allergy");

            var tortellini = new GateFood(279321, "Cheese Tortellini", "Tortellini, pasta with cheese filling, fresh-refrigerated, as purchased");
            guard.Lowered(tortellini, "cheese tortellini", new[] { "20 ounces cheese tortellini" }).Should().BeEmpty("cheese tortellini is the tortellini composite, whose contents are known");
            foreach (var type in DefaultRestrictionCriteria.ByName.Keys.Where(t => TypeNames(t, tortellini.Description!)))
                TypeNames(type, "20 ounces cheese tortellini").Should().BeTrue($"{type} read the USDA description as restricted, and the composite keeps its flag");

            var breaded = new GateFood(276291, "Breaded Chicken Thighs", "Chicken, thighs, frozen, breaded, reheated");
            RecheckDietGuard.Known("chicken thighs").Should().BeTrue();
            var thighs = guard.Lowered(breaded, "chicken thighs", new[] { "6 chicken thighs" });
            thighs.Should().Contain("Wheat Allergy").And.OnlyContain(t => DefaultRestrictionCriteria.ByName[t].Severity >= RecheckDietGuard.SevereFrom,
                "chicken thighs are known by the label table, so for a diet of choice the breading the prepared food had may leave with it, but known contents never account for a severe type the food named (staging checkpoint 7, S11)");

            var vinegar = new GateFood(9, "Apple Cider Vinegar");
            guard.Lowered(vinegar, "qt sweet cider", new[] { "1 qt sweet cider" }).Should().Contain("Sulfite Sensitivity", "a take-back to an unknown wording keeps the flag the vinegar gave");
            guard.Lowered(vinegar, "qt hard cider", new[] { "1 qt hard cider" }).Should().NotContain("Sulfite Sensitivity", "hard cider is a fruit wine the sulfite types hold by %hard cider% (staging checkpoint 5, Q13), so its own words keep the flag");
        }

        [Theory]
        [InlineData("Soy Protein Crumbles", "tvp", "1 cup tvp", "Soy Allergy")]
        [InlineData("Soy Protein Crumbles", "textured vegetable protein", "1 cup textured vegetable protein", "Soy Allergy")]
        [InlineData("Dry Milk Powder", "lactose", "2 tbsp lactose", "Milk Allergy")]
        [InlineData("Whole Wheat Berries", "einkorn", "2 cups einkorn", "Wheat Allergy")]
        [InlineData("Whole Wheat Berries", "emmer", "2 cups emmer", "Celiac Disease")]
        [InlineData("Dried Egg White", "lysozyme", "1 tsp lysozyme", "Egg Allergy")]
        public void A_label_name_the_vocabulary_carries_keeps_the_flag_by_the_lines_own_words(string food, string wording, string line, string type)
        {
            RecheckDietGuard.Known(wording).Should().BeTrue();
            TypeNames(type, line).Should().BeTrue($"{wording} is a label name of the class {type} restricts (diet-vocabulary.tsv, staging checkpoint 5)");
            RecheckDietGuard.For().Lowered(new GateFood(1, food), wording, new[] { line }).Should().NotContain(type, "the line's own words keep the flag, so a take-back lowers nothing");
        }

        [Theory]
        [InlineData("Veal Rennet Tablets", "animal rennet", "1 tsp animal rennet", "Vegetarian")]
        [InlineData("Veal Rennet Tablets", "calf rennet", "1 tsp calf rennet", "Vegetarian")]
        public void A_known_wording_whose_contents_the_type_restricts_but_its_name_does_not_say_keeps_the_flag(string food, string wording, string line, string type)
        {
            RecheckDietGuard.Known(wording).Should().BeTrue();
            RecheckDietGuard.For().Lowered(new GateFood(1, food), wording, new[] { line }).Should().Contain(type,
                "the gate reads a recipe line by its food's name and composite companions only, so a contents word the name does not carry would drop the flag (review CK9T-1)");
        }

        [Fact]
        public void A_move_is_judged_by_the_targets_name_and_description()
        {
            var guard = RecheckDietGuard.For();
            var hamburger = new GateFood(277965, "Plain Fast Food Hamburger", "Fast foods, hamburger; single, regular patty; plain");
            guard.Lowered(hamburger, new GateFood(2, "Patty"), new[] { "1 patty" }).Should().Contain("Vegetarian");
            guard.Lowered(hamburger, new GateFood(3, "Ground Beef Patty", "Beef, ground, patties, frozen, cooked"), new[] { "1 patty" }).Should().NotContain("Vegetarian");
            RecheckDietGuard.Reason(new[] { "Vegetarian" }, new GateFood(2, "Patty")).Should().Be(RecheckDietGuard.HeldReason + " (a move to Patty would lower Vegetarian)");
            var liqueur = new GateFood(4, "Coffee Cream Liqueur", "Alcoholic beverage, liqueur, coffee with cream, 34 proof");
            var spirit = new GateFood(5, "Distilled Spirit", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", FdcId: "174815");
            guard.Lowered(liqueur, spirit, new[] { "4 tablespoonfuls of brandy" }).Should().BeEmpty("a USDA food's description is its own record of what it is, and the gate reads it");
            guard.Lowered(liqueur, spirit with { FdcId = null }, new[] { "4 tablespoonfuls of brandy" }).Should().Contain("Milk Allergy");
        }

        [Theory]
        [InlineData("Milk Allergy", "1 cup chopped peanut butter cups", true)]
        [InlineData("Soy Allergy", "1 cup chopped peanut butter cups", true)]
        [InlineData("Milk Allergy", "2 cups crushed peanut butter cookies", true)]
        [InlineData("Wheat Allergy", "2 cups crushed peanut butter cookies", true)]
        [InlineData("Milk Allergy", "1 coconut cream pie", true)]
        [InlineData("Dairy-Free", "1 banana cream pie", true)]
        [InlineData("Egg Allergy", "1 coconut cream pie", true)]
        [InlineData("Milk Allergy", "1 can coconut cream", false)]
        [InlineData("Milk Allergy", "1/2 cup peanut butter", false)]
        public void A_name_that_holds_an_exception_phrase_inside_a_food_made_with_the_restricted_thing_is_a_composite(string type, string line, bool names)
        {
            TypeNames(type, line).Should().Be(names);
        }

        [Theory]
        [InlineData("% oat%", "Cereals, oats, regular and quick, not fortified, dry", true)]
        [InlineData("oat%", "Oats, whole grain, rolled", true)]
        [InlineData("%ham%", "Pork, cured, ham, boneless, regular", true)]
        [InlineData("%flour%", "Flour, almond", false)]
        [InlineData("%flour%", "Flour, almond, with wheat flour", true)]
        public void The_order_of_speech_only_clears_a_description_by_a_named_exception_never_by_where_a_term_begins(string pattern, string description, bool names)
        {
            Rule(pattern).IsDescriptionMatch(description).Should().Be(names);
            Rule(pattern).IsAliasMatch(description, IngredientLinkService.UsdaDescriptionContext).Should().Be(names);
        }

        [Fact]
        public void The_guard_reads_a_stored_type_beside_the_defaults()
        {
            var stored = new[] { new DietDataCriterion(1, 70000, null, null, null, null, 5, null, "%patty%", "Our burger rule") };
            var food = new GateFood(1, "Patty Melt");
            RecheckDietGuard.For(stored).Lowered(food, "melt", new[] { "melt" }).Should().Contain("Our burger rule");
            RecheckDietGuard.For().Lowered(food, "melt", new[] { "melt" }).Should().NotContain("Our burger rule");
        }
    }
}
