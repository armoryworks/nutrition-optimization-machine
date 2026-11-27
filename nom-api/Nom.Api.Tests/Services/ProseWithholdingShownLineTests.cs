using FluentAssertions;
using Nom.Data.Recipe;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// A stored ingredient line is served with its HTML entities read: staging checkpoint 8 found "&amp;frac14;" and
    /// "&amp;frac12;" shown as written in the lines of two withheld recipes.
    /// </summary>
    public class ProseWithholdingShownLineTests
    {
        [Theory]
        [InlineData("1 &frac14; cups oat flour", "1 ¼ cups oat flour")]
        [InlineData("&frac12; teaspoon salt", "½ teaspoon salt")]
        [InlineData("1 &amp;frac12; cups milk", "1 ½ cups milk")]
        [InlineData("salt &amp; pepper", "salt & pepper")]
        [InlineData("2 cups flour", "2 cups flour")]
        public void A_line_is_served_with_its_entities_read(string stored, string shown)
        {
            ProseWithholding.LineWording(new RecipeEntity(), stored).Should().Be(shown);
            ProseWithholding.LineName(null, stored, "Flour", 1, "Cup", true).Should().Be(shown);
        }

        [Fact]
        public void A_facts_only_line_is_still_never_served()
        {
            ProseWithholding.LineWording(new RecipeEntity { SourceRights = SourceRightsRule.FactsOnly }, "1 &frac14; cups oat flour").Should().BeNull();
        }
    }
}
