using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Shortlisting USDA foods for recipe ingredient names. Descriptions are real SR Legacy rows;
    /// the leading segment carries the food unless it is a class word ("Spices, cinnamon, ground").
    /// </summary>
    public class FoodNameMatcherTests
    {
        private static readonly FoodNameMatcher Matcher = new(new[]
        {
            new FoodCandidate(1, "173430", "Butter, salted", "sr_legacy_food"),
            new FoodCandidate(2, "173410", "Butter, without salt", "sr_legacy_food"),
            new FoodCandidate(3, "171287", "Egg, whole, raw, fresh", "sr_legacy_food"),
            new FoodCandidate(4, "172183", "Egg, white, raw, fresh", "sr_legacy_food"),
            new FoodCandidate(5, "171320", "Spices, cinnamon, ground", "sr_legacy_food"),
            new FoodCandidate(6, "173468", "Salt, table", "sr_legacy_food"),
            new FoodCandidate(7, "169655", "Sugars, granulated", "sr_legacy_food"),
            new FoodCandidate(8, "168833", "Sugars, brown", "sr_legacy_food"),
            new FoodCandidate(9, "170567", "Nuts, almonds", "sr_legacy_food"),
            new FoodCandidate(10, "171413", "Oil, olive, salad or cooking", "sr_legacy_food"),
            new FoodCandidate(11, "169640", "Honey", "sr_legacy_food"),
            new FoodCandidate(12, "170393", "Carrots, raw", "sr_legacy_food"),
            new FoodCandidate(13, "170924", "Spices, ginger, ground", "sr_legacy_food"),
            new FoodCandidate(14, "169231", "Ginger root, raw", "sr_legacy_food"),
            new FoodCandidate(15, "170931", "Spices, pepper, black", "sr_legacy_food"),
            new FoodCandidate(16, "168576", "Peppers, hungarian, raw", "sr_legacy_food"),
            new FoodCandidate(17, "174133", "Beverages, Wine, non-alcoholic", "sr_legacy_food"),
            new FoodCandidate(18, "174538", "Sauce, fish, ready-to-serve", "sr_legacy_food"),
            new FoodCandidate(19, "173472", "Vinegar, red wine", "sr_legacy_food"),
            new FoodCandidate(20, "171185", "Milk, chocolate beverage, hot cocoa, homemade", "sr_legacy_food"),
            new FoodCandidate(21, "175010", "Pastry, Pastelitos de Guava (guava pastries)", "sr_legacy_food"),
        });

        [Theory]
        [InlineData("salt", 6)]
        [InlineData("Honey", 11)]
        [InlineData("carrots", 12)]
        [InlineData("chopped carrots", 12)]
        [InlineData("cinnamon", 5)]
        [InlineData("almonds", 9)]
        public void Unambiguous_names_match_exactly(string name, long expectedId)
        {
            Matcher.Exact(name)!.IngredientId.Should().Be(expectedId);
        }

        [Theory]
        [InlineData("butter")]
        [InlineData("eggs")]
        [InlineData("sugar")]
        public void Ambiguous_names_have_no_exact_match_but_a_shortlist(string name)
        {
            Matcher.Exact(name).Should().BeNull("several USDA foods share that name");
            Matcher.Shortlist(name).Should().HaveCountGreaterThan(1);
        }

        [Fact]
        public void Shortlist_ranks_the_closer_description_first()
        {
            Matcher.Shortlist("unsalted butter").First().Name.Should().Be("Butter, without salt");
            Matcher.Shortlist("brown sugar").First().Name.Should().Be("Sugars, brown");
            Matcher.Shortlist("extra virgin olive oil").First().Name.Should().Be("Oil, olive, salad or cooking");
        }

        [Fact]
        public void Fresh_and_ground_are_kept_apart()
        {
            Matcher.Exact("fresh ginger").Should().BeNull();
            Matcher.Shortlist("ground ginger").First().Name.Should().Be("Spices, ginger, ground");
        }

        [Theory]
        [InlineData("black pepper", 15)]
        [InlineData("unsalted butter", 2)]
        public void Covering_matches_need_every_word(string name, long expectedId)
        {
            Matcher.Covering(name)!.IngredientId.Should().Be(expectedId);
        }

        [Fact]
        public void Covering_refuses_when_several_foods_qualify()
        {
            Matcher.Covering("butter").Should().BeNull();
        }

        [Theory]
        [InlineData("wine")]
        [InlineData("fish")]
        [InlineData("red wine")]
        [InlineData("hot milk")]
        [InlineData("pastry")]
        public void A_lone_candidate_that_is_a_different_food_is_not_a_match(string name)
        {
            (Matcher.Exact(name) ?? Matcher.Covering(name)).Should().BeNull();
        }

        [Fact]
        public void A_class_that_names_the_food_must_be_said()
        {
            Matcher.Exact("olive oil")!.IngredientId.Should().Be(10);
            Matcher.Exact("fish sauce")!.IngredientId.Should().Be(18);
            Matcher.Exact("red wine vinegar")!.IngredientId.Should().Be(19);
        }

        [Fact]
        public void Unrelated_names_get_no_candidates()
        {
            Matcher.Shortlist("xanthan gum").Should().BeEmpty();
            Matcher.Exact("sugar snap peas").Should().BeNull();
        }
    }
}
