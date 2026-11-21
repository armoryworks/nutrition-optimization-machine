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
        public void Accepts_Lard_AtUsdaEnergy()
        {
            // USDA SR Legacy 171401 Lard: 902 kcal, 0/0/100.
            _v.Validate(new FoodQualityInput("Lard", 902m, 0m, 0m, 100m)).Accepted.Should().BeTrue();
            _v.Validate(new FoodQualityInput("Lard", 903m, 0m, 0m, 100m)).Reasons.Should().Contain("calories_impossible");
        }

        [Fact]
        public void Accepts_AcidicFruit_BelowTheAtwaterFigure()
        {
            // USDA 167746 Lemons, raw, without peel: 29 kcal, 1.1 protein, 9.3 carb, 0.3 fat — the carb is largely citric acid.
            var lemon = new FoodQualityInput("Lemons, raw, without peel", 29m, 1.1m, 9.3m, 0.3m);
            _v.Validate(lemon).Accepted.Should().BeTrue();

            var mystery = new FoodQualityInput("Mystery fruit, raw", 29m, 1.1m, 9.3m, 0.3m);
            _v.Validate(mystery).Reasons.Should().Contain("atwater_mismatch", "only known acidic foods get the allowance");

            var overstated = new FoodQualityInput("Lemon juice, raw", 80m, 0.4m, 6.9m, 0.2m);
            _v.Validate(overstated).Reasons.Should().Contain("atwater_mismatch", "the allowance is only for a stated figure below the Atwater one");
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

        [Fact]
        public void Rejects_AtwaterMismatch_WhenStatedCaloriesAreUnderReported()
        {
            var r = _v.Validate(new FoodQualityInput("Pork Tenderloin", 18m, 17.9m, 0m, 2.2m));
            r.Accepted.Should().BeFalse();
            r.Reasons.Should().Contain("atwater_mismatch");
        }

        [Fact]
        public void Rejects_AtwaterMismatch_WhenStatedCaloriesAreZero()
        {
            var r = _v.Validate(new FoodQualityInput("Red Nicaraguan Beans", 0m, 22.22m, 63.89m, 0m));
            r.Accepted.Should().BeFalse();
            r.Reasons.Should().Contain("atwater_mismatch");
        }

        [Fact]
        public void SkipsAtwater_ForSeasoningsUsedInSmallAmounts()
        {
            var pepper = new FoodQualityInput("Spices, pepper, black", 251m, 10.4m, 64m, 3.3m);
            _v.Validate(pepper).Reasons.Should().Contain("atwater_mismatch");
            _v.Validate(pepper with { UsedInSmallAmounts = true }).Accepted.Should().BeTrue();
            _v.Validate(pepper with { UsedInSmallAmounts = true, KcalPer100g = 5000m }).Reasons.Should().Contain("calories_impossible");
        }

        [Theory]
        [InlineData(FoodComponent.Sodium, 38758, null)]
        [InlineData(FoodComponent.Sodium, 387580, "sodium_impossible")]
        [InlineData(FoodComponent.Cholesterol, -1, "cholesterol_negative")]
        [InlineData(FoodComponent.VitaminD, 250, null)]
        [InlineData(FoodComponent.SaturatedFat, 3.1, null)]
        [InlineData(FoodComponent.SaturatedFat, 30, "saturated_fat_exceeds_fat")]
        [InlineData(FoodComponent.TransFat, 10.9, null)]
        [InlineData(FoodComponent.Fiber, 2.4, null)]
        [InlineData(FoodComponent.Fiber, 40, "fiber_exceeds_carb")]
        [InlineData(FoodComponent.Sugars, 26, "sugars_exceeds_carb")]
        [InlineData(FoodComponent.VitaminA, 30000, null)]
        [InlineData(FoodComponent.VitaminC, 1678, null)]
        [InlineData(FoodComponent.Selenium, 1917, null)]
        [InlineData(FoodComponent.Copper, 14.6, null)]
        [InlineData(FoodComponent.Copper, 370, "copper_impossible")]
        [InlineData(FoodComponent.Manganese, 269.1, "manganese_impossible")]
        [InlineData(FoodComponent.VitaminB12, -0.1, "vitamin_b12_negative")]
        [InlineData(FoodComponent.Alcohol, 33.4, null)]
        [InlineData(FoodComponent.Alcohol, 101, "alcohol_impossible")]
        public void CheckComponent_BoundsEachLabelNutrientAndItsParent(string key, double amount, string? expected)
        {
            _v.CheckComponent(key, (decimal)amount, fatGramsPer100g: 9.5m, carbGramsPer100g: 23m).Should().Be(expected);
        }

        [Fact]
        public void Accepts_Wine_WhenItsAlcoholIsCounted()
        {
            var wine = new FoodQualityInput("Alcoholic beverage, wine, table, white", 82m, 0.07m, 2.6m, 0m, AlcoholGramsPer100g: 10.3m);
            _v.Validate(wine).Accepted.Should().BeTrue();
            _v.Validate(wine with { AlcoholGramsPer100g = null }).Reasons.Should().Equal("atwater_mismatch");
            _v.Validate(wine with { AlcoholGramsPer100g = 0m }).Reasons.Should().Equal("atwater_mismatch");
        }

        [Fact]
        public void Accepts_DistilledSpirit_WithNoMacros()
        {
            var spirit = new FoodQualityInput("Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", 231m, 0m, 0m, 0m, AlcoholGramsPer100g: 33.4m);
            _v.Validate(spirit).Accepted.Should().BeTrue();
        }

        [Fact]
        public void Rejects_AtwaterMismatch_WhenAlcoholDoesNotExplainTheCalories()
        {
            var wine = new FoodQualityInput("Alcoholic beverage, wine, table, white", 300m, 0.07m, 2.6m, 0m, AlcoholGramsPer100g: 10.3m);
            _v.Validate(wine).Reasons.Should().Equal("atwater_mismatch");
        }

        [Fact]
        public void Rejects_ImpossibleAlcohol()
        {
            var wine = new FoodQualityInput("Alcoholic beverage, wine, table, white", 82m, 0.07m, 2.6m, 0m);
            _v.Validate(wine with { AlcoholGramsPer100g = -1m }).Reasons.Should().Contain("alcohol_negative");
            _v.Validate(wine with { AlcoholGramsPer100g = 120m }).Reasons.Should().Contain("alcohol_impossible");
            _v.Validate(new FoodQualityInput("Liqueur", 600m, 40m, 40m, 0m, AlcoholGramsPer100g: 40m)).Reasons.Should().Contain("macro_sum_impossible");
        }

        [Theory]
        [InlineData("Alcoholic Beverage, wine, table, red, Gamay", 78, 0.07, 2.38, 0, 9.8, null, 3.36, 8.37, 3.92, "own-factors")]
        [InlineData("Alcoholic beverage, distilled, vodka, 80 proof", 231, 0, 0, 0, 33.4, 0.0, null, null, null, "general")]
        [InlineData("Cocoa, dry powder, unsweetened", 228, 19.6, 57.9, 13.7, 0.0, 37.0, 1.83, 8.37, 1.33, "own-factors")]
        [InlineData("Oat bran, raw", 246, 17.3, 66.22, 7.03, 0.0, 15.4, 1.82, 8.37, 2.35, "own-factors")]
        [InlineData("Mustard greens, raw", 27, 2.86, 4.67, 0.42, 0.0, 3.2, 2.44, 8.37, 3.57, "own-factors")]
        [InlineData("Baobab powder", 250, 3.69, 79.65, 0.47, null, 44.5, 3.36, 8.37, 3.6, "atwater_mismatch")]
        [InlineData("Sweetener, herbal extract powder from Stevia leaf", 0, 0, 100, 0, 0.0, 0.0, null, null, null, "atwater_mismatch")]
        public void CheckEnergy_ConfirmsUsdaFoodsByTheirOwnFactors_OrTheFiberAwareFormula(
            string name, double kcal, double protein, double carb, double fat, double? alcohol, double? fiber,
            double? proteinFactor, double? fatFactor, double? carbFactor, string expected)
        {
            var food = new FoodQualityInput(name, (decimal)kcal, (decimal)protein, (decimal)carb, (decimal)fat,
                AlcoholGramsPer100g: (decimal?)alcohol, FiberGramsPer100g: (decimal?)fiber,
                CalorieFactors: CalorieFactors.Of((decimal?)proteinFactor, (decimal?)fatFactor, (decimal?)carbFactor));

            _v.CheckEnergy(food).Should().Be(expected);
            _v.Validate(food).Reasons.Contains("atwater_mismatch").Should().Be(expected == "atwater_mismatch");
        }

        [Fact]
        public void Cocoa_powder_failed_the_old_general_check_and_passes_by_its_own_factors()
        {
            var cocoa = new FoodQualityInput("Cocoa, dry powder, unsweetened", 228m, 19.6m, 57.9m, 13.7m, FiberGramsPer100g: 37m);

            _v.CheckEnergy(cocoa).Should().Be(EnergyCheckOutcome.Mismatch, "4·P + 4·(C − fiber) + 2·fiber + 9·F is 286 kcal against 228");
            _v.CheckEnergy(cocoa with { CalorieFactors = CalorieFactors.Of(1.83m, 8.37m, 1.33m) }).Should().Be(EnergyCheckOutcome.OwnFactors);
        }

        [Fact]
        public void The_general_check_counts_fiber_at_two_kcal_a_gram_and_holds_fifteen_percent()
        {
            var bran = new FoodQualityInput("Bran cereal", 290m, 13m, 74m, 3m, FiberGramsPer100g: 30m);

            _v.CheckEnergy(bran with { FiberGramsPer100g = null }).Should().Be(EnergyCheckOutcome.Mismatch, "4·13 + 4·74 + 9·3 = 375 is 29 % over, which the old ±30 % let through");
            _v.CheckEnergy(bran).Should().Be(EnergyCheckOutcome.General, "with fiber at 2 kcal/g it is 315, 9 % over");
        }

        [Fact]
        public void A_usda_energy_stated_by_plain_atwater_on_total_carbohydrate_passes_within_five_percent_and_no_further()
        {
            var nettles = new FoodQualityInput("Stinging Nettles, blanched (Northern Plains Indians)", 42m, 2.71m, 7.49m, 0.11m, FiberGramsPer100g: 6.9m);
            var cattail = new FoodQualityInput("Cattail, Narrow Leaf Shoots (Northern Plains Indians)", 25m, 1.18m, 5.14m, 0m, FiberGramsPer100g: 4.4m);

            _v.CheckEnergy(nettles).Should().Be(EnergyCheckOutcome.General, "4·2.71 + 4·7.49 + 9·0.11 is 41.8 kcal against USDA's 42, though the fiber-aware formula is 33 % low");
            _v.CheckEnergy(cattail).Should().Be(EnergyCheckOutcome.General);
            _v.CheckEnergy(nettles with { KcalPer100g = 46m }).Should().Be(EnergyCheckOutcome.Mismatch, "9 % off plain Atwater and 39 % off the fiber-aware formula");
            _v.CheckEnergy(new FoodQualityInput("Sweetener, herbal extract powder from Stevia leaf", 0m, 0m, 100m, 0m, FiberGramsPer100g: 0m)).Should().Be(EnergyCheckOutcome.Mismatch);
        }

        [Fact]
        public void A_foundation_food_is_compared_with_its_energy_by_specific_factors_when_USDA_states_it()
        {
            var apple = new FoodQualityInput("Apples, red delicious, with skin, raw", 62m, 0.19m, 14.8m, 0.21m,
                FiberGramsPer100g: 2m, CalorieFactors: CalorieFactors.Of(3.36m, 8.37m, 3.6m));

            _v.CheckEnergy(apple).Should().Be(EnergyCheckOutcome.Mismatch, "1008 is USDA's general figure; 55.7 kcal by own factors is 10 % under it");
            _v.CheckEnergy(apple with { SpecificKcalPer100g = 56m }).Should().Be(EnergyCheckOutcome.OwnFactors);
        }

        [Fact]
        public void Seasonings_leaveners_and_tiny_energies_are_exempt_and_say_so()
        {
            _v.CheckEnergy(new FoodQualityInput("Spices, pepper, black", 251m, 10.39m, 63.95m, 3.26m, UsedInSmallAmounts: true)).Should().Be(EnergyCheckOutcome.ExemptSmall);
            _v.CheckEnergy(new FoodQualityInput("Iceberg Lettuce", 14m, 0.9m, 3m, 0.1m)).Should().Be(EnergyCheckOutcome.ExemptSmall);
            _v.CheckEnergy(new FoodQualityInput("Mystery", null, 1m, 1m, 1m)).Should().BeNull();
        }

        [Fact]
        public void A_calorie_factor_of_zero_is_no_factor()
        {
            CalorieFactors.Of(0m, 0m, 0m).Should().BeNull("USDA ships 0/0/0 factor rows for some foods");
            CalorieFactors.Of(4m, 9m, null).Should().BeNull();
            CalorieFactors.Of(4m, 9m, 4m).Should().Be(new CalorieFactors(4m, 9m, 4m));
        }

        [Fact]
        public void CheckComponent_SkipsTheParentCheckWhenTheParentIsUnknown()
        {
            _v.CheckComponent(FoodComponent.SaturatedFat, 30m, fatGramsPer100g: null, carbGramsPer100g: null).Should().BeNull();
        }
    }
}
