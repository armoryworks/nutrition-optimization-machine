using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    /// <summary>
    /// The label parser must report what is printed and nothing more: per-serving amounts as
    /// read, per-100 g only from an explicit gram weight, no volume-to-mass guesses, no amounts
    /// invented from a %DV. Fixtures are OCR-shaped texts of real label layouts, some noisy.
    /// </summary>
    public class NutritionFactsParserTests
    {
        private static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NutritionLabels", name));

        private static NutrientReading Reading(NutritionFactsParseResult result, string key) =>
            result.Nutrients.Should().ContainSingle(n => n.Key == key, $"expected one '{key}' reading").Subject;

        [Fact]
        public void Fda2020Cereal_ReadsEveryLineAndDerivesPer100gFromGrams()
        {
            var r = NutritionFactsParser.Parse(Fixture("fda-2020-cereal.txt"));

            r.Parsed.Should().BeTrue();
            r.Basis.Should().Be(NutritionBasis.Serving);
            r.ServingsPerContainer.Should().Be(8);
            r.ServingsPerContainerApprox.Should().BeFalse();
            r.ServingText.Should().Be("2/3 cup");
            r.ServingGrams.Should().Be(55);
            r.ServingMl.Should().BeNull();
            r.Calories.Should().Be(230);
            r.DualColumn.Should().BeFalse();

            Reading(r, "fat").Should().BeEquivalentTo(new { Amount = 8m, Unit = "g", PercentDailyValue = 10 });
            Reading(r, "saturated_fat").Should().BeEquivalentTo(new { Amount = 1m, Unit = "g", PercentDailyValue = 5 });
            Reading(r, "trans_fat").Should().BeEquivalentTo(new { Amount = 0m, Unit = "g", PercentDailyValue = (int?)null });
            Reading(r, "cholesterol").Should().BeEquivalentTo(new { Amount = 0m, Unit = "mg", PercentDailyValue = 0 });
            Reading(r, "sodium").Should().BeEquivalentTo(new { Amount = 160m, Unit = "mg", PercentDailyValue = 7 });
            Reading(r, "carbohydrate").Should().BeEquivalentTo(new { Amount = 37m, Unit = "g", PercentDailyValue = 13 });
            Reading(r, "fiber").Should().BeEquivalentTo(new { Amount = 4m, Unit = "g", PercentDailyValue = 14 });
            Reading(r, "sugars").Should().BeEquivalentTo(new { Amount = 12m, Unit = "g", PercentDailyValue = (int?)null });
            Reading(r, "added_sugars").Should().BeEquivalentTo(new { Amount = 10m, Unit = "g", PercentDailyValue = 20 });
            Reading(r, "protein").Should().BeEquivalentTo(new { Amount = 3m, Unit = "g" });
            Reading(r, "vitamin_d").Should().BeEquivalentTo(new { Amount = 2m, Unit = "mcg", PercentDailyValue = 10 });
            Reading(r, "calcium").Should().BeEquivalentTo(new { Amount = 260m, Unit = "mg", PercentDailyValue = 20 });
            Reading(r, "iron").Should().BeEquivalentTo(new { Amount = 8m, Unit = "mg", PercentDailyValue = 45 });
            Reading(r, "potassium").Should().BeEquivalentTo(new { Amount = 240m, Unit = "mg", PercentDailyValue = 6 });
            r.Nutrients.Should().HaveCount(14);
            r.FieldsRecognised.Should().Be(15);

            r.Per100g.Should().NotBeNull();
            r.Per100g!.Calories.Should().Be(418.18m);
            r.Per100g.Nutrients.Single(n => n.Key == "fat").Amount.Should().Be(14.55m);
            r.Per100g.Nutrients.Single(n => n.Key == "sodium").Should().BeEquivalentTo(new { Amount = 290.91m, Unit = "mg" });
            r.Warnings.Should().BeEmpty();
            r.Confidence.Should().Be(1m);
        }

        [Fact]
        public void Pre2020Label_TakesCaloriesNotCaloriesFromFat_AndKeepsMicronutrientsAsPercentOnly()
        {
            var r = NutritionFactsParser.Parse(Fixture("pre-2020-calories-from-fat.txt"));

            r.Calories.Should().Be(250);
            r.ServingGrams.Should().Be(228);
            r.ServingText.Should().Be("1 cup");
            r.ServingsPerContainer.Should().Be(2);
            Reading(r, "fat").Should().BeEquivalentTo(new { Amount = 12m, PercentDailyValue = 18 });
            Reading(r, "trans_fat").Amount.Should().Be(3);
            Reading(r, "sugars").Amount.Should().Be(5);
            Reading(r, "vitamin_a").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 4 });
            Reading(r, "vitamin_c").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 2 });
            Reading(r, "calcium").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 20 });
            Reading(r, "iron").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 4 });
            r.Per100g!.Nutrients.Should().NotContain(n => n.Key == "calcium");
        }

        [Fact]
        public void DualColumn_TakesTheFirstColumnAsPerServing()
        {
            var r = NutritionFactsParser.Parse(Fixture("dual-column-per-container.txt"));

            r.DualColumn.Should().BeTrue();
            r.SecondColumnLabel.Should().Be("Per container");
            r.Calories.Should().Be(140);
            r.ServingGrams.Should().Be(140);
            Reading(r, "fat").Should().BeEquivalentTo(new { Amount = 5m, PercentDailyValue = 6 });
            Reading(r, "saturated_fat").Should().BeEquivalentTo(new { Amount = 2m, PercentDailyValue = 10 });
            Reading(r, "trans_fat").Amount.Should().Be(0);
            Reading(r, "cholesterol").Should().BeEquivalentTo(new { Amount = 15m, Unit = "mg", PercentDailyValue = 5 });
            Reading(r, "carbohydrate").Should().BeEquivalentTo(new { Amount = 17m, PercentDailyValue = 6 });
            Reading(r, "sugars").Should().BeEquivalentTo(new { Amount = 7m, PercentDailyValue = (int?)null });
            Reading(r, "added_sugars").Should().BeEquivalentTo(new { Amount = 4m, PercentDailyValue = 8 });
            Reading(r, "protein").Amount.Should().Be(9);
            Reading(r, "potassium").Should().BeEquivalentTo(new { Amount = 470m, PercentDailyValue = 10 });
            r.Nutrients.Should().HaveCount(14);
        }

        [Fact]
        public void EuPer100gTable_SetsBasisAndConvertsSaltToSodiumWithWarning()
        {
            var r = NutritionFactsParser.Parse(Fixture("eu-per-100g.txt"));

            r.Basis.Should().Be(NutritionBasis.Per100g);
            r.Calories.Should().Be(250);
            r.ServingGrams.Should().BeNull();
            Reading(r, "fat").Amount.Should().Be(8.0m);
            Reading(r, "saturated_fat").Amount.Should().Be(1.0m);
            Reading(r, "carbohydrate").Amount.Should().Be(37);
            Reading(r, "sugars").Amount.Should().Be(12);
            Reading(r, "fiber").Amount.Should().Be(4);
            Reading(r, "protein").Amount.Should().Be(3);
            Reading(r, "sodium").Should().BeEquivalentTo(new { Amount = 160m, Unit = "mg" });
            r.Nutrients.Should().NotContain(n => n.Key == "salt");
            r.Warnings.Should().Contain("sodium_derived_from_salt");
            r.Per100g!.Calories.Should().Be(250);
            r.Per100g.Nutrients.Single(n => n.Key == "fat").Amount.Should().Be(8.0m);
        }

        [Fact]
        public void OcrNoise_IsRepairedInsideNumbersAndLabels()
        {
            var r = NutritionFactsParser.Parse(Fixture("ocr-noise.txt"));

            r.Calories.Should().Be(230);
            Reading(r, "fat").Should().BeEquivalentTo(new { Amount = 8m, PercentDailyValue = 10 });
            Reading(r, "saturated_fat").Should().BeEquivalentTo(new { Amount = 1m, PercentDailyValue = 5 });
            Reading(r, "trans_fat").Amount.Should().Be(0);
            Reading(r, "cholesterol").Should().BeEquivalentTo(new { Amount = 0m, Unit = "mg", PercentDailyValue = 0 });
            Reading(r, "sodium").Should().BeEquivalentTo(new { Amount = 160m, Unit = "mg", PercentDailyValue = 7 });
            Reading(r, "added_sugars").Should().BeEquivalentTo(new { Amount = 10m, PercentDailyValue = 20 });
            Reading(r, "protein").Amount.Should().Be(3);
            Reading(r, "vitamin_d").Should().BeEquivalentTo(new { Amount = 2m, Unit = "mcg", PercentDailyValue = 10 });
            Reading(r, "calcium").Should().BeEquivalentTo(new { Amount = 260m, Unit = "mg", PercentDailyValue = 20 });
            r.Nutrients.Should().HaveCount(14);
            r.Warnings.Should().BeEmpty();
        }

        [Fact]
        public void ValuesOnTheNextLine_LessThan_AndNotSignificantSource()
        {
            var r = NutritionFactsParser.Parse(Fixture("cookies-values-on-next-line.txt"));

            r.ServingsPerContainer.Should().Be(12);
            r.ServingsPerContainerApprox.Should().BeTrue();
            r.ServingText.Should().Be("3 cookies");
            r.ServingGrams.Should().Be(34);
            r.Calories.Should().Be(160);
            Reading(r, "fat").Should().BeEquivalentTo(new { Amount = 7m, PercentDailyValue = 9 });
            Reading(r, "saturated_fat").Should().BeEquivalentTo(new { Amount = 2m, PercentDailyValue = 10 });
            Reading(r, "cholesterol").Should().BeEquivalentTo(new { Amount = 0m, PercentDailyValue = 0 });
            Reading(r, "sodium").Should().BeEquivalentTo(new { Amount = 110m, PercentDailyValue = 5 });
            Reading(r, "carbohydrate").Should().BeEquivalentTo(new { Amount = 25m, PercentDailyValue = 9 });
            Reading(r, "fiber").Should().BeEquivalentTo(new { Amount = (decimal?)null, LessThan = 1m, PercentDailyValue = 3 });
            Reading(r, "sugars").Amount.Should().Be(13);
            Reading(r, "added_sugars").Should().BeEquivalentTo(new { Amount = 12m, PercentDailyValue = 24 });
            Reading(r, "protein").Amount.Should().Be(1);
            r.NotSignificantSource.Should().BeEquivalentTo(new[] { "vitamin_d", "calcium", "iron", "potassium" });
            r.Nutrients.Should().NotContain(n => n.Key == "vitamin_d");
            r.Per100g!.Nutrients.Should().NotContain(n => n.Key == "fiber");
        }

        [Fact]
        public void BeverageWithOnlyMillilitres_NeverAssumesOneGramPerMillilitre()
        {
            var r = NutritionFactsParser.Parse(Fixture("beverage-ml-only.txt"));

            r.ServingsPerContainer.Should().Be(1);
            r.ServingsPerContainerApprox.Should().BeFalse();
            r.ServingText.Should().Be("8 fl oz");
            r.ServingMl.Should().Be(240);
            r.ServingGrams.Should().BeNull();
            r.Calories.Should().Be(110);
            Reading(r, "added_sugars").Should().BeEquivalentTo(new { Amount = 24m, PercentDailyValue = 48 });
            r.Per100g.Should().BeNull();
            r.Warnings.Should().Contain("serving_weight_needed_for_per_100g");
            r.NotSignificantSource.Should().BeEquivalentTo(new[]
            {
                "saturated_fat", "trans_fat", "cholesterol", "fiber", "vitamin_d", "calcium", "iron", "potassium",
            });
            r.Parsed.Should().BeTrue();
        }

        [Fact]
        public void LinearSmallPackageLabel_SplitsOneLineIntoItsNutrients()
        {
            var r = NutritionFactsParser.Parse(Fixture("linear-small-package.txt"));

            r.ServingText.Should().Be("1 package");
            r.ServingGrams.Should().Be(28);
            r.ServingsPerContainer.Should().Be(1);
            r.Calories.Should().Be(150);
            Reading(r, "fat").Should().BeEquivalentTo(new { Amount = 9m, PercentDailyValue = 12 });
            Reading(r, "saturated_fat").Should().BeEquivalentTo(new { Amount = 1.5m, PercentDailyValue = 8 });
            Reading(r, "trans_fat").Amount.Should().Be(0);
            Reading(r, "cholesterol").Should().BeEquivalentTo(new { Amount = 0m, Unit = "mg", PercentDailyValue = 0 });
            Reading(r, "sodium").Should().BeEquivalentTo(new { Amount = 170m, PercentDailyValue = 7 });
            Reading(r, "carbohydrate").Should().BeEquivalentTo(new { Amount = 15m, PercentDailyValue = 5 });
            Reading(r, "fiber").Should().BeEquivalentTo(new { Amount = 1m, PercentDailyValue = 4 });
            Reading(r, "sugars").Amount.Should().Be(1);
            Reading(r, "added_sugars").Should().BeEquivalentTo(new { Amount = 0m, PercentDailyValue = 0 });
            Reading(r, "protein").Amount.Should().Be(2);
            Reading(r, "vitamin_d").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 0 });
            Reading(r, "calcium").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 0 });
            Reading(r, "iron").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 2 });
            Reading(r, "potassium").Should().BeEquivalentTo(new { Amount = (decimal?)null, PercentDailyValue = 2 });
            r.DualColumn.Should().BeFalse();
        }

        [Fact]
        public void AsPackagedAsPrepared_UsesTheFirstColumnAndNamesTheSecond()
        {
            var r = NutritionFactsParser.Parse(Fixture("as-packaged-as-prepared.txt"));

            r.DualColumn.Should().BeTrue();
            r.SecondColumnLabel.Should().Be("As Prepared");
            r.ServingText.Should().Be("1/4 cup mix");
            r.ServingGrams.Should().Be(35);
            r.ServingsPerContainer.Should().Be(10);
            r.ServingsPerContainerApprox.Should().BeTrue();
            r.Calories.Should().Be(130);
            Reading(r, "fat").Should().BeEquivalentTo(new { Amount = 1.5m, PercentDailyValue = 2 });
            Reading(r, "saturated_fat").Should().BeEquivalentTo(new { Amount = 0m, PercentDailyValue = 0 });
            Reading(r, "sodium").Should().BeEquivalentTo(new { Amount = 290m, PercentDailyValue = 13 });
            Reading(r, "carbohydrate").Should().BeEquivalentTo(new { Amount = 26m, PercentDailyValue = 9 });
            Reading(r, "sugars").Amount.Should().Be(11);
            Reading(r, "protein").Amount.Should().Be(2);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   \n\n  ")]
        [InlineData("Best before 2027-01-01\nKeep refrigerated")]
        public void NothingRecognisable_IsNotParsed(string text)
        {
            var r = NutritionFactsParser.Parse(text);
            r.Parsed.Should().BeFalse();
            r.Basis.Should().Be(NutritionBasis.Unknown);
            r.Nutrients.Should().BeEmpty();
            r.Calories.Should().BeNull();
            r.Per100g.Should().BeNull();
        }

        [Fact]
        public void CaloriesBeforeTheWord_AndServingForms()
        {
            NutritionFactsParser.Parse("230 calories").Calories.Should().Be(230);

            var grams = NutritionFactsParser.Parse("Serving size 30g\nCalories 100");
            grams.ServingGrams.Should().Be(30);
            grams.ServingText.Should().Be("30 g");
            grams.Per100g!.Calories.Should().Be(333.33m);

            var ounces = NutritionFactsParser.Parse("Serving Size (2 oz/56g)");
            ounces.ServingGrams.Should().Be(56);

            var ml = NutritionFactsParser.Parse("Serving size 240mL\nCalories 90");
            ml.ServingMl.Should().Be(240);
            ml.ServingGrams.Should().BeNull();
            ml.ServingText.Should().Be("240 mL");
            ml.Per100g.Should().BeNull();
        }

        [Fact]
        public void KilojoulesOnly_AreConvertedWithAWarning()
        {
            var r = NutritionFactsParser.Parse("Energy 1046kJ\nFat 8g");
            r.Calories.Should().Be(250);
            r.Warnings.Should().Contain("calories_converted_from_kj");
        }

        [Fact]
        public void IngredientListAndFooter_AreIgnored()
        {
            var r = NutritionFactsParser.Parse("Total Fat 8g 10%\n* The % Daily Value (DV) tells you how much a nutrient contributes. 2,000 calories a day.\nINGREDIENTS: SUGAR, SALT, PROTEIN ISOLATE\nSodium 999g");
            r.Nutrients.Should().ContainSingle().Which.Key.Should().Be("fat");
            r.Calories.Should().BeNull();
        }

        [Fact]
        public void InconsistentValues_ProduceWarningsNotRejections()
        {
            var r = NutritionFactsParser.Parse("Serving size 1 cup (100g)\nCalories 950\nTotal Fat 3g\nSaturated Fat 5g\nTotal Carbohydrate 10g\nDietary Fiber 8g\nTotal Sugars 9g\nIncludes 12g Added Sugars\nProtein 2g");
            r.Parsed.Should().BeTrue();
            r.Warnings.Should().Contain("saturated_plus_trans_exceeds_fat");
            r.Warnings.Should().Contain("fiber_plus_sugars_exceeds_carbohydrate");
            r.Warnings.Should().Contain("added_sugars_exceeds_sugars");
            r.Warnings.Should().Contain("per100g_calories_impossible");
            r.Warnings.Should().Contain("atwater_mismatch");
            r.Nutrients.Should().HaveCount(7);
        }

        [Fact]
        public void Assess_RecomputesPer100gAfterAUserSuppliesTheServingWeight()
        {
            var r = NutritionFactsParser.Parse("Serving size 1 bar\nCalories 200\nTotal Fat 10g\nTotal Carbohydrate 20g\nProtein 5g");
            r.Per100g.Should().BeNull();
            r.Warnings.Should().Contain("serving_weight_needed_for_per_100g");

            r.ServingGrams = 50;
            NutritionFactsParser.Assess(r);
            r.Per100g!.Calories.Should().Be(400);
            r.Per100g.Nutrients.Single(n => n.Key == "fat").Amount.Should().Be(20);
            r.Warnings.Should().NotContain("serving_weight_needed_for_per_100g");
        }
    }
}
