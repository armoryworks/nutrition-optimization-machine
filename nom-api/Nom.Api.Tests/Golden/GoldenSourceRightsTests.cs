using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The source rights rule against its golden sets: source-rights.tsv (the class and AI flag of each of
    /// the 22 judged hosts, built from the facts and quoted sentences of source-rejudge.md, again under
    /// source-check/4 for the hosts its facts change, terms-only checks under source-check/5, and constructed cases), source-check-5.tsv
    /// (the 23 source-check/5 reports the staging scraper stored, replayed whole), rights-sentences.tsv (NOM's reading of each quoted sentence)
    /// and recipe-titles.tsv (the title rule). All are rules with no model, so their floor is every row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenSourceRightsTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenSourceRightsTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_source_takes_the_class_its_own_words_give_it()
        {
            var report = GoldenSet.Run("source-rights.tsv", "status", row =>
            {
                var verdict = SourceRightsRule.Derive(Report(row));
                var expectedQuote = row["expectQuote"];
                var quote = verdict.Quote ?? "-";
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectClass"] = verdict.Class ?? "-",
                    ["expectAi"] = verdict.AiReserved ? "yes" : "no",
                    ["expectQuote"] = expectedQuote == "-" ? quote : quote.Contains(expectedQuote, StringComparison.Ordinal) ? expectedQuote : quote,
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Each_saved_source_check_5_report_is_classed_and_read_by_the_policy()
        {
            var reports = SavedReports();
            var report = GoldenSet.Run("source-check-5.tsv", "status", row =>
            {
                var saved = reports[row["host"]];
                saved.RuleVersion.Should().Be("source-check/5");
                var verdict = SourceRightsRule.Derive(saved);
                var judgement = SourceAdmissionPolicy.Judge(SourceAdmissionPolicy.Facts(saved, 0, 0, rejectedBefore: false));
                var expectedQuote = row["expectQuote"];
                var quote = verdict.Quote ?? "-";
                var asNewer = SourceRightsRule.Derive(Reversion(saved, "source-check/7"));
                asNewer.Class.Should().Be(verdict.Class, $"{row.Id} under a newer rule version is classed on the facts NOM knows");
                asNewer.NewerRuleVersion.Should().BeTrue();
                judgement.Verdict.Should().Be(SourceAdmissionPolicy.NotClear, $"no current source is clear ({row.Id})");
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectClass"] = verdict.Class ?? "-",
                    ["expectAi"] = verdict.AiReserved ? "yes" : "no",
                    ["expectQuote"] = expectedQuote == "-" ? quote : quote.Contains(expectedQuote, StringComparison.Ordinal) ? expectedQuote : quote,
                    ["expectConclusive"] = judgement.Conclusive ? "yes" : "no",
                    ["expectFullCheck"] = SourceRightsRule.AllowsFullCheck(AsTermsOnly(saved)) ? "yes" : "no",
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Each_change_source_check_6_makes_is_read_as_the_rules_say()
        {
            var reports = SavedReports();
            var report = GoldenSet.Run("source-check-6.tsv", "status", row =>
            {
                var changed = Changed(reports[row["host"]], row.Value("change"));
                var verdict = SourceRightsRule.Derive(changed);
                var judgement = SourceAdmissionPolicy.Judge(SourceAdmissionPolicy.Facts(changed, 0, 0, rejectedBefore: false));
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectClass"] = verdict.Class ?? "-",
                    ["expectAi"] = verdict.AiReserved ? "yes" : "no",
                    ["expectConclusive"] = judgement.Conclusive ? "yes" : "no",
                    ["expectCredits"] = judgement.Reasons.Single(r => r.Code == "credits").Outcome,
                    ["expectVerdict"] = judgement.Verdict,
                    ["expectAgainst"] = string.Join(";", judgement.Against.Select(r => r.Code)),
                    ["expectSamples"] = (changed.Samples?.Pages.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Each_saved_report_read_under_source_check_6_keeps_its_class_and_is_conclusive()
        {
            foreach (var (host, saved) in SavedReports())
            {
                var current = Reversion(saved, SourceAdmissionPolicy.ScraperRuleVersion);
                var verdict = SourceRightsRule.Derive(current);
                verdict.Class.Should().Be(SourceRightsRule.Derive(saved).Class, $"{host}: source-check/6 changes credits and the sampled pages, never the facts a class rests on");
                verdict.NewerRuleVersion.Should().BeFalse();
                SourceAdmissionPolicy.Judge(SourceAdmissionPolicy.Facts(current, 0, 0, rejectedBefore: false)).Conclusive.Should().Be(saved.Complete, host);
            }
        }

        /// <summary>A stored report with the changes of a source-check-6.tsv row applied: <c>version=</c>, <c>reason:code=outcome</c>, <c>drop-index-samples</c>.</summary>
        private static ScraperSourceCheckReport Changed(ScraperSourceCheckReport report, string? change)
        {
            var copy = JsonSerializer.Deserialize<ScraperSourceCheckReport>(JsonSerializer.Serialize(report))!;
            foreach (var op in Split(change, ";"))
            {
                if (op.StartsWith("version=", StringComparison.Ordinal)) copy.RuleVersion = op["version=".Length..];
                else if (op.StartsWith("reason:", StringComparison.Ordinal))
                {
                    var at = op.IndexOf('=', StringComparison.Ordinal);
                    var code = op["reason:".Length..at];
                    copy.Reasons.Single(r => r.Code == code).Outcome = op[(at + 1)..];
                }
                else if (op == "drop-index-samples" && copy.Samples != null) copy.Samples.Pages.RemoveAll(p => IndexPage(p.Url));
                else throw new InvalidOperationException($"unknown change {op}");
            }

            return copy;
        }

        private static bool IndexPage(string url)
        {
            var segment = new Uri(url).AbsolutePath.TrimEnd('/').Split('/').Last().ToLowerInvariant();
            return segment is "recipe" or "recipes" or "all-recipes" or "index" || segment.StartsWith("recipe-index", StringComparison.Ordinal) || segment.EndsWith("-recipes", StringComparison.Ordinal);
        }

        private static ScraperSourceCheckReport Reversion(ScraperSourceCheckReport report, string ruleVersion)
        {
            var copy = JsonSerializer.Deserialize<ScraperSourceCheckReport>(JsonSerializer.Serialize(report))!;
            copy.RuleVersion = ruleVersion;
            return copy;
        }

        /// <summary>The source-check/5 reports the staging scraper stored for the 22 current web sources and the myplate.gov request, keyed by host.</summary>
        internal static Dictionary<string, ScraperSourceCheckReport> SavedReports() =>
            JsonSerializer.Deserialize<Dictionary<string, ScraperSourceCheckReport>>(
                File.ReadAllText(Path.Combine(GoldenSet.Folder, "source-check-5-reports.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        /// <summary>The report as a terms-only check of the same site would give it: the same robots.txt, homepage and terms facts, no recipe page.</summary>
        internal static ScraperSourceCheckReport AsTermsOnly(ScraperSourceCheckReport report)
        {
            var copy = JsonSerializer.Deserialize<ScraperSourceCheckReport>(JsonSerializer.Serialize(report))!;
            copy.Mode = SourceRightsRule.TermsOnlyMode;
            copy.Samples = new ScraperCheckSamples { Requested = SourceAdmissionPolicy.TermsOnlySampleSize };
            if (copy.Reservation != null) copy.Reservation.TdmRepStatus = "not-checked";
            return copy;
        }

        [Fact]
        public void Each_quoted_sentence_reads_as_what_it_forbids()
        {
            var report = GoldenSet.Run("rights-sentences.tsv", "status", row =>
            {
                var reading = RightsSentences.Read(row["sentence"]);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectAutomated"] = reading.Automated ? "yes" : "no",
                    ["expectReuse"] = reading.Reuse ? "yes" : "no",
                    ["expectAi"] = reading.Ai ? "yes" : "no",
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void A_distinctive_title_becomes_the_dish_name_and_a_dish_name_stays()
        {
            var report = GoldenSet.Run("recipe-titles.tsv", "status", row =>
            {
                var reading = RecipeTitleRule.Reduce(row["title"], RecipeTitleRule.BrandNames(row.Value("credit")));
                return GoldenSet.Compare(row, new Dictionary<string, string> { ["expect"] = reading.Title });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void A_credit_line_keeps_the_cue_and_the_credited_name_only()
        {
            var report = GoldenSet.Run("credit-lines.tsv", "status", row =>
            {
                var credit = CreditLines.Join(row["texts"].Split(" | "));
                return GoldenSet.Compare(row, new Dictionary<string, string> { ["expect"] = credit ?? "-" });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        /// <summary>The report a row describes, in the scraper's shape (source-check/2, or source-check/4 and later with the facts of the access column, a terms-only check with <c>mode=terms-only</c>).</summary>
        internal static ScraperSourceCheckReport Report(Nom.Import.Golden.GoldenRow row)
        {
            var host = row["host"];
            var home = $"https://{host}/";
            var robotsUrl = $"https://{host}/robots.txt";
            var homepage = row["homepage"];
            var refusedBefore = homepage.StartsWith("refused:", StringComparison.Ordinal) ? homepage["refused:".Length..] : null;
            var homeParts = homepage.Split(' ');
            var access = new ScraperCheckAccess
            {
                RefusedBeforeFetch = refusedBefore,
                HomepageUrl = home,
                HomepageOutcome = refusedBefore != null ? "not-fetched" : homeParts[0],
                HomepageStatus = homeParts.Length > 1 && int.TryParse(homeParts[1], out var code) ? code : homeParts[0] == "Ok" ? 200 : null,
            };
            if (homeParts.Contains("challenge")) access.BotChallengePages.Add(home);
            foreach (var page in Split(row.Value("challenged"), ";"))
            {
                if (page.StartsWith("refused:", StringComparison.Ordinal)) access.RefusedPages.Add(page["refused:".Length..]);
                else access.BotChallengePages.Add(page);
            }

            var nomBot = row["nombot"];
            var robots = new ScraperCheckRobots
            {
                Url = robotsUrl,
                Status = row["robots"],
                NomBot = new ScraperCheckRobotsGroup { AllowsHomepage = nomBot == "allowed", DisallowsEverything = nomBot == "all-disallowed" },
                General = new ScraperCheckRobotsGroup { AllowsHomepage = nomBot != "all-disallowed", DisallowsEverything = nomBot == "all-disallowed" },
            };
            foreach (var agent in Split(row.Value("aiBlocked"), ","))
            {
                if (agent.StartsWith("signal:", StringComparison.Ordinal))
                {
                    robots.ContentSignals.Add(JsonSerializer.SerializeToElement(agent["signal:".Length..]));
                    continue;
                }

                var parts = agent.Split(':');
                robots.AiCrawlers.Add(new ScraperCheckAiCrawler { Agent = parts[0], Rule = parts.Length > 1 && parts[1] == "partly" ? "partly-blocked" : "blocked" });
            }

            var reservation = new ScraperCheckReservation { TdmRepUrl = $"https://{host}/.well-known/tdmrep.json", TdmRepStatus = row["tdmRep"] == "reserved" ? "found" : row["tdmRep"] };
            if (row["tdmRep"] == "reserved") reservation.TdmRepReserved.Add(JsonSerializer.SerializeToElement(new { location = "/", reserved = 1 }));
            foreach (var page in Split(row.Value("noAi"), ";"))
            {
                if (page.StartsWith("header:", StringComparison.Ordinal)) reservation.NoAiHeaderPages.Add(page["header:".Length..]);
                else reservation.NoAiMetaPages.Add(page);
            }

            var terms = new ScraperCheckTerms();
            foreach (var page in Split(row.Value("termsPages"), ";"))
            {
                var at = page.LastIndexOf('=');
                terms.Pages.Add(new ScraperCheckTermsPage { Url = page[..at], Outcome = page[(at + 1)..] });
            }

            foreach (var sentence in Split(row.Value("sentences"), " || "))
            {
                var at = sentence.IndexOf(" :: ", StringComparison.Ordinal);
                var url = sentence[..at];
                var text = sentence[(at + 4)..];
                if (url.EndsWith("/robots.txt", StringComparison.Ordinal)) robots.ProhibitionComments.Add(text);
                else terms.Prohibitions.Add(new ScraperCheckProhibition { Category = "reuse", Text = text, FoundAt = url });
            }

            var licence = new ScraperCheckLicence { Kind = row.Value("licenceKind"), Basis = row.Value("licenceKind") == null ? "none" : "per-page" };
            if (licence.Kind != null) licence.SampleLicences[licence.Kind] = 20;
            foreach (var statement in Split(row.Value("statements"), " || "))
            {
                var at = statement.IndexOf('=');
                licence.Statements.Add(new ScraperCheckStatement { Kind = statement[..at], Text = statement[(at + 1)..], FoundAt = home, Where = "page-text" });
            }

            var enumeration = new ScraperCheckEnumeration { ViaSitemap = true, RecipeUrlCount = 1000 };
            var samples = new ScraperCheckSamples { Requested = 20, Pages = new List<ScraperCheckSamplePage> { new() { Url = $"https://{host}/a-recipe/" } } };
            string? mode = null;
            foreach (var fact in Split(row.Value("access"), ";"))
            {
                if (fact.StartsWith("links=", StringComparison.Ordinal)) access.HomepageLinks = int.Parse(fact["links=".Length..]);
                else if (fact.StartsWith("sitemap-refused:", StringComparison.Ordinal)) enumeration.RefusedPages.Add(fact["sitemap-refused:".Length..]);
                else if (fact.StartsWith("mode=", StringComparison.Ordinal))
                {
                    mode = fact["mode=".Length..];
                    if (mode == SourceRightsRule.TermsOnlyMode) samples = new ScraperCheckSamples { Requested = SourceAdmissionPolicy.TermsOnlySampleSize };
                }
                else if (fact.StartsWith("samples-refused:", StringComparison.Ordinal))
                {
                    var parts = fact["samples-refused:".Length..].Split('@');
                    samples.Refused = int.Parse(parts[0]);
                    samples.Pages.Add(new ScraperCheckSamplePage { Url = parts[1], Outcome = "HttpError", HttpStatus = 403 });
                }
            }

            return new ScraperSourceCheckReport
            {
                Domain = host,
                RuleVersion = row["ruleVersion"],
                Mode = mode,
                Complete = row["complete"] == "yes",
                Verdict = "not-clear",
                Reasons = new List<ScraperCheckReason> { new() { Code = "licence", Outcome = row["licencePass"] == "yes" ? "pass" : "fail" } },
                Access = access,
                Robots = robots,
                Reservation = reservation,
                Terms = terms,
                Licence = licence,
                Enumeration = enumeration,
                Samples = samples,
            };
        }

        private static IEnumerable<string> Split(string? cell, string separator) =>
            string.IsNullOrEmpty(cell) ? Enumerable.Empty<string>() : cell.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
