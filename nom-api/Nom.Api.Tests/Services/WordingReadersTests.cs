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
    /// The readers' deterministic half: the evidence pack and its small-pack batching, the shuffle
    /// each reader sees, the outcome table with the nutrition-equivalent rule, the rules that confirm
    /// two "unclear" answers, the lexicon's term and meaning, the evidence key, the fixed readings of
    /// an unclear alcohol word, and the gate reading a fixed reading's sense and a lexicon entry.
    /// </summary>
    public class WordingReadersTests
    {
        private static ReaderFood Food(long id, string text, int? category = 14, decimal? kcal = 100, decimal? protein = 10, decimal? fat = 5, decimal? carbohydrate = 20, int rows = 30) =>
            new(id, text, text, category, rows, kcal, protein, fat, carbohydrate);

        private static WordingPack Pack(string wording, params ReaderFood[] candidates) =>
            new(wording, Array.Empty<ReaderLine>(), new Dictionary<string, double>(), candidates, true);

        private static ReaderAnswer FoodAnswer(long id) => new(IngredientWordingVerdict.Food, id);

        private static readonly ReaderAnswer None = new(IngredientWordingVerdict.None, null);
        private static readonly ReaderAnswer Unclear = new(IngredientWordingVerdict.Unclear, null);

        private static readonly ReaderFood DryDessert = Food(1, "Alcoholic beverage, wine, dessert, dry", 14, 152, 0.2m, 0m, 11.67m, 31);
        private static readonly ReaderFood SweetDessert = Food(2, "Alcoholic beverage, wine, dessert, sweet", 14, 160, 0.2m, 0m, 13.69m, 33);
        private static readonly ReaderFood TableWine = Food(3, "Alcoholic beverage, wine, table, all", 14, 83, 0.07m, 0m, 2.72m, 33);

        [Fact]
        public void The_same_food_from_both_readers_goes_to_the_gate()
        {
            var outcome = WordingReaders.Combine(FoodAnswer(1), FoodAnswer(1), Pack("sack", DryDessert, SweetDessert));
            outcome.Should().Be(new ReaderOutcome(IngredientWordingVerdict.Food, 1, WordingReaders.Agree));
        }

        [Fact]
        public void Two_nutrition_equivalent_foods_count_as_agreement_and_the_one_with_fewer_qualifiers_the_wording_lacks_is_taken()
        {
            var outcome = WordingReaders.Combine(FoodAnswer(1), FoodAnswer(2), Pack("sack", DryDessert, SweetDessert));
            outcome.Rule.Should().Be(WordingReaders.Equivalent);
            outcome.FoodId.Should().Be(2, "both lack a word the wording has, so the one with more nutrient rows wins");

            WordingReaders.Combine(FoodAnswer(1), FoodAnswer(2), Pack("dry sack", DryDessert, SweetDessert)).FoodId.Should().Be(1, "the wording names dry");
        }

        [Fact]
        public void Two_foods_that_are_not_nutrition_equivalent_write_nothing()
        {
            var outcome = WordingReaders.Combine(FoodAnswer(1), FoodAnswer(3), Pack("wine", DryDessert, TableWine));
            outcome.Writes.Should().BeFalse();
            outcome.Rule.Should().Be(WordingReaders.Disagree);
        }

        [Theory]
        [InlineData(152, 170, false)]
        [InlineData(152, 165, true)]
        public void Energy_must_be_within_ten_percent(double a, double b, bool equivalent)
        {
            WordingReaders.NutritionEquivalent(DryDessert with { Kcal = (decimal)a }, SweetDessert with { Kcal = (decimal)b }).Should().Be(equivalent);
        }

        [Fact]
        public void Another_lead_word_another_category_or_a_missing_fact_is_not_equivalent()
        {
            WordingReaders.NutritionEquivalent(DryDessert, Food(4, "Beer, regular", 14, 152, 0.2m, 0m, 11.67m)).Should().BeFalse();
            WordingReaders.NutritionEquivalent(DryDessert, SweetDessert with { CategoryId = 9 }).Should().BeFalse();
            WordingReaders.NutritionEquivalent(DryDessert, SweetDessert with { Protein = null }).Should().BeFalse();
            WordingReaders.NutritionEquivalent(DryDessert with { Carbohydrate = 10m }, SweetDessert with { Carbohydrate = 13.69m }).Should().BeFalse("26 % apart");
        }

        [Fact]
        public void Small_macronutrient_amounts_count_as_equal()
        {
            WordingReaders.NutritionEquivalent(DryDessert with { Fat = 0.1m }, SweetDessert with { Fat = 0.4m }).Should().BeTrue();
        }

        [Fact]
        public void A_food_against_none_or_unclear_or_a_missing_answer_writes_nothing()
        {
            var pack = Pack("sack", DryDessert);
            WordingReaders.Combine(FoodAnswer(1), None, pack).Rule.Should().Be(WordingReaders.Disagree);
            WordingReaders.Combine(Unclear, FoodAnswer(1), pack).Rule.Should().Be(WordingReaders.Disagree);
            WordingReaders.Combine(None, Unclear, pack).Rule.Should().Be(WordingReaders.Disagree);
            WordingReaders.Combine(null, FoodAnswer(1), pack).Rule.Should().Be(WordingReaders.Unreadable);
            WordingReaders.Combine(FoodAnswer(9), FoodAnswer(9), pack).Writes.Should().BeFalse("a food no candidate is");
        }

        [Fact]
        public void Two_none_or_two_unclear_answers_go_to_their_own_checks()
        {
            WordingReaders.Combine(None, None, Pack("larks")).Should().Be(new ReaderOutcome(IngredientWordingVerdict.None, null, WordingReaders.BothNone));
            WordingReaders.Combine(Unclear, Unclear, Pack("stock")).Should().Be(new ReaderOutcome(IngredientWordingVerdict.Unclear, null, WordingReaders.BothUnclear));
        }

        [Fact]
        public void Two_unclear_answers_are_confirmed_only_by_a_rule()
        {
            var even = new Dictionary<string, double> { ["mass"] = 1.0 };
            WordingReaders.UnclearRule("stock", new[] { "1 pint stock" }, even).Should().Contain("class word");
            WordingReaders.UnclearRule("flaked salmon", new[] { "1 cup flaked salmon", "2 cups flaked salmon" }, new Dictionary<string, double> { ["volume"] = 0.6, ["count"] = 0.4 })
                .Should().Contain("split unit profile");
            WordingReaders.UnclearRule("cream", new[] { "1 cup cream", "a cup of milk", "1 gill cream" }, even).Should().Contain("do not name the wording");
            WordingReaders.UnclearRule("raspberry jam", new[] { "1 cup raspberry jam", "2 tbsp raspberry jam" }, even).Should().BeNull();
        }

        [Fact]
        public void A_wording_with_fewer_than_three_lines_gets_a_small_pack_and_small_packs_go_six_to_a_call()
        {
            var foods = Enumerable.Range(1, 15).Select(i => Food(i, "Food " + i)).ToList();
            var one = WordingReaders.Pack("larks", new[] { (1L, new ReaderLine("6 larks", "Lark Pie", "book", "count")) }, foods);
            one.Small.Should().BeTrue();
            one.Candidates.Should().HaveCount(WordingReaders.SmallCandidates);

            var lines = Enumerable.Range(1, 60).Select(i => ((long)(i % 45), new ReaderLine($"{i} cups flour", "Bread " + i, "web", "volume"))).ToList();
            var full = WordingReaders.Pack("flour", lines, foods);
            full.Small.Should().BeFalse();
            full.Lines.Should().HaveCount(WordingReaders.MaxLines);
            full.Candidates.Should().HaveCount(WordingReaders.MaxCandidates);
            full.UnitProfile.Should().Equal(new Dictionary<string, double> { ["volume"] = 1.0 });

            var smalls = Enumerable.Range(1, 13).Select(i => one with { Wording = "w" + i }).ToList();
            var calls = WordingReaders.Calls(smalls.Append(full with { Wording = "big" }));
            calls.Select(c => c.Count).Should().Equal(1, 6, 6, 1);
        }

        [Fact]
        public void Each_reader_sees_the_candidates_in_its_own_order_and_a_rerun_repeats_it()
        {
            var pack = Pack("saleratus", Enumerable.Range(1, 8).Select(i => Food(i, "Food " + i)).ToArray());
            var a = WordingReaders.Shuffled(pack, "A").Candidates.Select(c => c.Id).ToList();
            var b = WordingReaders.Shuffled(pack, "B").Candidates.Select(c => c.Id).ToList();

            a.Should().BeEquivalentTo(pack.Candidates.Select(c => c.Id));
            a.Should().NotEqual(b);
            WordingReaders.Shuffled(pack, "A").Candidates.Select(c => c.Id).Should().Equal(a);
        }

        [Fact]
        public void A_lexicon_term_is_one_word_and_both_readers_must_propose_the_same_meaning()
        {
            WordingReaders.LexiconTerm("saleratus").Should().Be("saleratus");
            WordingReaders.LexiconTerm("baking soda").Should().BeNull();
            WordingReaders.Meaning("  Baking Soda ", "saleratus").Should().Be("baking soda");
            WordingReaders.Meaning("saleratus", "saleratus").Should().BeNull();
            WordingReaders.Meaning("a very long name of a food", "x").Should().BeNull();

            var a = new ReaderAnswer(IngredientWordingVerdict.None, null, Means: "baking soda");
            WordingReaders.AgreedMeaning(a, a with { Means = "Baking soda" }, "saleratus").Should().Be("baking soda");
            WordingReaders.AgreedMeaning(a, a with { Means = "baking powder" }, "saleratus").Should().BeNull();
            WordingReaders.AgreedMeaning(a, a with { Means = null }, "saleratus").Should().BeNull();
        }

        [Fact]
        public void A_lexicon_entry_is_what_lets_a_pair_with_no_shared_word_through_the_gate()
        {
            var shortening = new GateFood(10, "Shortening", "Shortening, vegetable, household, composite", CatalogAdmissionRule.SrLegacyFood, (long)CurationStatusEnum.Curated, 4, null, 30, true, false, "173584");
            var catalog = GateCatalog.Build(new[] { shortening });
            var pair = new GatePair("crisco", 0, shortening, ReadersAgreed: true);

            LinkGate.Check(pair, catalog).Select(h => h.Code).Should().Contain(LinkGate.HeadNoun);
            LinkGate.Check(pair, WordingReaders.WithLexicon(catalog, WordingReaders.GateLexicon(new[] { ("crisco", "shortening") }))).Should().BeEmpty();
        }

        [Fact]
        public void A_retracted_pair_stays_held_until_its_lines_double()
        {
            WordingReaders.EvidenceKey(10).Should().Be("wording-readers:lines-10");
            WordingReaders.StillRetracted("wording-readers:lines-10", 19).Should().BeTrue();
            WordingReaders.StillRetracted("wording-readers:lines-10", 20).Should().BeFalse();
            WordingReaders.StillRetracted(null, 500).Should().BeTrue();
            WordingReaders.StillRetracted("wording-tiers:lines-10", 15).Should().BeTrue("a tier's retract holds by the tiers' rule");
            WordingReaders.StillRetracted("other-key", 5).Should().BeFalse();
        }

        [Theory]
        [InlineData("1 glass of wine", "wine")]
        [InlineData("1 quart cider", "cider")]
        [InlineData("1 pint alcohol", "alcohol")]
        public void A_wording_whose_lines_the_rules_leave_unclear_is_read_in_the_fixed_reading_mode(string line, string mode)
        {
            AlcoholFixedReading.ModeOf(AlcoholLineContext.Read(line, new AlcoholRecipeContext("Pudding"))).Should().Be(mode);
        }

        [Fact]
        public void A_wording_the_rules_read_is_not_put_to_the_fixed_readings()
        {
            AlcoholFixedReading.ModeOf(AlcoholLineContext.Read("1 cup sherry")).Should().BeNull();
            AlcoholFixedReading.ModeOf(AlcoholLineContext.Read("1 cup wine", new AlcoholRecipeContext("Beef Stew", HasSourceSite: true))).Should().BeNull("a modern recipe's bare wine is table wine by rule");
        }

        private static FixedPack Fixed(string mode, params FixedLine[] lines) => new("cider", mode, lines);

        private static FixedLine Line(string raw, string? steps = null, bool site = false) => new(raw, "Recipe", Array.Empty<string>(), steps, site);

        [Fact]
        public void A_fixed_reading_is_written_only_when_both_readers_give_it_on_every_line()
        {
            var pack = Fixed(AlcoholFixedReading.Cider, Line("1 quart cider"), Line("2 cups cider"));
            var (reading, _) = AlcoholFixedReading.Combine(pack, new[] { "hard", "hard" }, new[] { "hard", "hard" });
            reading!.Should().Match<AlcoholFixedReading.Reading>(r => r.Sense == "hard-cider" && r.FdcId == "174146");

            AlcoholFixedReading.Combine(pack, new[] { "hard", "sweet" }, new[] { "hard", "hard" }).Reading.Should().BeNull();
            AlcoholFixedReading.Combine(pack, new[] { "hard", "hard" }, new[] { "hard", AlcoholFixedReading.Unsure }).Reading.Should().BeNull();
            AlcoholFixedReading.Combine(pack, new[] { "hard", "hard" }, new string?[] { "hard", null }).Reading.Should().BeNull();
            AlcoholFixedReading.Combine(pack, new[] { "hard" }, new[] { "hard" }).Reading.Should().BeNull("every line must be read");
        }

        [Fact]
        public void A_data_rule_refuses_a_reading_its_recipe_contradicts()
        {
            var fermented = Fixed(AlcoholFixedReading.Cider, Line("1 gallon cider", "put it in a cask and let it ferment"));
            AlcoholFixedReading.Combine(fermented, new[] { "sweet" }, new[] { "sweet" }).Reading.Should().BeNull();
            AlcoholFixedReading.Combine(fermented, new[] { "hard" }, new[] { "hard" }).Reading.Should().NotBeNull();

            var modern = new FixedPack("wine", AlcoholFixedReading.Wine, new[] { Line("1 cup wine", site: true) });
            AlcoholFixedReading.Combine(modern, new[] { "fortified" }, new[] { "fortified" }).Reading.Should().BeNull();
        }

        [Fact]
        public void The_gate_reads_a_fixed_readings_sense_in_place_of_the_lines_own()
        {
            var tableWine = new GateFood(20, "Alcoholic beverage, wine, table, all", "Alcoholic beverage, wine, table, all", CatalogAdmissionRule.SrLegacyFood,
                (long)CurationStatusEnum.Curated, 14, null, 33, true, false, "173185");
            var catalog = GateCatalog.Build(new[] { tableWine });
            var line = new GateLine("1 glass of wine", "gram", 100m, new AlcoholRecipeContext("Stewed Carp"));

            LinkGate.Check(new GatePair("wine", 0, tableWine, ReadersAgreed: true, Lines: new[] { line }), catalog).Select(h => h.Code).Should().Contain(LinkGate.AlcoholNoFood);
            LinkGate.Check(new GatePair("wine", 0, tableWine, ReadersAgreed: true, Lines: new[] { line with { Sense = "wine-table" } }), catalog).Select(h => h.Code)
                .Should().NotContain(LinkGate.AlcoholNoFood).And.NotContain(LinkGate.AlcoholDrink);
            LinkGate.Check(new GatePair("wine", 0, tableWine, ReadersAgreed: true, Lines: new[] { line with { Sense = "fortified" } }), catalog).Select(h => h.Code)
                .Should().Contain(LinkGate.AlcoholDrink, "the fortified reading does not allow table wine");
        }

        [Fact]
        public void Every_fixed_reading_maps_to_a_sense_that_allows_its_food()
        {
            foreach (var mode in new[] { AlcoholFixedReading.Wine, AlcoholFixedReading.Cider, AlcoholFixedReading.Alcohol })
            {
                AlcoholFixedReading.Options(mode).Should().EndWith(AlcoholFixedReading.Unsure);
                foreach (var reading in AlcoholFixedReading.Of(mode).Where(r => r.FdcId != null))
                {
                    var allowed = AlcoholLineContext.Refuse(new AlcoholReading(reading.Sense!, AlcoholLineContext.KindOf(reading.Sense!), "fixed"), reading.FdcId, reading.Gloss.Contains("apple") ? "Apple juice" : "Alcoholic beverage");
                    allowed.Should().BeNull($"{mode} {reading.Code} -> {reading.FdcId}");
                }
            }
        }

        [Fact]
        public void The_signature_holds_the_gate_and_alcohol_rules_and_is_stable()
        {
            WordingReaders.SignatureSource().Should().Contain(LinkGate.Signature).And.Contain(AlcoholLineContext.Signature);
            WordingReaders.Signature.Should().HaveLength(16).And.Be(WordingReaders.Signature);
        }
    }
}
