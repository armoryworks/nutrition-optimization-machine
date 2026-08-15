using FluentAssertions;
using Nom.Data.Nutrition;
using Nom.Data.Reference;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// Classification from a product *category* label (FDC's branded_food_category), which is a
    /// far better signal than a product name. Precision matters more than recall here: a wrong
    /// group would let the wrong food satisfy a household food-group minimum.
    /// </summary>
    public class FoodGroupCategoryHeuristicsTests
    {
        [Theory]
        [InlineData("Cheese", (long)FoodGroupEnum.Dairy)]
        [InlineData("Yogurt", (long)FoodGroupEnum.Dairy)]
        [InlineData("Popcorn, Peanuts, Seeds & Related Snacks", (long)FoodGroupEnum.NutsSeeds)]
        [InlineData("Breads & Buns", (long)FoodGroupEnum.Grains)]
        [InlineData("Frozen Fish & Seafood", (long)FoodGroupEnum.ProteinFoods)]
        [InlineData("Fruit & Vegetable Juice, Nectars & Fruit Drinks", (long)FoodGroupEnum.Beverages)]
        public void ClassifiesCommonCategories(string category, long expected)
        {
            FoodGroupHeuristics.ClassifyByCategory(category).Should().Be(expected);
        }

        [Fact]
        public void NutButters_ClassifyAsNutsSeeds_NotAsASpreadOrCondiment()
        {
            FoodGroupHeuristics.ClassifyByCategory("Nut & Seed Butters")
                .Should().Be((long)FoodGroupEnum.NutsSeeds);
        }

        [Theory]
        // Regression: this compound retail category contains "cheese" and used to classify BBQ
        // sauce as Dairy, which would have satisfied a household dairy minimum.
        [InlineData("Ketchup, Mustard, BBQ & Cheese Sauce")]
        [InlineData("Other Cooking Sauces")]
        [InlineData("Salad Dressing & Mayonnaise")]
        [InlineData("Dips & Salsa")]
        [InlineData("Herbs & Spices")]
        public void CondimentCategories_StayUnclassified(string category)
        {
            FoodGroupHeuristics.ClassifyByCategory(category).Should().BeNull();
        }

        [Theory]
        // Conclusive: callers must not fall back to name keywords for these.
        [InlineData("Ketchup, Mustard, BBQ & Cheese Sauce", true)]
        [InlineData("Other Cooking Sauces", true)]
        [InlineData("Nut & Seed Butters", false)]  // a real food group despite reading like a spread
        [InlineData("Cheese", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IdentifiesConclusiveNonFoodGroupCategories(string? category, bool expected)
        {
            FoodGroupHeuristics.IsNonFoodGroupCategory(category).Should().Be(expected);
        }

        [Fact]
        public void UnknownOrEmptyCategory_IsNull()
        {
            FoodGroupHeuristics.ClassifyByCategory("Miscellaneous Widgets").Should().BeNull();
            FoodGroupHeuristics.ClassifyByCategory("").Should().BeNull();
            FoodGroupHeuristics.ClassifyByCategory(null).Should().BeNull();
        }

        [Theory]
        [InlineData("Snack, Energy & Granola Bars", true)]
        [InlineData("Yogurt", true)]
        [InlineData("Flours & Corn Meal", false)]
        [InlineData("Baking Additives & Extracts", false)]
        [InlineData("Herbs & Spices", false)]
        [InlineData("Vegetable & Cooking Oils", false)]
        public void FlagsDirectlyEdibleVsCookingInput(string category, bool expected)
        {
            FoodGroupHeuristics.IsDirectlyEdibleCategory(category).Should().Be(expected);
        }

        [Fact]
        public void DirectlyEdible_IsNullWithoutACategory()
        {
            FoodGroupHeuristics.IsDirectlyEdibleCategory(null).Should().BeNull();
            FoodGroupHeuristics.IsDirectlyEdibleCategory("").Should().BeNull();
        }
    }
}
