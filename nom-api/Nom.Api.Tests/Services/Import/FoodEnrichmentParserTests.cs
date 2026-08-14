using FluentAssertions;
using Nom.Data.Nutrition;
using Nom.Data.Reference;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// Parsing a local-model classification reply into a validated food group + whole-food flag,
    /// and tolerant resolution of free-text group names.
    /// </summary>
    public class FoodEnrichmentParserTests
    {
        [Fact]
        public void Parses_CleanJson()
        {
            var r = FoodEnrichmentParser.Parse("{\"food_group\":\"Vegetables\",\"whole_food\":true}");
            r.FoodGroupId.Should().Be((long)FoodGroupEnum.Vegetables);
            r.IsWholeFood.Should().BeTrue();
        }

        [Fact]
        public void Parses_JsonWrappedInProse()
        {
            var reply = "Sure! Here is the classification:\n{\"food_group\": \"Protein Foods\", \"whole_food\": false}\nHope that helps.";
            var r = FoodEnrichmentParser.Parse(reply);
            r.FoodGroupId.Should().Be((long)FoodGroupEnum.ProteinFoods);
            r.IsWholeFood.Should().BeFalse();
        }

        [Fact]
        public void Parses_StringBooleans()
        {
            var r = FoodEnrichmentParser.Parse("{\"foodGroup\":\"Fruits\",\"wholeFood\":\"yes\"}");
            r.FoodGroupId.Should().Be((long)FoodGroupEnum.Fruits);
            r.IsWholeFood.Should().BeTrue();
        }

        [Fact]
        public void Drops_HallucinatedGroup()
        {
            // "Superfoods" isn't a real group — must not be trusted.
            var r = FoodEnrichmentParser.Parse("{\"food_group\":\"Superfoods\",\"whole_food\":true}");
            r.FoodGroupId.Should().BeNull();
            r.IsWholeFood.Should().BeTrue();
        }

        [Fact]
        public void FallsBack_ToDisplayNameInProse()
        {
            var r = FoodEnrichmentParser.Parse("This is clearly in the Dairy group and is a whole food: yes.");
            r.FoodGroupId.Should().Be((long)FoodGroupEnum.Dairy);
            r.IsWholeFood.Should().BeTrue();
        }

        [Fact]
        public void Returns_NullsForGarbage()
        {
            var r = FoodEnrichmentParser.Parse("I'm not sure what that is.");
            r.FoodGroupId.Should().BeNull();
            r.IsWholeFood.Should().BeNull();
        }

        [Theory]
        [InlineData("Vegetables")]
        [InlineData("vegetable")]
        [InlineData("VEG")]
        [InlineData("veggies")]
        public void Catalog_ResolvesVegetableSynonyms(string text)
        {
            FoodGroupCatalog.TryResolve(text).Should().Be((long)FoodGroupEnum.Vegetables);
        }

        [Theory]
        [InlineData("Protein Foods")]
        [InlineData("proteinfoods")]
        [InlineData("protein")]
        [InlineData("meat")]
        public void Catalog_ResolvesProteinSynonyms(string text)
        {
            FoodGroupCatalog.TryResolve(text).Should().Be((long)FoodGroupEnum.ProteinFoods);
        }

        [Theory]
        [InlineData("Fats/Oils")]
        [InlineData("Nuts/Seeds")]
        [InlineData("Sweets/Snacks")]
        public void Catalog_ResolvesSlashedNames(string text)
        {
            FoodGroupCatalog.TryResolve(text).Should().NotBeNull();
        }

        [Fact]
        public void Catalog_ReturnsNullForUnknown()
        {
            FoodGroupCatalog.TryResolve("Superfoods").Should().BeNull();
            FoodGroupCatalog.TryResolve("").Should().BeNull();
            FoodGroupCatalog.TryResolve(null).Should().BeNull();
        }
    }
}
