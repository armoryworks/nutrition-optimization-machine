using FluentAssertions;
using Nom.Import.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// One item's weight comes from USDA's own countable portions, so "1 piece" of garlic is a clove,
    /// of celery a stalk and of a strawberry one medium berry. Rows are copied from the SR Legacy
    /// 2018-04 and Foundation 2025-12-18 food_portion.csv files.
    /// </summary>
    public class UsdaCountWeightTests
    {
        private static UsdaPortion Sr(decimal amount, string modifier, decimal grams) => new(amount, "9999", modifier, "", grams, "undetermined");

        [Fact]
        public void Garlic_is_a_clove_not_a_cup_or_teaspoon()
        {
            var garlic = new[] { Sr(1, "cup", 136), Sr(1, "tsp", 2.8m), Sr(1, "clove", 3), Sr(3, "cloves", 9) };

            UsdaCountWeight.GramsPerCount(garlic, "Garlic, raw")!.Grams.Should().Be(3m);
        }

        [Fact]
        public void A_bulb_or_head_is_never_one_item()
        {
            var garlic = new[] { Sr(1, "bulb", 40), Sr(1, "head", 50) };

            UsdaCountWeight.GramsPerCount(garlic, "Garlic, raw").Should().BeNull();
        }

        [Fact]
        public void A_whole_bird_is_never_one_item_even_from_half_of_one()
        {
            var goose = new[] { Sr(1, "cup, chopped or diced", 140), Sr(1, "unit (yield from 1 lb ready-to-cook goose)", 188), Sr(0.5m, "goose", 774) };

            UsdaCountWeight.GramsPerCount(goose, "Goose, domesticated, meat and skin, cooked, roasted").Should().BeNull();
        }

        [Fact]
        public void Medium_beats_the_other_sizes_and_the_cups()
        {
            var strawberries = new[]
            {
                Sr(1, "cup, halves", 152), Sr(1, "cup, whole", 144), Sr(1, "extra large (1-5/8\" dia)", 27),
                Sr(1, "large (1-3/8\" dia)", 18), Sr(1, "medium (1-1/4\" dia)", 12), Sr(1, "pint as purchased, yields", 357),
                Sr(1, "small (1\" dia)", 7), Sr(1, "NLEA serving", 147),
            };

            var item = UsdaCountWeight.GramsPerCount(strawberries, "Strawberries, raw")!;

            item.Grams.Should().Be(12m);
            item.Portion.Should().StartWith("1 medium");
        }

        [Fact]
        public void Celery_is_a_medium_stalk_and_never_a_strip()
        {
            var celery = new[]
            {
                Sr(1, "cup chopped", 101), Sr(1, "NLEA serving", 110), Sr(1, "tbsp", 7.5m),
                Sr(1, "stalk, large (11\"-12\" long)", 64), Sr(1, "stalk, medium (7-1/2\" - 8\" long)", 40),
                Sr(1, "stalk, small (5\" long)", 17), Sr(1, "strip (4\" long)", 4),
            };

            UsdaCountWeight.GramsPerCount(celery, "Celery, raw")!.Grams.Should().Be(40m);
        }

        [Fact]
        public void A_portion_named_after_the_food_counts_and_a_multi_item_portion_is_divided()
        {
            UsdaCountWeight.GramsPerCount(new[] { Sr(0.5m, "cup slices", 52), Sr(1, "cucumber (8-1/4\")", 301) }, "Cucumber, with peel, raw")!
                .Grams.Should().Be(301m);
            UsdaCountWeight.GramsPerCount(new[] { Sr(1, "cup", 123), Sr(10, "raspberries", 19) }, "Raspberries, raw")!
                .Grams.Should().Be(1.9m);
        }

        [Fact]
        public void The_size_the_food_names_wins()
        {
            var eggs = new[] { Sr(1, "large", 50), Sr(1, "medium", 44), Sr(1, "small", 38) };

            UsdaCountWeight.GramsPerCount(eggs, "Egg, whole, raw, fresh")!.Grams.Should().Be(44m);
            UsdaCountWeight.GramsPerCount(eggs, "Eggs, Grade A, Large, egg whole")!.Grams.Should().Be(50m);
        }

        [Fact]
        public void Foundation_names_the_unit_by_id_and_sizes_it_in_the_modifier()
        {
            var cantaloupe = new[]
            {
                new UsdaPortion(1, "1000", "cubes", "", 160, "cup"),
                new UsdaPortion(1, "1071", "large (about 6-1/2\" dia)", "", 814, "each"),
                new UsdaPortion(1, "1060", "medium (1/8 of medium melon)", "", 69, "wedge"),
                new UsdaPortion(1, "1071", "medium (about 5\" dia)", "", 552, "each"),
            };

            UsdaCountWeight.GramsPerCount(cantaloupe, "Melons, cantaloupe, raw")!.Grams.Should().Be(552m);
        }

        [Fact]
        public void Weights_volumes_and_servings_are_not_items()
        {
            var oats = new[] { Sr(1, "cup", 81), Sr(0.33m, "cup", 27), Sr(1, "oz", 28.35m), Sr(1, "serving", 40), Sr(1, "package", 400) };

            UsdaCountWeight.GramsPerCount(oats, "Cereals, oats, regular and quick, not fortified, dry").Should().BeNull();
        }
    }
}
