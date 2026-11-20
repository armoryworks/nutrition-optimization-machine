using System;
using System.Collections.Generic;
using System.Globalization;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The USDA foods and constructed cases of catalog-admission.tsv against the catalog admission
    /// rule: the status each lands in, its food group and edibility from its category, how its
    /// energy was confirmed, and why it was not curated. The rule needs no model, so its floor is
    /// every row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenCatalogAdmissionTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenCatalogAdmissionTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_food_is_admitted_held_or_rejected_as_the_rule_gives()
        {
            var report = GoldenSet.Run("catalog-admission.tsv", "status", row =>
            {
                var decision = CatalogAdmissionRule.Decide(Facts(row));
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectStatus"] = ((CurationStatusEnum)decision.Status).ToString(),
                    ["expectGroup"] = decision.FoodGroupId is long g ? ((FoodGroupEnum)g).ToString() : "-",
                    ["expectEdible"] = decision.IsWholeFood is bool e ? (e ? "true" : "false") : "-",
                    ["expectEnergy"] = GoldenSet.Cell(decision.EnergyCheck),
                    ["expectReason"] = GoldenSet.Cell(decision.Reason),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        private static AdmissionFacts Facts(Nom.Import.Golden.GoldenRow row)
        {
            decimal? Number(string column) => row.Value(column) is { } v ? decimal.Parse(v, CultureInfo.InvariantCulture) : null;
            CalorieFactors? factors = null;
            if (row.Value("factors") is { } text)
            {
                var parts = text.Split('/');
                factors = CalorieFactors.Of(decimal.Parse(parts[0], CultureInfo.InvariantCulture), decimal.Parse(parts[1], CultureInfo.InvariantCulture), decimal.Parse(parts[2], CultureInfo.InvariantCulture));
            }

            var nutrition = new FoodQualityInput(row["description"], Number("kcal"), Number("protein"), Number("carb"), Number("fat"),
                AlcoholGramsPer100g: Number("alcohol"), FiberGramsPer100g: Number("fiber"), CalorieFactors: factors, SpecificKcalPer100g: Number("specificKcal"));
            return new AdmissionFacts(row["dataType"],
                row.Value("categoryId") is { } id ? int.Parse(id, CultureInfo.InvariantCulture) : null,
                row.Value("category"),
                row["description"],
                nutrition,
                row.Value("duplicateOf") is { } dup ? long.Parse(dup, CultureInfo.InvariantCulture) : null);
        }
    }
}
