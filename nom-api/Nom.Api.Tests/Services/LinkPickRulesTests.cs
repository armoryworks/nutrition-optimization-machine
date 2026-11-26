using System.Collections.Generic;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The rules the link lane answers before asking its model: a wording of two foods is linked to
    /// none, while two variants of one food, two preparations and an amount given another way are
    /// still asked.
    /// </summary>
    public class LinkPickRulesTests
    {
        [Theory]
        [InlineData("butter or margarine")]
        [InlineData("salt and pepper")]
        [InlineData("olive or avocado oil")]
        [InlineData("cumin + coriander")]
        [InlineData("each: sugar and cinnamon")]
        [InlineData("plain yogurt or sour cream, at room temperature")]
        [InlineData("shallot or small onion")]
        [InlineData("butter, lard, or dripping")]
        public void A_wording_of_two_foods_is_linked_to_none(string name)
        {
            LinkPickRules.NamesTwoFoods(name).Should().BeTrue();
            LinkPickRules.RuleNone(name).Should().Be(LinkPickRules.TwoFoods);
        }

        [Theory]
        [InlineData("light or dark brown sugar")]
        [InlineData("fresh or frozen peas")]
        [InlineData("peeled and sliced squash")]
        [InlineData("peeled & chopped pears")]
        [InlineData("onion or two")]
        [InlineData("fresh mint or 1 teaspoon dried")]
        [InlineData("or more of warm water")]
        [InlineData("half and half")]
        [InlineData("garlic, minced or grated")]
        [InlineData("butter or substitute")]
        [InlineData("sweetened or unsweetened applesauce")]
        [InlineData("lentils, drained, or 2 cups cooked lentils")]
        public void Two_variants_of_one_food_are_still_asked(string name)
        {
            LinkPickRules.NamesTwoFoods(name).Should().BeFalse();
            LinkPickRules.RuleNone(name).Should().BeNull();
        }

        private static readonly Dictionary<string, FoodCandidate> Staples = new()
        {
            ["Onions, raw"] = new FoodCandidate(1, "170000", "Onions, raw", "sr_legacy_food"),
            ["Butter, salted"] = new FoodCandidate(2, "173410", "Butter, salted", "sr_legacy_food"),
            ["Salt, table"] = new FoodCandidate(3, "173468", "Salt, table", "sr_legacy_food"),
        };

        [Theory]
        [InlineData("good-sized onion", "170000")]
        [InlineData("small onion chopped fine", "170000")]
        [InlineData("butter the size of an egg", "173410")]
        [InlineData("melted butter, divided", "173410")]
        [InlineData("flaked sea salt", "173468")]
        public void A_staple_named_with_sizes_praise_or_preparation_is_the_staple(string name, string fdcId)
        {
            var target = LinkPickRules.RuleTarget(name, Staples);

            target.Should().NotBeNull();
            target!.Value.Food.FdcId.Should().Be(fdcId);
            target.Value.Source.Should().Be(LinkPickRules.PlainWordsSource);
        }

        [Theory]
        [InlineData("butter")]
        [InlineData("frozen butter")]
        [InlineData("onion, in rings with the skins")]
        [InlineData("grated ginger")]
        [InlineData("browned butter")]
        public void A_wording_that_names_more_than_a_staple_is_left_to_the_model(string name)
        {
            LinkPickRules.RuleTarget(name, Staples).Should().BeNull();
        }

        [Theory]
        [InlineData("freeze-dried blueberries", "Blueberries, raw")]
        [InlineData("freeze dried mango", "Mangos, raw")]
        [InlineData("unsweetened dried cherries", "Cherries, dried, sweetened")]
        [InlineData("sweetened applesauce", "Applesauce, canned, unsweetened, without added ascorbic acid")]
        [InlineData("cooked wild rice", "Wild rice, raw")]
        public void A_pick_that_contradicts_the_wording_is_refused(string name, string food)
        {
            LinkPickRules.Contradicts(name, new FoodCandidate(1, "1", food, "sr_legacy_food")).Should().NotBeNull();
        }

        [Theory]
        [InlineData("freeze-dried blueberries", "Blueberries, freeze-dried")]
        [InlineData("unsweetened coconut flakes", "Nuts, coconut meat, dried (desiccated), not sweetened")]
        [InlineData("roasted beets", "Beets, raw")]
        [InlineData("raw or cooked shrimp", "Crustaceans, shrimp, raw")]
        [InlineData("sweetened dried cranberries", "Cranberries, dried, sweetened")]
        public void A_pick_the_wording_allows_is_kept(string name, string food)
        {
            LinkPickRules.Contradicts(name, new FoodCandidate(1, "1", food, "sr_legacy_food")).Should().BeNull();
        }
    }
}
