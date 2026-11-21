using System.Collections.Generic;
using System.Linq;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The constructed statements of prepared-contents.tsv against the ingredient statement reader: whether each is read,
    /// why not, the named ingredients with their parents, and the declared and warned allergens. The reader needs no
    /// model, so its floor is every row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenPreparedContentsTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenPreparedContentsTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_statement_is_read_as_the_rule_gives()
        {
            var report = GoldenSet.Run("prepared-contents.tsv", "status", row =>
            {
                var reading = IngredientStatementReader.Read(row["statement"]);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectRead"] = reading.Read ? "yes" : "no",
                    ["expectReason"] = GoldenSet.Cell(reading.Reason?.Split(':')[0]),
                    ["expectIngredients"] = Joined(reading.Ingredients.Select(i => (i.Parent == null ? string.Empty : i.Parent + ">") + i.Name + (i.HasParts ? "+" : string.Empty))),
                    ["expectDeclared"] = Joined(reading.Declared.Select(Allergen)),
                    ["expectAdvisory"] = Joined(reading.Advisory.Select(Allergen)),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        private static string Allergen(StatementAllergen a) => a.Word + "/" + (a.Class ?? "-");

        private static string Joined(IEnumerable<string> parts)
        {
            var list = parts.ToList();
            return list.Count == 0 ? "-" : string.Join('|', list);
        }
    }
}
