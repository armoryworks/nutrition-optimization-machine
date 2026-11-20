using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Where a joined recipe line falls apart into its source lines, and which " + " belongs to a
    /// single line. Lines here are real staging rows.
    /// </summary>
    public class FoldedLineTests
    {
        private static IReadOnlySet<string> Source(params string[] lines) => lines.Select(FoldedLine.Key).ToHashSet();

        [Fact]
        public void Two_source_lines_joined_are_read_back_as_two()
        {
            var source = Source("1 3/4 cup granulated sugar", "1/3 cup granulated sugar, (for rolling cookies)", "2 large eggs");

            FoldedLine.Segments("1 3/4 cup granulated sugar + 1/3 cup granulated sugar, (for rolling cookies)", source)
                .Should().Equal("1 3/4 cup granulated sugar", "1/3 cup granulated sugar, (for rolling cookies)");
        }

        [Theory]
        [InlineData("1 cup + 2 Tablespoons (225g) granulated sugar")]
        [InlineData("1  large egg + 1 egg white")]
        [InlineData("salt + pepper (to taste)")]
        public void A_line_that_is_one_line_of_the_source_is_not_a_fold(string line)
        {
            FoldedLine.Segments(line, Source(line, "1 cup flour")).Should().Equal(line);
        }

        [Fact]
        public void A_plus_inside_one_of_the_joined_lines_stays_inside_it()
        {
            var source = Source("3 large eggs", "1 large egg + 1 teaspoon water, (lightly beaten for egg wash)");

            FoldedLine.Segments("3  large eggs + 1  large egg + 1 teaspoon water, (lightly beaten for egg wash)", source)
                .Should().Equal("3  large eggs", "1  large egg + 1 teaspoon water, (lightly beaten for egg wash)");
        }

        [Fact]
        public void The_reading_with_the_fewest_lines_wins_when_the_source_allows_two()
        {
            var source = Source("1 large egg", "1 egg white", "1 large egg + 1 egg white", "2 egg yolks");

            FoldedLine.Segments("2 egg yolks + 1 large egg + 1 egg white", source)
                .Should().Equal("2 egg yolks", "1 large egg + 1 egg white");
        }

        [Fact]
        public void A_line_whose_parts_are_not_lines_of_the_source_is_not_read()
        {
            FoldedLine.Segments("1 cup sugar + 2 cups flour", Source("1 cup sugar", "2 cups bread flour")).Should().BeNull();
        }

        [Fact]
        public void Entities_spacing_and_case_do_not_keep_a_line_from_its_source()
        {
            var source = Source("&frac12; cup  Olive Oil", "Olive oil, for drizzling");

            FoldedLine.Segments("½ cup olive oil + olive oil, for drizzling", source)
                .Should().Equal("½ cup olive oil", "olive oil, for drizzling");
        }

        [Fact]
        public void Without_a_source_list_every_part_is_a_line()
        {
            FoldedLine.Segments("1 tablespoonful of butter + 1/4 pound of butter", null)
                .Should().Equal("1 tablespoonful of butter", "1/4 pound of butter");
            FoldedLine.Segments("sugar + sugar", null).Should().Equal("sugar", "sugar");
        }

        [Fact]
        public void A_stored_source_without_a_readable_list_leaves_the_line_unread()
        {
            FoldedLine.Segments("1 cup sugar + 2 cups sugar", new HashSet<string>()).Should().BeNull();
        }

        [Theory]
        [InlineData("3/4 c + 2 T milk")]
        [InlineData("1 cup + 1 cup")]
        [InlineData("½ C. + 2 Tbsp. shortening")]
        [InlineData("2 large + 1 small onion")]
        [InlineData("1 lb + 4 oz butter")]
        [InlineData("butter +  + flour")]
        public void Without_a_source_list_a_part_that_is_only_an_amount_leaves_the_line_unread(string line)
        {
            FoldedLine.Segments(line, null).Should().BeNull();
        }

        [Fact]
        public void A_line_is_returned_as_written_between_its_separators()
        {
            FoldedLine.Segments("  2 cups  Flour  +  1 tsp Salt ", null).Should().Equal("2 cups  Flour", "1 tsp Salt");
        }

        [Fact]
        public void The_source_list_is_the_recipe_ingredient_array_of_the_stored_document()
        {
            FoldedLine.SourceLines("""{"@type":"Recipe","recipeIngredient":["1 cup  Sugar","&frac12; cup butter",3,""]}""")
                .Should().BeEquivalentTo(new[] { "1 cup sugar", "½ cup butter" });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("""{"@type":"Recipe","name":"Cake"}""")]
        [InlineData("""{"recipeIngredient":"1 cup sugar"}""")]
        [InlineData("""[{"recipeIngredient":["1 cup sugar"]}]""")]
        [InlineData("""{"recipeIngredient":["1 cup sugar"}""")]
        public void A_document_without_a_readable_list_gives_no_source_lines(string? rawJsonLd)
        {
            FoldedLine.SourceLines(rawJsonLd).Should().BeEmpty();
        }
    }
}
