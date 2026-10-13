using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Snack vs dessert by added-sugar share of energy: under 10% snack, 25%+ dessert, between both.
    /// Per-100 g values are USDA SR Legacy.
    /// </summary>
    public class CourseClassifierTests
    {
        private static CourseLine Line(string name, decimal grams, decimal kcal, decimal sugars, decimal? added = null) =>
            new(name, grams, kcal, sugars, added);

        [Fact]
        public void Chocolate_chip_cookies_are_dessert()
        {
            var verdict = CourseClassifier.Classify(new[]
            {
                Line("Wheat flour, white, all-purpose", 280, 364, 0.27m),
                Line("Butter, without salt", 227, 717, 0.06m),
                Line("Sugars, granulated", 150, 387, 99.8m),
                Line("Sugars, brown", 165, 380, 97m),
                Line("Egg, whole, raw, fresh", 100, 143, 0.37m),
                Line("Candies, semisweet chocolate", 340, 480, 54.5m),
            });
            verdict!.Course.Should().Be(SweetCourse.Dessert);
            verdict.AddedSugarShare.Should().BeGreaterThan(0.25m);
        }

        [Fact]
        public void Banana_nice_cream_is_a_snack_because_fruit_sugar_is_not_added_sugar()
        {
            var verdict = CourseClassifier.Classify(new[]
            {
                Line("Bananas, raw", 240, 89, 12.2m),
                Line("Milk, reduced fat, fluid, 2% milkfat", 60, 50, 5.06m),
            });
            verdict!.Course.Should().Be(SweetCourse.Snack);
            verdict.AddedSugarShare.Should().Be(0);
        }

        [Fact]
        public void Yogurt_bark_with_some_honey_and_chocolate_is_both()
        {
            var verdict = CourseClassifier.Classify(new[]
            {
                Line("Yogurt, Greek, plain, whole milk", 500, 97, 4m),
                Line("Strawberries, raw", 150, 32, 4.89m),
                Line("Honey", 21, 304, 82.1m),
                Line("Candies, semisweet chocolate", 20, 480, 54.5m),
                Line("Nuts, almonds", 30, 579, 4.35m),
            });
            verdict!.Course.Should().Be(SweetCourse.Both);
        }

        [Fact]
        public void A_labelled_added_sugar_value_wins_over_the_name()
        {
            var verdict = CourseClassifier.Classify(new[]
            {
                Line("GRANOLA BITES", 100, 450, 20m, added: 2m),
                Line("Nuts, almonds", 100, 579, 4.35m),
            });
            verdict!.Course.Should().Be(SweetCourse.Snack);
        }

        [Theory]
        [InlineData("Peas, edible-podded, raw (sugar snap)", false)]
        [InlineData("Cocoa, dry powder, unsweetened", false)]
        [InlineData("Syrups, maple", true)]
        [InlineData("Milk, canned, condensed, sweetened", true)]
        [InlineData("Jams and preserves", true)]
        public void Sweetener_names(string name, bool sweetener)
        {
            CourseClassifier.IsSweetener(name).Should().Be(sweetener);
        }

        [Fact]
        public void Too_little_data_leaves_the_recipe_alone()
        {
            CourseClassifier.Classify(new[]
            {
                Line("Sugars, granulated", 100, 387, 99.8m),
                new CourseLine("mystery crumble", null, null, null, null),
                new CourseLine("secret sauce", null, null, null, null),
            }).Should().BeNull();
        }
    }
}
