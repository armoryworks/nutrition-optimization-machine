using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    public class DishGroupSuggesterTests
    {
        [Theory]
        [InlineData("BEST Chewy Chocolate Chip Cookies", "chocolate chip cookies")]
        [InlineData("chocolate chip cookies", "chocolate chip cookies")]
        [InlineData("Grandma's Classic Banana Bread", "banana bread")]
        [InlineData("Easy One-Pot Butter Chicken", "butter chicken")]
        [InlineData("Baking Powder Biscuit II", "baking powder biscuit")]
        [InlineData("Fruit Rolls (Pin Wheel Biscuit)", "fruit rolls")]
        [InlineData("The Ultimate 30-Minute Weeknight Spaghetti", "spaghetti")]
        [InlineData("Creamy Garlic Chicken Recipe", "garlic chicken")]
        public void Normalize_strips_marketing_and_numbering(string input, string expected)
        {
            Assert.Equal(expected, HeuristicDishGroupSuggester.Normalize(input));
        }

        [Theory]
        [InlineData("My", null)]
        [InlineData("", null)]
        public void Normalize_rejects_empty_results(string input, string? expected)
        {
            Assert.Equal(expected, HeuristicDishGroupSuggester.Normalize(input));
        }

        [Theory]
        [InlineData("chocolate chip cookies", "chocolate-chip-cookies")]
        [InlineData("  Butter Chicken!  ", "butter-chicken")]
        public void Slugify_produces_url_safe_keys(string input, string expected)
        {
            Assert.Equal(expected, DishGroupService.Slugify(input));
        }
    }
}
