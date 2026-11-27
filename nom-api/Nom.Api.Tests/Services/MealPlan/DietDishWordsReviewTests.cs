using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Data.Plan;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    public class DietDishWordsReviewTests
    {
        private static DietDataCriterion Like(long id, string pattern, string type, int severity = 5) => new(id, 1, null, null, null, null, severity, null, pattern, type);

        private static DietGateStoreBuilder.SetState Configured(params DietDataCriterion[] criteria)
        {
            var set = new DietGateStoreBuilder.SetState(new DietGateSetEntity { Id = 1, CriteriaKey = "k", RestrictionTypeId = 1, Severe = true, State = DietGateStore.Complete });
            DietGateStoreBuilder.Configure(set, new DietGateStoreBuilder.Spec(1, 0, "k", "k", true, criteria.ToList()));
            return set;
        }

        private static DietGateStoreBuilder.Reading Lost() =>
            new(1, Array.Empty<string>(), Array.Empty<DietCriteriaMatcher.RecipeLine>(), Array.Empty<long>(), 1, Array.Empty<string>(), Array.Empty<string>()) { DoughLost = true, FlourLost = true, LostDish = "omelette" };

        [Fact]
        public void A_severe_set_the_dish_words_never_measured_keeps_every_lost_part_hold()
        {
            var corn = Configured(Like(1, "%corn%", "Corn Allergy"), Like(2, "%maize%", "Corn Allergy"));
            corn.Measured.Should().BeFalse("the shares were read for the vocabulary's reference types only");
            corn.HoldsBase.Should().BeTrue();
            new DietGateStoreBuilder().Decide(Lost(), 1, corn, new()).Should().NotBeNull("a Johnny Cake whose cornmeal the lines lost is not known to be free of corn (review ck19-diet-1)");

            var walnut = Configured(Like(1, "%walnut%", "Walnut Allergy"));
            walnut.Measured.Should().BeFalse();
            new DietGateStoreBuilder().Decide(Lost(), 1, walnut, new()).Should().NotBeNull();

            var soy = Configured(Like(1, "%soy%", "Soy Allergy"), Like(2, "%tofu%", "Soy Allergy"));
            soy.Measured.Should().BeTrue();
            soy.DishClasses.Should().Equal("soy");
        }

        [Fact]
        public void A_food_fingerprint_moves_with_its_fdc_id_and_with_its_categorys_unread_classes()
        {
            var plain = new DietFoodCatalog.Food(1, "Semisweet Chocolate", true, 19, "sr_legacy_food", null, null, null, null, true, null);
            var none = new List<ConsensusRow>();
            DietFoodCatalog.Of(new[] { plain }, Array.Empty<DietFoodCatalog.Alias>(), consensus: none).Fingerprint(1)
                .Should().NotBe(DietFoodCatalog.Of(new[] { plain with { FdcId = "167976" } }, Array.Empty<DietFoodCatalog.Alias>(), consensus: none).Fingerprint(1), "an FDC id set on a food is another reading of it (review ck19-diet-2)");

            var read = new List<ConsensusRow>
            {
                new("1", "Milk Chocolate", "any", BrandedConsensus.Read, 40, 0, Array.Empty<string>()) { Category = 19 },
                new("1", "Milk Chocolate", "milk", BrandedConsensus.Usually, 40, 39, Array.Empty<string>()) { Category = 19 },
            };
            var described = plain with { FdcId = "167976" };
            var withRead = DietFoodCatalog.Of(new[] { described }, Array.Empty<DietFoodCatalog.Alias>(), consensus: read);
            withRead.UnreadClassesOf(described).Should().Contain("milk");
            withRead.Fingerprint(1).Should().NotBe(DietFoodCatalog.Of(new[] { described }, Array.Empty<DietFoodCatalog.Alias>(), consensus: none).Fingerprint(1), "a consensus derived again that changes the category's unread classes decides the food again");
            DietGateStore.Version.Should().Contain(BrandedConsensus.Signature).And.Contain(DietDishWords.Signature, "a regenerated table is a new version of the store (review ck19-diet-2, ck19-diet-4)");
        }

        [Fact]
        public void The_referral_patterns_are_part_of_the_store_version_and_the_version_fits_its_column()
        {
            DietGateStore.Version.Should().Contain(IngredientLineParser.ReferralSignature, "a widened referral pattern must start a new build, not leave decided recipes on the old reading (review ck19w31-3)");
            IngredientLineParser.ReferralSignature.Should().MatchRegex("^referral/[0-9]+:[0-9a-f]{8}$");
            DietGateStore.Version.Length.Should().BeLessThanOrEqualTo(1023, "DietGateBuild.Version is varchar(1023) since ck21");
        }

        [Fact]
        public void A_food_read_from_a_few_single_ingredient_statements_is_split_on_a_class_one_of_them_states()
        {
            var tally = new ConsensusTally { Matched = 8, Read = 8, Single = 6 };
            tally.Stating["milk"] = new List<string> { "11", "12" };
            var rows = BrandedConsensus.Rows(new[] { new ConsensusFood(1, "2", "Chocolate, dark", 19, "Chocolate, dark") }, new Dictionary<long, ConsensusTally> { [1] = tally });
            rows.Should().Contain(r => r.Class == BrandedConsensus.AnyClass && r.Verdict == BrandedConsensus.Read);
            rows.Should().Contain(r => r.Class == "milk" && r.Verdict == BrandedConsensus.Split, "a stated class among a few read products is unknown, never clear (review ck19-diet-5)");
            rows.Should().NotContain(r => r.Class == "soy");
        }

        [Fact]
        public void A_title_food_no_line_names_holds_nothing_by_its_name_alone()
        {
            var butter = DietPatternRule.For("%butter%");
            DietRecipeText.ReadsTitle("Boil the beans with the ham until tender.", butter, false, "Butter Beans with Ham", new[] { "1 lb lima beans", "ham" })
                .Should().BeFalse("the title word is below the measured line and holds nothing by itself (review ck19-diet-3)");
        }
    }
}
