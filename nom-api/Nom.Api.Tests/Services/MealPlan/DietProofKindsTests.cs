using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    /// The kinds the ck17 proof draws closed (diet-food-words/3, recipe-text/4): a form of the catalog is never a line's
    /// clear knowledge, an old spelling is read as its catalog word by a derived list, a book method that names a dish no
    /// line carries holds the recipe, and the lines that lost what the title, the leavening, a reference or another recipe
    /// says the dish holds are not read as the whole recipe.
    /// </summary>
    public class DietProofKindsTests
    {
        private static readonly DietFoodWords.Food[] Catalog =
        {
            new(1, "Beef", 13, "Beef, ground, patty, cooked, broiled"),
            new(2, "Turkey", 5, "Turkey, ground, patties, broiled"),
            new(3, "Pork", 10, "Pork, cured, ham, patties, unheated"),
            new(4, "Chicken patty", 5, "Chicken patty, frozen, cooked"),
            new(5, "Bananas", 9, "Bananas, raw"),
            new(6, "Rice", 20, "Rice, white, long-grain, raw"),
            new(7, "Corn", 11, "Corn, sweet, yellow, raw"),
            new(8, "Banana pudding", 19, "Puddings, banana, dry mix"),
            new(9, "Rice pudding", 19, "Puddings, rice, ready-to-eat"),
            new(10, "Corn pudding", 11, "Corn pudding, home prepared"),
            new(11, "Fricassee", 25, "Restaurant, fricassee"),
        };

        private static DietFoodWords.Vocabulary NothingFlagged(DietFoodWords.Table table) =>
            DietFoodWords.Build(table, _ => false, _ => Array.Empty<IReadOnlyList<string>>(), new HashSet<string>(), Array.Empty<DietFoodWords.Kind>());

        [Fact]
        public void A_form_the_catalog_knows_only_on_clear_foods_never_makes_a_line_clear()
        {
            var table = DietFoodWords.TableOf(Catalog);
            var set = NothingFlagged(table);
            var patty = table.IndexOf("patty");
            var pudding = table.IndexOf("pudding");

            set.FormOf(patty).Should().Be(DietFoodWords.FormKind.Shape, "patty is the shape of beef, turkey and pork, and of no food of its own");
            set.ClassOf(patty).Should().Be(DietFoodWords.WordClass.Clear);
            set.Read(table.Read("6 patties")).Value.Should().Be(DietFoodWords.WordClass.NotKnown, "77479 Turkey Patties: the patties are pastry shells");
            set.Read(table.Read("2 beef patties")).Value.Should().Be(DietFoodWords.WordClass.Clear, "beside a word the set understands the line is read by that word");

            set.FormOf(pudding).Should().Be(DietFoodWords.FormKind.Dish, "a pudding is what a prepared mixture of the catalog is, made of banana, rice or corn");
            set.ClassOf(pudding).Should().Be(DietFoodWords.WordClass.NotKnown);
            set.Read(table.Read("1 cup rice pudding")).Value.Should().Be(DietFoodWords.WordClass.NotKnown);

            set.FormOf(table.IndexOf("beef")).Should().Be(DietFoodWords.FormKind.None, "a commodity is no form");
            set.ClassOf(table.IndexOf("rice")).Should().Be(DietFoodWords.WordClass.Clear);
        }

        [Fact]
        public void A_dish_the_corpus_names_recipes_for_holds_a_method_only_where_no_curated_food_holds_the_word()
        {
            DietFoodWords.MethodDishes.Should().NotBeEmpty();
            DietFoodWords.MethodDishes.Should().OnlyContain(d => d.Titles >= 3 && d.Titles * 5 >= d.Methods * 2);
            DietFoodWords.IsMethodDish("pye").Should().BeTrue("68410: \"Make a pye according to this form\"");
            DietFoodWords.IsMethodDish("casserole").Should().BeFalse("a casserole is a dish to bake in far more often than a dish");

            var table = DietFoodWords.TableOf(Catalog);
            var set = NothingFlagged(table);
            table.IndexOf("pye").Should().Be(-1, "no curated food holds pye, so the recipe's reading holds a method that makes one");
            set.HoldsInMethod(table.IndexOf("patty")).Should().BeFalse("a shape the catalog knows is read by its class: \"fill the patties\"");
            set.HoldsInMethod(table.IndexOf("fricassee")).Should().BeFalse("a dish the catalog knows is read by its class: a method names the dish it is served with as often as the one it makes");
            set.HoldsInMethod(table.IndexOf("rice")).Should().BeFalse();
        }

        [Fact]
        public void An_old_spelling_is_read_as_its_catalog_word_only_from_the_derived_list()
        {
            DietFoodWords.Spellings.Should().Contain(s => s.Variant == "cocoanut" && s.Word == "coconut");
            DietFoodWords.Spellings.Should().OnlyContain(s => s.Variant.Length >= 4 && s.Variant[0] == s.Word[0] && s.Curated >= 3 && s.WordLines * 2 >= s.Curated);
            DietFoodWords.Normal("Cocoanuts").Should().Be("coconut");
            DietFoodWords.Normal("cinamon").Should().Be("cinnamon");
            DietFoodWords.Normal("pye").Should().Be("pye", "no line on a curated food carries pye, so it is not mapped");
            DietFoodWords.Respell("1 cup grated cocoanut").Should().Be("1 cup grated coconut");
            DietFoodWords.Respell(null).Should().BeNull();
            FluentActions.Invoking(() => DietFoodWords.ParseSpellings("spelling\tword\tlines\trecipes\tcurated\tword lines\nsirup\tsyrup\t1\t1\t3\t2\nsyrup\tsirop\t1\t1\t3\t2\n"))
                .Should().Throw<System.IO.InvalidDataException>();
        }

        [Theory]
        [InlineData("Prepare Cocoanut Pudding.", "Prepare Coconut Pudding.")]
        [InlineData("Make Cocoanut Cake batter and add the dates.", "Make Coconut Cake batter and add the dates.")]
        [InlineData("Use Shalot Sauce No. 12 and serve.", "Use Shallot Sauce No. 12 and serve.")]
        [InlineData("SIRUP FOR ICES", "SYRUP FOR ICES")]
        public void An_old_spelling_keeps_its_case_so_a_method_that_names_another_recipe_still_builds_on_it(string text, string respelt)
        {
            DietFoodWords.Respell(text).Should().Be(respelt, "review ck17dpr-1: a lower-case catalog word turned the referral rules off");
            DietRecipeText.BuildsOnAnother(DietFoodWords.Respell(text)).Should().Be(DietRecipeText.BuildsOnAnother(text));
            if (text.StartsWith("Prepare") || text.StartsWith("Make")) DietRecipeText.BuildsOnAnother(DietFoodWords.Respell(text)).Should().BeTrue();
        }

        [Theory]
        [InlineData("The clams are prepared exactly as is lobster, except that they must be kept a little longer in the sauce.")]
        [InlineData("Add 2-4 tablespns. of cream to each cup of liquid in any of the recipes for lemon sauce.")]
        [InlineData("Dip slices of sea-trout in beaten egg. Serve with the sauce given in the recipe for Baked Trout with Mushroom Sauce.")]
        [InlineData("439")]
        [InlineData("Pick and wash two heads of nice white celery; cut it into pieces about an inch long; stew it in a pint of water, and a tea-spoonful of salt")]
        [InlineData("Prepare beefsteak pie à l’Américaine as specified in the original recipe.")]
        [InlineData("Prepare beef marrow as in No. 381. Fill and serve bouchées as in No. 381.")]
        [InlineData("Add one and one half cups chopped nuts and candied fruits to Fudge Filling.")]
        [InlineData("Prepare as directed on package. Add cream or butter and the yolk of an egg.")]
        [InlineData("Add to the first recipe a few anchovies, or slices of pickled cucumbers.")]
        [InlineData("Prepare and stew the artichokes precisely as before.")]
        [InlineData("Pour over the nuts the mass described above. Treat as before.")]
        [InlineData("Strain and finish the same as apple jelly.")]
        [InlineData("Prepare in the same manner as strawberries a la mode.")]
        [InlineData("Stew the fish as before, dish it up, have ready a muscle sauce (No. 70) pour it over the fish.")]
        [InlineData("Prepare the artichokes as above. Blanch them.")]
        [InlineData("Proceed as directed.")]
        [InlineData("Proceed with a gold web exactly the same as with the silver web.")]
        [InlineData("Make this with cream instead of milk.")]
        [InlineData("Instead of using pearl-barley, use flour that you have dried in a bakepan till it turns yellow.")]
        [InlineData("Let it boil for five minutes; pour this water off; then add 1 teaspoon of salt,")]
        [InlineData("Let the Fish be fry'd after the common manner, and when they are cold lay them in a Dish. Pour on the following Pickle.")]
        [InlineData("Take sorrel as above, but instead of stock or gravy, add two yolks of eggs.")]
        [InlineData("Same as raspberry.")]
        [InlineData("Add to recipe for Brown Sauce (see No. 185), or to recipe for White Sauce.")]
        [InlineData("Prepare sauce same as No. 184, and stand the saucepan on one side.")]
        [InlineData("Put the ingredients together as directed in the Raised Loaf Cake.")]
        [InlineData("Cut the apples in two crosswise. Proceed as with the whole apple.")]
        [InlineData("Prepare Duchess Potato.")]
        [InlineData("Make same as Strawberry Mousse, using one cup boiled coffee in place of fruit juice.")]
        [InlineData("To 4 eggs, lightly beaten, add 4 tbp milk or water. (Incomplete recipe - steps missing)")]
        [InlineData("Prepare in same manner as Preserved Green Tomatoes, except that the tomatoes must have skins removed.")]
        [InlineData("Proceed exactly as described for the pheasant. Serve with cabbage.")]
        [InlineData("Fry it in butter, like perch. For sauce, see Turbot à la Mazarine, No. 207.")]
        [InlineData("The preparation and ingredients are the same as given under No. 38, Fish Croquettes.")]
        [InlineData("Roast and serve a capon in any of the ways directed for turkeys.")]
        [InlineData("See boiled corned beef, Article 58.")]
        [InlineData("Roast two spring chickens in vegetables. Sauce the same as in the last article.")]
        [InlineData("Hashed Potatoes au Gratin, 1004.")]
        [InlineData("Add one pint of grated pineapple and juice of one lemon to above syrup and freeze rapidly.")]
        [InlineData("Proceed same as ordinary fancy or drop cake. Bake in hot oven.")]
        [InlineData("Cook them as directed on page 168.")]
        [InlineData("Mix and bake same as Vanilla Wafers.")]
        [InlineData("Use Champagne instead of Madeira wine. Serve it with a Champagne sauce.")]
        [InlineData("Substitute a little rose-water for the pineapple. Squeeze out the juice of the orange and lemon.")]
        [InlineData("Follow the last recipe in every particular.")]
        [InlineData("Take the juice of a half a small orange and add to the recipe for foamy omelet.")]
        [InlineData("Cook pears in syrup until tender; finish same as peaches.")]
        [InlineData("Make as apple jelly. Note: The seed of the quince is very mucilaginous.")]
        [InlineData("Prepare as Egg Cordial, omitting wine and cream, and using the juice of one orange.")]
        [InlineData("Add 2 tbsp. of sugar and 1/2 cup of fruit, (currants, raisins, peel, or a mixture of all) after the fat is added.")]
        [InlineData("Make the soufflé preparation, but use tea instead of milk. Add thereto one and a half oz. of chopped pistachios.")]
        [InlineData("Pare and cut some ripe peaches into fine slices; bake the pancakes the same as in foregoing recipe and lay the peaches between.")]
        [InlineData("Melt chocolate. Add water, milk, butter, and sugar. Mix as in lemon marmalade.")]
        [InlineData("647, 977")]
        [InlineData("Put into cold water, and let it come to a boil; simmer for two hours and a half, and serve the same as a round.")]
        [InlineData("Simmer for five minutes, then add the meat of one crab, prepared Monza.")]
        [InlineData("Omit molasses, add ¼ cup vinegar and ¼ cup water, and cook until the liquid is absorbed.")]
        [InlineData("Mix with sliced oranges and bananas. Serve like the foregoing.")]
        [InlineData("Make dressing number two. Add salt and sugar to suit the taste.")]
        public void A_method_that_is_another_recipe_builds_on_it(string method)
        {
            DietRecipeText.BuildsOnAnother(method).Should().BeTrue();
        }

        [Theory]
        [InlineData("Boil the sugar and water together for three minutes, cool and add the lemon juice.")]
        [InlineData("Mix all and serve (hot).")]
        [InlineData("Bake in a quick oven!")]
        [InlineData("Prepare the potatoes. Bake them.")]
        [InlineData("Serve hot with cream.")]
        [InlineData("Add a No. 2 can of tomatoes and simmer.")]
        [InlineData("Roll the paste and make as thin as possible.")]
        [InlineData("Make as many balls as the mixture allows, and fry them.")]
        [InlineData("Add the flour after the butter is melted, and stir until smooth.")]
        [InlineData("Bake at 350 degrees for 45 minutes.")]
        [InlineData("Serve as a garnish for roast lamb.")]
        public void A_method_that_ends_its_last_sentence_builds_on_nothing_else(string method)
        {
            DietRecipeText.BuildsOnAnother(method).Should().BeFalse();
        }

        [Fact]
        public void A_method_word_is_paired_with_the_head_of_the_words_that_follow_it()
        {
            DietFoodWords.MethodPairs("Stir the whole well, adding soy bean sauce, salt and pepper.", new HashSet<string>())
                .Should().Contain(("soy", "sauce"), "136720 Pei Yu: the soy is a sauce, not a bean");
            DietFoodWords.MethodPairs("Put in slices of sponge cake.", new HashSet<string>()).Should().Contain(("sponge", "cake"));
            DietFoodWords.MethodPairs("Mince the onion.", new HashSet<string>()).Should().Contain(("mince", null));
            DietRecipeText.Contractions("Stir the whole well, adding soy bean sauce, salt and pepper.").Should().Contain("soy sauce");
            DietRecipeText.Contractions("Add a teaspoonful of cream of tartar, made without eggs.").Should().BeEmpty("a run with a word of grammar is not read without it");
        }

        [Theory]
        [InlineData("Fillings of your choice", true)]
        [InlineData("1 tbsp nut butter of choice", true)]
        [InlineData("your favorite cheese", true)]
        [InlineData("any kind of oil", true)]
        [InlineData("1 cup chosen apples", false)]
        [InlineData("2 cups milk", false)]
        public void A_line_that_leaves_its_food_to_the_cook_is_read_as_a_choice(string line, bool choice)
        {
            DietRecipeText.LeavesChoice(line).Should().Be(choice);
        }

        [Fact]
        public void A_dough_dish_or_a_leavening_whose_dough_the_lines_lost_is_read_as_lost()
        {
            DietRecipeText.DoughLost("CRANBERRY TART", new[] { "cranberries", "water" }).Should().BeTrue("103893");
            DietRecipeText.DoughLost("Gooseberry Tart", new[] { "1½ pint of gooseberries", "½ lb. of short crust" }).Should().BeFalse();
            DietRecipeText.DoughLost("Cranberry Sauce", new[] { "cranberries", "water" }).Should().BeFalse();
            DietRecipeText.DoughLost("Brown betty", new[] { "enough apples to make two cupfuls" }).Should().BeTrue("122491: a betty is layered with crumbs, by the corpus's own betties (derived, Contents/diet-dough-titles.tsv)");
            DietRecipeText.DoughLost("Brown betty", new[] { "apples", "2 cups bread crumbs" }).Should().BeFalse();
            DietRecipeText.PasteLost("Pare and core the apples; beat five eggs very light, and mix all together, and bake in a paste.", new[] { "half a peck of apples", "five eggs" }).Should().BeTrue("109291: the paste is in no line");
            DietRecipeText.PasteLost("Enclose apples in puff paste. Boil in nets.", new[] { "six large baking apples", "pieces of puff paste" }).Should().BeFalse();
            DietRecipeText.PasteLost("Put in a dish and bake with or without paste.", new[] { "calf's head" }).Should().BeFalse();
            DietRecipeText.PasteLost("Beat the egg, mix all the ingredients and bake between 2 crusts.", new[] { "1 large lemon", "1 egg" }).Should().BeTrue("96979");
            DietRecipeText.PasteLost("Crush the crackers and add them. Fill the lower crust of the pie with this mixture, cover with the top crust.", new[] { "8 apples", "8 crackers" }).Should().BeTrue("31363: the crackers are the filling's");
            DietRecipeText.PasteLost("Fill the lower crust with the mixture and bake.", new[] { "pastry for two crusts", "8 apples" }).Should().BeFalse();
            DietRecipeText.BuildsOnAnother("Bake in a medium oven.", new[] { "Two large tenderloins; dressing (such as is used for turkey dressing)" }).Should().BeTrue("79705: the dressing is another recipe's");
            DietRecipeText.DoughLost("RHUBARB PIE No. 2", new[] { "2 pints rhubarb", "1 cupful sugar" }).Should().BeTrue("the number in the series is no part of the dish (139238's title)");
            DietRecipeText.Title("FRIED HALIBUT--II").Trim().Should().Be("FRIED HALIBUT");
            DietRecipeText.DoughTitles.Should().Contain(t => t.Word == "betty").And.OnlyContain(t => t.Titles >= 10 && t.WithDough * 10 >= t.Titles * 9);
            FluentActions.Invoking(() => DietRecipeText.ParseDoughTitles("word\ttitles\twith dough\nbetty\t31\t20\n")).Should().Throw<System.IO.InvalidDataException>("a word under the derivation's bar is refused");

            DietRecipeText.LeaveningLost(new[] { "baking powder", "ginger", "baking soda", "water" }).Should().BeTrue("69561 Simple Dessert");
            DietRecipeText.LeaveningLost(new[] { "1 egg", "1 teacupful sugar", "1 teaspoonful cream of tartar", "½ teaspoonful of soda" }).Should().BeTrue("94494 Lemon Jumbles");
            DietRecipeText.LeaveningLost(new[] { "2 cups flour", "2 teaspoons baking powder" }).Should().BeFalse();
            DietRecipeText.LeaveningLost(new[] { "two even teaspoonfuls of cream tartar", "pint of boiling water" }).Should().BeFalse("a cream of tartar drink raises nothing");
        }

        [Fact]
        public void A_food_the_title_joins_to_the_dish_that_no_line_or_method_names_is_read_from_the_title()
        {
            var cream = DietPatternRule.For("%cream%");
            const string method = "Peel ripe pears, strew each layer with sugar and bake three hours.";
            DietRecipeText.ReadsTitle(method, cream, false, "Baked Pears and Cream", new[] { "ripe pears", "sugar", "nutmeg", "water" }).Should().BeTrue("105632");
            DietRecipeText.ReadsTitle(method, cream, false, "Baked Pears and Cream", new[] { "ripe pears", "a cup of cream" }).Should().BeFalse();
            DietRecipeText.ReadsTitle(method, cream, false, "Cream Baked Pears", new[] { "ripe pears" }).Should().BeFalse("only what the title joins with and or with is read");
        }

        private const long PersonId = 70, HouseholdId = 7, CeliacRef = 60800, Gram = 1;

        private static ApplicationDbContext NewContext(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptors).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeEntity Book(string name, string method, params (long Food, string Raw, string? Kind)[] lines)
        {
            var recipe = new RecipeEntity
            {
                Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, ScrapedAtUtc = DateTime.UtcNow,
                RecipeIngredients = new List<RecipeIngredientEntity>(),
                RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = method } },
            };
            var order = 0;
            foreach (var (food, raw, kind) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, LineKind = kind, SortOrder = order++ });
            return recipe;
        }

        private static IngredientEntity Curated(string name, int category, string description) =>
            new() { Name = name, FdcCategoryId = category, CurationStatusId = (long)CurationStatusEnum.Curated, Aliases = new List<IngredientAliasEntity> { new() { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext } } };

        [Fact]
        public async Task Lines_that_hold_no_food_a_reference_that_names_more_than_its_food_and_a_lost_dough_hold_a_severe_set()
        {
            using var db = NewContext();
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Celiac Disease", RestrictionTypeId = CeliacRef });
            var rabbit = Curated("Rabbit", 17, "Game meat, rabbit, domesticated, composite of cuts, raw");
            var cheddar = Curated("Cheddar Cheese", 1, "Cheese, cheddar");
            var cranberries = Curated("Cranberries", 9, "Cranberries, raw");
            var water = Curated("Water", 14, "Water, tap");
            var note = new IngredientEntity { Name = "None specified in the text", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(rabbit, cheddar, cranberries, water, note);
            await db.SaveChangesAsync();
            var creamed = Book("Creamed Salt Pork", "None specified in the text", (note.Id, "None specified in the text", RecipeIngredientLineKind.Note));
            var smoked = Book("Smoked Cheddar Rabbit", "A Basic Rabbit made with smoked cheddar in place of the usual cheddar.",
                (cheddar.Id, "Smoked Cheddar", null), (rabbit.Id, "Basic Rabbit", RecipeIngredientLineKind.Reference));
            var roast = Book("Roast Rabbit", "Roast the rabbit in a hot oven.", (rabbit.Id, "rabbit", RecipeIngredientLineKind.Reference));
            var tart = Book("CRANBERRY TART", "Wash and pick over the berries and simmer until they burst.", (cranberries.Id, "cranberries", null), (water.Id, "water", null));
            var sauce = Book("Cranberry Sauce", "Wash and pick over the berries and simmer until they burst.", (cranberries.Id, "cranberries", null), (water.Id, "water", null));
            var burrito = Book("How to Fold a Burrito", "Lay the tortilla flat and fold it over the fillings.", (water.Id, "Fillings of your choice", null), (cranberries.Id, "cranberries", null));
            var pye = Book("To stew Carp and Eels", "Stew the cranberries with the water, then make a pye according to this form.", (cranberries.Id, "cranberries", null), (water.Id, "water", null));
            var paste = Book("Cranberry Compote", "Stew the cranberries with the water, mix all together, and bake in a paste.", (cranberries.Id, "cranberries", null), (water.Id, "water", null));
            db.Recipes.AddRange(creamed, smoked, roast, tart, sauce, pye, burrito, paste);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var severe = await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId);
            var offered = (await db.Recipes.WithoutHeld(db, severe).Select(r => r.Id).ToListAsync()).ToHashSet();
            offered.Should().Contain(new[] { roast.Id, sauce.Id });
            offered.Should().NotContain(new[] { creamed.Id, smoked.Id, tart.Id, pye.Id, burrito.Id, paste.Id });
            var flags = await db.DietRecipeFlags.Where(f => severe.Stored!.SetIds.Contains(f.SetId)).ToListAsync();
            flags.Should().Contain(f => f.RecipeId == creamed.Id && f.Reason == DietFlagReason.NoLine, "81361: a note is no ingredient line");
            flags.Should().Contain(f => f.RecipeId == smoked.Id && f.Reason == DietFlagReason.UnknownWord && f.IngredientId == rabbit.Id, "49553: \"Basic Rabbit\" names another recipe, not the rabbit it is linked to");
            flags.Should().Contain(f => f.RecipeId == tart.Id && f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.Title, "103893: a tart's pastry is not in its lines");
            flags.Should().Contain(f => f.RecipeId == burrito.Id && f.Reason == DietFlagReason.UnknownWord && f.IngredientId == water.Id, "59672: whatever food the line is on, the cook chooses the filling");
            flags.Should().Contain(f => f.RecipeId == pye.Id && f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.Method, "68410: the method makes a pye no line lists");
            flags.Should().Contain(f => f.RecipeId == paste.Id && f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.Title, "109291: the paste it is baked in is in no line");
        }
    
        [Fact]
        public void A_portion_of_a_cake_in_a_method_is_the_cake_and_a_method_that_stirs_something_in_begins_midway()
        {
            DietRecipeText.Mentions("Cool and spread between layers of cake.").Should().Contain("cake", "51930 PEACH-CREAM FILLING is spread between the layers of a cake no line gives");
            DietRecipeText.Mentions("Dip small pieces of cake into the glaze.").Should().Contain("cake");
            DietRecipeText.Mentions("Shape into small flat cakes and fry.").Should().BeEmpty("a cake the method shapes is no food");
            DietRecipeText.Mentions("Spread over one cake, cover with thin slices of red bananas, and repeat for remaining cakes.").Should().Contain("cake", "28948 BANANA FILLING");
            DietRecipeText.Mentions("Pour into the cake pan and bake.").Should().BeEmpty("the cake's tin is no cake");
            DietRecipeText.BuildsOnAnother("Made in a similar way to Cumberland pudding, with the omission of the apples.").Should().BeTrue("43905 College Pudding");
            DietRecipeText.BuildsOnAnother("Pass the pulp through a sieve. Finish as ripe apricot paste.").Should().BeTrue("111203");
            DietRecipeText.BuildsOnAnother("Boil it and finish as soon as it thickens.").Should().BeFalse();
            DietRecipeText.Mentions("Mix well. Spread over hot cake. Place under broiler.").Should().Contain("cake", "123641 Broiled Coconut Topping");
            DietRecipeText.BuildsOnAnother("Make exactly as you would a beef-steak pie.").Should().BeTrue("74411 Veal Pie");
            DietRecipeText.BuildsOnAnother("Boil the eggs and cut them in dice.", new[] { "six eggs", "three parts of a pint of melted butter" }).Should().BeTrue("92138 Egg Sauce: the butter sauce's flour is in no line");
            DietRecipeText.BuildsOnAnother("Put them into half a pint of good melted butter.").Should().BeTrue("72347 Shrimp Sauce");
            DietRecipeText.BuildsOnAnother("Brush with melted butter and bake.", new[] { "2 tablespoonfuls of melted butter" }).Should().BeFalse("butter melted by the spoon is butter");
            DietRecipeText.BuildsOnAnother("Stir in the corn and bake forty-five minutes in a buttered pudding-dish.").Should().BeTrue("105291: the eggs and what the corn is stirred into are in no line");
            DietRecipeText.BuildsOnAnother("Beat in a mortar three yolks of eggs that have been boiled hard.").Should().BeFalse();
            DietRecipeText.BuildsOnAnother("Stir into the hot milk and boil.").Should().BeFalse();
        }

        [Fact]
        public void A_size_given_by_a_food_is_no_food_of_the_line()
        {
            DietFoodWords.Respell("walnut-sized piece of butter rolled in twice as much flour").Should().NotContain("walnut", "74666: the walnut is the size of the butter");
            DietFoodWords.Respell("a piece of butter the size of an egg").Should().NotContain("egg");
            DietFoodWords.Respell("butter as big as a walnut").Should().NotContain("walnut");
            DietFoodWords.Respell("half a cup of chopped walnuts").Should().Contain("walnuts");
        }

        [Fact]
        public void A_consomme_recipe_names_its_egg_white_clarifier_only_under_a_consomme_title()
        {
            DietRecipeText.ConsommeClarifiers("THE PREPARATION OF CLARIFIED CONSOMMÉ", new[] { "lean beef", "the white of an egg" }, "Mix the meat with the white of an egg.")
                .Should().Equal("white of an egg");
            DietRecipeText.ConsommeClarifiers("Consommé", new[] { "shin of beef", "water" }, "Simmer six hours and strain.").Should().BeEmpty("Francatelli's consommé is strong broth");
            DietRecipeText.ConsommeClarifiers("Meringue", new[] { "whites of four eggs" }, null).Should().BeEmpty();
            DietRecipeText.NamesConsomme("1 quart of consommé").Should().BeTrue();
            DietRecipeText.NamesConsomme("1 quart of beef stock").Should().BeFalse();
        }

        [Fact]
        public async Task A_consomme_line_is_not_understood_for_a_severe_set_that_flags_the_egg_white_its_own_book_clarifies_it_with()
        {
            using var db = NewContext();
            const long EggRef = 60830, PeanutRef = 60840;
            foreach (var (household, person, type, typeId) in new[] { (7L, 70L, "Egg Allergy", EggRef), (9L, 90L, "Peanut Allergy", PeanutRef) })
            {
                db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = household, PersonId = person, IsActive = true });
                db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = typeId, Name = type });
                db.Restrictions.Add(new RestrictionEntity { PersonId = person, Name = type, RestrictionTypeId = typeId });
            }
            var consomme = Curated("Beef Consomme", 6, "Soup, beef broth or bouillon, canned, ready-to-serve");
            var beef = Curated("Beef", 13, "Beef, round, lean, raw");
            var egg = Curated("Egg white", 1, "Egg, white, raw, fresh");
            var carrot = Curated("Carrots", 11, "Carrots, raw");
            db.Ingredients.AddRange(consomme, beef, egg, carrot);
            await db.SaveChangesAsync();
            const string escoffier = "A guide to modern cookery (Escoffier, A. (Auguste), 1935)", francatelli = "The cook's guide (Francatelli, Charles Elmé)";
            var clarified = Book("THE PREPARATION OF CLARIFIED CONSOMMÉ", "Mix the lean beef with the white of an egg and add the stock.", (beef.Id, "lean beef", null), (egg.Id, "the white of an egg", null));
            var julienne = Book("Julienne", "Cook the carrots in the consommé.", (consomme.Id, "1 quart of consommé", null), (carrot.Id, "2 carrots", null));
            var broth = Book("Consommé", "Simmer the beef six hours and strain.", (beef.Id, "shin of beef", null));
            var other = Book("Julienne Soup", "Cook the carrots in the consommé.", (consomme.Id, "1 quart of consommé", null), (carrot.Id, "2 carrots", null));
            clarified.SourceAttribution = julienne.SourceAttribution = escoffier;
            broth.SourceAttribution = other.SourceAttribution = francatelli;
            db.Recipes.AddRange(clarified, julienne, broth, other);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var resolver = new HouseholdRestrictionResolver(db);
            var eggSets = (await resolver.ResolveSevereForPersonAsync(70)).Stored!.SetIds;
            var offered = (await db.Recipes.WithoutHeld(db, await resolver.ResolveSevereForPersonAsync(70)).Select(r => r.Id).ToListAsync()).ToHashSet();
            offered.Should().Contain(other.Id, "the book's own consommé is broth");
            offered.Should().NotContain(new[] { clarified.Id, julienne.Id });
            (await db.DietRecipeFlags.Where(f => f.RecipeId == julienne.Id && eggSets.Contains(f.SetId)).ToListAsync())
                .Should().Contain(f => f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.UnknownWord && f.IngredientId == consomme.Id,
                    "Escoffier clarifies his consommé with egg white, whatever food the line is linked to");
            (await db.Recipes.WithoutHeld(db, await resolver.ResolveSevereForPersonAsync(90)).Select(r => r.Id).ToListAsync()).Should().Contain(new[] { julienne.Id, other.Id }, "egg white is nothing Peanut Allergy flags");
        }

        [Fact]
        public async Task A_recipe_whose_book_is_written_is_held_at_once_and_read_again_against_its_new_books_consomme()
        {
            using var db = NewContext(DietRecipeStateClearer.Shared);
            const long EggRef = 60830;
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = EggRef, Name = "Egg Allergy" });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Egg Allergy", RestrictionTypeId = EggRef });
            var consomme = Curated("Beef Consomme", 6, "Soup, beef broth or bouillon, canned, ready-to-serve");
            var beef = Curated("Beef", 13, "Beef, round, lean, raw");
            var egg = Curated("Egg white", 1, "Egg, white, raw, fresh");
            var carrot = Curated("Carrots", 11, "Carrots, raw");
            db.Ingredients.AddRange(consomme, beef, egg, carrot);
            await db.SaveChangesAsync();
            const string escoffier = "A guide to modern cookery (Escoffier, A. (Auguste), 1935)";
            var clarified = Book("THE PREPARATION OF CLARIFIED CONSOMMÉ", "Mix the lean beef with the white of an egg and add the stock.", (beef.Id, "lean beef", null), (egg.Id, "the white of an egg", null));
            var julienne = Book("Julienne", "Cook the carrots in the consommé.", (consomme.Id, "1 quart of consommé", null), (carrot.Id, "2 carrots", null));
            clarified.SourceAttribution = escoffier;
            db.Recipes.AddRange(clarified, julienne);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            async Task<HashSet<long>> OfferedAsync() =>
                (await db.Recipes.WithoutHeld(db, await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(PersonId)).Select(r => r.Id).ToListAsync()).ToHashSet();
            (await OfferedAsync()).Should().Contain(julienne.Id, "a recipe of no book that clarifies its consommé with egg white");

            julienne.SourceAttribution = escoffier;
            await db.SaveChangesAsync();
            (await OfferedAsync()).Should().NotContain(julienne.Id, "review ck17dpr-2: the book the consommé hold reads was written, so the decision on the old book no longer stands");
            await builder.RunChangesAsync(db);
            (await OfferedAsync()).Should().NotContain(julienne.Id, "decided again, its own book clarifies its consommé with egg white");
        }
}
}
