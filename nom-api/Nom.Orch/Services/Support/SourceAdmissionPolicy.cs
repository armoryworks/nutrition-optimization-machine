using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services.Support
{
    /// <summary>One reason of a source verdict: a check, its outcome (<c>pass</c>, <c>fail</c>, <c>unknown</c>, <c>info</c>) and what was found.</summary>
    public sealed record SourceReason(string Code, string Outcome, string? Detail);

    /// <summary>
    /// The facts a source verdict is drawn from: the scraper's report as it came back, and what
    /// NOM's own vetting made of the sample recipes. Built from a report by <see cref="SourceAdmissionPolicy.Facts"/>.
    /// </summary>
    public sealed record SourceCheckFacts(
        string? ScraperVerdict,
        string? RuleVersion,
        bool Complete,
        IReadOnlyList<SourceReason> Reasons,
        string? LicenceKind,
        int SampleRequested,
        int VettingChecked,
        int VettingPassed,
        int LinesTotal,
        int LinesFine,
        bool RejectedBefore);

    /// <summary>
    /// The single verdict: <c>clear</c> or <c>not-clear</c>. <see cref="Conclusive"/> is false when
    /// the check did not finish or came under a rule version NOM does not read; such a not-clear
    /// only says "check again" and never changes what is harvested.
    /// </summary>
    public sealed record SourceJudgement(string Verdict, bool Conclusive, IReadOnlyList<SourceReason> Reasons)
    {
        public bool Clear => Verdict == SourceAdmissionPolicy.Clear;

        /// <summary>The reasons that keep the verdict from clear.</summary>
        public IEnumerable<SourceReason> Against => Reasons.Where(r => r.Outcome is SourceAdmissionPolicy.Fail or SourceAdmissionPolicy.Unknown);
    }

    /// <summary>
    /// Whether a site is clearly usable without ambiguity (owner, 2026-11-20: the approval is a
    /// rubber stamp only for such a site). A pure function of the facts, no model: clear only when
    /// the scraper's check is complete under a rule version this policy knows, the scraper itself
    /// says clear, every one of its reasons passed or is information, the licence is one that allows
    /// commercial reuse and adaptation with at most attribution, the sample was the full 20 pages,
    /// NOM's own vetting passes 18 of them, 85 % of their ingredient lines carry a quantity or need
    /// none, and no admin rejected the domain before. Anything missing or unreadable counts against.
    /// </summary>
    public static class SourceAdmissionPolicy
    {
        public const string Version = "source-admission/2";
        public const string ScraperRuleVersion = "source-check/4";
        public const string EarlierScraperRuleVersion = "source-check/2";
        public const string Clear = "clear";
        public const string NotClear = "not-clear";
        public const string Pass = "pass";
        public const string Fail = "fail";
        public const string Unknown = "unknown";
        public const string Info = "info";
        public const int SampleSize = 20;
        public const int TermsOnlySampleSize = 0;
        public const int NeededOfSample = 18;
        public const double LinesFineShare = 0.85;

        /// <summary>
        /// The scraper rule versions whose reports NOM reads: the current one, and the earlier one whose reports may
        /// still be stored. A new check is asked under the current one only; a stored report under the earlier one
        /// still gives its facts, and its source or candidate is due to be checked again.
        /// </summary>
        public static readonly IReadOnlySet<string> ReadableRuleVersions = new HashSet<string>(StringComparer.Ordinal)
        {
            ScraperRuleVersion, EarlierScraperRuleVersion,
        };

        /// <summary>The scraper's reasons under <see cref="ScraperRuleVersion"/> (the same codes as under the earlier version), every one of which must be present.</summary>
        public static readonly IReadOnlyList<string> ScraperReasons = new[]
        {
            "check-complete", "https", "bot-challenge", "robots-nombot", "robots-general", "robots-ai", "crawl-delay",
            "reservation", "terms", "licence", "credits", "enumerable", "structured-data", "free-to-read", "language", "sample-yield",
        };

        /// <summary>Licence kinds that allow commercial reuse and adaptation with no condition beyond attribution.</summary>
        public static readonly IReadOnlySet<string> ClearLicences = new HashSet<string>(StringComparer.Ordinal)
        {
            "cc0", "public-domain-mark", "public-domain", "cc-by",
        };

        /// <summary>Licence kinds under which a recipe imported by hand may be recorded as public domain.</summary>
        public static readonly IReadOnlySet<string> PublicDomainLicences = new HashSet<string>(StringComparer.Ordinal)
        {
            "cc0", "public-domain-mark", "public-domain",
        };

        public static SourceJudgement Judge(SourceCheckFacts facts)
        {
            var reasons = new List<SourceReason>();
            var byCode = new Dictionary<string, SourceReason>(StringComparer.Ordinal);
            foreach (var reason in facts.Reasons)
            {
                byCode.TryAdd(reason.Code, reason);
            }

            var knownRules = facts.RuleVersion != null && ReadableRuleVersions.Contains(facts.RuleVersion);
            reasons.Add(new SourceReason("rule-version", knownRules ? Pass : Unknown,
                knownRules ? $"report under {facts.RuleVersion}" : $"report under {facts.RuleVersion ?? "no rule version"}; this policy reads {ScraperRuleVersion} and {EarlierScraperRuleVersion} only"));

            foreach (var code in ScraperReasons)
            {
                reasons.Add(byCode.TryGetValue(code, out var given)
                    ? given with { Outcome = Normal(given.Outcome) }
                    : new SourceReason(code, Unknown, "missing from the report"));
            }

            foreach (var extra in facts.Reasons.Where(r => !ScraperReasons.Contains(r.Code)).GroupBy(r => r.Code).Select(g => g.First()))
            {
                reasons.Add(extra with { Outcome = Normal(extra.Outcome) });
            }

            reasons.Add(facts.ScraperVerdict == Clear
                ? new SourceReason("scraper-verdict", Pass, "the scraper says clear")
                : new SourceReason("scraper-verdict", Fail, $"the scraper says {facts.ScraperVerdict ?? "nothing"}"));

            reasons.Add(facts.LicenceKind != null && ClearLicences.Contains(facts.LicenceKind)
                ? new SourceReason("licence-kind", Pass, $"{facts.LicenceKind} allows commercial reuse and adaptation")
                : new SourceReason("licence-kind", Fail, facts.LicenceKind == null
                    ? "no licence found: all rights are reserved by default"
                    : $"{facts.LicenceKind} is not a licence that allows commercial reuse and adaptation without further conditions"));

            reasons.Add(facts.SampleRequested == SampleSize
                ? new SourceReason("sample-size", Pass, $"{SampleSize} pages sampled")
                : new SourceReason("sample-size", Unknown, $"{facts.SampleRequested} pages sampled; a verdict needs {SampleSize}"));

            reasons.Add(facts.VettingPassed >= NeededOfSample
                ? new SourceReason("nom-vetting", Pass, $"{facts.VettingPassed} of {facts.VettingChecked} sample recipes pass NOM's vetting")
                : facts.VettingChecked >= NeededOfSample
                    ? new SourceReason("nom-vetting", Fail, $"{facts.VettingPassed} of {facts.VettingChecked} sample recipes pass NOM's vetting; {NeededOfSample} must")
                    : new SourceReason("nom-vetting", Unknown, $"only {facts.VettingChecked} sample recipes to vet; {NeededOfSample} must pass"));

            var share = facts.LinesTotal == 0 ? 0 : facts.LinesFine / (double)facts.LinesTotal;
            reasons.Add(facts.LinesTotal == 0
                ? new SourceReason("nom-quantities", Unknown, "no sample ingredient lines")
                : share >= LinesFineShare
                    ? new SourceReason("nom-quantities", Pass, $"{facts.LinesFine} of {facts.LinesTotal} ingredient lines carry a quantity or need none")
                    : new SourceReason("nom-quantities", Fail, $"{facts.LinesFine} of {facts.LinesTotal} ingredient lines carry a quantity or need none; 85 % must"));

            reasons.Add(facts.RejectedBefore
                ? new SourceReason("rejected-before", Fail, "an admin rejected this domain; that decision is final")
                : new SourceReason("rejected-before", Pass, "never rejected"));

            var conclusive = knownRules && facts.Complete;
            var clear = conclusive && reasons.All(r => r.Outcome is Pass or Info);
            return new SourceJudgement(clear ? Clear : NotClear, conclusive, reasons);
        }

        /// <summary>The facts of a report, with NOM's vetting of its sample recipes (passed of checked).</summary>
        public static SourceCheckFacts Facts(ScraperSourceCheckReport report, int vettingChecked, int vettingPassed, bool rejectedBefore)
        {
            var lines = report.Samples?.Pages
                .Where(p => p.Recipe != null)
                .SelectMany(p => p.Recipe!.Ingredients)
                .ToList() ?? new List<ScraperIngredient>();
            return new SourceCheckFacts(
                report.Verdict,
                report.RuleVersion,
                report.Complete,
                report.Reasons.Select(r => new SourceReason(r.Code, r.Outcome, r.Detail)).ToList(),
                report.Licence?.Kind,
                report.Samples?.Requested ?? 0,
                vettingChecked,
                vettingPassed,
                lines.Count,
                lines.Count(LineIsFine),
                rejectedBefore);
        }

        /// <summary>A sample ingredient line that carries a quantity, or one the import accepts without one ("salt to taste", a heading).</summary>
        public static bool LineIsFine(ScraperIngredient line) =>
            line.Quantity is > 0 || RecipeVettingService.IsAcceptablyUnquantified(line.RawLine);

        /// <summary>A domain as the candidate list keys it: lower case, no scheme or path, no leading "www.".</summary>
        public static string? NormalizeDomain(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim().ToLowerInvariant();
            if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.Host.Contains('.', StringComparison.Ordinal)) return null;
            var host = uri.Host.TrimEnd('.');
            return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        }

        private static string Normal(string? outcome) =>
            outcome is Pass or Fail or Unknown or Info ? outcome : Unknown;
    }
}
