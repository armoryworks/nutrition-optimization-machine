using System.Collections.Generic;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>The reviewed ingredient wordings of wordings.tsv: their normalised and bare names, and whether each is residue.</summary>
    [Trait("Category", "Golden")]
    public class GoldenWordingTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenWordingTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Every_reviewed_wording_keeps_its_normalised_and_bare_name_and_residue_flag()
        {
            var report = GoldenSet.Run("wordings.tsv", "status", row =>
            {
                var normalized = IngredientNameNormalizer.Normalize(row["name"]);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["normalized"] = normalized,
                    ["bare"] = IngredientNameNormalizer.Bare(normalized),
                    ["residue"] = IngredientNameNormalizer.IsResidue(row["name"]) ? "yes" : "no",
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
