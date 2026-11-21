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

    /// <summary>The class a source's report gives it, with the deciding words and where they were found, and its AI reservation.</summary>
    public sealed record SourceRightsVerdict(
        string? Class,
        string Reason,
        string? Quote,
        string? QuoteUrl,
        bool AiReserved,
        string? AiQuote,
        string? AiQuoteUrl)
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
    /// <item>A report under a rule version this rule does not read, or from a check that did not finish, gives
    /// no class (inconclusive): the class already stored stands.</item>
    /// <item><b>permission-needed</b>: the terms could not be read or the site refuses automated fetches: the
    /// check refused to fetch it, the homepage was not read, robots.txt was refused or unreachable or bars
    /// NomBot or all crawlers, a page answered a bot challenge or refused NomBot, a terms page was not read, or
    /// the TDM reservation file could not be read.</item>
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
        public const string Version = "source-rights/1";
        public const string Clear = "clear";
        public const string FactsOnly = "facts-only";
        public const string PermissionNeeded = "permission-needed";
        public const string Forbidden = "forbidden";

        public static readonly IReadOnlyList<string> Classes = new[] { Clear, FactsOnly, PermissionNeeded, Forbidden };

        private static readonly string[] UnreadTermsOutcomes = { "refused", "bot-challenge", "robots-disallowed", "redirected-off-domain", "over-budget", "failed", "not-read" };

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

            if (report.RuleVersion != SourceAdmissionPolicy.ScraperRuleVersion)
                return new SourceRightsVerdict(null, $"report under {report.RuleVersion ?? "no rule version"}; this rule reads {SourceAdmissionPolicy.ScraperRuleVersion}", null, null, aiReserved, aiQuote, aiUrl);
            if (!report.Complete)
                return new SourceRightsVerdict(null, $"the check did not finish ({report.IncompleteReason ?? "incomplete"})", null, null, aiReserved, aiQuote, aiUrl);

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

            if (report.Terms?.Pages.FirstOrDefault(p => UnreadTermsOutcomes.Contains(p.Outcome)) is { } unreadTerms)
                return ("the terms could not be read", $"{unreadTerms.Url} was not read ({unreadTerms.Outcome})", unreadTerms.Url);

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

        /// <summary>The class a source's recipes take: its derived class, lifted to clear by a recorded written permission.</summary>
        public static string? Effective(string? rightsClass, bool permission) =>
            permission && rightsClass is PermissionNeeded or Forbidden or FactsOnly ? Clear : rightsClass;
    }
}
