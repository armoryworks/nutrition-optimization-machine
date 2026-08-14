using System.Linq;
using System.Reflection;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Guards the column-width clamp on imported text: sources we don't control
    /// (19th-century cookbook prose especially) exceed varchar widths, and a
    /// single long line must not fail the recipe.
    /// </summary>
    public class ScrapedTextClampTests
    {
        private static string? Clamp(string? value, int max) =>
            (string?)typeof(RecipeScrapingService)
                .GetMethod("Clamp", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object?[] { value, max });

        [Fact]
        public void Short_values_pass_through_untouched()
        {
            Assert.Equal("Cream Sauce", Clamp("Cream Sauce", 255));
            Assert.Null(Clamp(null, 255));
        }

        [Fact]
        public void Long_values_are_trimmed_to_the_column_width()
        {
            var long_step = string.Join(" ", Enumerable.Repeat("Stir the sauce gently.", 40));
            var clamped = Clamp(long_step, 255);

            Assert.NotNull(clamped);
            Assert.True(clamped!.Length <= 255);
            Assert.StartsWith("Stir the sauce gently.", clamped);
        }

        [Fact]
        public void Trailing_whitespace_from_the_cut_is_removed()
        {
            Assert.Equal("abc", Clamp("abc     def", 6));
        }

        [Fact]
        public void Value_exactly_at_the_limit_is_kept_whole()
        {
            var exact = new string('x', 255);
            Assert.Equal(exact, Clamp(exact, 255));
        }
    }
}
