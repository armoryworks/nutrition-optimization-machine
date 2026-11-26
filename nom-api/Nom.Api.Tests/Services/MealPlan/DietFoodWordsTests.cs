using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The reading of a line's food words for a severe set (<see cref="DietFoodWords"/>, staging checkpoint 7, S3), the
    /// recipe text it is read beside (S4, S5), a named tree nut in a prepared meal's statement (R7) and a drink offered as
    /// an alternative (S12).
    /// </summary>
    public class DietFoodWordsTests
    {
        [Theory]
        [InlineData("4 sponge-fingers", "sponge|finger")]
        [InlineData("1 1/2 cups finely chopped onions, divided", "onion")]
        [InlineData("2 large chocolate chip cookies + 1 more to crumble on top", "chocolate|chip|cookie|crumble")]
        [InlineData("⅓ cup Homogenized Spry", "homogenized|spry")]
        [InlineData("Salt and pepper to taste", "salt|pepper")]
        [InlineData("Garnish (optional)", "")]
        public void A_lines_food_words_leave_out_amounts_measures_grammar_and_line_words(string line, string words)
        {
            DietFoodWords.Words(line).Should().Equal(words.Split('|', StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public void The_line_words_are_derived_words_beside_foods_and_none_is_a_food_a_line_names_alone()
        {
            var words = DietFoodWords.LineWords;
            words.Should().NotBeEmpty();
            words.Should().OnlyContain(w => w.Foods >= 20 && w.Lines > 0 && w.Alone < 20);
            words.Select(w => w.Word).Should().Contain(new[] { "optional", "chopped", "divided" }).And.NotContain(new[] { "dripping", "spry", "sponge", "balsamic" });
            DietFoodWords.ParseLineWords("# c\nword\tfoods\tlines\talone\town\nzest\t21\t30\t0\t1\n").Single().Word.Should().Be("zest");
            FluentActions.Invoking(() => DietFoodWords.ParseLineWords("word\tfoods\tlines\talone\town\nx\t21\t30\t0\t1\nx\t20\t1\t0\t0\n")).Should().Throw<System.IO.InvalidDataException>();
        }

        [Fact]
        public void Sulfite_reads_the_dried_form_of_a_fruit_by_kind_from_the_vocabularys_marker_terms()
        {
            var kinds = DietFoodWords.KindsOf(DefaultRestrictionCriteria.ByName["Sulfite Sensitivity"].Patterns);
            kinds.Should().ContainSingle(k => k.Word == "dried" && k.Categories.Contains(9));
            DietFoodWords.KindsOf(DefaultRestrictionCriteria.ByName["Peanut Allergy"].Patterns).Should().BeEmpty();
        }

        private static readonly DietFoodWords.Food[] Catalog =
        {
            new(1, "Rice", 20, "Rice, white, long-grain, raw"),
            new(2, "Cookies, brownies, commercially prepared", 18, "Cookies, brownies, commercially prepared"),
            new(3, "Restaurant, Chinese, vegetable lo mein, without meat", 36, null),
            new(4, "Cookies, peanut butter", 18, "Cookies, peanut butter, commercially prepared"),
            new(5, "Currants", 9, "Currants, red and white, raw"),
            new(6, "Breaded Chicken", 5, "Chicken, broilers or fryers, breast, meat only, cooked, breaded"),
            new(7, "Chicken Breast", 5, "Chicken, broilers or fryers, breast, meat only, raw"),
            new(8, "Balsamic Vinegar", 2, "Vinegar, balsamic"),
        };

        private static DietFoodWords.Vocabulary Wheat(DietFoodWords.Table table) =>
            DietFoodWords.Build(table,
                i => table.Foods[i].Id is 2 or 4 or 6,
                i => table.Foods[i].Id switch
                {
                    2 or 4 => new IReadOnlyList<string>[] { new[] { "cookie" } },
                    6 => new IReadOnlyList<string>[] { new[] { "breaded" } },
                    _ => Array.Empty<IReadOnlyList<string>>(),
                },
                new HashSet<string> { "cookie", "breaded" }, Array.Empty<DietFoodWords.Kind>());

        [Fact]
        public void A_word_is_flagged_only_where_the_catalog_knows_it_on_flagged_foods_clear_on_plain_clear_foods_and_unknown_otherwise()
        {
            var table = DietFoodWords.TableOf(Catalog);
            var wheat = Wheat(table);
            wheat.ClassOf(table.IndexOf("brownie")).Should().Be(DietFoodWords.WordClass.Flagged, "a brownie is known only as a kind of cookie");
            wheat.ClassOf(table.IndexOf("rice")).Should().Be(DietFoodWords.WordClass.Clear);
            wheat.ClassOf(table.IndexOf("chicken")).Should().Be(DietFoodWords.WordClass.Clear, "breaded chicken is flagged by its term, not by chicken");
            wheat.ClassOf(table.IndexOf("mein")).Should().Be(DietFoodWords.WordClass.NotKnown, "a restaurant dish's clear verdict is no knowledge of what it is made of");
            wheat.ClassOf(table.IndexOf("cookie")).Should().Be(DietFoodWords.WordClass.Ruled);
            wheat.Read(table.Read("1 Salted Caramel Skillet Brownie")).Value.Should().Be(DietFoodWords.WordClass.Flagged);
            wheat.Read(table.Read("1 pound dry lo mein")).Value.Should().Be(DietFoodWords.WordClass.NotKnown);
            wheat.Read(table.Read("2 cups white rice, raw")).Value.Should().Be(DietFoodWords.WordClass.Clear);
            wheat.Read(table.Read("rolled gaufrettes")).Should().Be(new DietFoodWords.LineReading(DietFoodWords.WordClass.NotKnown, -1));
        }

        [Fact]
        public void A_method_word_holds_where_it_is_what_a_flagged_food_is_not_where_it_only_qualifies_it()
        {
            var table = DietFoodWords.TableOf(Catalog.Concat(new DietFoodWords.Food[]
            {
                new(10, "Baking Powder", 18, "Leavening agents, baking powder, double-acting"),
                new(13, "Vanilla Wafers", 18, "Wafers, vanilla"),
                new(11, "Sponge Cake", 18, "Cake, sponge, commercially prepared"),
                new(12, "Water", 14, "Water, tap"),
            }));
            var soy = DietFoodWords.Build(table, i => table.Foods[i].Id is 10 or 11 or 13, i => table.Foods[i].Id == 11 ? new IReadOnlyList<string>[] { new[] { "cake" } } : Array.Empty<IReadOnlyList<string>>(),
                new HashSet<string> { "cake" }, Array.Empty<DietFoodWords.Kind>());
            int Of(string w) => table.IndexOf(w);
            soy.ClassOf(Of("leavening")).Should().Be(DietFoodWords.WordClass.Flagged);
            soy.HoldsInMethod(Of("leavening")).Should().BeFalse("review ck16-5: a word of a first part of more words is no food alone (\"onion rings\", \"ice creams\")");
            soy.HoldsInMethod(Of("leavening"), Of("agent")).Should().BeTrue();
            soy.HoldsInMethod(Of("wafer")).Should().BeTrue();
            soy.HoldsInMethod(Of("sponge")).Should().BeFalse("\"until the sponge is light\" of a gelatine sponge");
            soy.HoldsInMethod(Of("sponge"), Of("cake")).Should().BeTrue("\"slices of sponge cake\"");
            soy.HoldsInMethod(Of("leavening"), Of("water")).Should().BeFalse("water is a word the set reads as clear");
            DietFoodWords.MethodPairs("Mince the onion and line with sponge cake.", new HashSet<string>()).Should()
                .Contain(("mince", null)).And.Contain(("sponge", "cake"));
        }

        [Fact]
        public void The_method_foods_are_derived_words_the_corpus_names_as_a_food_and_a_tool_is_none()
        {
            DietFoodWords.MethodFoods.Should().NotBeEmpty();
            DietFoodWords.MethodFoods.Should().OnlyContain(w => w.Recipes >= 2 && w.Recipes * 10 >= w.Methods);
            DietFoodWords.IsMethodFood("rizotto").Should().BeTrue();
            DietFoodWords.IsMethodFood("pullet").Should().BeTrue();
            foreach (var tool in new[] { "pan", "oven", "cloth", "paper", "dripping", "ice" }) DietFoodWords.IsMethodFood(tool).Should().BeFalse(tool);
            DietFoodWords.IsMethodFood("cider").Should().BeFalse("its lines are fewer than a fifth of the methods that name it");
            DietFoodWords.IsMethodFood("cider", DietFoodWords.MethodFoodShare / 2).Should().BeTrue();
            DietFoodWords.ParseMethodFoods("# c\nword\tfoods\trecipes\tmethods\nrizotto\t1\t4\t11\n").Single().Word.Should().Be("rizotto");
        }

        [Fact]
        public void A_kind_flags_a_treatment_word_beside_a_head_word_of_its_categories_and_the_treatment_word_alone_is_ruled()
        {
            var table = DietFoodWords.TableOf(Catalog.Append(new DietFoodWords.Food(9, "Dried Oregano", 2, "Spices, oregano, dried")));
            var sulfite = DietFoodWords.Build(table, i => table.Foods[i].Id == 8, i => table.Foods[i].Id == 8 ? new IReadOnlyList<string>[] { new[] { "vinegar" } } : Array.Empty<IReadOnlyList<string>>(),
                new HashSet<string> { "vinegar" }, new[] { new DietFoodWords.Kind("dried", new HashSet<int> { 9 }) });
            sulfite.KindIn(table.Read("dried currants")).Should().Be(table.IndexOf("dried"));
            sulfite.KindIn(table.Read("1 tsp dried oregano")).Should().Be(-1);
            sulfite.Read(table.Read("1 tsp dried oregano")).Value.Should().Be(DietFoodWords.WordClass.Clear);
            sulfite.ClassOf(table.IndexOf("balsamic")).Should().Be(DietFoodWords.WordClass.Flagged, "balsamic is known only as a kind of vinegar");
        }

        [Theory]
        [InlineData("Make the same as vanilla, using strong coffee.")]
        [InlineData("Prepare the same as white potatoes.")]
        [InlineData("Use recipe for Calumet One-egg Cake, adding grated orange rind.")]
        [InlineData("Roll out as for pie crust and bake.")]
        [InlineData("Cream the butter (see page 34).")]
        [InlineData("Boil it. This recipe is incomplete.")]
        [InlineData("Cut into rounds, prepared as above.")]
        [InlineData("Beat sixteen eggs very well in")]
        [InlineData("Clean and cut up the bird according to directions previously given.")]
        public void A_method_that_sends_the_cook_to_another_recipe_or_is_marked_incomplete_builds_on_another(string method)
        {
            DietRecipeText.BuildsOnAnother(method).Should().BeTrue();
        }

        [Fact]
        public void A_line_that_only_changes_an_amount_of_another_recipe_builds_on_it_and_a_plain_method_does_not()
        {
            DietRecipeText.BuildsOnAnother("Add 2 tbsp. of sugar, and use one egg.", new[] { "2 tbsp. of sugar", "one egg", "only 1/2 cup of milk" }).Should().BeTrue("69144 Scones gives only what differs from the biscuits");
            DietRecipeText.BuildsOnAnother("Beat the eggs and fold in the flour.", new[] { "2 eggs", "1 cup flour" }).Should().BeFalse();
        }

        [Theory]
        [InlineData("Chopped nuts may be added if desired.", "")]
        [InlineData("This sauce is used for boiled chicken or fish.", "")]
        [InlineData("Pass separately either a French or Mayonnaise dressing.", "")]
        [InlineData("A plum pudding made without eggs.", "A plum pudding made")]
        [InlineData("Shape the meat like a loaf of bread.", "Shape the meat")]
        public void An_optional_addition_a_use_a_negation_or_a_simile_is_not_read_as_the_dishs_own_food(string sentence, string read)
        {
            string.Join(" ", DietRecipeText.Sentences(sentence)).Should().Be(read);
        }

        [Theory]
        [InlineData("Sift the flour with the salt and add sugar if desired.", "flour", "sugar")]
        [InlineData("Add, if liked, a little sherry and stir in the flour.", "flour", "sherry")]
        [InlineData("Mix the flour, sugar and nutmeg (optional).", "flour", "nutmeg")]
        [InlineData("Sift the flour, and raisins may be added if desired, then bake.", "flour", "raisins")]
        [InlineData("If desired, add chopped nuts and fold in the beaten eggs.", "eggs", "nuts")]
        public void An_optional_addition_leaves_the_rest_of_its_sentence_read(string sentence, string read, string left)
        {
            var text = string.Join(" ", DietRecipeText.Sentences(sentence));
            text.Should().Contain(read, "review ck16-1: a food that shares the sentence with an optional one is still the dish's");
            text.Should().NotContain(left);
        }

        [Fact]
        public void A_baked_good_of_flour_in_the_title_is_read_when_no_line_names_a_flour()
        {
            var bread = DietPatternRule.For("%bread%", "Celiac Disease");
            DietRecipeText.ReadsTitle("Sift together all the dry ingredients.", bread, false, "Bread", new[] { "1 cup milk", "1/2 cup nut meats" }).Should().BeTrue();
            DietRecipeText.ReadsTitle("Sift together all the dry ingredients.", bread, false, "Bread", new[] { "1 cup milk", "3 cups flour" }).Should().BeFalse();
        }

        [Theory]
        [InlineData("coconut", false)]
        [InlineData("cashews", false)]
        [InlineData("tree nuts", true)]
        [InlineData("nuts", true)]
        public void A_statement_that_names_its_tree_nut_names_no_unnamed_nut(string word, bool unnamed)
        {
            var words = IngredientStatementReader.GateWords(new StatementAllergen(word, IngredientStatementReader.ClassOf(word))).ToList();
            words.Contains("nuts").Should().Be(unnamed);
            words.Should().Contain(word);
            if (!unnamed) words.Should().Contain("almond", "the other members still name the class for the tree nut types");
        }

        [Fact]
        public void A_drink_offered_as_an_alternative_in_a_bracket_counts_as_the_lines_text()
        {
            DietCriteriaMatcher.LineCounts("1/4 cup beef stock (or I used dry red wine)").Should().BeTrue("staging checkpoint 7, S12");
            DietCriteriaMatcher.LineCounts("1/4 cup beef stock (or water)").Should().BeFalse();
            DietCriteriaMatcher.LineCounts("1/4 cup beef stock").Should().BeFalse();
        }
    }
}
