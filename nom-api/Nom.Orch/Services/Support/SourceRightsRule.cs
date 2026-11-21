using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services.Support
{
    /// <summary>What one sentence of a site's own words forbids: automated access, reuse of the content, or AI use.</summary>
    public sealed record RightsSentenceReading(bool Automated, bool Reuse, bool Ai)
    {
        public bool Forbids => Automated || Reuse;

        public bool Any => Automated || Reuse || Ai;

        public static readonly RightsSentenceReading None = new(false, false, false);
    }

    /// <summary>
    /// NOM's own reading of a sentence the source check quoted from a site's terms, footer or robots.txt
    /// comments. A sentence forbids something only when it holds a prohibition cue ("may not", "agree not
    /// to", "without ... written permission", "is prohibited", "never granted" ...) together with an object
    /// word of a category: automated access (scrape, crawl, spider, robot, data mining, harvest, automated,
    /// aggregation), reuse (reproduce, reprint, republish, redistribute, copy, use on an application, for
    /// publication), or AI use (artificial intelligence, machine learning, AI training, text and data mining,
    /// LLM). A grant limited to "personal (non-commercial) use only" counts as a reuse prohibition without a
    /// cue. A sentence about the site reserving the right to change things forbids nothing, and neither does
    /// a rule of conduct (no spyware, nothing unlawful, no unsolicited messages) as to automated access or
    /// reuse. The conduct test is made per prohibited action: a sentence that lists several (split at ";" and
    /// at ", or to" / ", and to") still forbids automated access or reuse when an action other than the
    /// conduct one names it ("not to use any robot ... to access the Website, or to introduce viruses").
    /// The scraper's own category is not read; its sentence is.
    /// </summary>
    public static class RightsSentences
    {
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        private static readonly Regex Cue = new(
            @"\b(?:prohibit\w*|forbid\w*|forbade|may\s+not|must\s+not|shall\s+not|should\s+not|cannot|can\s+not|can't|do\s+not|don't|does\s+not|will\s+not|won't|agree\s+not\s+to|agree\s+that\s+you\s+will\s+not|not\s+(?:be\s+)?permitted|not\s+allowed|never\s+(?:be\s+)?granted|without\s+(?:\S+\s+){0,5}?(?:permission|consent|authori[sz]ation)|no\s+(?:\S+\s+){0,3}?(?:may|shall|can)\s+be)\b",
            Options);

        private static readonly Regex ReservesRight = new(@"\breserves?\s+the\s+right\b", Options);

        private static readonly Regex UserCue = new(@"\b(?:you|users?|visitors?|third\s+parties)\b", Options);

        private static readonly Regex Conduct = new(
            @"\b(?:spyware|virus(?:es)?|malware|malicious|trojan|worms?|keystroke|unlawful|illegal|fraudulent|harmful|defamatory|obscene|unsolicited|spam)\b",
            Options);

        private static readonly Regex Action = new(@";|,\s*(?:or|and)\s+to\b|\s(?:or|and)\s+to\s+(?=[a-z])", Options);

        private static readonly Regex Tdm = new(@"\btext\s+and\s+data\s+mining\b|\btdm\b", Options);

        private static readonly Regex Automated = new(
            @"\b(?:scrap(?:e|es|ed|ing|er|ers)|crawl\w*|spider\w*|robots?|bots?|data[\s-]?min(?:e|es|ed|ing)|harvest\w*|automat\w*|aggregat\w*|systematic\w*)\b",
            Options);

        private static readonly Regex Reuse = new(
            @"\b(?:reproduc\w*|reprint\w*|republish\w*|redistribut\w*|re-?post\w*|duplicat\w*|cop(?:y|ied|ies|ying)|derivative\s+works?|for\s+publication|used\s+(?:on|in)\s+(?:an|any)\s+(?:app|application)\w*)\b",
            Options);

        private static readonly Regex PersonalUseOnly = new(
            @"\b(?:(?:personal|non-?commercial)(?:\s*,?\s*(?:and\s+)?(?:personal|non-?commercial))?\s+use\s+only|solely\s+for\s+(?:your\s+)?(?:own\s+)?(?:personal|non-?commercial))\b",
            Options);

        private static readonly Regex AiWords = new(
            @"\b(?:artificial\s+intelligence|machine\s+learning|ai|a\.i\.|llms?|large\s+language\s+models?|generative|train(?:ing)?|fine-?tun\w*)\b",
            Options);

        public static RightsSentenceReading Read(string? sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return RightsSentenceReading.None;
            var text = Regex.Replace(sentence.Replace('’', '\'').Replace('“', '"').Replace('”', '"'), @"\s+", " ").Trim();
            var personalOnly = PersonalUseOnly.IsMatch(text);
            var cue = Cue.IsMatch(text);
            if (ReservesRight.IsMatch(text) && !UserCue.IsMatch(text) && !personalOnly) return RightsSentenceReading.None;
            if (!cue && !personalOnly) return RightsSentenceReading.None;

            var tdm = Tdm.IsMatch(text);
            var rest = Tdm.Replace(text, " ");
            var ai = cue && (tdm || AiWords.IsMatch(rest));
            var actions = Action.Split(rest).Where(a => !Conduct.IsMatch(a)).ToList();
            var automated = cue && actions.Any(a => Automated.IsMatch(a));
            var reuse = actions.Any(a => PersonalUseOnly.IsMatch(a) || (cue && Reuse.IsMatch(a)));
            return new RightsSentenceReading(automated, reuse, ai);
        }
    }

    /// <summary>
    /// The class a source's report gives it, with the deciding words and where they were found, and its AI reservation.
    /// <see cref="TermsOnly"/> marks a verdict from a terms-only check (no recipe page read): its AI flag can only be raised.
    /// </summary>
    public sealed record SourceRightsVerdict(
        string? Class,
        string Reason,
        string? Quote,
        string? QuoteUrl,
        bool AiReserved,
        string? AiQuote,
        string? AiQuoteUrl,
        bool TermsOnly = false)
    {
        public bool Conclusive => Class != null;
    }

    /// <summary>
    /// The rights class of a web source, by rule from its stored source-check report and nothing else (no
    /// model, no judgement, nothing outside the report), in this order:
    /// <list type="number">
    /// <item><b>forbidden</b>: a sentence of the site's own words (its terms or footer as the check quoted
    /// them, or a robots.txt comment) that <see cref="RightsSentences"/> reads as forbidding automated access
    /// or reuse; the sentence and its URL are kept.</item>
    /// <item>A report under a rule version this rule does not read (it reads <c>source-check/4</c> and stored
    /// <c>source-check/2</c> reports), from a check that did not finish, or for a host with no address (DNS gave no
    /// A or AAAA record, so nothing could be read) gives no class (inconclusive): the class already stored stands.</item>
    /// <item><b>permission-needed</b>: the terms could not be read or the site refuses automated fetches: the
    /// check refused to fetch it, the homepage was not read or carries no links as served (built by scripts),
    /// robots.txt was refused or unreachable or bars NomBot or all crawlers, a page (robots.txt, homepage, terms,
    /// sitemap or sample) answered a bot challenge or refused NomBot, a terms page was not read (also one linked on
    /// another domain, and in a terms-only check one whose link or redirect names a recipe page), or the TDM reservation
    /// file could not be read.</item>
    /// <item><b>clear</b>: the report's licence is one that grants reuse (CC0, Public Domain Mark, a
    /// public-domain statement, CC BY) and its licence reason passed.</item>
    /// <item><b>facts-only</b>: otherwise; nothing grants reuse and nothing forbids automated access.</item>
    /// </list>
    /// <see cref="SourceRightsVerdict.AiReserved"/> is set apart from the class when a sentence reserves
    /// against AI use, a page carries <c>noai</c>, a TDM reservation is declared, a Content-Signal says
    /// <c>ai-train=no</c> or <c>ai-input=no</c>, or robots.txt blocks a named AI crawler.
    /// </summary>
    public static class SourceRightsRule
    {
        public const string Version = "source-rights/2";
        public const string Clear = "clear";
        public const string FactsOnly = "facts-only";
        public const string PermissionNeeded = "permission-needed";
        public const string Forbidden = "forbidden";
        public const string NoAddress = "NoAddress";
        public const string TermsOnlyMode = "terms-only";

        public static readonly IReadOnlyList<string> Classes = new[] { Clear, FactsOnly, PermissionNeeded, Forbidden };

        private static readonly string[] UnreadTermsOutcomes = { "refused", "bot-challenge", "robots-disallowed", "redirected-off-domain", "over-budget", "failed", "not-read", "off-domain", "recipe-like" };

        private static readonly int[] RefusalStatuses = { 401, 402, 403, 429 };

        public static SourceRightsVerdict Derive(ScraperSourceCheckReport? report)
        {
            if (report == null) return new SourceRightsVerdict(null, "no report", null, null, false, null, null);

            var sentences = Sentences(report).Select(s => (s.Text, s.Url, Reading: RightsSentences.Read(s.Text))).ToList();
            var (aiReserved, aiQuote, aiUrl) = Reservation(report, sentences.Where(s => s.Reading.Ai).Select(s => (s.Text, s.Url)).FirstOrDefault());

            var forbidding = sentences.Where(s => s.Reading.Automated).Concat(sentences.Where(s => s.Reading.Reuse)).FirstOrDefault();
            if (forbidding.Text != null)
            {
                return new SourceRightsVerdict(Forbidden, forbidding.Reading.Automated ? "the site's words forbid automated access" : "the site's words forbid reuse",
                    forbidding.Text, forbidding.Url, aiReserved, aiQuote, aiUrl);
            }

            if (report.RuleVersion == null || !SourceAdmissionPolicy.ReadableRuleVersions.Contains(report.RuleVersion))
                return new SourceRightsVerdict(null, $"report under {report.RuleVersion ?? "no rule version"}; this rule reads {SourceAdmissionPolicy.ScraperRuleVersion} and {SourceAdmissionPolicy.EarlierScraperRuleVersion}", null, null, aiReserved, aiQuote, aiUrl);
            if (!report.Complete)
                return new SourceRightsVerdict(null, $"the check did not finish ({report.IncompleteReason ?? "incomplete"})", null, null, aiReserved, aiQuote, aiUrl);
            if (report.Access?.HomepageOutcome == NoAddress)
                return new SourceRightsVerdict(null, $"the host {Host(report)} has no address (DNS gave no A or AAAA record), so nothing could be read", null, null, aiReserved, aiQuote, aiUrl);

            if (Unreadable(report) is { } unread)
                return new SourceRightsVerdict(PermissionNeeded, unread.Reason, unread.Quote, unread.Url, aiReserved, aiQuote, aiUrl);

            var licence = report.Licence;
            var licencePassed = report.Reasons.Any(r => r.Code == "licence" && r.Outcome == SourceAdmissionPolicy.Pass);
            if (licence?.Kind != null && SourceAdmissionPolicy.ClearLicences.Contains(licence.Kind) && licencePassed)
            {
                var statement = licence.Statements.FirstOrDefault(s => s.Kind == licence.Kind);
                var page = report.Samples?.Pages.FirstOrDefault()?.Url;
                return new SourceRightsVerdict(Clear, $"{licence.Kind} grants reuse",
                    statement?.Text ?? $"{licence.SampleLicences.GetValueOrDefault(licence.Kind)} sampled recipe pages carry a {licence.Kind} licence in their markup",
                    statement?.FoundAt ?? page ?? report.Access?.HomepageUrl, aiReserved, aiQuote, aiUrl);
            }

            var reserved = licence?.Statements.FirstOrDefault(s => s.Kind == "all-rights-reserved")
                ?? licence?.Statements.FirstOrDefault(s => s.Kind == "copyright-notice");
            return new SourceRightsVerdict(FactsOnly, "nothing grants reuse and nothing forbids automated access",
                reserved?.Text ?? "no licence, no prohibition and no terms against automated access were found",
                reserved?.FoundAt ?? report.Access?.HomepageUrl, aiReserved, aiQuote, aiUrl);
        }

        /// <summary>The sentences of the site's own words the report quotes: robots.txt comments first (served on the domain itself), then terms and footer prohibitions.</summary>
        public static IEnumerable<(string Text, string? Url)> Sentences(ScraperSourceCheckReport report)
        {
            foreach (var comment in report.Robots?.ProhibitionComments ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(comment)) yield return (comment, report.Robots?.Url);
            }

            foreach (var p in report.Terms?.Prohibitions ?? new List<ScraperCheckProhibition>())
            {
                if (!string.IsNullOrWhiteSpace(p.Text)) yield return (p.Text!, p.FoundAt);
            }
        }

        private static (string Reason, string Quote, string? Url)? Unreadable(ScraperSourceCheckReport report)
        {
            var access = report.Access;
            if (access?.RefusedBeforeFetch is { Length: > 0 } refused)
                return ("the check did not fetch the site", $"the scraper refused to fetch it ({refused})", access.HomepageUrl);
            if (access == null || access.HomepageOutcome != "Ok")
            {
                var status = access?.HomepageStatus is { } code ? $" (HTTP {code})" : string.Empty;
                var how = access?.BotChallengePages.Contains(access.HomepageUrl ?? string.Empty) == true ? "a bot challenge" : access?.HomepageOutcome ?? "nothing";
                return ("the homepage could not be read, so its terms and footer are unknown", $"the homepage answered {how}{status}", access?.HomepageUrl);
            }

            if (access.HomepageLinks == 0)
                return ("the homepage as served carries no links (it is built by scripts), so its terms are unknown", "the homepage as served carries no links", access.HomepageUrl);

            var robots = report.Robots;
            if (robots?.Status is "refused" or "unreachable")
                return ("the site refuses automated fetches", $"robots.txt was {robots.Status}", robots.Url);
            if (robots?.NomBot is { } nomBot && (nomBot.DisallowsEverything || !nomBot.AllowsHomepage || nomBot.SamplePathsDisallowed.Count > 0))
                return ("the site refuses automated fetches", "robots.txt disallows NomBot" + (nomBot.SamplePathsDisallowed.FirstOrDefault() is { } path ? $" ({path})" : string.Empty), robots.Url);
            if (robots?.General is { } general && (general.DisallowsEverything || !general.AllowsHomepage))
                return ("the site refuses automated fetches", "robots.txt disallows every crawler", robots.Url);

            if (access.BotChallengePages.FirstOrDefault() is { } challenged)
                return ("the site refuses automated fetches", $"{challenged} answered a bot challenge", challenged);
            if (access.RefusedPages.FirstOrDefault() is { } refusedPage)
                return ("the site refuses automated fetches", $"{refusedPage} refused NomBot", refusedPage);
            if (report.Enumeration?.RefusedPages.FirstOrDefault() is { } refusedSitemap)
                return ("the site refuses automated fetches", $"{refusedSitemap} refused NomBot", refusedSitemap);
            if (report.Samples is { Refused: > 0 } samples)
            {
                var page = samples.Pages.FirstOrDefault(p => p.HttpStatus is { } code && RefusalStatuses.Contains(code))?.Url;
                return ("the site refuses automated fetches", $"{samples.Refused} sampled recipe page(s) refused NomBot", page ?? access.HomepageUrl);
            }

            if (report.Terms?.Pages.FirstOrDefault(p => UnreadTermsOutcomes.Contains(p.Outcome)) is { } unreadTerms)
                return ("the terms could not be read", unreadTerms.Outcome == "off-domain"
                    ? $"{unreadTerms.Url} is linked as the terms but is on another domain, so it was not read"
                    : $"{unreadTerms.Url} was not read ({unreadTerms.Outcome})", unreadTerms.Url);

            if (report.Reservation?.TdmRepStatus == "unreadable")
                return ("the site's TDM reservation could not be read", "the TDM reservation file could not be read", report.Reservation.TdmRepUrl);
            return null;
        }

        private static (bool Reserved, string? Quote, string? Url) Reservation(ScraperSourceCheckReport report, (string? Text, string? Url) sentence)
        {
            if (sentence.Text != null) return (true, sentence.Text, sentence.Url);

            var reservation = report.Reservation;
            if (reservation?.NoAiMetaPages.FirstOrDefault() is { } meta) return (true, $"{meta} carries a noai robots meta", meta);
            if (reservation?.NoAiHeaderPages.FirstOrDefault() is { } header) return (true, $"{header} sends X-Robots-Tag noai", header);
            if (reservation != null && reservation.TdmRepReserved.Count > 0) return (true, "the TDM reservation file reserves text and data mining", reservation.TdmRepUrl);
            if (reservation?.TdmHeaderPages.FirstOrDefault() is { } tdm) return (true, $"{tdm} sends tdm-reservation: 1", tdm);

            var signal = report.Robots?.ContentSignals.Select(Text).FirstOrDefault(s => Regex.IsMatch(s, @"ai-(?:train|input)\s*=\s*no", RegexOptions.IgnoreCase));
            if (signal != null) return (true, $"robots.txt Content-Signal: {signal}", report.Robots?.Url);

            var blocked = report.Robots?.AiCrawlers.Where(a => a.Rule is "blocked" or "partly-blocked").Select(a => a.Rule == "blocked" ? a.Agent : $"{a.Agent} (partly)").ToList() ?? new List<string>();
            if (blocked.Count > 0) return (true, $"robots.txt blocks named AI crawlers: {string.Join(", ", blocked)}", report.Robots?.Url);
            return (false, null, null);
        }

        private static string Text(JsonElement element) =>
            element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();

        /// <summary>Whether a report comes from a terms-only check (the scraper's <c>sampleSize</c> 0 mode, <c>mode</c> "terms-only"): robots.txt, the homepage and the terms pages only, no recipe page.</summary>
        public static bool IsTermsOnly(ScraperSourceCheckReport? report) =>
            report?.Mode == TermsOnlyMode || report?.Samples is { Requested: SourceAdmissionPolicy.TermsOnlySampleSize };

        /// <summary>
        /// The verdict a report gives a source that holds <paramref name="heldClass"/>, decided on a sentence or a
        /// refusal found at <paramref name="heldQuoteUrl"/>. A full check's verdict stands as derived. A terms-only check
        /// reads no recipe page or sitemap, so it can confirm or find a bar but cannot lift one it did not read: when the
        /// source is forbidden or needs permission on the words of a page the terms-only check did not read (a recipe page's
        /// footer, a refused sample or sitemap), a verdict of facts-only or clear gives no class, and the held class stands.
        /// The pages that hold a bar are the held quote's and every page of <paramref name="lastFullReport"/> (the source's
        /// last full check) with a forbidding sentence or a refusal, not only the first one quoted; each must have been read
        /// again. Its AI flag can only be raised (<see cref="SourceRightsVerdict.TermsOnly"/>).
        /// </summary>
        public static SourceRightsVerdict Derive(ScraperSourceCheckReport? report, string? heldClass, string? heldQuoteUrl, ScraperSourceCheckReport? lastFullReport = null)
        {
            var verdict = Derive(report);
            if (!IsTermsOnly(report)) return verdict;
            verdict = verdict with { TermsOnly = true };
            if (heldClass is not (Forbidden or PermissionNeeded) || verdict.Class is not (Clear or FactsOnly)) return verdict;
            var read = PagesRead(report!);
            var bars = new List<string?> { heldQuoteUrl };
            if (lastFullReport != null && !IsTermsOnly(lastFullReport)) bars.AddRange(BarPages(lastFullReport));
            var unread = bars.Where(url => url == null || !read.Contains(Page(url))).Select(url => url ?? "the page").FirstOrDefault();
            if (unread == null) return verdict;
            return verdict with
            {
                Class = null,
                Reason = $"a terms-only check does not read {unread}, where the source was found {heldClass}, so it cannot lift that class; the class stands",
                Quote = null,
                QuoteUrl = null,
            };
        }

        /// <summary>The pages of a report that hold a bar: each page with a forbidding sentence and each page that refused or was not read; null for a refusal with no page named.</summary>
        private static IEnumerable<string?> BarPages(ScraperSourceCheckReport report)
        {
            foreach (var (text, url) in Sentences(report))
            {
                if (RightsSentences.Read(text).Forbids) yield return url;
            }

            if (Unreadable(report) is not { } unread) yield break;
            yield return unread.Url;
            var access = report.Access;
            foreach (var url in (access?.BotChallengePages ?? new List<string>()).Concat(access?.RefusedPages ?? new List<string>()).Concat(report.Enumeration?.RefusedPages ?? new List<string>()))
                yield return url;
            foreach (var page in report.Terms?.Pages.Where(p => UnreadTermsOutcomes.Contains(p.Outcome)) ?? Enumerable.Empty<ScraperCheckTermsPage>())
                yield return page.Url;
            if (report.Samples is { Refused: > 0 } samples)
            {
                var refused = samples.Pages.Where(p => p.HttpStatus is { } code && RefusalStatuses.Contains(code)).Select(p => p.Url).ToList();
                foreach (var url in refused) yield return url;
                if (refused.Count < samples.Refused) yield return null;
            }
        }

        private static HashSet<string> PagesRead(ScraperSourceCheckReport report)
        {
            var pages = new HashSet<string>(StringComparer.Ordinal);
            foreach (var url in new[] { report.Access?.HomepageUrl, report.Access?.HomepageFinalUrl, report.Robots?.Url, report.Reservation?.TdmRepUrl }
                .Concat(report.Terms?.Pages.Where(p => p.Outcome == "read").Select(p => p.Url) ?? Enumerable.Empty<string>()))
            {
                if (!string.IsNullOrWhiteSpace(url)) pages.Add(Page(url));
            }

            return pages;
        }

        private static string Page(string url)
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return url.Trim().TrimEnd('/').ToLowerInvariant();
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            return (host + uri.AbsolutePath.TrimEnd('/')).ToLowerInvariant();
        }

        private static string Host(ScraperSourceCheckReport report) =>
            Uri.TryCreate(report.Access?.HomepageUrl, UriKind.Absolute, out var uri) ? uri.Host : report.Domain;

        /// <summary>The class a source's recipes take: its derived class, lifted to clear by a recorded written permission.</summary>
        public static string? Effective(string? rightsClass, bool permission) =>
            permission && rightsClass is PermissionNeeded or Forbidden or FactsOnly ? Clear : rightsClass;
    }
}
