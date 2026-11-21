using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nom.Import.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The rules of the container size lane, one by one: the branded mode test and its refusals,
    /// the cited can table, the class map, the USDA container portions, the branded scan, the
    /// resolver and the key qualifier.
    /// </summary>
    public class ContainerSizeRuleTests
    {
        private static Dictionary<int, int> Sizes(params (int Grams, int Products)[] sizes) => sizes.ToDictionary(s => s.Grams, s => s.Products);

        [Fact]
        public void A_mode_counts_with_twenty_products_half_of_them_in_its_band_and_twice_the_runner_up()
        {
            var mode = ContainerModeRule.Decide(Sizes((425, 12), (432, 3), (850, 5)));
            mode!.Passes.Should().BeTrue();
            mode.Grams.Should().Be(425);
            mode.Band.Should().Be(15);
            mode.RunnerUp.Should().Be(5);
        }

        [Theory]
        [InlineData(ContainerModeRule.FewProducts, 425, 15, 850, 4)]
        [InlineData(ContainerModeRule.NoClearMode, 425, 10, 850, 9, 113, 5)]
        [InlineData(ContainerModeRule.RunnerUpClose, 425, 11, 850, 9)]
        public void A_mode_is_refused_with_few_products_no_clear_mode_or_a_close_runner_up(string refusal, params int[] pairs)
        {
            var sizes = new Dictionary<int, int>();
            for (var i = 0; i < pairs.Length; i += 2) sizes[pairs[i]] = pairs[i + 1];
            ContainerModeRule.Decide(sizes)!.Refusal.Should().Be(refusal);
        }

        [Fact]
        public void A_size_word_reads_only_the_sizes_below_or_above_the_overall_mode_band()
        {
            var sizes = Sizes((411, 40), (794, 33), (283, 22), (800, 2));
            ContainerModeRule.Decide(sizes)!.Passes.Should().BeFalse("14.5 oz holds 40 of 97 and 28 oz is close behind");
            ContainerModeRule.Decide(sizes, ContainerKinds.Large)!.Grams.Should().Be(794);
            ContainerModeRule.Decide(sizes, ContainerKinds.Large)!.Passes.Should().BeTrue();
            ContainerModeRule.Decide(sizes, ContainerKinds.Small)!.Grams.Should().Be(283);
            ContainerModeRule.Decide(sizes, ContainerKinds.Small)!.Passes.Should().BeTrue();
        }

        [Fact]
        public void A_can_table_row_without_its_citation_is_refused()
        {
            const string header = "id\tname\tcanNumber\tkind\tera\tnetOz\tnetOzMax\tfluidOz\tcups\ttitle\tpublisher\tyear\tpage\tlocator\tnote\n";
            var rows = CanSizeTable.Parse(header
                + "a\tNo. 2\t2\tcan\thistoric\t20\t-\t-\t-\t-\tUSDA\t1932\t3\tarchive.org/x\t\"20 oz\"\n"
                + "b\tNo. 2\t2\tcan\thistoric\t20\t-\t-\t-\tA title\tUSDA\t-\t3\tarchive.org/x\t\"20 oz\"\n"
                + "c\tNo. 2\t2\tcan\thistoric\t20\t-\t-\t-\tA title\tUSDA\t1932\t-\t-\t-\n"
                + "d\tNo. 2\t2\tcan\tsome day\t-\t-\t-\t-\tA title\tUSDA\t1932\t3\tarchive.org/x\t\"20 oz\"\n");
            var problems = CanSizeTable.Problems(rows);
            problems.Should().Contain("a: no title").And.Contain("b: no year").And.Contain("c: no page").And.Contain("c: no locator")
                .And.Contain("c: no quoted text").And.Contain("d: era some day").And.Contain("d: no size");
        }

        [Fact]
        public void A_numbered_can_is_sized_by_its_era_and_an_unknown_era_needs_both_to_agree()
        {
            CanSizeTable.Numbered("2", ContainerEras.Historic)!.Value.Grams.Should().Be(567.0m);
            CanSizeTable.Numbered("2 1/2", ContainerEras.Modern)!.Value.Grams.Should().Be(793.8m);
            CanSizeTable.Numbered("2-1/2", ContainerEras.Unknown)!.Value.Grams.Should().Be(793.8m);
            CanSizeTable.Numbered("303", ContainerEras.Historic)!.Value.Grams.Should().Be(453.6m);
            CanSizeTable.Numbered("303", ContainerEras.Modern).Should().BeNull();
            CanSizeTable.Numbered("303", ContainerEras.Unknown).Should().BeNull();
            CanSizeTable.Numbered("10", ContainerEras.Historic).Should().BeNull("the 1932 broadcast gives cups and no weight");
            CanSizeTable.Rows.Single(r => r.Id == "h32-1-short").NetGrams.Should().BeNull("10.5 to 13 oz is wider than 20 %");
        }

        [Fact]
        public void Two_rows_of_one_era_that_disagree_give_no_size()
        {
            const string header = "id\tname\tcanNumber\tkind\tera\tnetOz\tnetOzMax\tfluidOz\tcups\ttitle\tpublisher\tyear\tpage\tlocator\tnote\n";
            var rows = CanSizeTable.Parse(header
                + "a\tNo. 5\t5\tcan\thistoric\t20\t-\t-\t-\tT\tP\t1932\t3\tL\tq\n"
                + "b\tNo. 5\t5\tcan\thistoric\t30\t-\t-\t-\tT\tP\t1933\t1\tL\tq\n");
            CanSizeTable.Numbered("5", ContainerEras.Historic, rows).Should().BeNull();
        }

        [Fact]
        public void A_class_map_row_without_categories_or_words_is_refused()
        {
            var classes = ContainerClasses.Parse("id\tkind\tbasis\tusdaCategories\tdescriptionAll\tdescriptionNone\tbrandedCategories\tproductAll\tproductNone\tnote\n"
                + "x\tcrate\tkg\t16\t-\t-\tCanned & Bottled Beans\t-\t-\tn\n");
            ContainerClasses.Problems(classes).Should().Contain("x: kind crate").And.Contain("x: basis kg").And.Contain("x: no description words").And.Contain("x: no product words");
        }

        [Fact]
        public void A_food_belongs_to_a_class_by_its_usda_category_and_description_words_never_by_its_name()
        {
            var beans = ContainerClasses.All.Single(c => c.Id == "canned-black-beans");
            beans.HoldsFood(16, "Beans, black, mature seeds, canned, low sodium").Should().BeTrue();
            beans.HoldsFood(16, "Beans, black, mature seeds, raw").Should().BeFalse();
            beans.HoldsFood(11, "Beans, black, canned").Should().BeFalse("the category decides first");
            beans.HoldsProduct("Canned & Bottled Beans", ContainerWords.Of("ORGANIC BLACK BEANS")).Should().BeTrue();
            beans.HoldsProduct("Canned & Bottled Beans", ContainerWords.Of("REFRIED BLACK BEANS")).Should().BeFalse();
            beans.HoldsProduct("Tomatoes", ContainerWords.Of("BLACK BEANS")).Should().BeFalse();
        }

        [Fact]
        public void A_usda_container_portion_counts_only_when_its_unit_is_the_container_itself()
        {
            var portions = new[]
            {
                new UsdaPortion(1, "9999", "can (303 x 406)", "", 298, null, "3"),
                new UsdaPortion(1, "9999", "can (12 oz) yields", "", 211, null, "2"),
                new UsdaPortion(0.33m, "9999", "package (9 oz)", "", 80, null, "4"),
                new UsdaPortion(1, "9999", "cup", "", 164, null, "1"),
            };
            var sizes = UsdaContainerPortion.Sizes(portions, "Corn, sweet, yellow, canned, whole kernel, drained solids");
            sizes.Should().ContainSingle();
            sizes[0].Should().BeEquivalentTo(new { Kind = ContainerKinds.Can, NetGrams = (decimal?)null, DrainedGrams = (decimal?)298m, Seq = "3" });

            var gelatin = UsdaContainerPortion.Sizes(new[] { new UsdaPortion(1, "9999", "envelope (1 tbsp)", "", 7, null, "1"), new UsdaPortion(1, "9999", "package (1 oz)", "", 28, null, "2") }, "Gelatins, dry powder, unsweetened");
            gelatin.Select(g => (g.Kind, g.NetGrams)).Should().BeEquivalentTo(new[] { (ContainerKinds.Envelope, (decimal?)7m), (ContainerKinds.Package, (decimal?)28m) });

            var drainedNet = UsdaContainerPortion.Sizes(new[] { new UsdaPortion(1, "9999", "can (6.5 oz), drained", "", 125, null, "4") }, "Fish, clam, canned");
            drainedNet[0].NetGrams.Should().Be(184.3m);
            drainedNet[0].DrainedGrams.Should().Be(125m);

            UsdaContainerPortion.Sizes(new[] { new UsdaPortion(1, "9999", "can", "", 165, null, "3"), new UsdaPortion(1, "9999", "can (12.5 oz), drained", "", 315, null, "5") },
                "Fish, tuna, light, canned in water, drained solids").Should().BeEmpty("two drained cans that disagree give nothing");
            UsdaContainerPortion.Sizes(new[] { new UsdaPortion(1, "9999", "container", "", 170, null, "1") }, "Yogurt, Greek, plain, nonfat").Should().BeEmpty("a container with no size is not a kind");
        }

        [Fact]
        public void The_branded_scan_keeps_one_current_us_product_per_gtin_with_a_single_package_weight()
        {
            var dir = Directory.CreateTempSubdirectory("branded-").FullName;
            try
            {
                const string head = "\"fdc_id\",\"brand_owner\",\"brand_name\",\"subbrand_name\",\"gtin_upc\",\"ingredients\",\"not_a_significant_source_of\",\"serving_size\",\"serving_size_unit\",\"household_serving_fulltext\",\"branded_food_category\",\"data_source\",\"package_weight\",\"modified_date\",\"available_date\",\"market_country\",\"discontinued_date\",\"preparation_state_code\",\"trade_channel\",\"short_description\",\"material_code\"";
                string Row(int id, string gtin, string weight, string available, string country = "United States", string discontinued = "", string category = "Canned & Bottled Beans") =>
                    $"\"{id}\",\"\",\"\",\"\",\"{gtin}\",\"\",\"\",\"\",\"\",\"\",\"{category}\",\"\",\"{weight}\",\"\",\"{available}\",\"{country}\",\"{discontinued}\",\"\",\"\",\"\",\"\"";
                File.WriteAllLines(Path.Combine(dir, "branded_food.csv"), new[]
                {
                    head,
                    Row(1, "0001", "15 oz/425 g", "2020-01-01"),
                    Row(2, "0001", "15.5 oz/439 g", "2024-01-01"),
                    Row(3, "0002", "4 x 15 oz", "2024-01-01"),
                    Row(4, "0003", "15 oz/425 g", "2024-01-01", country: "Canada"),
                    Row(5, "0004", "15 oz/425 g", "2024-01-01", discontinued: "2023-01-01"),
                    Row(6, "0005", "16 oz/454 g", "2024-01-01"),
                    Row(7, "0006", "15 oz/425 g", "2024-01-01"),
                    Row(8, "0007", "15 oz/425 g", "2024-01-01", category: "Candy"),
                });
                File.WriteAllLines(Path.Combine(dir, "food.csv"), new[]
                {
                    "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\",\"publication_date\"",
                    "\"1\",\"branded_food\",\"BLACK BEANS\",\"\",\"\"",
                    "\"2\",\"branded_food\",\"BLACK BEANS\",\"\",\"\"",
                    "\"3\",\"branded_food\",\"BLACK BEANS\",\"\",\"\"",
                    "\"4\",\"branded_food\",\"BLACK BEANS\",\"\",\"\"",
                    "\"5\",\"branded_food\",\"BLACK BEANS\",\"\",\"\"",
                    "\"6\",\"branded_food\",\"REFRIED BLACK BEANS\",\"\",\"\"",
                    "\"7\",\"branded_food\",\"SEASONED BLACK BEANS\",\"\",\"\"",
                    "\"8\",\"branded_food\",\"BLACK BEANS\",\"\",\"\"",
                });
                var scan = BrandedContainerScan.Scan(dir, ContainerClasses.All);
                scan.SizesByClass["canned-black-beans"].Should().BeEquivalentTo(new Dictionary<int, int> { [439] = 1 },
                    "the 2024 record of GTIN 1 replaces the 2020 one; the multi-pack, Canada, discontinued, refried, seasoned and candy products do not count");
                scan.SkippedByReason.Should().ContainKey("not US").And.ContainKey("discontinued").And.ContainKey("package weight unread or a multi-pack");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static ContainerSizeCandidate Can(decimal net, decimal? drained = null, string era = ContainerEras.Modern, string word = "", string status = ContainerSizeStatus.Active) =>
            new(ContainerKinds.Can, era, word, net, drained, "test", status);

        [Fact]
        public void A_line_is_weighed_by_its_stated_size_then_its_numbered_can_then_its_food_s_size()
        {
            var sizes = new[] { Can(425m, 254m) };
            ContainerSizeResolver.Resolve("1 (15.5 oz) can beans", ContainerEras.Modern, sizes).Grams.Should().Be(439.4m);
            ContainerSizeResolver.Resolve("1 No. 2 can beans", ContainerEras.Modern, sizes).Grams.Should().Be(567.0m);
            ContainerSizeResolver.Resolve("2 cans beans", ContainerEras.Modern, sizes).Grams.Should().Be(850m);
            ContainerSizeResolver.Resolve("1 can beans, drained", ContainerEras.Modern, sizes, "Beans, canned, drained solids").Grams.Should().Be(254m);
            ContainerSizeResolver.Resolve("1 can beans, do not drain", ContainerEras.Modern, sizes).Grams.Should().Be(425m);
            ContainerSizeResolver.Resolve("1 can beans, drained", ContainerEras.Modern, sizes, "Beans, canned, solids and liquids").Reason.Should().Be(ContainerSizeResolver.AsPacked);
            ContainerSizeResolver.Resolve("1 jar beans", ContainerEras.Modern, sizes).Reason.Should().Be(ContainerSizeResolver.NoSize);
            ContainerSizeResolver.Resolve("1 cup beans", ContainerEras.Modern, sizes).Reason.Should().Be(ContainerSizeResolver.NoContainer);
        }

        [Fact]
        public void A_size_counts_only_when_active_and_an_old_or_undated_book_takes_the_period_size_else_the_modern_one()
        {
            ContainerSizeResolver.Resolve("1 can beans", ContainerEras.Modern, new[] { Can(425m, status: ContainerSizeStatus.Shadow) }).Counted.Should().BeFalse();
            var undated = ContainerSizeResolver.Resolve("1 can beans", ContainerEras.Unknown, new[] { Can(425m) });
            (undated.Grams, undated.Basis).Should().Be((425m, ContainerBasis.ModernFallback));
            var old = ContainerSizeResolver.Resolve("1 can milk", ContainerEras.Unknown, new[] { Can(340m), Can(411m, era: ContainerEras.Historic) });
            (old.Grams, old.Basis).Should().Be((411m, ContainerBasis.PeriodSize));
            ContainerSizeResolver.Resolve("1 can soup", ContainerEras.Historic, new[] { Can(298m) }).Grams.Should().Be(298m);
            ContainerSizeResolver.Resolve("1 can soup", ContainerEras.Modern, new[] { Can(298m, era: ContainerEras.Historic) }).Counted.Should().BeFalse("a modern recipe never takes an old size");
            ContainerSizeResolver.Resolve("1 large can tomatoes", ContainerEras.Modern, new[] { Can(190m), Can(794m, word: ContainerKinds.Large) }).Grams.Should().Be(794m);
        }

        [Fact]
        public void An_unsized_vegetable_can_in_an_old_book_takes_the_period_can_nearer_the_modern_size()
        {
            const int Vegetables = 11;
            var corn = ContainerSizeResolver.Resolve("1 can corn", ContainerEras.Historic, new[] { Can(432m) }, usdaCategoryId: Vegetables);
            (corn.Grams, corn.Basis).Should().Be((453.6m, ContainerBasis.PeriodNearer), "No. 303 (16 oz) is nearer 432 g than No. 2 (20 oz)");
            corn.SourceRef.Should().StartWith("deterministic:can-table:h33-veg-medium+deterministic:can-table:h33-303");
            ContainerSizeResolver.Resolve("1 can tomatoes", ContainerEras.Unknown, new[] { Can(560m) }, usdaCategoryId: Vegetables).Grams.Should().Be(567m, "No. 2 (20 oz) is nearer 560 g");
            ContainerSizeResolver.Resolve("1 can tomatoes", ContainerEras.Unknown, new[] { Can(794m) }, usdaCategoryId: Vegetables).Basis.Should().Be(ContainerBasis.ModernFallback, "a 28 oz can is not a medium can");
            ContainerSizeResolver.Resolve("1 can tomato paste", ContainerEras.Historic, new[] { Can(170m) }, usdaCategoryId: Vegetables).Grams.Should().Be(170m, "a 6 oz can is not a medium can");
            ContainerSizeResolver.Resolve("1 can corn, drained", ContainerEras.Historic, new[] { Can(432m, 298m) }, "Corn, sweet, yellow, canned, drained solids", usdaCategoryId: Vegetables)
                .Grams.Should().Be(312.9m, "the period net scaled by the modern can's drained share");
            ContainerSizeResolver.Resolve("1 can mushrooms", ContainerEras.Historic, Array.Empty<ContainerSizeCandidate>(), usdaCategoryId: Vegetables)
                .Reason.Should().Be(ContainerSizeResolver.PeriodWithoutModern);
            ContainerSizeResolver.Resolve("1 can corn", ContainerEras.Modern, new[] { Can(432m) }, usdaCategoryId: Vegetables).Grams.Should().Be(432m, "a modern recipe takes the modern size");
            ContainerSizeResolver.Resolve("1 large can corn", ContainerEras.Historic, new[] { Can(432m), Can(822m, word: ContainerKinds.Large) }, usdaCategoryId: Vegetables)
                .Basis.Should().Be(ContainerBasis.ModernFallback, "the period source names medium cans only");
            ContainerSizeResolver.Resolve("1 can beans", ContainerEras.Historic, new[] { Can(425m) }, usdaCategoryId: 16).Basis.Should().Be(ContainerBasis.ModernFallback, "no period row names legumes");
        }

        [Fact]
        public void Every_kind_in_an_old_or_undated_book_falls_back_to_the_modern_size_as_its_last_tier()
        {
            ContainerSizeCandidate Of(string kind, decimal net, string era = ContainerEras.Modern) => new(kind, era, string.Empty, net, null, "test");

            ContainerSizeResolver.Resolve("1 jar pasta sauce", ContainerEras.Historic, new[] { Of(ContainerKinds.Jar, 680m) }).Basis.Should().Be(ContainerBasis.ModernFallback);
            var cheese = ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Historic, new[] { Of(ContainerKinds.Package, 227m) });
            (cheese.Grams, cheese.Basis).Should().Be((227m, ContainerBasis.ModernFallback), "owner's third-round answer: there has to be a way to convert these");
            ContainerSizeResolver.Resolve("1 package gelatin", ContainerEras.Unknown, new[] { Of(ContainerKinds.Package, 85m) }).Grams.Should().Be(85m);
            ContainerSizeResolver.Resolve("1 box crackers", ContainerEras.Unknown, new[] { Of(ContainerKinds.Box, 283m) }).Grams.Should().Be(283m);
            ContainerKinds.All.Should().OnlyContain(k => ContainerSizeResolver.FallsBackToModern(k));
            ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Modern, new[] { Of(ContainerKinds.Package, 227m) }).Grams.Should().Be(227m);
            ContainerSizeResolver.Resolve("1 package gelatin", ContainerEras.Historic, new[] { Of(ContainerKinds.Package, 85m, ContainerEras.Historic) }).Grams.Should().Be(85m, "a size of the book's time still counts");
            ContainerSizeResolver.Resolve("1 bottle wine", ContainerEras.Historic, new[] { Of(ContainerKinds.Bottle, 746m, ContainerEras.Any) }).Grams.Should().Be(746m, "a size that holds in any era still counts");
        }

        [Fact]
        public void A_historic_size_is_a_key_s_own_answer_only_for_a_dated_old_book()
        {
            ContainerBasis.OwnEra(ContainerBasis.PeriodSize, ContainerEras.Historic).Should().BeTrue();
            ContainerBasis.OwnEra(ContainerBasis.PeriodSize, ContainerEras.Unknown).Should().BeFalse("an undated book takes the historic size by fallback");
            ContainerBasis.OtherEra(ContainerBasis.PeriodSize, ContainerEras.Unknown).Should().BeTrue();
            ContainerBasis.OwnEra(ContainerBasis.AnyEra, ContainerEras.Unknown).Should().BeTrue();
            ContainerBasis.OwnEra(ContainerBasis.FoodSize, ContainerEras.Modern).Should().BeTrue();
            ContainerBasis.OtherEra(ContainerBasis.ModernFallback, ContainerEras.Historic).Should().BeTrue();
            ContainerBasis.OtherEra(ContainerBasis.PeriodNearer, ContainerEras.Unknown).Should().BeTrue();
            ContainerBasis.OtherEra(ContainerBasis.Stated, ContainerEras.Unknown).Should().BeFalse();
            ContainerBasis.OtherEra(ContainerBasis.Numbered, ContainerEras.Historic).Should().BeFalse();
        }

        [Fact]
        public void A_period_row_must_name_its_products_and_candidate_cans_the_table_sizes()
        {
            CanSizeTable.Period(ContainerKinds.Can, string.Empty, 11).Select(c => (c.CanNumber, c.Grams)).Should().Equal(("303", 453.6m), ("2", 567.0m));
            CanSizeTable.Period(ContainerKinds.Jar, string.Empty, 11).Should().BeEmpty();
            CanSizeTable.Period(ContainerKinds.Can, string.Empty, 16).Should().BeEmpty();
            var header = "id\tname\tcanNumber\tkind\tera\tnetOz\tnetOzMax\tfluidOz\tcups\ttitle\tpublisher\tyear\tpage\tlocator\tnote\tproducts\tcandidates\n";
            var two = "two\tNo. 2\t2\tcan\thistoric\t20\t-\t-\t-\tT\tP\t1933\t1\tL\tq\t-\t-\n";
            CanSizeTable.Problems(CanSizeTable.Parse(header + two + "m\tMedium\t-\tcan\thistoric\t16\t20\t-\t-\tT\tP\t1933\t1\tL\tq\tcategory:11\t303|2\n"))
                .Should().ContainSingle().Which.Should().Contain("candidate No. 303 has no size");
            CanSizeTable.Problems(CanSizeTable.Parse(header + two + "m\tMedium\t-\tcan\thistoric\t20\t-\t-\t-\tT\tP\t1933\t1\tL\tq\tvegetables\t2|2\n"))
                .Should().Contain(p => p.Contains("category:")).And.NotContain(p => p.Contains("no size of era"));
            CanSizeTable.Problems(CanSizeTable.Parse(header + two + "m\tMedium\t-\tcan\thistoric\t20\t-\t-\t-\tT\tP\t1933\t1\tL\tq\t-\t2|2\n"))
                .Should().Contain(p => p.Contains("names both its products"));
        }

        private static List<ContainerHoldoutLine> Lines(int hits, int misses, DateTime? start = null)
        {
            var at = start ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Enumerable.Range(0, hits + misses).Select(i => new ContainerHoldoutLine(i < hits, at.AddMinutes(i))).ToList();
        }

        [Fact]
        public void A_key_with_its_own_hold_out_applies_at_95_percent_and_a_90_percent_lower_bound()
        {
            LaneQualifier.MinimumSample(ContainerSizeQualifier.Lower).Should().Be(35);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(40, 0), Lines(40, 0), true).Status.Should().Be(ContainerSizeStatus.Active);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(37, 3), Lines(37, 3), true).Status.Should().Be(ContainerSizeStatus.Shadow, "92.5 % is under 95 %");
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(20, 0), Lines(20, 0), true).Status.Should().Be(ContainerSizeStatus.Shadow, "20 of 20 bounds at 83.9 % and the class has too few lines");
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(20, 0), Lines(60, 0), true).Status.Should().Be(ContainerSizeStatus.Active, "too few for the bound, and its class qualifies");
        }

        [Fact]
        public void A_key_with_few_lines_applies_on_its_class_and_its_own_source_test()
        {
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(3, 0), Lines(57, 2), true).Status.Should().Be(ContainerSizeStatus.Active);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(3, 0), Lines(57, 2), false).Status.Should().Be(ContainerSizeStatus.Shadow);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(3, 0), Lines(40, 0), true).Status.Should().Be(ContainerSizeStatus.Shadow, "a class needs 50 lines");
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(0, 0), Lines(50, 5), true).Status.Should().Be(ContainerSizeStatus.Shadow, "a class at 90.9 % is under 95 %");
        }

        [Fact]
        public void An_active_key_pauses_on_three_misses_in_its_last_twenty_or_its_class_under_90_percent_over_200_and_stays_paused()
        {
            var key = Lines(40, 0).Concat(Lines(17, 3, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))).ToList();
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Active, key, key, true).Status.Should().Be(ContainerSizeStatus.Paused);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Active, Lines(40, 0), Lines(170, 30), true).Status.Should().Be(ContainerSizeStatus.Paused);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Active, Lines(40, 0), Lines(185, 15), true).Status.Should().Be(ContainerSizeStatus.Active);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Paused, Lines(100, 0), Lines(100, 0), true).Status.Should().Be(ContainerSizeStatus.Paused, "a paused key comes back only when its size or source changes");
        }

        [Fact]
        public void A_shadow_key_that_meets_a_pause_condition_stays_in_shadow_whatever_its_overall_share()
        {
            var key = Lines(380, 0).Concat(Lines(17, 3, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))).ToList();
            var recent = ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, key, key, true);
            recent.Status.Should().Be(ContainerSizeStatus.Shadow, "3 of its last 20 lines missed, which would pause it the run after it applied");
            recent.Reason.Should().Contain("3 misses in its last 20");

            var classLines = Lines(170, 30);
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(40, 0), classLines, true).Status.Should().Be(ContainerSizeStatus.Shadow, "its class agreed on 170 of its last 200");
            ContainerSizeQualifier.Decide(ContainerSizeStatus.Shadow, Lines(3, 0), Lines(57, 2), true).Status.Should().Be(ContainerSizeStatus.Active, "a clean key still applies on its class");
        }

        [Theory]
        [InlineData("1 can tomatoes", ContainerKinds.Can, "")]
        [InlineData("2 pkgs. cream cheese", ContainerKinds.Package, "")]
        [InlineData("1 large jar marinara", ContainerKinds.Jar, ContainerKinds.Large)]
        [InlineData("1 small container Greek yogurt", ContainerKinds.Tub, ContainerKinds.Small)]
        [InlineData("1 envelope gelatine", ContainerKinds.Envelope, "")]
        [InlineData("1 pouch tuna", ContainerKinds.Packet, "")]
        [InlineData("2 cups flour", null, "")]
        public void A_line_names_its_container_kind_and_size_word(string line, string? kind, string word)
        {
            ContainerKinds.FromText(line).Should().Be(kind);
            ContainerKinds.SizeWord(line).Should().Be(word);
        }
    }
}
