using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    public class DietDishWordsTests
    {
        private static DietDishWords.Words W(string title, string method = "") => new(title.Split(' ', System.StringSplitOptions.RemoveEmptyEntries), method.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));

        private static IReadOnlySet<string> F(params string[] classes) => classes.ToHashSet();

        [Fact]
        public void A_word_that_nearly_always_comes_with_a_class_holds_it_and_one_below_the_line_holds_nothing()
        {
            var recipes = Enumerable.Range(0, 20).Select(i => (W("potato frittata", "beat the egg"), F("egg", i < 10 ? "milk" : "egg")))
                .Concat(Enumerable.Range(0, 20).Select(i => (W("potato omelette", "beat"), i < 15 ? F("egg") : F())))
                .ToList();
            var rows = DietDishWords.Derive(recipes, DietDishWords.AllCorpus);
            rows.Should().Contain(r => r.Word == "frittata" && r.Place == DietDishWords.TitlePlace && r.Class == "egg" && r.Recipes == 20 && r.Flagged == 20, "staging checkpoint 8, T2: 127188 Potatoes 'in Frittata'");
            rows.Should().NotContain(r => r.Word == "omelette", "15 of 20 is below the line");
            rows.Should().NotContain(r => r.Class == "milk", "10 of 20 frittatas is below the line");
            rows.Should().NotContain(r => r.Place == DietDishWords.MethodPlace && r.Word == "beat", "a method word that ends no title is a step, not a dish");
        }

        [Fact]
        public void A_title_word_holds_its_class_only_where_no_line_carries_it()
        {
            var rows = new[] { new DietDishWords.Row("gravy", DietDishWords.TitlePlace, "gluten", DietDishWords.WebCorpus, 31, 31) };
            DietDishWords.Holds(W("eggs en cocotte with gravy"), new HashSet<string> { "egg" }, rows).Should().ContainKey("gluten", "129719 EGGS EN COCOTTE WITH GRAVY");
            DietDishWords.Holds(W("eggs en cocotte with gravy"), new HashSet<string> { "gravy" }, rows).Should().BeEmpty("a line carries the gravy and is read itself");
        }

        [Fact]
        public void A_lost_part_holds_only_what_most_of_its_dishes_hold_and_a_word_no_web_title_measures_keeps_holding()
        {
            var recipes = Enumerable.Range(0, 30).Select(i => (W("cranberry tart"), true, F("wheat", "gluten", i < 20 ? "milk" : "egg"))).ToList();
            var lost = DietDishWords.DeriveLost(recipes, DietDishWords.WebCorpus);
            lost.Where(r => r.Word == DietDishWords.DoughWord).Select(r => r.Class).Should().BeEquivalentTo(new[] { "wheat", "gluten", "milk" }, "egg is on 10 of 30, under half");
            DietDishWords.DeriveLost(Enumerable.Range(0, 20).Select(_ => (W("plain stock"), false, F())), DietDishWords.WebCorpus)
                .Should().ContainSingle(r => r.Word == "stock" && r.Class == DietDishWords.NoClass, "a measured word below the line holds nothing");
            DietDishWords.LostClasses("rizotto-never-titled").Should().BeNull("a word too few web titles carry is not known to be safe");
            DietDishWords.LostClasses(DietDishWords.DoughWord).Should().Contain("gluten").And.NotContain("peanut", "staging checkpoint 8, T8: a lost dough held Peanut Allergy");
        }

        [Fact]
        public void The_committed_table_parses_and_holds_the_checkpoint_dishes()
        {
            DietDishWords.All.Should().Contain(r => r.Word == "frittata" && r.Class == "egg");
            DietDishWords.All.Should().Contain(r => r.Word == "gravy" && r.Place == DietDishWords.TitlePlace && r.Class == "gluten");
            DietDishWords.All.Where(r => r.Place != DietDishWords.LostPlace).Should().OnlyContain(r => r.Recipes >= DietDishWords.MinRecipes && r.Flagged >= DietDishWords.HoldShare * r.Recipes);
        }

        [Fact]
        public void A_named_foods_flour_is_that_food_ground_and_a_bare_flour_is_wheat()
        {
            var ground = new HashSet<string> { "mustard", "cashew" };
            BrandedConsensus.GroundAs("MUSTARD FLOUR", ground).Should().Be("MUSTARD");
            BrandedConsensus.GroundAs("ENRICHED FLOUR", ground).Should().Be("ENRICHED FLOUR");
            BrandedConsensus.StatedClasses("WATER, VINEGAR, MUSTARD FLOUR, SALT.", ground).Should().NotContain("gluten", "staging checkpoint 8, T4: Honey Mustard Dressing read as gluten");
            BrandedConsensus.StatedClasses("WATER, VINEGAR, MUSTARD FLOUR, SALT.").Should().Contain("gluten", "without the ground words the flour is read as before");
            BrandedConsensus.StatedClasses("CASHEW FLOUR, SUGAR.", ground).Should().Contain("treenut").And.NotContain("wheat");
        }

        [Fact]
        public void A_hyphenated_product_word_is_joined_where_the_joined_word_is_a_key_word()
        {
            BrandedConsensus.Joined("SEMI-SWEET CHOCOLATE", new HashSet<string> { "semisweet", "chocolate" }).Should().Be("SEMISWEET CHOCOLATE", "staging checkpoint 8, T12");
            BrandedConsensus.Joined("READY-TO-EAT", new HashSet<string> { "semisweet" }).Should().Be("READY-TO-EAT");
        }

        private static DietDataCriterion Like(string pattern, string type) => new(1, 1, null, null, null, null, 5, null, pattern, type);

        [Fact]
        public void A_fruit_its_description_gives_as_dried_is_counted_for_sulfite_and_its_stewed_form_inherits_it_but_a_juice_does_not()
        {
            var foods = new[]
            {
                new DietFoodCatalog.Food(1, "Figs, dried, stewed", true, 9, "sr_legacy_food", null, null, null, null, true, "169941"),
                new DietFoodCatalog.Food(2, "Stewed Prunes", true, 9, "sr_legacy_food", null, null, null, null, true, "1"),
                new DietFoodCatalog.Food(3, "Prune juice, canned", true, 9, "sr_legacy_food", null, null, null, null, true, "2"),
                new DietFoodCatalog.Food(4, "Figs, raw", true, 9, "sr_legacy_food", null, null, null, null, true, "3"),
            };
            var aliases = new[] { new DietFoodCatalog.Alias(2, "Plums, dried (prunes), stewed, without added sugar", IngredientLinkService.UsdaDescriptionContext) };
            var catalog = DietFoodCatalog.Of(foods, aliases, consensus: new List<ConsensusRow>());
            var decision = DietFoodReader.Decide(DietFoodReader.Read(catalog, foods.Select(f => f.Id).ToList()), new[] { Like("%sulfite%", "Sulfite Sensitivity") });
            decision.Foods[1].Should().BeEquivalentTo(new long[] { 1, 2 }, "staging checkpoint 8, T2: 31416 STEWED FIGS and 96827's prunes were shown");
            decision.Sources[(1, 2)].Should().Be(DietFoodReader.DriedFruitSource);
        }

        [Fact]
        public void A_curated_made_product_whose_brands_were_never_read_is_of_unknown_contents_for_a_severe_set_never_clear()
        {
            var foods = new[]
            {
                new DietFoodCatalog.Food(1, "Semisweet Chocolate", true, 19, "sr_legacy_food", null, null, null, null, true, "167976"),
                new DietFoodCatalog.Food(2, "Semisweet Chocolate", true, 19, "sr_legacy_food", null, null, null, null, true, null),
            };
            var catalog = DietFoodCatalog.Of(foods, System.Array.Empty<DietFoodCatalog.Alias>());
            var reading = DietFoodReader.Read(catalog, foods.Select(f => f.Id).ToList());
            BrandedConsensus.UnreadClasses(19).Should().Contain(new[] { "milk", "soy" }, "the chocolates and candies read state them");
            DietFoodReader.Decide(reading, new[] { Like("%soy%", "Soy Allergy") }).Unread.Should().BeEquivalentTo(new long[] { 1, 2 }, "staging checkpoint 8, T12: 60790, 64215 and 66458 were shown for Soy Allergy; a food with no FDC id and no row of its name is unread too (lead answer, wave 31)");
            DietFoodReader.Decide(reading, new[] { new DietDataCriterion(1, 1, null, null, null, null, 3, null, "%soy%", "Soy Allergy") }).Unread.Should().BeEmpty("only a severe set holds an unread food");
        }
    }
}
