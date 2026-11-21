using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The constructed cases of sources.tsv against <see cref="SourceAdmissionPolicy.Judge"/>: the
    /// facts a source check returns (the scraper's verdict, rule version and reasons, the licence
    /// kind, the sample size, NOM's vetting and quantity counts, an earlier rejection) and the
    /// single verdict, whether it is conclusive, and the reasons that keep it from clear. Every
    /// scraper reason a row does not name passes (crawl-delay and sample-yield are information).
    /// The policy needs no model, so its floor is every row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenSourceAdmissionTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenSourceAdmissionTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_site_is_clear_only_when_every_fact_makes_it_plain()
        {
            var report = GoldenSet.Run("sources.tsv", "status", row =>
            {
                var judgement = SourceAdmissionPolicy.Judge(new SourceCheckFacts(
                    row.Value("scraperVerdict"),
                    row.Value("ruleVersion"),
                    row["complete"] == "yes",
                    Reasons(row.Value("reasons")),
                    row.Value("licenceKind"),
                    Int(row["sample"]),
                    Int(row["vetChecked"]),
                    Int(row["vetPassed"]),
                    Int(row["lines"]),
                    Int(row["linesFine"]),
                    row["rejectedBefore"] == "yes"));
                var against = judgement.Against.Select(r => r.Code).ToList();
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectVerdict"] = judgement.Verdict,
                    ["expectConclusive"] = judgement.Conclusive ? "yes" : "no",
                    ["expectAgainst"] = against.Count == 0 ? "-" : string.Join(";", against),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        private static int Int(string cell) => int.Parse(cell, CultureInfo.InvariantCulture);

        private static List<SourceReason> Reasons(string? cell)
        {
            var given = (cell ?? string.Empty).Split(';', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(':'))
                .ToDictionary(p => p[0] == "missing" ? p[1] : p[0], p => p[0] == "missing" ? null : p[1]);
            return SourceAdmissionPolicy.ScraperReasons
                .Where(code => !(given.TryGetValue(code, out var outcome) && outcome == null))
                .Select(code => new SourceReason(code,
                    given.TryGetValue(code, out var outcome) ? outcome! : code is "crawl-delay" or "sample-yield" ? SourceAdmissionPolicy.Info : SourceAdmissionPolicy.Pass,
                    "constructed"))
                .ToList();
        }
    }
}
