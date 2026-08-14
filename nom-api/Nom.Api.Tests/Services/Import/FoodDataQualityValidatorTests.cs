using FluentAssertions;
using Nom.Data.Nutrition;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// The pollution gate for imported/fetched food nutrition — rejects physically
    /// implausible records (the noisy corners of FDC Branded Foods).
    /// </summary>
    public class FoodDataQualityValidatorTests
    {
        private readonly FoodDataQualityValidator _v = new();

        // A realistic banana per 100g: 89 kcal, 1.1 protein, 23 carb, 0.3 fat.
        private static FoodQualityInput Banana(
            string? name = "Banana, raw",
            decimal? kcal = 89m, decimal? protein = 1.1m, decimal? carb = 23m,
            decimal? fat = 0.3m) =>
            new(name, kcal, protein, carb, fat);

        [Fact]
        public void Accepts_PlausibleWholeFood()
        {
            _v.Validate(Banana()).Accepted.Should().BeTrue();
        }

        [Fact]
        public void Accepts_PureFat_AtEnergyCeiling()
        {
            // Oil: ~884 kcal, 0/0/100 — extreme but real; must pass.
            var oil = new FoodQualityInput("Olive Oil", 884m, 0m, 0m, 100m);
            _v.Validate(oil).Accepted.Should().BeTrue();
        }

        [Fact]
        public void Rejects_ImpossibleCalories()
        {
            var r = _v.Validate(Banana(kcal: 5000m));
            r.Accepted.Should().BeFalse();
            r.Reasons.Should().Contain("calories_impossible");
        }

        [Fact]
        public void Rejects_NegativeMacro()
        {
            _v.Validate(Banana(protein: -3m)).Reasons.Should().Contain("protein_negative");
        }

        [Fact]
        public void Rejects_MacroOver100g()
        {
            _v.Validate(Banana(carb: 250m)).Reasons.Should().Contain("carb_impossible");
        }

        [Fact]
        public void Rejects_MacroSumOver100g()
        {
            // Each macro individually <100 but the sum is impossible.
            var r = _v.Validate(Banana(kcal: 700m, protein: 60m, carb: 60m, fat: 40m));
            r.Reasons.Should().Contain("macro_sum_impossible");
        }

        [Fact]
        public void Rejects_MissingCaloriesAndMacros()
        {
            var r = _v.Validate(new FoodQualityInput("Mystery", null, null, null, null));
            r.Accepted.Should().BeFalse();
            r.Reasons.Should().Contain(new[]
            {
                "calories_missing", "protein_missing", "carb_missing", "fat_missing",
            });
        }

        [Fact]
        public void Rejects_JunkName()
        {
            _v.Validate(Banana(name: "")).Reasons.Should().Contain("name_missing");
            _v.Validate(Banana(name: "12345 %%%")).Reasons.Should().Contain("name_not_alphabetic");
        }

        [Fact]
        public void Rejects_AtwaterMismatch()
        {
            // Macros imply ~4*0+4*90+9*0 = 360 kcal but the record claims 100 — mis-scaled units.
            var r = _v.Validate(Banana(kcal: 100m, protein: 0m, carb: 90m, fat: 0m));
            r.Accepted.Should().BeFalse();
            r.Reasons.Should().Contain("atwater_mismatch");
        }

        [Fact]
        public void SkipsAtwater_ForVeryLowCalorieFoods()
        {
            // Lettuce-ish: tiny calories where the Atwater ratio is too noisy to judge.
            var r = _v.Validate(new FoodQualityInput("Iceberg Lettuce", 14m, 0.9m, 3m, 0.1m));
            r.Reasons.Should().NotContain("atwater_mismatch");
            r.Accepted.Should().BeTrue();
        }
    }
}
