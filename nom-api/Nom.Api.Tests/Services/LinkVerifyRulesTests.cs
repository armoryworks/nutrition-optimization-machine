using FluentAssertions;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The rules the link lane's judge answers around its model: a wording of two foods is more than
    /// one food without asking, and a "same" the pair contradicts is refused as a variety or form.
    /// </summary>
    public class LinkVerifyRulesTests
    {
        [Theory]
        [InlineData("butter or margarine")]
        [InlineData("salt and pepper")]
        [InlineData("butter, lard, or dripping")]
        public void A_wording_of_two_foods_is_more_than_one_food_without_asking(string wording) =>
            LinkVerifyRules.Before(wording).Should().Be(LinkJudgement.MoreThanOneFood);

        [Theory]
        [InlineData("light or dark brown sugar")]
        [InlineData("half and half")]
        [InlineData("garlic, minced or grated")]
        [InlineData("unsalted butter")]
        public void A_wording_of_one_food_is_asked(string wording) =>
            LinkVerifyRules.Before(wording).Should().BeNull();

        [Theory]
        [InlineData("unsalted butter", "Butter, light, stick, without salt")]
        [InlineData("leg of lamb", "Lamb, leg, whole, separable lean only, cooked, roasted")]
        [InlineData("fish fillets", "Fish, cod, Atlantic, cooked, dry heat")]
        [InlineData("whipped cream", "Cream, whipped, cream topping, pressurized")]
        [InlineData("vermicelli", "Vermicelli, made from soy")]
        [InlineData("freeze-dried strawberries", "Strawberries, raw")]
        [InlineData("unsweetened coconut", "Nuts, coconut meat, dried (desiccated), sweetened, flaked, packaged")]
        [InlineData("cooked chickpeas", "Chickpeas (garbanzo beans, bengal gram), mature seeds, raw")]
        public void A_same_the_pair_contradicts_is_refused_as_a_variety_or_form(string wording, string description)
        {
            LinkVerifyRules.Refuses(wording, description).Should().NotBeNull();
            LinkVerifyRules.After(wording, description, LinkJudgement.Same).Should().Be(LinkJudgement.VarietyOrForm);
        }

        [Theory]
        [InlineData("unsalted butter", "Butter, without salt")]
        [InlineData("light butter", "Butter, light, stick, without salt")]
        [InlineData("cream", "Cream, fluid, light (coffee cream or table cream)")]
        [InlineData("tuna", "Fish, tuna, light, canned in water, drained solids")]
        [InlineData("leg of lamb", "Lamb, leg, whole, separable lean only, raw")]
        [InlineData("roast beef", "Beef, round, eye of round, separable lean only, cooked, roasted")]
        [InlineData("smoked tongue", "Beef, variety meats and by-products, tongue, cooked, simmered")]
        [InlineData("canned whipped cream", "Cream, whipped, cream topping, pressurized")]
        [InlineData("roasted almonds", "Nuts, almonds, dry roasted, without salt added")]
        public void A_same_the_pair_does_not_contradict_stands(string wording, string description)
        {
            LinkVerifyRules.Refuses(wording, description).Should().BeNull();
            LinkVerifyRules.After(wording, description, LinkJudgement.Same).Should().Be(LinkJudgement.Same);
        }

        [Theory]
        [InlineData("dry red wine", "Soup, stock, beef, home-prepared")]
        [InlineData("brandy", "Alcoholic beverage, liqueur, coffee with cream, 34 proof")]
        [InlineData("fresh herbs", "Parsley, fresh")]
        public void A_same_for_a_pair_that_shares_no_word_is_a_different_food(string wording, string description)
        {
            LinkVerifyRules.SharesNoWord(wording, description).Should().BeTrue();
            LinkVerifyRules.After(wording, description, LinkJudgement.Same).Should().Be(LinkJudgement.DifferentFood);
        }

        [Theory]
        [InlineData("fresh mint leaves", "Spearmint, fresh")]
        [InlineData("limes", "Limes, raw")]
        [InlineData("boiling water", "Beverages, water, tap, drinking")]
        [InlineData("fresh", "Parsley, fresh")]
        public void A_pair_that_shares_a_word_is_left_to_the_model(string wording, string description) =>
            LinkVerifyRules.SharesNoWord(wording, description).Should().BeFalse();

        [Theory]
        [InlineData(LinkJudgement.DifferentFood)]
        [InlineData(LinkJudgement.Unsure)]
        [InlineData(LinkJudgement.MoreThanOneFood)]
        public void A_rule_never_turns_another_verdict_into_same(LinkJudgement judgement) =>
            LinkVerifyRules.After("unsalted butter", "Butter, without salt", judgement).Should().Be(judgement);
    }
}
