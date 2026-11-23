using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nom.Api.Tests.Golden;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The source lane after the first run against a real scraper (checkpoint 3, findings S1 to S3): NOM reads
    /// source-check/5 reports and a newer version on the facts it knows; a check is due by time and by a change of
    /// the policy only once, never on every tick because of the rule version a report came back under; and the first
    /// check of a source or candidate is terms-only, so no recipe page of a site whose own words forbid automated
    /// access, or that refuses NomBot, is ever requested.
    /// </summary>
    public class SourceLaneTermsFirstTests
    {
        private const long Admin = 7;
        private static readonly DateTime T0 = new(2026, 11, 22, 6, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private static readonly string[] Forbidden =
        {
            "www.eatingwell.com", "www.seriouseats.com", "pinchofyum.com", "www.skinnytaste.com", "budgetbytes.com", "www.budgetbytes.com",
            "sallysbakingaddiction.com", "www.davidlebovitz.com", "www.ambitiouskitchen.com",
        };

        private static readonly string[] Refusing = { "minimalistbaker.com", "cookieandkate.com", "culiplan.com", "fr.frije.com" };

        private static readonly string[] FactsOnly =
        {
            "www.gimmesomeoven.com", "www.acozykitchen.com", "www.twopeasandtheirpod.com", "www.loveandlemons.com", "therecipecritic.com",
            "www.howsweeteats.com", "chocolatecoveredkatie.com", "www.101cookbooks.com", "www.halfbakedharvest.com",
        };

        private static readonly string[] Denylisted =
        {
            "budgetbytes.com", "www.budgetbytes.com", "www.seriouseats.com", "cookieandkate.com", "minimalistbaker.com", "sallysbakingaddiction.com",
            "pinchofyum.com", "www.skinnytaste.com", "www.eatingwell.com",
        };

        /// <summary>A scraper that answers each check job at once with the report its <see cref="Answer"/> gives for the host and sample size, and counts every call.</summary>
        private sealed class CountingScraper : IRecipeScraperClient
        {
            private readonly Dictionary<string, ScraperSourceCheckReport> _jobs = new();
            private readonly Func<DateTime> _now;

            public CountingScraper(Func<DateTime> now, Func<string, int, ScraperSourceCheckReport> answer)
            {
                _now = now;
                Answer = answer;
            }

            public Func<string, int, ScraperSourceCheckReport> Answer { get; }

            public List<(string Domain, int SampleSize)> Checks { get; } = new();

            public List<IReadOnlyList<string>> Discoveries { get; } = new();

            public bool IsConfigured => true;

            public Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, IReadOnlyList<string> excludeDomains, CancellationToken cancellationToken = default)
            {
                Discoveries.Add(seedDomains);
                return Task.FromResult(ScraperJobAnswer.Of(new ScraperJob
                {
                    JobId = $"d{Discoveries.Count}", Kind = "discovery", Status = ScraperJob.Completed, CreatedUtc = _now(), DeadlineUtc = _now().AddMinutes(10), BudgetSeconds = budgetSeconds,
                    Result = JsonSerializer.SerializeToElement(new ScraperDiscoveryResult { SeedsVisited = seedDomains.ToList() }, Json),
                }));
            }

            public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default)
            {
                Checks.Add((domain, sampleSize));
                var id = $"c{Checks.Count}";
                _jobs[id] = Answer(domain, sampleSize);
                return Task.FromResult(ScraperJobAnswer.Of(Job(id, ScraperJob.Running, null)));
            }

            public Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default) =>
                Task.FromResult(ScraperJobAnswer.Of(Job(jobId, ScraperJob.Completed, _jobs[jobId])));

            private ScraperJob Job(string id, string status, ScraperSourceCheckReport? report) => new()
            {
                JobId = id, Kind = "source-check", Status = status, CreatedUtc = _now(), DeadlineUtc = _now().AddMinutes(20), BudgetSeconds = 1200,
                Result = report == null ? null : JsonSerializer.SerializeToElement(report, Json),
            };
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static SourceAdmissionService Lane(ApplicationDbContext db, IRecipeScraperClient scraper, Func<DateTime> now, Action<SourceAdmissionSettings>? configure = null)
        {
            var settings = new SourceAdmissionSettings { Enabled = true, PollSeconds = 1, TermsOnlyChecks = true, FullChecks = true };
            configure?.Invoke(settings);
            return new SourceAdmissionService(db, scraper, new RecipeVettingService(), Options.Create(settings), NullLogger<SourceAdmissionService>.Instance,
                Options.Create(new SourceRightsSettings { Enforce = true }))
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = now,
            };
        }

        /// <summary>The 22 current web sources as a restored database holds them: approved, never checked, no class.</summary>
        private static async Task<List<ScrapingSourceEntity>> CurrentSourcesAsync(ApplicationDbContext db)
        {
            var hosts = GoldenSourceRightsTests.SavedReports().Keys.Where(h => h != "myplate.gov").OrderBy(h => h, StringComparer.Ordinal).ToList();
            hosts.Should().HaveCount(22);
            var sources = hosts.Select(h => new ScrapingSourceEntity
            {
                Domain = h, Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0.AddDays(-90),
            }).ToList();
            db.ScrapingSources.AddRange(sources);
            await db.SaveChangesAsync();
            return sources;
        }

        /// <summary>
        /// What a scraper with no denylist answers: a terms-only check reads each site's robots.txt, homepage and terms
        /// (the facts and quoted sentences of the golden *-5t rows for the sites the research found barred, the saved
        /// report's own facts for the others); a full check gives the saved source-check/5 report.
        /// </summary>
        private static Func<string, int, ScraperSourceCheckReport> OpenScraper()
        {
            var saved = GoldenSourceRightsTests.SavedReports();
            var termsRows = GoldenSet.Read("source-rights.tsv").Rows.Where(r => r.Id.EndsWith("-5t", StringComparison.Ordinal)).ToDictionary(r => r["host"]);
            return (host, sampleSize) => sampleSize == SourceAdmissionPolicy.TermsOnlySampleSize
                ? termsRows.TryGetValue(host, out var row) ? GoldenSourceRightsTests.Report(row) : GoldenSourceRightsTests.AsTermsOnly(saved[host])
                : saved[host];
        }

        /// <summary>What the staging scraper with its denylist answers: the saved report, read as a terms-only check when one was asked.</summary>
        private static Func<string, int, ScraperSourceCheckReport> DenylistScraper()
        {
            var saved = GoldenSourceRightsTests.SavedReports();
            return (host, sampleSize) => sampleSize == SourceAdmissionPolicy.TermsOnlySampleSize ? GoldenSourceRightsTests.AsTermsOnly(saved[host]) : saved[host];
        }

        private static async Task ApproveFactsOnlyUseAsync(ApplicationDbContext db, DateTime now)
        {
            var rights = new SourceRightsService(db, Options.Create(new SourceRightsSettings { Enforce = true }), NullLogger<SourceRightsService>.Instance) { Now = () => now };
            foreach (var source in await db.ScrapingSources.Where(s => s.RightsClass == SourceRightsRule.FactsOnly).ToListAsync())
            {
                (await rights.SetFactsOnlyUseAsync(source.Id, true, Admin, "reviewed")).Ok.Should().BeTrue();
            }
        }

        private static async Task TicksAsync(SourceAdmissionService lane, Func<Task> advance, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                await lane.RunNextCheckAsync();
                await advance();
            }
        }

        [Fact]
        public async Task From_no_class_the_22_current_sources_get_a_terms_only_check_first_and_no_barred_site_has_a_recipe_page_requested()
        {
            await using var db = NewContext();
            await CurrentSourcesAsync(db);
            var now = T0;
            var scraper = new CountingScraper(() => now, OpenScraper());
            var lane = Lane(db, scraper, () => now);

            await TicksAsync(lane, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 3 * 48);
            scraper.Checks.Should().HaveCount(22).And.OnlyContain(c => c.SampleSize == SourceAdmissionPolicy.TermsOnlySampleSize,
                "with FullChecks on, no full check starts until an admin approves facts-only use of the site");
            await ApproveFactsOnlyUseAsync(db, now);
            await TicksAsync(lane, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 3 * 48);

            var byHost = scraper.Checks.GroupBy(c => c.Domain).ToDictionary(g => g.Key, g => g.Select(c => c.SampleSize).ToList());
            foreach (var host in Forbidden.Concat(Refusing))
            {
                byHost[host].Should().Equal(new[] { SourceAdmissionPolicy.TermsOnlySampleSize }, $"{host} gets one terms-only check and never a recipe page");
            }

            foreach (var host in FactsOnly)
            {
                byHost[host].Should().Equal(new[] { SourceAdmissionPolicy.TermsOnlySampleSize, SourceAdmissionPolicy.SampleSize }, $"{host}'s terms forbid nothing and an admin approved it, so its full check follows");
            }

            scraper.Checks.Take(22).Should().OnlyContain(c => c.SampleSize == SourceAdmissionPolicy.TermsOnlySampleSize, "the terms-only pass comes before any full check");
            var classes = await db.ScrapingSources.AsNoTracking().ToDictionaryAsync(s => s.Domain, s => s.RightsClass);
            Forbidden.Select(h => classes[h]).Should().OnlyContain(c => c == SourceRightsRule.Forbidden);
            Refusing.Select(h => classes[h]).Should().OnlyContain(c => c == SourceRightsRule.PermissionNeeded);
            FactsOnly.Select(h => classes[h]).Should().OnlyContain(c => c == SourceRightsRule.FactsOnly);

            var review = await lane.ReviewAsync();
            review.Approved.Single(s => s.Domain == "www.eatingwell.com").Check!.Mode.Should().Be(SourceRightsRule.TermsOnlyMode);
            review.Approved.Single(s => s.Domain == "www.acozykitchen.com").Check!.Mode.Should().Be(SourceRightsRule.FullMode);
        }

        [Fact]
        public async Task A_scraper_that_refuses_a_host_leaves_it_permission_needed_with_its_reason_and_it_is_not_checked_again()
        {
            await using var db = NewContext();
            await CurrentSourcesAsync(db);
            var now = T0;
            var scraper = new CountingScraper(() => now, DenylistScraper());
            var lane = Lane(db, scraper, () => now);

            await TicksAsync(lane, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 3 * 48);
            await ApproveFactsOnlyUseAsync(db, now);
            await TicksAsync(lane, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 3 * 48);

            foreach (var host in Denylisted)
            {
                scraper.Checks.Where(c => c.Domain == host).Select(c => c.SampleSize).Should().Equal(new[] { SourceAdmissionPolicy.TermsOnlySampleSize }, $"{host} is refused once and never asked again");
                var source = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Domain == host);
                source.RightsClass.Should().Be(SourceRightsRule.PermissionNeeded);
                source.RightsQuote.Should().Contain("denylist");
            }

            scraper.Checks.Where(c => c.SampleSize == SourceAdmissionPolicy.SampleSize).Select(c => c.Domain)
                .Should().BeEquivalentTo(FactsOnly, "only the sites whose terms-only check found no bar get a full check");

            var barred = await db.ScrapingSources.SingleAsync(s => s.Domain == "pinchofyum.com");
            var requested = await lane.RequestTermsCheckAsync(barred.Id, Admin, "the scraper's denylist was lifted");
            requested.Ok.Should().BeTrue(requested.Error);
        }

        [Fact]
        public async Task An_admin_may_start_a_terms_only_check_of_a_source_with_no_class_and_it_reads_no_recipe_page()
        {
            await using var db = NewContext();
            var source = new ScrapingSourceEntity { Domain = "www.eatingwell.com", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0, CheckedAtUtc = T0 };
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            var scraper = new CountingScraper(() => T0, OpenScraper());
            var lane = Lane(db, scraper, () => T0, s => s.MaxChecksPerDay = 0);

            (await lane.RequestTermsCheckAsync(source.Id, Admin, null)).Ok.Should().BeTrue();
            var check = await lane.RunNextCheckAsync();

            scraper.Checks.Should().Equal(("www.eatingwell.com", SourceAdmissionPolicy.TermsOnlySampleSize));
            check!.Mode.Should().Be(SourceRightsRule.TermsOnlyMode);
            (await db.ScrapingSources.AsNoTracking().SingleAsync()).RightsClass.Should().Be(SourceRightsRule.Forbidden);
        }

        [Fact]
        public async Task With_full_checks_held_only_the_terms_only_pass_runs_and_without_the_terms_only_mode_nothing_unclassed_is_fetched()
        {
            await using var db = NewContext();
            await CurrentSourcesAsync(db);
            var now = T0;
            var scraper = new CountingScraper(() => now, OpenScraper());

            var off = Lane(db, scraper, () => now, s => s.TermsOnlyChecks = false);
            (await off.RunNextCheckAsync()).Should().BeNull("an unclassed source may only be checked terms-only first");
            scraper.Checks.Should().BeEmpty();

            var held = Lane(db, scraper, () => now, s => s.FullChecks = false);
            await TicksAsync(held, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 4 * 48);

            scraper.Checks.Should().HaveCount(22).And.OnlyContain(c => c.SampleSize == SourceAdmissionPolicy.TermsOnlySampleSize);
            scraper.Checks.Select(c => c.Domain).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public async Task Discovery_waits_for_the_review_of_the_terms_only_pass_and_then_seeds_only_from_sources_that_may_be_fetched()
        {
            await using var db = NewContext();
            await CurrentSourcesAsync(db);
            var now = T0;
            var scraper = new CountingScraper(() => now, OpenScraper());
            var held = Lane(db, scraper, () => now, s => s.FullChecks = false);

            await TicksAsync(held, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 3 * 48);
            var duringPass = await held.RunDiscoveryAsync();
            duringPass.Status.Should().Be(SourceDiscoveryRunEntity.Skipped);
            duringPass.Error.Should().Contain("Full checks are held");
            scraper.Discoveries.Should().BeEmpty("discovery reads recipe pages, so nothing seeds it during the terms-only pass");

            var lane = Lane(db, scraper, () => now);
            var unapproved = await lane.RunDiscoveryAsync();
            unapproved.Status.Should().Be(SourceDiscoveryRunEntity.Skipped);
            unapproved.Error.Should().Contain("9 are facts-only and wait for an admin to approve facts-only use");
            scraper.Discoveries.Should().BeEmpty("a facts-only class from a terms-only check is not an admin's facts-only approval");

            var rights = new SourceRightsService(db, Options.Create(new SourceRightsSettings { Enforce = true }), NullLogger<SourceRightsService>.Instance) { Now = () => now };
            var approved = await db.ScrapingSources.SingleAsync(s => s.Domain == "www.101cookbooks.com");
            (await rights.SetFactsOnlyUseAsync(approved.Id, true, Admin, "reviewed")).Ok.Should().BeTrue();
            var seeded = await lane.RunDiscoveryAsync();

            seeded.Status.Should().Be(SourceDiscoveryRunEntity.Completed);
            scraper.Discoveries.Single().Should().Equal("101cookbooks.com");
            scraper.Checks.Where(c => c.SampleSize > 0).Select(c => c.Domain).Should().NotIntersectWith(Forbidden.Concat(Refusing));
        }

        [Fact]
        public async Task A_full_check_answered_under_a_mode_nom_does_not_know_still_counts_as_the_candidates_full_check()
        {
            await using var db = NewContext();
            await CandidateAsync(db, "sampled.example");
            await TermsPassedAsync(db, "sampled.example");
            var now = T0;
            var scraper = new CountingScraper(() => now, (host, size) =>
            {
                var report = SourceAdmissionServiceTestsReport(host, SourceAdmissionPolicy.ScraperRuleVersion);
                report.Mode = "sampled";
                report.Samples = new ScraperCheckSamples { Requested = SourceAdmissionPolicy.SampleSize, Attempted = SourceAdmissionPolicy.SampleSize };
                return report;
            });
            var lane = Lane(db, scraper, () => now, s => s.MaxChecksPerDay = 1000);

            await TicksAsync(lane, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 48 * 3);

            scraper.Checks.Should().Equal(new[] { ("www.sampled.example", SourceAdmissionPolicy.SampleSize) },
                "the check NOM asked for was a full one; the mode its report names does not make it due again");
        }

        [Fact]
        public async Task A_lane_terms_only_check_answered_with_recipe_pages_leaves_no_report_that_classes_the_source()
        {
            await using var db = NewContext();
            var source = new ScrapingSourceEntity { Domain = "noterms.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0 };
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            var scraper = new CountingScraper(() => T0, (host, size) =>
            {
                var report = SourceRightsServiceTests.FactsOnlyReport(host);
                report.Samples = new ScraperCheckSamples { Requested = SourceAdmissionPolicy.SampleSize, Attempted = SourceAdmissionPolicy.SampleSize };
                return report;
            });

            var check = await Lane(db, scraper, () => T0).RunNextCheckAsync();
            check!.Status.Should().Be(SourceCheckEntity.Failed);
            scraper.Checks.Should().Equal(("noterms.example", SourceAdmissionPolicy.TermsOnlySampleSize));

            var rights = new SourceRightsService(db, Options.Create(new SourceRightsSettings { Enforce = true }), NullLogger<SourceRightsService>.Instance) { Now = () => T0 };
            await rights.DeriveAsync(apply: true, staleOnly: true);

            var after = await db.ScrapingSources.AsNoTracking().SingleAsync();
            after.RightsClass.Should().BeNull("the report of a terms-only check the scraper answered with recipe pages is not used");
            SourceRightsService.MayFetch(after).Should().BeFalse();
        }

        [Fact]
        public async Task The_last_full_report_passes_over_the_lanes_own_terms_only_checks()
        {
            await using var db = NewContext();
            var source = new ScrapingSourceEntity { Domain = "barred.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0 };
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            var full = SourceRightsServiceTests.FactsOnlyReport("barred.example");
            full.Terms!.Prohibitions.Add(new ScraperCheckProhibition { Text = "You agree not to use any robot or spider to access this site.", FoundAt = "https://barred.example/a-recipe/" });
            var termsOnly = SourceAdmissionServiceTests.TermsOnlyReport("barred.example");
            termsOnly.Terms!.Prohibitions.Add(new ScraperCheckProhibition { Text = "You agree not to use any robot or spider to access this site.", FoundAt = "https://barred.example/terms" });
            db.SourceChecks.AddRange(
                new SourceCheckEntity
                {
                    Domain = "barred.example", Purpose = SourceAdmissionService.RecheckPurpose, Mode = SourceRightsRule.FullMode, ScrapingSourceId = source.Id, Status = SourceCheckEntity.Completed,
                    StartedUtc = T0.AddDays(-9), FinishedUtc = T0.AddDays(-9), Verdict = SourceAdmissionPolicy.NotClear, ReportJson = JsonSerializer.Serialize(full, Json),
                },
                new SourceCheckEntity
                {
                    Domain = "barred.example", Purpose = SourceAdmissionService.RecheckPurpose, Mode = SourceRightsRule.TermsOnlyMode, ScrapingSourceId = source.Id, Status = SourceCheckEntity.Completed,
                    StartedUtc = T0.AddDays(-2), FinishedUtc = T0.AddDays(-2), Verdict = SourceAdmissionPolicy.NotClear, ReportJson = JsonSerializer.Serialize(termsOnly, Json),
                });
            await db.SaveChangesAsync();

            var last = await SourceRightsService.LastFullReportAsync(db, source.Id);

            last.Should().NotBeNull("the lane's terms-only check is passed over, not taken as the end of the search");
            last!.Terms!.Prohibitions.Single().FoundAt.Should().Be("https://barred.example/a-recipe/");
            var lifted = SourceAdmissionServiceTests.TermsOnlyReport("barred.example");
            SourceRightsRule.Derive(lifted, SourceRightsRule.Forbidden, "https://barred.example/terms", last).Conclusive
                .Should().BeFalse("the recipe page that held the bar in the last full check was not read again");
        }

        [Fact]
        public void A_report_under_a_newer_rule_version_never_makes_a_source_clear()
        {
            var current = SourceRightsServiceTests.ClearReport("open.example");
            SourceRightsRule.Derive(current).Class.Should().Be(SourceRightsRule.Clear);

            var newer = SourceRightsServiceTests.ClearReport("open.example");
            newer.RuleVersion = "source-check/7";
            var verdict = SourceRightsRule.Derive(newer);
            verdict.Should().Match<SourceRightsVerdict>(v => v.Class == SourceRightsRule.FactsOnly && v.NewerRuleVersion && v.NewerLicenceUnread);
            SourceRightsRule.Derive(newer, SourceRightsRule.Clear, "https://open.example/").Conclusive.Should().BeFalse("a source found clear keeps its class");

            using var db = NewContext();
            var source = new ScrapingSourceEntity { Domain = "open.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0 };
            SourceRightsService.ApplyVerdict(db, source, SourceRightsRule.Derive(newer, source.RightsClass, source.RightsQuoteUrl), 1, T0, Admin);
            source.RightsClass.Should().Be(SourceRightsRule.FactsOnly);
            SourceRightsService.MayFetch(source).Should().BeFalse("a newer version's licence is not read, so it never opens full recipe text to harvest");
        }

        [Fact]
        public async Task A_candidate_whose_check_came_back_under_another_rule_version_is_not_due_on_every_tick()
        {
            await using var db = NewContext();
            var newer = await CandidateAsync(db, "newer.example");
            await StoredCheckAsync(db, newer, "source-check/7", SourceAdmissionPolicy.Version, conclusive: false, T0.AddHours(-1));
            var older = await CandidateAsync(db, "older.example");
            await StoredCheckAsync(db, older, "source-check/4", "source-admission/2", conclusive: true, T0.AddDays(-2));
            await TermsPassedAsync(db, "newer.example", "older.example");
            var now = T0;
            var scraper = new CountingScraper(() => now, (host, size) => SourceAdmissionServiceTestsReport(host, "source-check/7"));
            var lane = Lane(db, scraper, () => now, s => s.MaxChecksPerDay = 1000);

            await TicksAsync(lane, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 48 * 6);

            scraper.Checks.Should().Equal(new[] { ("www.older.example", SourceAdmissionPolicy.SampleSize) },
                "the older check is due once because the policy changed since; a check under another rule version is due again only by time");

            now = T0.AddDays(8);
            await TicksAsync(lane, () => { now = now.AddMinutes(30); return Task.CompletedTask; }, 4);
            scraper.Checks.Select(c => c.Domain).Should().Equal("www.older.example", "www.newer.example", "www.older.example");
        }

        [Fact]
        public void A_report_under_a_newer_rule_version_is_read_on_what_nom_knows_and_never_clear()
        {
            static SourceCheckFacts Facts(string ruleVersion, IEnumerable<SourceReason> reasons) =>
                new("clear", ruleVersion, true, reasons.ToList(), "cc0", 20, 20, 20, 100, 100, false);
            List<SourceReason> AllPass() => SourceAdmissionPolicy.ScraperReasons
                .Select(code => new SourceReason(code, code is "crawl-delay" or "sample-yield" ? "info" : "pass", "fixture")).ToList();

            var judgement = SourceAdmissionPolicy.Judge(Facts("source-check/7", AllPass()));
            judgement.Clear.Should().BeFalse();
            judgement.Conclusive.Should().BeFalse("a newer version changes nothing but the display");
            judgement.Reasons.Single(r => r.Code == "rule-version").Detail.Should().StartWith("newer check version");
            SourceAdmissionPolicy.Judge(Facts(SourceAdmissionPolicy.ScraperRuleVersion, AllPass())).Clear.Should().BeTrue();
            SourceAdmissionPolicy.Judge(Facts("source-check/3", AllPass())).Should().Match<SourceJudgement>(j => !j.Clear && !j.Conclusive);

            var unknownReason = SourceAdmissionPolicy.Judge(Facts(SourceAdmissionPolicy.ScraperRuleVersion, AllPass().Append(new SourceReason("new-reason", "pass", null))));
            unknownReason.Clear.Should().BeFalse("a reason NOM does not know is unknown, never a pass");
            unknownReason.Reasons.Single(r => r.Code == "new-reason").Outcome.Should().Be(SourceAdmissionPolicy.Unknown);

            var info = AllPass().Select(r => r.Code == "licence" ? r with { Outcome = "info" } : r);
            SourceAdmissionPolicy.Judge(Facts(SourceAdmissionPolicy.ScraperRuleVersion, info)).Reasons.Single(r => r.Code == "licence").Outcome
                .Should().Be(SourceAdmissionPolicy.Unknown, "only crawl-delay and sample-yield may be information");

            var shuffled = AllPass();
            shuffled.Reverse();
            SourceAdmissionPolicy.Judge(Facts(SourceAdmissionPolicy.ScraperRuleVersion, shuffled)).Clear.Should().BeTrue("reasons are read by their code, not their place");

            var report = SourceRightsServiceTests.FactsOnlyReport("open.example");
            report.RuleVersion = "source-check/7";
            SourceRightsRule.Derive(report).Should().Match<SourceRightsVerdict>(v => v.Class == SourceRightsRule.FactsOnly && v.NewerRuleVersion);
        }

        [Fact]
        public void A_value_the_rule_does_not_know_is_never_a_pass()
        {
            var report = SourceRightsServiceTests.FactsOnlyReport("odd.example");
            report.Robots!.AiCrawlers.Add(new ScraperCheckAiCrawler { Agent = "GPTBot", Rule = "throttled" });
            SourceRightsRule.Derive(report).Should().Match<SourceRightsVerdict>(v => v.Class == SourceRightsRule.FactsOnly && v.AiReserved && v.AiQuote!.Contains("throttled"));

            var mode = SourceRightsServiceTests.FactsOnlyReport("odd.example");
            mode.Mode = "sampled";
            SourceRightsRule.IsTermsOnly(mode).Should().BeTrue("a mode NOM does not know never stands in for a full check");
            SourceRightsRule.Derive(mode, SourceRightsRule.Forbidden, "https://odd.example/a-recipe/").Conclusive.Should().BeFalse();

            var clear = GoldenSourceRightsTests.AsTermsOnly(SourceRightsServiceTests.FactsOnlyReport("open.example"));
            SourceRightsRule.Derive(clear, SourceRightsRule.Clear, "https://open.example/r1").Conclusive
                .Should().BeFalse("a terms-only check cannot see the per-page licence a clear source was found clear on");
            SourceRightsRule.AllowsFullCheck(clear).Should().BeTrue();
            SourceRightsRule.AllowsFullCheck(SourceRightsServiceTests.FactsOnlyReport("open.example")).Should().BeFalse("only a terms-only report opens the way to a full check");
        }

        private static ScraperSourceCheckReport SourceAdmissionServiceTestsReport(string host, string ruleVersion)
        {
            var report = SourceRightsServiceTests.FactsOnlyReport(host);
            report.RuleVersion = ruleVersion;
            report.Reasons = SourceAdmissionPolicy.ScraperReasons.Select(code => new ScraperCheckReason { Code = code, Outcome = code == "licence" ? "fail" : "pass" }).ToList();
            return report;
        }

        private static async Task<SourceCandidateEntity> CandidateAsync(ApplicationDbContext db, string domain)
        {
            var candidate = new SourceCandidateEntity
            {
                Domain = domain, EvidenceUrl = $"https://www.{domain}/a-recipe/", FoundVia = "seed.example", Signal = "RecipeJsonLd",
                FirstSeenUtc = T0.AddDays(-10), LastSeenUtc = T0, TimesSeen = 1, State = SourceCandidateEntity.NotClear,
            };
            db.SourceCandidates.Add(candidate);
            await db.SaveChangesAsync();
            return candidate;
        }

        private static async Task StoredCheckAsync(ApplicationDbContext db, SourceCandidateEntity candidate, string ruleVersion, string policyVersion, bool conclusive, DateTime at)
        {
            var check = new SourceCheckEntity
            {
                Domain = "www." + candidate.Domain, Purpose = SourceAdmissionService.CandidatePurpose, SourceCandidateId = candidate.Id, Status = SourceCheckEntity.Completed,
                StartedUtc = at, FinishedUtc = at, RuleVersion = ruleVersion, Verdict = SourceAdmissionPolicy.NotClear, Conclusive = conclusive, PolicyVersion = policyVersion,
                ReportJson = JsonSerializer.Serialize(SourceAdmissionServiceTestsReport(candidate.Domain, ruleVersion), Json),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            candidate.LastCheckId = check.Id;
            await db.SaveChangesAsync();
        }

        private static async Task TermsPassedAsync(ApplicationDbContext db, params string[] domains)
        {
            foreach (var domain in domains)
            {
                var candidate = await db.SourceCandidates.SingleAsync(c => c.Domain == domain);
                db.SourceChecks.Add(new SourceCheckEntity
                {
                    SourceCandidateId = candidate.Id,
                    Domain = domain, Purpose = SourceAdmissionService.CandidatePurpose, Mode = SourceRightsRule.TermsOnlyMode, Status = SourceCheckEntity.Completed,
                    StartedUtc = T0.AddDays(-3), FinishedUtc = T0.AddDays(-3), RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion, Verdict = SourceAdmissionPolicy.NotClear,
                    PolicyVersion = SourceAdmissionPolicy.Version, ReportJson = JsonSerializer.Serialize(SourceAdmissionServiceTests.TermsOnlyReport(domain), Json),
                });
            }

            await db.SaveChangesAsync();
        }
    }
}
