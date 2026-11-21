using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The source rights rule against its golden sets: source-rights.tsv (the class and AI flag of each of
    /// the 22 judged hosts, built from the facts and quoted sentences of source-rejudge.md, again under
    /// source-check/4 for the hosts its facts change, and constructed cases), rights-sentences.tsv (NOM's reading of each quoted sentence) and recipe-titles.tsv (the title
    /// rule). All three are rules with no model, so their floor is every row.
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

        /// <summary>The report a row describes, in the scraper's shape (source-check/2, or source-check/4 with the facts of the access column).</summary>
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
            foreach (var fact in Split(row.Value("access"), ";"))
            {
                if (fact.StartsWith("links=", StringComparison.Ordinal)) access.HomepageLinks = int.Parse(fact["links=".Length..]);
                else if (fact.StartsWith("sitemap-refused:", StringComparison.Ordinal)) enumeration.RefusedPages.Add(fact["sitemap-refused:".Length..]);
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
