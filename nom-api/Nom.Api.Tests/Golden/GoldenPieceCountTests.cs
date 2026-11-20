using System.Collections.Generic;
using System.Globalization;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The reviewed quotes of piece-counts.tsv against the rule that decides whether a count in
    /// pieces read from a quote stands (the repair lane's fills and the revert of old ones): "a" or
    /// "an" before one countable item is a count, before a vague amount, a preparation or a utensil
    /// it is not.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenPieceCountTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenPieceCountTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void A_count_in_pieces_stands_or_is_reverted_as_reviewed()
        {
            var report = GoldenSet.Run("piece-counts.tsv", "status", row =>
            {
                decimal? gramsPerItem = row["gramsPerItem"] == "-" ? null : decimal.Parse(row["gramsPerItem"], CultureInfo.InvariantCulture);
                var actual = RecipeRepairGrounding.PieceRefusal(row["quote"], gramsPerItem) == null ? "keep" : "revert";
                return GoldenSet.Compare(row, new Dictionary<string, string> { ["expect"] = actual });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
