using System;
using System.Collections.Generic;
using System.Globalization;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The reviewed lines of line-guard.tsv against the line guard: each stored row and the row a rule read are
    /// judged with the line and its method, and the verdict and reason are compared.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLineGuardTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenLineGuardTests(ITestOutputHelper output) => _output = output;

        private static LineAmountGuard.Amount Amount(GoldenRow row, string column, bool fromMethod)
        {
            var parts = row[column] == GoldenTsv.NoAnswer ? null : row[column].Split(' ', 2);
            var quantity = parts == null ? 0m : decimal.Parse(parts[0], CultureInfo.InvariantCulture);
            return new LineAmountGuard.Amount(quantity, parts?[1], row.Value("kind"), row["food"], row["pieces"] == "yes" ? 50m : null, fromMethod && quantity > 0);
        }

        private static string Judge(GoldenRow row)
        {
            var steps = row["steps"] == GoldenTsv.NoAnswer ? Array.Empty<string>() : row["steps"].Split(" ¦ ");
            var decision = LineAmountGuard.Judge(row["line"], Amount(row, "stored", false), Amount(row, "rebuilt", row["from"] == "method"), steps);
            return $"{decision.Verdict.ToString().ToLowerInvariant()} {decision.Reason}";
        }

        [Fact]
        public void Each_reviewed_line_gets_the_guards_verdict_as_reviewed()
        {
            var report = GoldenSet.Run("line-guard.tsv", "status", row => GoldenSet.Compare(row, new Dictionary<string, string> { ["expect"] = Judge(row) }));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
