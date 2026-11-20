using System.Collections.Generic;
using System.Linq;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The rewrite fidelity check on public-domain recipes of prose.tsv, each turned into a
    /// rewrite whose fault is known by construction: the source given back unchanged, the source
    /// without a step that alone states a number, and the source with a number it never states.
    /// The check must fail each with at least the reason the construction gives.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenProseTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenProseTests(ITestOutputHelper output) => _output = output;

        private static RewriteFidelity.SourceText Rewrite(RewriteFidelity.SourceText source, string variant, GoldenRow row)
        {
            if (variant == "identical") return source;
            if (variant == "given")
                return new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty);
            if (variant == "add-number")
                return source with { Steps = source.Steps.Take(source.Steps.Count - 1).Append(source.Steps[^1] + " Rest it 437 minutes.").ToList() };
            var drop = int.Parse(variant["drop-step-".Length..]) - 1;
            return source with { Steps = source.Steps.Where((_, i) => i != drop).ToList() };
        }

        [Fact]
        public void Each_constructed_rewrite_fails_the_fidelity_check_for_its_known_fault()
        {
            var report = GoldenSet.Run("prose.tsv", "status", row =>
            {
                var source = new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty);
                var finding = RewriteFidelity.Check(source, Rewrite(source, row["variant"], row));
                var reasons = finding.Reasons;
                var pass = row["expect"] == "passed" ? finding.Passed : reasons.Contains(row["expect"]);
                return new GoldenOutcome(pass, row["expect"], finding.ToString(),
                    new Dictionary<string, string> { ["expect"] = finding.Passed ? "passed" : reasons.FirstOrDefault() ?? GoldenTsv.NoAnswer });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
