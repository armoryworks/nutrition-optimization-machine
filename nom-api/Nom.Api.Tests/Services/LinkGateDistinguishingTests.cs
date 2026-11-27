using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The link gate's rules of <c>link-gate/13</c> (staging checkpoint 4, P6, P9 and P13): every
    /// distinguishing word of the food must be accounted for by the wording or be the default of
    /// its kind, a part the food only comes with is not the food, and the rule fixes the
    /// checkpoint's re-check rows showed (a food's own name, a percent meaning pure, nonfat or
    /// lowfat, "not sweetened", alternatives of form, a part word that only qualifies the head, a
    /// ratio, a packing medium, the dry product of a legume, grain or seed). Descriptions are real
    /// SR Legacy rows.
    /// </summary>
    public class LinkGateDistinguishingTests
    {
        private const long Curated = (long)CurationStatusEnum.Curated;

        private static long _next = 900000;

        private static GateFood Food(string name, string description, int category) =>
            new(++_next, name, description, CatalogAdmissionRule.SrLegacyFood, Curated, category, NutrientRows: 30, CoreNutrients: true);

        private static IReadOnlyList<GateHold> Check(string wording, GateFood target, params GateFood[] kin) =>
            LinkGate.Check(new GatePair(wording, 0, target, ModelPick: true), GateCatalog.Build(kin.Append(target).ToList()));

        private static IReadOnlyList<string> Codes(string wording, GateFood target, params GateFood[] kin) => Check(wording, target, kin).Select(h => h.Code).ToList();

        [Fact]
        public void A_word_of_the_food_the_wording_does_not_account_for_holds_the_pick()
        {
            var brownRice = Food("Flour, rice, brown", "Flour, rice, brown", 20);
            var wheat = Food("Flour, wheat, all-purpose, enriched, bleached", "Flour, wheat, all-purpose, enriched, bleached", 20);
            Check("brown flour", brownRice, wheat).Should().Contain(h => h.Code == LinkGate.Variety && h.Detail == "rice", "browned wheat flour is not brown rice flour");
            Codes("brown rice flour", brownRice, wheat).Should().NotContain(LinkGate.Variety);

            var coho = Food("Fish, salmon, coho, wild, raw", "Fish, salmon, coho, wild, raw", 15);
            var atlantic = Food("Fish, salmon, Atlantic, wild, raw", "Fish, salmon, Atlantic, wild, raw", 15);
            Check("wild salmon", coho, atlantic).Should().Contain(h => h.Code == LinkGate.Variety && h.Detail == "coho");
            Codes("wild coho salmon", coho, atlantic).Should().NotContain(LinkGate.Variety);

            var whipped = Food("Whipped Butter", "Butter, whipped, with salt", 1);
            var unsalted = Food("Unsalted Butter", "Butter, without salt", 1);
            Check("salt butter", whipped, unsalted).Should().Contain(h => h.Code == LinkGate.Variety && h.Detail == "whipped");

            var vanilla = Food("Nonfat Vanilla Yogurt", "Yogurt, vanilla, non-fat", 1);
            var plain = Food("Plain Nonfat Greek Yogurt", "Yogurt, Greek, plain, nonfat", 1);
            Check("non-fat plain yogurt", vanilla, plain).Should().Contain(h => h.Code == LinkGate.Variety && h.Detail == "vanilla");
        }

        [Fact]
        public void A_colour_is_held_unless_it_is_the_default_of_its_kind_and_the_wording_does_not_contradict_it()
        {
            var red = Food("Red Bell Pepper", "Peppers, sweet, red, raw", 11);
            var green = Food("Green Pepper", "Peppers, sweet, green, raw", 11);
            Check("sweet pepper", red, green).Should().Contain(h => h.Code == LinkGate.Variety && h.Detail == "red", "no plainly named sweet pepper is red, so red is no default");

            var noSalt = Food("Corn, sweet, yellow, canned, cream style, no salt added", "Corn, sweet, yellow, canned, cream style, no salt added", 11);
            var creamStyle = Food("Cream-Style Corn", "Corn, sweet, yellow, canned, cream style, regular pack", 11);
            var white = Food("Corn, sweet, white, canned, cream style, regular pack", "Corn, sweet, white, canned, cream style, regular pack", 11);
            Codes("No. 2 can Cream Style Corn", noSalt, creamStyle, white).Should().NotContain(LinkGate.Variety, "Cream-Style Corn is the yellow one, so yellow is the default of cream-style corn");
            Codes("No. 2 can Cream Style Corn", creamStyle, noSalt, white).Should().NotContain(LinkGate.Variety);
            Codes("white cream style corn", noSalt, creamStyle, white).Should().Contain(LinkGate.Containment, "a wording naming another colour is held before any default is read");
        }

        [Fact]
        public void A_word_joined_by_a_hyphen_to_a_form_the_wording_names_is_accounted_for()
        {
            var freezeDried = Food("Chives, freeze-dried", "Chives, freeze-dried", 11);
            var raw = Food("Chives, raw", "Chives, raw", 11);
            Codes("dried chives", freezeDried, raw).Should().NotContain(LinkGate.Variety);
        }

        [Fact]
        public void A_part_the_food_only_comes_with_is_not_the_food_unless_the_wording_names_it_the_same_way()
        {
            var oranges = Food("Oranges With Peel", "Oranges, raw, with peel", 9);
            Codes("fresh orange peel", oranges).Should().Contain(LinkGate.Part);

            var salmon = Food("Unsalted Canned Pink Salmon", "Fish, salmon, pink, canned, without salt, solids with bone and liquid", 15);
            Codes("1 can salmon with bones", salmon).Should().NotContain(LinkGate.Part, "the wording asks for the fish with its bones, which the entry has");
            Codes("salmon bones", salmon).Should().Contain(LinkGate.Part);
        }

        [Fact]
        public void Leaves_are_a_part_of_a_seed_spice_but_the_dried_leaf_of_a_leaf_spice()
        {
            Codes("dried celery leaves", Food("Celery Seed", "Spices, celery seed", 2)).Should().Contain(LinkGate.Part);
            Codes("bay leaves", Food("Bay Leaf", "Spices, bay leaf", 2)).Should().NotContain(LinkGate.Part);
        }

        [Fact]
        public void A_grated_spice_and_a_handful_of_a_spice_that_is_no_leaf_herb_are_the_dried_spice()
        {
            Codes("few grates of fresh nutmeg", Food("Nutmeg", "Spices, nutmeg, ground", 2)).Should().NotContain(LinkGate.FreshHerb);
            Codes("handful of broken cinnamon", Food("Ground Cinnamon", "Spices, cinnamon, ground", 2)).Should().NotContain(LinkGate.FreshHerb);
            Codes("handful of tarragon", Food("Dried Tarragon", "Spices, tarragon, dried", 2)).Should().Contain(LinkGate.FreshHerb);
        }

        [Fact]
        public void A_cake_of_chocolate_is_a_measure_of_chocolate_not_a_chocolate_cake()
        {
            var cake = Food("Chocolate Cake", "Cake, chocolate, prepared from recipe without frosting", 18);
            var baking = Food("Baking Chocolate", "Chocolate, baking, unsweetened, squares", 19);
            Codes("one-half cake chocolate", cake, baking).Should().Contain(LinkGate.HeadLead);
            Codes("chocolate cake", cake, baking).Should().NotContain(LinkGate.HeadLead);
        }

        [Fact]
        public void Not_sweetened_is_unsweetened_and_a_stated_percent_of_pure_nonfat_or_lowfat_is_met_by_its_words()
        {
            var coconut = Food("Desiccated Coconut", "Nuts, coconut meat, dried (desiccated), not sweetened", 12);
            Codes("unsweetened shredded coconut", coconut).Should().NotContain(LinkGate.Polarity);
            Codes("sweetened shredded coconut", coconut).Should().Contain(LinkGate.Polarity);

            Codes("100% whole wheat flour", Food("Whole Wheat Flour", "Flour, whole wheat, unenriched", 20)).Should().NotContain(LinkGate.Percent);
            Codes("0% greek yogurt", Food("Plain Nonfat Greek Yogurt", "Yogurt, Greek, plain, nonfat", 1)).Should().NotContain(LinkGate.Percent);
            Codes("2% greek yogurt", Food("Low-Fat Plain Greek Yogurt", "Yogurt, Greek, plain, lowfat", 1)).Should().NotContain(LinkGate.Percent);
            Codes("2% milk", Food("Whole Milk", "Milk, whole, 3.25% milkfat, with added vitamin D", 1)).Should().Contain(LinkGate.Percent);
        }

        [Fact]
        public void Forms_given_as_alternatives_and_the_dry_product_of_a_legume_or_seed_are_no_form_conflict()
        {
            var cranberries = Food("Cranberries", "Cranberries, raw", 9);
            Codes("fresh or frozen cranberries", cranberries).Should().NotContain(LinkGate.Form);
            Codes("frozen cranberries", cranberries).Should().Contain(LinkGate.Form);

            Codes("dry lentils", Food("Lentils", "Lentils, raw", 16)).Should().NotContain(LinkGate.Form);
            Codes("raw sesame seeds", Food("Sesame Seeds", "Seeds, sesame seeds, whole, dried", 12)).Should().NotContain(LinkGate.Form);
            Codes("dried apples", Food("Apples", "Apples, raw, with skin", 9)).Should().Contain(LinkGate.Form);
        }

        [Fact]
        public void A_part_word_that_qualifies_another_food_word_a_ratio_or_the_foods_own_name_is_no_part_conflict()
        {
            Codes("chicken bone broth", Food("Chicken Broth", "Soup, chicken broth, ready-to-serve", 6)).Should().NotContain(LinkGate.Part);
            Codes("sugar per pint of juice", Food("Sugar", "Sugars, granulated", 19)).Should().NotContain(LinkGate.Part);
            Codes("clam juice", Food("Clam Juice", "Mollusks, clam, mixed species, canned, liquid", 15)).Should().NotContain(LinkGate.Part).And.NotContain(LinkGate.Group);
            Codes("beet leaves", Food("Beet Greens", "Beet greens, raw", 11)).Should().NotContain(LinkGate.Part);
            Codes("celery leaves", Food("Celery", "Celery, raw", 11)).Should().Contain(LinkGate.Part);
        }

        [Fact]
        public void A_group_word_after_in_is_a_packing_medium_not_the_head()
        {
            Codes("sardines in oil", Food("Sardines", "Fish, sardine, Atlantic, canned in oil, drained solids with bone", 15)).Should().NotContain(LinkGate.Group);
            Codes("glasses of sherry", Food("Red Wine Vinegar", "Vinegar, red wine", 2)).Should().Contain(LinkGate.Group);
        }

        [Fact]
        public void An_oil_flour_butter_or_milk_of_a_food_is_not_the_food()
        {
            Codes("roasted peanut oil", Food("Peanuts, spanish, oil-roasted, without salt", "Peanuts, spanish, oil-roasted, without salt", 16)).Should().Contain(LinkGate.HeadNoun);
            Codes("almond flour", Food("Almonds", "Nuts, almonds", 12)).Should().Contain(LinkGate.HeadNoun);
            Codes("peanut oil", Food("Peanut Oil", "Oil, peanut, salad or cooking", 4)).Should().NotContain(LinkGate.HeadNoun);
            Codes("butter milk", Food("Buttermilk", "Milk, buttermilk, fluid, cultured, lowfat", 1)).Should().NotContain(LinkGate.HeadNoun);
        }

        [Fact]
        public void A_bare_made_product_word_is_no_evidence_for_one_particular_product()
        {
            Codes("homemade sauce", Food("White Sauce", "Sauce, homemade, white, medium", 6)).Should().Contain(LinkGate.Generic);
            Codes("gravy", Food("Turkey Gravy", "Gravy, turkey, canned, ready-to-serve", 6)).Should().Contain(LinkGate.Generic);
            Codes("white sauce", Food("White Sauce", "Sauce, homemade, white, medium", 6)).Should().NotContain(LinkGate.Generic);
            Codes("jam", Food("Jams and preserves", "Jams and preserves", 19)).Should().NotContain(LinkGate.Generic);
        }

        [Fact]
        public void The_signature_covers_the_tables_of_the_distinguishing_rule()
        {
            var source = LinkGate.SignatureSource();
            source.Should().Contain("colours:").And.Contain("entry-words:").And.Contain("ratio:").And.Contain("dry-goods=16|").And.Contain("unsweetened-food=not sweetened|")
                .And.Contain("products-of-food:butter|flour|milk|oil|");
            LinkGate.Version.Should().Be("link-gate/14");
        }
    }
}
