using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    public class DietRecipeTextTests
    {
        private static DietPatternRule Rule(string pattern, string type) => DietPatternRule.For(pattern, type);

        [Theory]
        [InlineData("1 cup bakery mix", "Celiac Disease")]
        [InlineData("1/4 cup Grape Nuts", "Wheat Allergy")]
        [InlineData("chaudfroid sauce", "Celiac Disease")]
        [InlineData("boiled dressing", "Egg Allergy")]
        [InlineData("half a pint of Madeira sauce", "Celiac Disease")]
        [InlineData("Marmite", "Celiac Disease")]
        public void A_dish_that_holds_a_restricted_food_by_its_definition_is_held(string line, string type)
        {
            DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p))
                .Any(p => Rule(p, type).IsMatch(line)).Should().BeTrue($"{type} holds {line} by what the dish is made of");
        }

        [Fact]
        public void Every_dish_cites_its_source_and_no_name_stands_for_two_dishes()
        {
            DietDishes.All.Should().NotBeEmpty();
            DietDishes.All.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.Source) && d.MadeOf.Count > 0);
            DietDishes.All.SelectMany(d => d.Names.Prepend(d.Term).Select(n => (Name: DietPatternRule.Fold(n).ToLowerInvariant(), Dish: d.Term)))
                .GroupBy(n => n.Name).Where(g => g.Select(n => n.Dish).Distinct().Count() > 1).Select(g => g.Key)
                .Should().BeEmpty("a name read for two dishes would hold one by the other's contents");
            DietDishes.Composites.Should().HaveCount(DietDishes.All.Count);
        }

        [Theory]
        [InlineData("Not specified in the text", true)]
        [InlineData("ingredients not fully listed in the text", true)]
        [InlineData("None provided", true)]
        [InlineData("n/a", true)]
        [InlineData("2 cups flour", false)]
        [InlineData("none of the above, 1 egg", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void A_placeholder_line_names_no_food(string? line, bool placeholder) =>
            DietRecipeText.IsPlaceholder(line).Should().Be(placeholder);

        [Theory]
        [InlineData(null, true, true)]
        [InlineData("https://example.org/recipe", true, false)]
        [InlineData("Project Gutenberg, The Virginia Housewife", true, true)]
        [InlineData(null, false, false)]
        [InlineData("", false, false)]
        public void Only_a_book_recipe_has_its_title_and_method_read(string? source, bool imported, bool book) =>
            DietRecipeText.FromBook(source, imported ? new System.DateTime(2026, 11, 1) : null).Should().Be(book);

        [Theory]
        [InlineData("EGG AND BROWN BREAD SANDWICH", "%sandwich%", true)]
        [InlineData("HALLOWEEN SANDWICH", "%sandwich%", true)]
        [InlineData("Spaghetti con Aglio e Pomodori", "%spaghetti%", true)]
        [InlineData("Vermicelli Pudding", "%vermicelli%", true)]
        [InlineData("Sandwich Filling", "%sandwich%", false)]
        [InlineData("Cream cheese for sandwiches", "%sandwich%", false)]
        [InlineData("Clayton's Popular Sandwich Paste", "%sandwich%", false)]
        [InlineData("Spaghetti Sauce", "%spaghetti%", false)]
        [InlineData("Mince Meat for Pies", "%pie%", false)]
        [InlineData("Mrs. Shippen's Directions for Mince Pie", "%pie%", true)]
        [InlineData("Egg Sauce", "%egg%", true)]
        [InlineData("Pappa col Brodo di Muggine", "%pappa%", true)]
        public void A_title_that_names_a_dish_by_its_bread_or_pasta_is_read_whatever_the_method_and_one_that_names_what_an_accompaniment_is_for_is_not(string title, string pattern, bool read) =>
            DietRecipeText.ReadsTitle("Mix and serve.", Rule(pattern, "Celiac Disease"), false, title).Should().Be(read);

        [Fact]
        public void The_title_of_a_crust_dish_is_read_whatever_the_method_and_any_other_title_only_without_one()
        {
            var pie = Rule("%pie%", "Celiac Disease");
            var cake = Rule("%cake%", "Celiac Disease");
            DietRecipeText.ReadsTitle("Bake till brown.", pie).Should().BeTrue("a pie is made in a crust the lines may leave out");
            DietRecipeText.ReadsTitle("Fry in hot fat.", cake).Should().BeFalse("potato cakes and almond cakes are made without flour as often as with it");
            DietRecipeText.ReadsTitle(null, cake).Should().BeTrue("with no method the title is all the recipe says");
            DietRecipeText.ReadsTitle("Fry.", cake, refersElsewhere: true).Should().BeTrue("a line sends the reader to another recipe");
        }

        [Fact]
        public void A_sauce_the_title_serves_the_dish_with_is_read_and_one_it_only_names_is_not()
        {
            var caper = Rule("%caper sauce%", "Celiac Disease");
            DietRecipeText.ReadsTitle("Boil the mutton.", caper, false, "Boiled leg of mutton, caper sauce").Should().BeTrue();
            DietRecipeText.ReadsTitle("Boil the mutton.", caper, false, "Boiled leg of mutton").Should().BeFalse();
            DietRecipeText.Title("Mock Turtle Soup").Should().NotContain("Turtle");
            DietRecipeText.Title("Sauce for Boiled Cod").Trim().Should().Be("Sauce");
            DietRecipeText.Title("To souce Veal to eat like Sturgeon").Should().NotContain("Sturgeon");
        }

        [Fact]
        public void A_title_names_macaroons_or_scallops_as_what_the_dish_holds_only_where_they_are_not_the_dish_itself()
        {
            var macaroon = Rule("%macaroon%", "Egg Allergy");
            var scallop = Rule("%scallop%", "Shellfish Allergy");
            DietRecipeText.ReadsTitle("Take about 3 lbs. of white sugar.", macaroon, false, "Macaroon Pyramid").Should().BeTrue("a pyramid built of macaroons the lines leave out");
            DietRecipeText.ReadsTitle("Beat the whites.", macaroon, false, "Cocoanut Macaroons").Should().BeFalse("the lines make the macaroons");
            DietRecipeText.ReadsTitle("Beat the whites.", macaroon, false, "Chocolate Macaroons with Cocoanut").Should().BeFalse();
            DietRecipeText.ReadsTitle("Fry the slices.", scallop, false, "Scallops of Sweetbread").Should().BeFalse("scallops of sweetbread are escalopes");
            DietRecipeText.ReadsTitle("Fry the slices.", scallop, false, "Fried Scallops").Should().BeTrue();
        }

        [Fact]
        public void What_a_method_sends_elsewhere_or_leaves_out_is_not_read()
        {
            DietRecipeText.RefersElsewhere(new[] { "Sauce made as directed in No. 1", "a few currants" }).Should().BeTrue();
            DietRecipeText.RefersElsewhere(new[] { "2 cups flour" }).Should().BeFalse();
            DietRecipeText.Sentences("Use tapioca instead of flour. Boil it.").Should().NotContain(s => s.Contains("flour"));
            DietRecipeText.Sentences("Stew the tripe as in the recipe for tripe and oysters.").Should().NotContain(s => s.Contains("oysters"));
            DietRecipeText.Sentences("Stir in the butter. Pour over fish when ready.", "Sauce for Fish").Should().NotContain(s => s.Contains("fish"));
            DietRecipeText.Sentences("Pour over fish when ready.", "Baked Shad").Should().Contain(s => s.Contains("fish"));
            DietRecipeText.Sentences("Serve with cold meat or fish.").Should().NotContain(s => s.Contains("fish"));
            DietRecipeText.Sentences("Strew sugar on them, as you do flower on frying fish.").Should().NotContain(s => s.Contains("fish") || s.Contains("flower"));
            DietRecipeText.Sentences("Add the wine. Remove garlic and pour around Baked Lake Trout.", "Sauce à l'Italienne").Should().NotContain(s => s.Contains("Trout"));
            DietRecipeText.Sentences("Arrange on platter, pour around the fillets Béchamel sauce.", "Rolled Fillets of Flounder with Béchamel Sauce").Should().Contain(s => s.Contains("Béchamel"));
            DietRecipeText.Sentences("Follow the directions for preparing the fish as for potted shrimps.", "POTTED SHRIMPS, DIABLE", new[] { "shrimps", "cayenne" }).Should().NotContain(s => s.Contains("the fish"));
            DietRecipeText.Sentences("Fill the fish, and sew up.", "STUFFED RED SNAPPER", new[] { "chopped oysters" }).Should().Contain(s => s.Contains("the fish"));
        }

        [Fact]
        public void A_recipe_whose_lines_are_not_the_whole_recipe_has_its_title_read()
        {
            DietRecipeText.RefersElsewhere(new[] { "mixture as for marbles", "chopped tomatoes" }).Should().BeTrue();
            DietRecipeText.RefersElsewhere(new[] { "Yeast dough, according to No. 8" }).Should().BeTrue();
            DietRecipeText.RefersElsewhere(new[] { "a dessert-spoonful of" }).Should().BeTrue("the line breaks off where its food would stand");
            DietRecipeText.RefersElsewhere(new[] { "4 ozs. flou" }).Should().BeTrue("the line breaks off inside its food's word");
            DietRecipeText.RefersElsewhere(new[] { "butter size of walnut", "bitter almond" }).Should().BeFalse("a whole word is no broken one");
            DietRecipeText.RefersElsewhere(new[] { "cucumbers" }, "Proceed with the cucumbers as in the last, but sauce over with a celery sauce (No. 1060).").Should().BeTrue();
            DietRecipeText.RefersElsewhere(new[] { "2 cups flour" }, "Mix and bake.").Should().BeFalse();
            DietRecipeText.IsPlaceholder("Ingredients as in \"Sponge Cake Roly-Poly\"").Should().BeTrue();
            DietRecipeText.BuildsOnAnother("Set aside while mixing Velvet Crumb Cake batter. Add the dates.").Should().BeTrue();
            DietRecipeText.BuildsOnAnother("Make Biscuit dough (p. 3).").Should().BeTrue();
            DietRecipeText.BuildsOnAnother("Make a soft dough and roll it out.").Should().BeFalse();
            DietRecipeText.BuildsOnAnother("Make like \"Brown Gravy,\" and add mixed herbs.").Should().BeTrue();
            DietRecipeText.BuildsOnAnother("Make a batter. If you have small fruit, put it in whole.", new[] { "batter as for suet pudding", "apples" }).Should().BeTrue();
            DietRecipeText.BuildsOnAnother("Make the sauce.", new[] { "ingredients for oyster sauce", "3 dozen oysters" }).Should().BeTrue();
            DietRecipeText.BuildsOnAnother("Mix.", new[] { "One pound sugar", "one pound" }).Should().BeTrue("a line that is only an amount has lost its food");
            DietRecipeText.BuildsOnAnother("Incomplete recipe, missing ingredients and steps.", new[] { "1 teaspoonful Crisco" }).Should().BeTrue();
            DietRecipeText.BuildsOnAnother("Cut off the green tops and boil.", new[] { "one pound of asparagus" }).Should().BeFalse();
        }

        [Fact]
        public void A_method_term_that_echoes_a_line_is_read_only_where_no_line_names_that_kind_of_food()
        {
            var crumb = Rule("%crumb%", "Wheat Allergy");
            var shortening = Rule("%shortening%", "Soy Allergy");
            DietRecipeText.ReadsMethod(crumb, new[] { "mixture as for marbles", "milk" }).Should().BeTrue("add more crumbs names crumbs no line gives");
            DietRecipeText.ReadsMethod(crumb, new[] { "1 cup bread crumbs" }).Should().BeFalse("the crumbs of the method are the line's");
            DietRecipeText.ReadsMethod(crumb).Should().BeFalse();
            DietRecipeText.ReadsMethod(shortening, new[] { "1/2 cup butter", "2 cups flour" }).Should().BeFalse("the method's shortening is the butter of the lines");
            DietRecipeText.ReadsMethod(shortening, new[] { "2 cups flour" }).Should().BeTrue();
            DietRecipeText.Mentions("Dip into frying batter.").Should().Contain("frying batter");
            DietRecipeText.Mentions("Put on whole nuts if desired.", new[] { "three squares of chocolate" }).Should().Contain(m => m.Contains("nuts"));
            DietRecipeText.Mentions("Put on whole nuts if desired.", new[] { "1 cup walnut meats" }).Should().BeEmpty("the nuts are the line's walnuts");
            DietRecipeText.Mentions("Roll into balls the size of a nut.", new[] { "sugar" }).Should().BeEmpty("a size is no ingredient");
            DietRecipeText.Sentences("Use to sweeten coffee or sprinkle on pancakes.").Should().BeEmpty("a use names what the recipe goes with");
        }

        [Theory]
        [InlineData("% nut%", "Peanut Allergy", "nut meats", true)]
        [InlineData("% nut%", "Peanut Allergy", "Pignolia nuts", false)]
        [InlineData("%tortilla%", "Wheat Allergy", "4 tortillas, corn", false)]
        [InlineData("%flour%", "Wheat Allergy", "4 tortillas, flour", true)]
        [InlineData("%cream%", "Milk Allergy", "1 cup divided vegan chocolate ice cream", false)]
        [InlineData("%mayo%", "Egg Allergy", "optional 2 tbsp vegan mayo", false)]
        [InlineData("%mayo%", "Soy Allergy", "optional 2 tbsp vegan mayo", true)]
        [InlineData("%farina%", "Celiac Disease", "one level table-spoonful of sea moss farina", false)]
        [InlineData("%oyster%", "Shellfish Allergy", "16 ounces mixed mushrooms, (I like sliced cremini, shittake and oyster)", false)]
        [InlineData("%scallop%", "Shellfish Allergy", "buttered inverted scallop shells", false)]
        public void A_line_is_read_from_its_start_in_the_order_it_is_written_and_in_its_order_of_speech(string pattern, string type, string line, bool read) =>
            Rule(pattern, type).IsLineMatch(line).Should().Be(read);

        [Theory]
        [InlineData("half a pint of Madeira wine sauce", "Celiac Disease")]
        [InlineData("Shrimp Sauce", "Wheat Allergy")]
        [InlineData("a pint of celery sauce", "Milk Allergy")]
        [InlineData("2 cups M&M's", "Milk Allergy")]
        [InlineData("2 cup white chocolate chips", "Milk Allergy")]
        [InlineData("1/2 cup Weetabix", "Wheat Allergy")]
        [InlineData("2 tablespoons oleomargarine", "Milk Allergy")]
        [InlineData("Pappa col pomodoro", "Wheat Allergy")]
        [InlineData("blanched chesnuts", "Tree Nut Allergy")]
        [InlineData("then pour in some garum", "Fish Allergy")]
        [InlineData("pickerel", "Fish Allergy")]
        [InlineData("crimped skate", "Fish Allergy")]
        [InlineData("Châteaubriand sauce", "Sulfite Sensitivity")]
        [InlineData("nouilles", "Egg Allergy")]
        [InlineData("nouilles", "Wheat Allergy")]
        [InlineData("olive oil cooking spray", "Soy Allergy")]
        [InlineData("baking spray with flour", "Wheat Allergy")]
        [InlineData("1 Tbsp Everything Bagel Seasoning", "Sesame Allergy")]
        public void A_preparation_or_product_that_holds_a_restricted_food_by_definition_or_label_is_held(string line, string type) =>
            DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Any(p => Rule(p, type).IsLineMatch(line)).Should().BeTrue();

        [Fact]
        public void The_maker_mars_is_not_read_as_the_candy() =>
            DefaultRestrictionCriteria.ByName["Milk Allergy"].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Where(p => p.Contains("m&m")).Select(p => Rule(p, "Milk Allergy"))
                .Should().OnlyContain(r => !r.IsMatch("Snacks, M&M MARS, KUDOS Whole Grain Bar, chocolate chip"));

        [Theory]
        [InlineData("%cream%", "Milk Allergy", "boil until it is as thick as cream")]
        [InlineData("%cream%", "Milk Allergy", "keep stirring until white and creamy")]
        [InlineData("%cream%", "Milk Allergy", "1 can cream-style corn")]
        [InlineData("% nut%", "Peanut Allergy", "a piece of sweet Butter as big as a Wall-nut")]
        [InlineData("%walnut%", "Tree Nut Allergy", "Paraffine wax, size of walnut")]
        [InlineData("%acorn%", "Tree Nut Allergy", "2 cups winter squash (such as acorn, butternut, or Hubbard)")]
        [InlineData("%butternut%", "Tree Nut Allergy", "2 cups winter squash (such as acorn, butternut, or Hubbard)")]
        [InlineData("%spot%", "Fish Allergy", "scrape off any spots that are on it")]
        [InlineData("%clam%", "Shellfish Allergy", "Clam cocktail sauce")]
        [InlineData("%harina%", "Wheat Allergy", "2 cups cornmeal (masa harina, instant)")]
        [InlineData("%creme%", "Milk Allergy", "Cookies, chocolate sandwich, with creme filling")]
        [InlineData("%oyster%", "Shellfish Allergy", "cut in pieces the size of a large oyster")]
        [InlineData("%pappa%", "Celiac Disease", "pappa reale")]
        public void A_measure_a_texture_or_another_food_named_with_the_term_is_not_read(string pattern, string type, string text) =>
            Rule(pattern, type).IsMatch(text).Should().BeFalse();
    }
}
