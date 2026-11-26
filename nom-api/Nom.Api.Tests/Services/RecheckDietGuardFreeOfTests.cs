using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The re-check's diet guard and a free-of marker (the lead's decision on ck12-links-small's owner question): "vegan
    /// butter", "dairy-free cheese" or "gluten-free flour" counts as the line naming what it lacks only for a diet of choice
    /// whose whole restriction the marker names; for an allergy, Celiac Disease and every other severe type, and for a diet
    /// that also restricts what the marker does not name, the take-back is read strictly as before.
    /// </summary>
    public class RecheckDietGuardFreeOfTests
    {
        private static readonly GateFood Butter = new(280603, "Butter", "Butter, salted");

        private static readonly GateFood Flour = new(276224, "Flour", "Wheat flour, white, all-purpose, enriched, bleached");

        private static readonly GateFood Oats = new(274747, "Whole Rolled Oats", "Oats, whole grain, rolled, old fashioned");

        private static readonly GateFood Milk = new(2, "Whole Milk", "Milk, whole, 3.25% milkfat");

        private static readonly GateFood GroundBeef = new(3, "Ground Beef", "Beef, ground, 80% lean meat / 20% fat, raw");

        [Theory]
        [InlineData("vegan butter", new[] { "Vegan", "Ethical Vegan", "Plant-Based", "Dairy-Free", "Vegetarian" }, new[] { "Milk Allergy", "Lactose Intolerance", "Paleo", "Whole30", "Sugar-Free", "Jain", "Buddhist Vegetarian" })]
        [InlineData("dairy-free cheese", new[] { "Dairy-Free" }, new[] { "Milk Allergy", "Lactose-Intolerant", "Vegan", "Paleo" })]
        [InlineData("gluten-free flour", new[] { "Gluten-Free" }, new[] { "Celiac Disease", "Gluten Sensitivity", "Wheat Allergy", "Grain-Free", "Paleo", "Low-FODMAP" })]
        [InlineData("meatless crumbles", new[] { "Vegetarian", "Pescatarian" }, new[] { "Vegan", "Jain", "Buddhist Vegetarian", "Halal" })]
        public void A_free_of_marker_covers_only_the_diets_of_choice_whose_whole_restriction_it_names(string wording, string[] covered, string[] strict)
        {
            var marked = RecheckDietGuard.For().MarkedFree(wording);
            marked.Should().Contain(covered);
            marked.Should().NotContain(strict);
        }

        [Theory]
        [InlineData("butter or vegan butter")]
        [InlineData("melted coconut oil or vegan butter")]
        [InlineData("unsalted butter")]
        public void A_marker_after_the_first_item_or_none_at_all_covers_nothing(string wording) =>
            RecheckDietGuard.For().MarkedFree(wording).Should().BeEmpty();

        [Fact]
        public void A_severe_type_never_counts_a_free_of_marker()
        {
            var guard = RecheckDietGuard.For();
            foreach (var wording in new[] { "vegan butter", "dairy-free milk", "gluten-free flour", "egg-free mayonnaise", "nut-free pesto", "soy-free sauce", "lactose-free milk" })
            {
                guard.MarkedFree(wording).Where(t => !(DefaultRestrictionCriteria.ByName.ContainsKey(t) && DefaultRestrictionCriteria.ByName[t].Severity < RecheckDietGuard.SevereFrom)).Should()
                    .BeEmpty(wording);
            }

            var stored = new DietDataCriterion(1, 1, null, null, null, null, 5, null, "%butter%", "Vegan");
            RecheckDietGuard.For(new[] { stored }).MarkedFree("vegan butter").Should().NotContain("Vegan", "a stored criterion at allergy severity makes the type severe");
            RecheckDietGuard.For(new[] { stored }).Lowered(Butter, "vegan butter", new[] { "1/2 cup vegan butter" }).Should().Contain("Vegan");
        }

        [Fact]
        public void A_take_back_marked_free_lowers_no_diet_of_choice_the_marker_covers_and_keeps_every_severe_flag_held()
        {
            var guard = RecheckDietGuard.For();

            var butter = guard.Lowered(Butter, "vegan butter", new[] { "1/2 cup vegan butter" });
            butter.Should().NotContain(new[] { "Vegan", "Dairy-Free", "Plant-Based" }, "the line says the butter is made without what those diets restrict");
            butter.Should().Contain("Milk Allergy", "an allergy reads the take-back strictly, so the line stays where it is");
            butter.Should().Contain(new[] { "Paleo", "Whole30" }, "those diets also restrict what the marker does not name");

            var flour = guard.Lowered(Flour, "gluten-free flour", new[] { "2 cups gluten-free flour" });
            flour.Should().NotContain("Gluten-Free");
            flour.Should().Contain(new[] { "Celiac Disease", "Gluten Sensitivity" }, "Celiac and every severe type are read strictly as before");

            var milk = guard.Lowered(Milk, "dairy-free milk", new[] { "1 cup dairy-free milk" });
            milk.Should().NotContain("Dairy-Free");
            milk.Should().Contain(new[] { "Milk Allergy", "Lactose Intolerance", "Vegan" }, "dairy-free does not say the milk is vegan");

            var oats = guard.Lowered(Oats, "gluten-free rolled oats", new[] { "1 cup gluten-free rolled oats" });
            oats.Should().NotContain("Gluten-Free");
            oats.Should().Contain("Carnivore", "gluten-free oats are still a grain");

            var beef = guard.Lowered(GroundBeef, "meatless crumbles", new[] { "1 lb meatless crumbles" });
            beef.Should().NotContain(new[] { "Vegetarian", "Pescatarian" });
            beef.Should().Contain(new[] { "Vegan", "Jain", "Buddhist Vegetarian" }, "meatless says nothing of dairy, eggs, onion or garlic");
        }

        [Fact]
        public void A_take_back_whose_marker_sits_after_the_first_item_is_read_strictly()
        {
            var guard = RecheckDietGuard.For();
            guard.Lowered(Butter, "melted coconut oil or vegan butter", new[] { "1/2 cup melted coconut oil or vegan butter" })
                .Should().Contain(new[] { "Vegan", "Dairy-Free" }, "the marker sits in the second item, so the take-back would drop flags the line does not name");
        }
    }
}
