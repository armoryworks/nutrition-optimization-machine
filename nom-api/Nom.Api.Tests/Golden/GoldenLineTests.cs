using System.Collections.Generic;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The reviewed recipe lines of lines.tsv: what the parser reads as the amount, the size a
    /// container line states, and the wording and bare name the normaliser gives. The parse and
    /// name columns carry their own status, since a line is often right on one and wrong on the other.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLineTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenLineTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Every_reviewed_line_parses_to_its_quantity_and_measurement()
        {
            var report = GoldenSet.Run("lines.tsv", "parse", row =>
            {
                var parsed = IngredientLineParser.Parse(row["raw"]);
                var size = IngredientLineParser.StatedSize(row["raw"]);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["quantity"] = GoldenSet.Number(parsed?.Quantity),
                    ["measurement"] = GoldenSet.Cell(parsed?.MeasurementName),
                    ["size"] = size == null ? "-" : $"{GoldenSet.Number(size.Quantity)} {size.MeasurementName}",
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_reviewed_line_normalises_to_its_wording_and_bare_name()
        {
            var report = GoldenSet.Run("lines.tsv", "name", row =>
            {
                var wording = IngredientNameNormalizer.Normalize(row["raw"]);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["wording"] = wording,
                    ["bare"] = IngredientNameNormalizer.Bare(wording),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
