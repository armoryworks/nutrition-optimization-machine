using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The constructed cases of branded-lift.tsv against the branded branch of the catalog admission
    /// rule: how many sources agree with a branded food's stored values (its FDC record counting as
    /// one), how many disagree, and whether the food is lifted to Curated or held and why. The rule
    /// needs no model, so its floor is every row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenBrandedLiftTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenBrandedLiftTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_branded_food_is_lifted_or_held_as_its_agreeing_sources_give()
        {
            var report = GoldenSet.Run("branded-lift.tsv", "status", row =>
            {
                var stored = new BrandedStoredValues(Number(row.Value("kcal")), Number(row.Value("protein")), Number(row.Value("carb")), Number(row.Value("fat")), Number(row.Value("servingGrams")));
                var agreement = BrandedSourceAgreement.Judge(stored, Readings(row.Value("sources")));
                var nutrition = new FoodQualityInput(row["description"], stored.Kcal, stored.Protein, stored.Carb, stored.Fat);
                var decision = CatalogAdmissionRule.Decide(new AdmissionFacts(CatalogAdmissionRule.BrandedFood, null, row.Value("category"), row["description"], nutrition,
                    AgreeingSources: agreement.Agreeing, DisagreeingSources: agreement.Disagreeing));
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectAgreeing"] = agreement.Agreeing.ToString(CultureInfo.InvariantCulture),
                    ["expectDisagreeing"] = agreement.Disagreeing.ToString(CultureInfo.InvariantCulture),
                    ["expectStatus"] = ((CurationStatusEnum)decision.Status).ToString(),
                    ["expectReason"] = GoldenSet.Cell(decision.Reason),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        private static decimal? Number(string? value) => value == null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);

        private static SourceValue? Value(string cell) =>
            cell == "-" ? null
            : cell.StartsWith('<') ? new SourceValue(null, decimal.Parse(cell[1..], CultureInfo.InvariantCulture))
            : new SourceValue(decimal.Parse(cell, CultureInfo.InvariantCulture));

        private static IEnumerable<BrandedSourceReading> Readings(string? cell) =>
            (cell ?? string.Empty).Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(r =>
            {
                var p = r.Split('|');
                return new BrandedSourceReading(p[0], p[1], Number(p[2] == "-" ? null : p[2]), Value(p[3]), Value(p[4]), Value(p[5]), Value(p[6]));
            });
    }
}
