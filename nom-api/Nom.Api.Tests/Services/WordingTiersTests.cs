using System;
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
    /// The deterministic wording tiers as pure rules: the harder key, the order of the tiers and
    /// their disagreement, the head-default table from data with its unit context, the alcohol tier's
    /// ready target, the gate's cue rules read on the wording as written, and the compound
    /// vocabulary's name-key resolution (links B11).
    /// </summary>
    public class WordingTiersTests
    {
        [Theory]
        [InlineData("For the frosting:", null)]
        [InlineData("Filling:", null)]
        [InlineData("a quarter of a pound of butter", "butter")]
        [InlineData("good salt", "salt")]
        [InlineData("chopped onion, for garnish", "onion")]
        [InlineData("flour, divided", "flour")]
        [InlineData("the remains of cold ham", "ham")]
        [InlineData("2 tbsp olive oil, plus more for the pan", "olive oil")]
        [InlineData("Eggs, separated", "eggs")]
        [InlineData("fresh lime juice", "fresh lime juice")]
        [InlineData("dried thyme", "dried thyme")]
        public void The_harder_key_takes_off_filler_preparation_and_notes_and_names_no_heading(string wording, string? key)
        {
            IngredientNameNormalizer.TierKey(wording).Should().Be(key);
        }

        [Fact]
        public void The_harder_key_leaves_the_table_key_alone()
        {
            IngredientNameNormalizer.WordingKey("good salt").Should().Be("good salt");
            IngredientNameNormalizer.WordingKey("flour, divided").Should().Be("flour, divided");
        }

        private static GateFood Curated(long id, string name, string? description = null, int? category = null, int rows = 30, string? fdcId = null) =>
            new(id, name, description, CatalogAdmissionRule.SrLegacyFood, (long)CurationStatusEnum.Curated, category, null, rows, true, false, fdcId);

        private static WordingTierCatalog Catalog(IEnumerable<GateFood> foods, IEnumerable<(string, long)>? aliases = null,
            IEnumerable<(string, string, long?)>? verified = null, WordingHeadDefaults? heads = null) =>
            WordingTierCatalog.Build(foods.ToList(), null, null, aliases, verified, heads);

        [Fact]
        public void The_first_tier_that_answers_decides_and_a_later_tier_naming_another_food_holds_the_pair()
        {
            var cream = Curated(1, "Light Cream");
            var heavy = Curated(2, "Heavy Cream");
            var catalog = Catalog(new[] { cream, heavy }, aliases: new[] { ("light cream", 2L) });

            var decision = WordingTiers.Decide("light cream", 0, Array.Empty<TierLine>(), catalog);

            decision.Answer!.Tier.Should().Be(WordingTiers.Alias);
            decision.Answers.Select(a => a.Tier).Should().Equal(WordingTiers.Alias, WordingTiers.NameKey);
            decision.Holds.Select(h => h.Code).Should().Contain(WordingTiers.TiersDisagree);
            decision.Writes.Should().BeFalse();
        }

        [Fact]
        public void A_verified_verdict_that_is_not_a_food_stops_the_pipeline()
        {
            var catalog = Catalog(new[] { Curated(1, "Cream") }, verified: new (string, string, long?)[] { ("cream", IngredientWordingVerdict.Unclear, null) });

            var decision = WordingTiers.Decide("the cream", 0, Array.Empty<TierLine>(), catalog);

            decision.Answer.Should().BeNull("the name key would give Cream, but the table says unclear");
            decision.Abstain.Should().Contain("unclear");
        }

        [Fact]
        public void A_compound_wording_gets_no_tier_and_a_heading_no_key()
        {
            var catalog = Catalog(new[] { Curated(1, "Butter"), Curated(2, "Lard") });

            WordingTiers.Decide("butter or lard", 0, Array.Empty<TierLine>(), catalog).Abstain.Should().Be(WordingTiers.CompoundAbstain);
            WordingTiers.Decide("For the crust:", 0, Array.Empty<TierLine>(), catalog).Should().Match<WordingTierDecision>(d => d.Key == null && d.Abstain == WordingTiers.HeadingAbstain);
        }

        [Fact]
        public void A_word_the_key_drops_still_holds_the_pair_through_the_cue_rules()
        {
            var tarragon = Curated(1, "Tarragon", "Spices, tarragon, dried", CatalogAdmissionRule.SpicesAndHerbs);
            var catalog = Catalog(new[] { tarragon });

            var bare = WordingTiers.Decide("tarragon", 0, Array.Empty<TierLine>(), catalog);
            var chopped = WordingTiers.Decide("chopped tarragon", 0, Array.Empty<TierLine>(), catalog);

            bare.Writes.Should().BeTrue();
            chopped.Key.Should().Be("tarragon");
            chopped.Answer!.Tier.Should().Be(WordingTiers.NameKey);
            chopped.Holds.Select(h => h.Code).Should().Contain(LinkGate.FreshHerb, "\"chopped\" is a fresh cue the key took off");
        }

        private static IEnumerable<HeadLine> Lines(string head, string unitClass, long food, int count) => Enumerable.Repeat(new HeadLine(head, unitClass, food), count);

        [Fact]
        public void A_head_default_needs_70_percent_of_at_least_20_lines()
        {
            var table = WordingHeadDefaults.Build(Lines("stock", "volume", 1, 13).Concat(Lines("stock", "volume", 2, 7))
                .Concat(Lines("suet", "mass", 3, 19))
                .Concat(Lines("gravy", "volume", 4, 14)).Concat(Lines("gravy", "volume", 5, 6)));

            table.Decide("stock", new[] { "volume" }).Default.Should().BeNull("13 of 20 is 65 %");
            table.Decide("suet", new[] { "mass" }).Default.Should().BeNull("19 lines are too few");
            table.Decide("gravy", new[] { "volume" }).Default!.FoodId.Should().Be(4, "14 of 20 is 70 %");
            table.Overall().Should().ContainKey("gravy").And.NotContainKey("stock");
        }

        [Fact]
        public void The_unit_decides_between_defaults_and_a_small_class_that_disagrees_refuses_the_overall_one()
        {
            var table = WordingHeadDefaults.Build(Lines("soda", "spoon", 10, 60).Concat(Lines("soda", "volume", 20, 20)).Concat(Lines("soda", "count", 20, 5)));

            table.Decide("soda", new[] { "spoon" }).Default!.FoodId.Should().Be(10);
            table.Decide("soda", new[] { "volume" }).Default!.FoodId.Should().Be(20);
            table.Decide("soda", new[] { "mass" }).Default!.FoodId.Should().Be(10, "a class with no lines falls back to the overall default (60 of 85)");
            table.Decide("soda", new[] { "count" }).Default.Should().BeNull("its 5 lines all name another food");
            table.Decide("soda", new[] { "spoon", "volume" }).Reason.Should().Contain("different foods");
        }

        [Theory]
        [InlineData("teaspoon", false, true, false, 1, WordingHeadDefaults.Spoon)]
        [InlineData("pinch", false, false, false, 1, WordingHeadDefaults.Spoon)]
        [InlineData("cup", false, true, false, 1, WordingHeadDefaults.Volume)]
        [InlineData("gram", true, false, false, 100, WordingHeadDefaults.Mass)]
        [InlineData("can", false, false, true, 1, WordingHeadDefaults.Container)]
        [InlineData("piece", false, false, false, 2, WordingHeadDefaults.Count)]
        [InlineData("cup", false, true, false, 0, WordingHeadDefaults.NoUnit)]
        public void A_line_falls_in_one_unit_class(string unit, bool mass, bool volume, bool container, int quantity, string expected)
        {
            WordingHeadDefaults.UnitClass(unit, mass, volume, container, quantity).Should().Be(expected);
        }

        [Fact]
        public void The_alcohol_tier_takes_the_first_ready_food_of_the_sense_and_never_one_short_of_nutrients()
        {
            var claretLike = new GateFood(1, "Alcoholic beverage, wine, table, red", "Alcoholic beverage, wine, table, red", CatalogAdmissionRule.SrLegacyFood,
                (long)CurationStatusEnum.PendingCuration, 14, null, 6, null, false, "173190");
            var tableWine = new GateFood(2, "Alcoholic beverage, wine, table, all", "Alcoholic beverage, wine, table, all", CatalogAdmissionRule.SrLegacyFood,
                (long)CurationStatusEnum.PendingCuration, 14, null, 33, null, false, "173185");
            var rum = new GateFood(3, "Alcoholic beverage, distilled, rum, 80 proof", "Alcoholic beverage, distilled, rum, 80 proof", CatalogAdmissionRule.SrLegacyFood,
                (long)CurationStatusEnum.PendingCuration, 14, null, 33, null, false, "174817");
            var spirit = new GateFood(4, "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", null, CatalogAdmissionRule.SrLegacyFood,
                (long)CurationStatusEnum.PendingCuration, 14, null, 33, null, false, "174815");
            var catalog = Catalog(new[] { claretLike, tableWine, rum, spirit });

            WordingTiers.Ready(claretLike).Should().BeFalse("6 nutrient rows are under 20");
            WordingTiers.AlcoholTarget("rum", new[] { new TierLine("2 tbsp rum", WordingHeadDefaults.Spoon) }, catalog)!.Value.Food.Should().Be(3);
            var red = WordingTiers.AlcoholTarget("red wine", new[] { new TierLine("1 cup red wine", WordingHeadDefaults.Volume) }, catalog);
            (red?.Food).Should().NotBe(1);
            WordingTiers.AlcoholTarget("brandy", new[] { new TierLine("1 glass brandy", WordingHeadDefaults.Volume), new TierLine("1 tsp vanilla extract", WordingHeadDefaults.Spoon) }, catalog)
                .Should().BeNull("a line with another sense breaks the shared reading");
        }

        [Fact]
        public void A_compound_part_resolves_by_the_harder_name_key_and_a_key_two_foods_share_resolves_to_neither()
        {
            var vocabulary = CompoundVocabulary.Build(
                new[] { new CompoundFood(1, "Ham", true), new CompoundFood(2, "Ham, minced", true), new CompoundFood(3, "Tongue, minced", true), new CompoundFood(4, "Tongue, sliced", true) },
                Array.Empty<CompoundAlias>(),
                new[] { "Ham", "Tongue" });

            vocabulary.Resolve("the remains of cold ham").Should().Be(1, "the food named exactly ham owns the key before Ham, minced");
            vocabulary.Resolve("the remains of cold tongue").Should().BeNull("two foods only come to the key tongue once a word is taken off");
            var plan = CompoundLineSplitter.Plan("the remains of cold ham or tongue", vocabulary);
            plan!.Kind.Should().Be(CompoundKind.PickAlternative);
            plan.Parts.Single().FoodId.Should().Be(1);
        }
    }
}
