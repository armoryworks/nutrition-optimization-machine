using System.Collections.Generic;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The reviewed lines of line-kinds.tsv against the deterministic pre-pass of line
    /// classification, and of veto.tsv against the veto on a label that takes a line out of the
    /// nutrition label. Both include look-alikes that must stay unanswered or stand.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLineKindTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenLineKindTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void The_deterministic_pre_pass_answers_only_the_reviewed_lines()
        {
            var report = GoldenSet.Run("line-kinds.tsv", "status", row =>
                GoldenSet.Compare(row, new Dictionary<string, string> { ["kind"] = GoldenSet.Cell(RecipeLineClassifier.Classify(row["raw"])) }));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void A_label_that_takes_a_food_out_of_the_recipe_is_refused_as_reviewed()
        {
            var curated = GoldenCatalog.Instance.CuratedNames;
            var report = GoldenSet.Run("veto.tsv", "status", row =>
            {
                var refusal = RecipeLineKindGuard.Refusal(row["kind"], decimal.Parse(row["quantity"], System.Globalization.CultureInfo.InvariantCulture),
                    row["raw"], row["onCurated"] == "yes", curated, row["wide"] == "yes");
                var actual = refusal ?? "-";
                var pass = row["expect"] == "refused" ? refusal != null : row["expect"] == actual;
                return new GoldenOutcome(pass, row["expect"], actual, new Dictionary<string, string> { ["expect"] = actual });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
