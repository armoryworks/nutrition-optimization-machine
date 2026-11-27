using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Data.Plan;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The branded consensus (branded-consensus/2): a generic curated made product carries what the label statements of
    /// its branded products state, by rule, for the severe criteria only. Staging checkpoint 7's diet proof showed a
    /// curated "Condensed Cream Of Chicken Soup" read clear for Celiac Disease and Wheat Allergy (draw 14, recipe 124035).
    /// </summary>
    public class BrandedConsensusTests
    {
        private static readonly IReadOnlySet<string> Heads = new HashSet<string>(StringComparer.Ordinal) { "soup", "cake", "rice", "corn", "beef", "pizza", "cookie", "apricot" };

        private static ConsensusProduct Product(string id, string description, string category, string statement = "water") => new(id, description, category, statement);

        [Theory]
        [InlineData("Condensed Cream Of Chicken Soup", "Soup, cream of chicken, canned, condensed", "soup", "condensed cream chicken soup")]
        [InlineData("Soup, cream of mushroom, canned, condensed", "Soup, cream of mushroom, canned, condensed", "soup", "soup cream mushroom condensed")]
        [InlineData("Tomato juice, canned, with salt added", "Tomato juice, canned, with salt added", "juice", "tomato juice")]
        public void A_generic_food_is_keyed_by_its_head_and_the_words_that_name_its_kind(string name, string description, string head, string words)
        {
            var key = BrandedConsensus.KeyOf(name, description);
            key.Should().NotBeNull();
            key!.Head.Should().Be(head);
            string.Join(' ', key.Words).Should().Be(words);
        }

        [Theory]
        [InlineData("Cereals ready-to-eat, MALT-O-MEAL, BERRY COLOSSAL CRUNCH", "Cereals ready-to-eat, MALT-O-MEAL, BERRY COLOSSAL CRUNCH")]
        [InlineData("Peanut Butter Cups", "Candies, REESE'S Peanut Butter Cups")]
        [InlineData("Candies, milk chocolate, with almonds", "Candies, milk chocolate, with almonds")]
        [InlineData("Beans, baked, canned, with franks", "Beans, baked, canned, with franks")]
        public void A_makers_own_product_or_a_food_with_another_food_in_it_has_no_kind_to_match(string name, string description) =>
            BrandedConsensus.KeyOf(name, description).Should().BeNull();

        [Theory]
        [InlineData("SUGAR COOKIES, SUGAR", "sugar cookie")]
        [InlineData("SOUP, CREAM OF CHICKEN", "cream of chicken soup")]
        [InlineData("KROGER, BAKED WHEAT CRISPS CRACKERS, ORIGINAL", "baked wheat crisp cracker")]
        [InlineData("BAKED BEANS WITH MOLASSES", "")]
        [InlineData("CHUNK LIGHT TUNA IN WATER", "chunk light tuna")]
        public void The_product_a_branded_description_names_is_its_first_part_and_not_its_flavour_or_an_addition(string description, string words) =>
            string.Join(' ', BrandedConsensus.ProductWords(description, Heads)).Should().Be(words);

        [Fact]
        public void A_product_matches_when_it_is_the_same_kind_and_not_another_form_dish_or_food()
        {
            var foods = new[]
            {
                new ConsensusFood(1, "171146", "Condensed Cream Of Chicken Soup", 6, "Soup, cream of chicken, canned, condensed"),
                new ConsensusFood(2, "171111", "Corn Cake", 23, "Snacks, corn cakes"),
            };
            var products = new List<ConsensusProduct>
            {
                Product("10", "CONDENSED CREAM OF CHICKEN SOUP", "Canned Condensed Soup"),
                Product("11", "CREAM OF CHICKEN CONDENSED SOUP, CREAM OF CHICKEN", "Canned Condensed Soup"),
                Product("12", "CONDENSED CREAM OF CHICKEN SOUP", "Frozen Dinners & Entrees"),
                Product("13", "CONDENSED CREAM OF CHICKEN SOUP, SPINACH, ON A THIN CRUST PIZZA", "Pizza"),
                Product("20", "BUTTER POPPED CORN FLAVOR RICE CAKES", "Other Snacks"),
                Product("21", "LIGHTLY SALTED CORN CAKES", "Other Snacks"),
            };
            var matches = BrandedConsensus.Match(foods, products, Heads);
            matches[1].Select(p => p.FdcId).Should().BeEquivalentTo(new[] { "10", "11" }, "a whole meal and a pizza that names the soup are not the soup");
            matches[2].Select(p => p.FdcId).Should().BeEquivalentTo(new[] { "21" }, "rice cakes are no corn cakes");
        }

        [Fact]
        public void A_canned_fruit_is_not_matched_to_its_dried_or_frozen_form()
        {
            var foods = new[] { new ConsensusFood(3, "171112", "Canned Apricots", 9, "Apricots, canned, water pack, solids and liquids") };
            var products = new List<ConsensusProduct>
            {
                Product("30", "DRIED APRICOTS", "Wholesome Snacks"),
                Product("31", "APRICOTS IN WATER", "Canned Fruit"),
                Product("32", "WHOLE APRICOTS", "Frozen Fruit & Fruit Juice Concentrates"),
            };
            BrandedConsensus.Match(foods, products, Heads)[3].Select(p => p.FdcId).Should().BeEquivalentTo(new[] { "31" });
        }

        [Fact]
        public void A_product_named_for_a_restricted_food_before_its_head_is_another_product_although_no_curated_description_is_headed_by_it()
        {
            var foods = new[] { new ConsensusFood(4, "174525", "White Sauce", 6, "Sauce, white, medium") };
            var products = new List<ConsensusProduct>
            {
                Product("40", "WHITE SAUCE", "Other Cooking Sauces"),
                Product("41", "WHITE CLAM SAUCE", "Other Cooking Sauces"),
                Product("42", "NEW ENGLAND STYLE WHITE CLAM SAUCE, NEW ENGLAND STYLE WHITE CLAM", "Other Cooking Sauces"),
            };
            BrandedConsensus.Match(foods, products, Heads)[4].Select(p => p.FdcId).Should().BeEquivalentTo(new[] { "40" }, "review ck18-homemade-matched-to-commercial: a white clam sauce is no white sauce");
        }

        [Theory]
        [InlineData("Potatoes, mashed, home-prepared, whole milk added", true)]
        [InlineData("Sauce, homemade, white, medium", true)]
        [InlineData("Salad dressing, french, home recipe", true)]
        [InlineData("Jellies, reduced sugar, home preserved", true)]
        [InlineData("Cake, sponge, prepared from recipe", true)]
        [InlineData("Candies, fudge, chocolate, prepared-from-recipe", true)]
        [InlineData("Corn pudding, home prepared", true)]
        [InlineData("Gravy, HEINZ Home Style, savory beef", false)]
        [InlineData("Archway Home Style Cookies, Molasses", false)]
        [InlineData("Mustard, prepared, yellow", false)]
        public void A_food_usda_describes_as_made_at_home_is_known_by_its_description(string description, bool home) =>
            BrandedConsensus.HomeMade(description).Should().Be(home);

        [Fact]
        public void A_food_made_at_home_takes_from_its_brands_only_what_they_usually_state_and_never_a_preservative()
        {
            var tally = new ConsensusTally { Matched = 88, Read = 88 };
            tally.Stating["milk"] = Enumerable.Range(1, 69).Select(i => i.ToString()).ToList();
            tally.Stating["soy"] = Enumerable.Range(1, 34).Select(i => i.ToString()).ToList();
            tally.Stating["sulfite"] = Enumerable.Range(1, 79).Select(i => i.ToString()).ToList();
            var home = new ConsensusFood(5, "170493", "Mashed Potatoes", 11, "Potatoes, mashed, home-prepared, whole milk added");
            var store = new ConsensusFood(6, "170000", "Refrigerated Mashed Potatoes", 11, "Potatoes, mashed, refrigerated");
            var tallies = new Dictionary<long, ConsensusTally> { [5] = tally, [6] = tally };

            var rows = BrandedConsensus.Rows(new[] { home, store }, tallies);

            rows.Where(r => r.Name == home.Name && r.Class != BrandedConsensus.AnyClass).Select(r => (r.Class, r.Verdict)).Should().BeEquivalentTo(new[] { ("milk", BrandedConsensus.Usually) },
                "review ck18-homemade-matched-to-commercial: a home kitchen adds no bisulfite, and the makers' split is their own additions");
            rows.Where(r => r.Name == store.Name && r.Class != BrandedConsensus.AnyClass).Select(r => (r.Class, r.Verdict)).Should().BeEquivalentTo(new[]
            {
                ("milk", BrandedConsensus.Usually), ("soy", BrandedConsensus.Split), ("sulfite", BrandedConsensus.Usually),
            });
        }

        [Theory]
        [InlineData("Water, chicken stock, wheat flour, cream (milk), modified food starch, soybean oil, salt.", "wheat,gluten,milk,soy")]
        [InlineData("Ingredients: water, mushrooms, vegetable oil, rice flour, salt. May contain wheat and milk.", "soy")]
        [InlineData("Coconut, sugar, sodium metabisulfite (to preserve freshness). Contains: coconut.", "sulfite")]
        [InlineData("Distilled vinegar, mustard seed, water, salt, turmeric.", "")]
        [InlineData("Barley malt, water, salt.", "gluten")]
        [InlineData("Almond oil. Contains: tree nuts (almonds).", "treenut")]
        public void A_statement_states_what_its_ingredients_or_its_contains_line_name_and_never_a_warning(string statement, string classes)
        {
            IReadOnlySet<string> stated = BrandedConsensus.StatedClasses(statement) ?? new HashSet<string>();
            var ordered = DietVocabulary.Classes.Select(c => c.Name).Where(stated.Contains).ToList();
            string.Join(",", ordered).Should().Be(classes);
        }

        [Fact]
        public void A_statement_the_reader_cannot_read_is_not_counted() =>
            BrandedConsensus.StatedClasses("water, (wheat flour, salt").Should().BeNull();

        [Theory]
        [InlineData(9, 9, null)]
        [InlineData(10, 8, "usually")]
        [InlineData(20, 15, "usually")]
        [InlineData(20, 14, "split")]
        [InlineData(20, 3, "split")]
        [InlineData(20, 2, null)]
        [InlineData(100, 0, null)]
        public void The_verdict_is_usually_from_three_in_four_of_ten_or_more_split_above_one_in_ten_and_clear_below(int read, int stating, string? verdict) =>
            BrandedConsensus.VerdictOf(read, stating).Should().Be(verdict);

        [Fact]
        public void The_committed_file_parses_and_holds_the_condensed_cream_soups_as_usually_wheat()
        {
            var rows = BrandedConsensus.All;
            rows.Should().NotBeEmpty();
            rows.Should().OnlyContain(r => (BrandedConsensus.Probes.ContainsKey(r.Class) && (r.Verdict == BrandedConsensus.Usually || r.Verdict == BrandedConsensus.Split)) || (r.Class == BrandedConsensus.AnyClass && r.Verdict == BrandedConsensus.Read));
            rows.Where(r => r.Class != BrandedConsensus.AnyClass).Should().OnlyContain(r => rows.Any(x => x.Class == BrandedConsensus.AnyClass && x.Fdc == r.Fdc && x.Name == r.Name), "every food with a verdict was read");
            rows.Should().OnlyContain(r => (r.Products >= BrandedConsensus.MinProducts || (r.Products >= BrandedConsensus.MinSingle && (r.Class == BrandedConsensus.AnyClass || r.Verdict == BrandedConsensus.Split))) && r.Examples.Count <= 3 && r.Examples.Count == Math.Min(3, r.Stating));
            foreach (var soup in new[] { "Condensed Cream Of Chicken Soup", "Condensed Mushroom Soup" })
                foreach (var cls in new[] { "wheat", "gluten" })
                    rows.Should().Contain(r => r.Name == soup && r.Class == cls && r.Verdict == BrandedConsensus.Usually, $"{soup} is thickened with wheat flour in nearly every brand");
        }

        [Fact]
        public void The_file_round_trips_and_refuses_a_class_or_verdict_it_does_not_know()
        {
            var rows = new[] { new ConsensusRow("171146", "Condensed Cream Of Chicken Soup", "wheat", "usually", 113, 109, new[] { "1", "2", "3" }) };
            BrandedConsensus.Parse(BrandedConsensus.Write(rows, "test")).Should().BeEquivalentTo(rows);
            var bad = BrandedConsensus.Write(rows, "test").Replace("\twheat\t", "\tgrain\t");
            ((Action)(() => BrandedConsensus.Parse(bad))).Should().Throw<System.IO.InvalidDataException>();
        }

        private static DietDataCriterion Criterion(long id, string pattern, int severity, string type) => new(id, 1, null, null, null, null, severity, null, pattern, type);

        private static readonly ConsensusRow[] Rows =
        {
            new("171146", "Condensed Cream Of Chicken Soup", "wheat", BrandedConsensus.Usually, 113, 109, new[] { "1", "2", "3" }),
            new("171146", "Condensed Cream Of Chicken Soup", "gluten", BrandedConsensus.Usually, 113, 109, new[] { "1", "2", "3" }),
            new("171563", "Chicken Bouillon Cubes", "wheat", BrandedConsensus.Split, 30, 6, new[] { "4", "5", "6" }),
            new("171563", "Chicken Bouillon Cubes", "soy", BrandedConsensus.Split, 30, 10, new[] { "4", "5", "6" }),
        };

        private static DietFoodCatalog Catalog(bool curated = true, string soupName = "Condensed Cream Of Chicken Soup", IReadOnlyList<ConsensusRow>? rows = null) =>
            DietFoodCatalog.Of(new[]
            {
                new DietFoodCatalog.Food(1, soupName, true, 6, "sr_legacy_food", null, null, null, null, curated, "171146"),
                new DietFoodCatalog.Food(2, "Chicken Bouillon Cubes", true, 6, "sr_legacy_food", null, null, null, null, curated, "171563"),
                new DietFoodCatalog.Food(3, "Wheat Flour", true, 20, "sr_legacy_food", null, null, null, null, true, "168944"),
            }, Array.Empty<DietFoodCatalog.Alias>(), consensus: rows ?? Rows);

        private static readonly DietDataCriterion[] WheatAllergy = { Criterion(1, "%wheat%", 5, "Wheat Allergy"), Criterion(2, "%flour%", 5, "Wheat Allergy") };

        [Fact]
        public void A_severe_set_flags_a_food_whose_brands_usually_state_what_it_restricts_and_holds_one_whose_brands_are_split()
        {
            var catalog = Catalog();
            var decision = DietFoodReader.Decide(DietFoodReader.Read(catalog, catalog.Foods.Keys.ToList()), WheatAllergy);
            decision.Foods[1].Should().Contain(new long[] { 1, 3 }, "the soup usually contains wheat; the flour names it");
            decision.Sources[(1, 1)].Should().Be(BrandedConsensus.FactSource);
            decision.Split.Should().BeEquivalentTo(new long[] { 2 }, "the bouillon cubes' brands are split on wheat");
        }

        [Fact]
        public void Soy_reads_only_the_soy_row_and_a_set_that_reads_neither_class_is_not_moved()
        {
            var catalog = Catalog();
            var reading = DietFoodReader.Read(catalog, catalog.Foods.Keys.ToList());
            var soy = DietFoodReader.Decide(reading, new[] { Criterion(1, "%soy%", 5, "Soy Allergy") });
            soy.Foods[1].Should().BeEmpty();
            soy.Split.Should().BeEquivalentTo(new long[] { 2 });
            var sesame = DietFoodReader.Decide(reading, new[] { Criterion(1, "%sesame%", 5, "Sesame Allergy") });
            sesame.Foods[1].Should().BeEmpty();
            sesame.Split.Should().BeEmpty();
        }

        [Fact]
        public void A_diet_of_choice_reads_only_what_the_brands_usually_state_and_an_uncurated_food_or_a_renamed_food_reads_no_consensus()
        {
            var diet = Catalog();
            var choice = DietFoodReader.Decide(DietFoodReader.Read(diet, diet.Foods.Keys.ToList()), new[] { Criterion(1, "%wheat%", 3, "Gluten-Free") });
            choice.Foods[1].Should().Contain(1, "staging checkpoint 8, T10: what nearly every maker puts in a made product (the milk of caramel for a Vegan household) holds for a diet of choice too");
            choice.Split.Should().BeEmpty("a split or an unread food is unknown only for a severe set");

            var uncurated = Catalog(curated: false);
            var u = DietFoodReader.Decide(DietFoodReader.Read(uncurated, uncurated.Foods.Keys.ToList()), WheatAllergy);
            u.Foods[1].Should().NotContain(1, "a line on an uncurated food is read by its words, not by a food fact");
            u.Split.Should().BeEmpty();

            var renamed = Catalog(soupName: "Cream Of Chicken Soup, Homemade");
            DietFoodReader.Decide(DietFoodReader.Read(renamed, renamed.Foods.Keys.ToList()), WheatAllergy).Foods[1].Should().NotContain(1);
        }

        [Fact]
        public void A_consensus_row_moves_the_food_fingerprint_so_the_store_decides_it_again()
        {
            Catalog().Fingerprint(1).Should().NotBe(Catalog(rows: Array.Empty<ConsensusRow>()).Fingerprint(1));
            Catalog().Fingerprint(3).Should().Be(Catalog(rows: Array.Empty<ConsensusRow>()).Fingerprint(3));
        }

        [Fact]
        public void Each_probe_is_read_by_its_class_type_and_barley_not_by_wheat_allergy()
        {
            foreach (var c in DietVocabulary.Classes)
            {
                var (severity, patterns) = DefaultRestrictionCriteria.ByName[c.ReferenceType];
                var criteria = patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Select((p, i) => Criterion(i + 1, p, severity, c.ReferenceType)).ToList();
                criteria.Should().Contain(x => BrandedConsensus.Reads(x, c.Name), $"{c.ReferenceType} reads the {c.Name} probe");
            }
            var (ws, wp) = DefaultRestrictionCriteria.ByName["Wheat Allergy"];
            wp.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Select((p, i) => Criterion(i + 1, p, ws, "Wheat Allergy")).Should().NotContain(x => BrandedConsensus.Reads(x, "gluten"));
            var (ps, pp) = DefaultRestrictionCriteria.ByName["Peanut Allergy"];
            pp.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Select((p, i) => Criterion(i + 1, p, ps, "Peanut Allergy")).Should().NotContain(x => BrandedConsensus.Reads(x, "treenut"));
        }
    }
}
