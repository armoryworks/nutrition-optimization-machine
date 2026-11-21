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
    /// The source rights follow-up: the source-check/4 report read beside a stored source-check/2 one; a source or
    /// candidate classed forbidden or permission-needed is never fetched by an automatic check, and the weekly re-check
    /// says so; an admin's terms-only check (the scraper's sampleSize 0 mode) is journaled, reads no recipe page and
    /// never lifts a bar it did not read.
    /// </summary>
    public class SourceRightsFollowupTests
    {
        private const long Admin = 7;
        private static readonly DateTime T0 = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private sealed class FakeScraper : IRecipeScraperClient
        {
            public readonly Queue<ScraperJobAnswer> Starts = new();
            public readonly Queue<ScraperJobAnswer> Polls = new();
            public readonly List<(string Domain, int SampleSize)> Checks = new();
            public Exception? FailNextStart;

            public bool IsConfigured => true;

            public Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default)
            {
                Checks.Add((domain, sampleSize));
                if (FailNextStart is { } failure)
                {
                    FailNextStart = null;
                    throw failure;
                }

                return Task.FromResult(Starts.Dequeue());
            }

            public Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default) => Task.FromResult(Polls.Dequeue());
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static SourceAdmissionService Admission(ApplicationDbContext db, FakeScraper scraper, Action<SourceAdmissionSettings>? configure = null)
        {
            var settings = new SourceAdmissionSettings { Enabled = true, PollSeconds = 1 };
            configure?.Invoke(settings);
            return new SourceAdmissionService(db, scraper, new RecipeVettingService(), Options.Create(settings), NullLogger<SourceAdmissionService>.Instance,
                Options.Create(new SourceRightsSettings { Enforce = true }))
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = () => T0,
            };
        }

        private static SourceRightsService Rights(ApplicationDbContext db) =>
            new(db, Options.Create(new SourceRightsSettings { Enforce = true }), NullLogger<SourceRightsService>.Instance) { Now = () => T0 };

        private static ScraperJob Job(string status, object? result) => new()
        {
            JobId = "job1", Kind = "source-check", Status = status, CreatedUtc = T0, DeadlineUtc = T0.AddMinutes(10), BudgetSeconds = 600,
            Result = result == null ? null : JsonSerializer.SerializeToElement(result, Json),
        };

        private static void CheckJob(FakeScraper scraper, ScraperSourceCheckReport report)
        {
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job(ScraperJob.Running, null)));
            scraper.Polls.Enqueue(ScraperJobAnswer.Of(Job(ScraperJob.Completed, report)));
        }

        private static ScraperSourceCheckReport TermsOnly(ScraperSourceCheckReport report)
        {
            report.Samples = new ScraperCheckSamples { Requested = SourceAdmissionPolicy.TermsOnlySampleSize };
            return report;
        }

        private static async Task<ScrapingSourceEntity> SourceAsync(ApplicationDbContext db, string domain, string? rightsClass, string? quoteUrl = null, Action<ScrapingSourceEntity>? configure = null)
        {
            var source = new ScrapingSourceEntity
            {
                Domain = domain, Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0.AddDays(-90),
                RightsClass = rightsClass, RightsQuote = rightsClass == null ? null : "a quoted sentence", RightsQuoteUrl = quoteUrl, RightsRuleVersion = SourceRightsRule.Version,
                CheckedAtUtc = T0.AddDays(-30), CheckVerdict = SourceAdmissionPolicy.NotClear,
            };
            configure?.Invoke(source);
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            return source;
        }

        private static async Task<long> StoredCheckAsync(ApplicationDbContext db, ScrapingSourceEntity source, ScraperSourceCheckReport report, string? reportJson = null)
        {
            var check = new SourceCheckEntity
            {
                Domain = source.Domain, Purpose = "recheck", ScrapingSourceId = source.Id, Status = SourceCheckEntity.Completed, StartedUtc = T0.AddDays(-1), FinishedUtc = T0.AddDays(-1),
                RuleVersion = report.RuleVersion, Verdict = SourceAdmissionPolicy.NotClear, Conclusive = true, ReportJson = reportJson ?? JsonSerializer.Serialize(report, Json),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            source.LastCheckId = check.Id;
            await db.SaveChangesAsync();
            return check.Id;
        }

        [Fact]
        public async Task A_stored_source_check_2_report_still_gives_its_class_and_a_source_check_4_report_is_read_too()
        {
            await using var db = NewContext();
            var old = await SourceAsync(db, "old.example", null);
            var oldReport = SourceRightsServiceTests.ForbiddenReport("old.example");
            oldReport.RuleVersion = SourceAdmissionPolicy.EarlierScraperRuleVersion;
            var stored = JsonSerializer.Serialize(oldReport, Json).Replace("\"homepageLinks\":null,", string.Empty).Replace("\"refusedPages\":[],", string.Empty);
            await StoredCheckAsync(db, old, oldReport, stored);
            var current = await SourceAsync(db, "new.example", null);
            var newReport = SourceRightsServiceTests.FactsOnlyReport("new.example");
            newReport.Access!.HomepageLinks = 42;
            await StoredCheckAsync(db, current, newReport);

            var run = await Rights(db).DeriveAsync(apply: true);

            run.Results.Select(r => (r.Domain, r.NewClass, r.Conclusive)).Should().BeEquivalentTo(new[]
            {
                ("new.example", SourceRightsRule.FactsOnly, true),
                ("old.example", SourceRightsRule.Forbidden, true),
            });
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == old.Id)).RightsRuleVersion.Should().Be("source-rights/2");
        }

        [Fact]
        public async Task The_weekly_recheck_skips_a_forbidden_or_permission_needed_source_says_so_and_checks_the_others()
        {
            await using var db = NewContext();
            var forbidden = await SourceAsync(db, "forbidden.example", SourceRightsRule.Forbidden, "https://forbidden.example/terms", s => s.CheckedAtUtc = T0.AddDays(-60));
            var needs = await SourceAsync(db, "needs.example", SourceRightsRule.PermissionNeeded, "https://needs.example/", s => s.CheckedAtUtc = T0.AddDays(-50));
            var permitted = await SourceAsync(db, "permitted.example", SourceRightsRule.Forbidden, "https://permitted.example/terms", s =>
            {
                s.CheckedAtUtc = T0.AddDays(-40);
                s.PermissionRecordedAtUtc = T0.AddDays(-41);
            });
            var facts = await SourceAsync(db, "facts.example", SourceRightsRule.FactsOnly, "https://facts.example/");
            var scraper = new FakeScraper();
            CheckJob(scraper, SourceRightsServiceTests.FactsOnlyReport("permitted.example"));
            CheckJob(scraper, SourceRightsServiceTests.FactsOnlyReport("facts.example"));
            var service = Admission(db, scraper);

            await service.RunNextCheckAsync();
            await service.RunNextCheckAsync();
            (await service.RunNextCheckAsync()).Should().BeNull("only the forbidden and the permission-needed source are left, and no automatic check fetches them");

            scraper.Checks.Should().Equal(("permitted.example", SourceAdmissionPolicy.SampleSize), ("facts.example", SourceAdmissionPolicy.SampleSize));
            var review = await service.ReviewAsync();
            review.Approved.Single(s => s.Id == forbidden.Id).RecheckSkipped.Should().Contain("skips forbidden.example").And.Contain("forbidden").And.Contain("terms-only");
            review.Approved.Single(s => s.Id == needs.Id).RecheckSkipped.Should().Contain("permission-needed");
            review.Approved.Single(s => s.Id == permitted.Id).RecheckSkipped.Should().BeNull("a recorded permission makes it clear, so it keeps the weekly re-check");
            review.Approved.Single(s => s.Id == facts.Id).RecheckSkipped.Should().BeNull();
            (await service.StatusAsync()).RecheckSkipped.Should().Be(2);
        }

        [Fact]
        public async Task A_candidate_whose_last_report_forbids_is_never_checked_again_by_itself()
        {
            await using var db = NewContext();
            var candidate = new SourceCandidateEntity
            {
                Domain = "barred.example", EvidenceUrl = "https://barred.example/a-recipe/", FoundVia = "seed.example", Signal = "RecipeJsonLd",
                FirstSeenUtc = T0.AddDays(-20), LastSeenUtc = T0, TimesSeen = 2, State = SourceCandidateEntity.NotClear,
            };
            db.SourceCandidates.Add(candidate);
            await db.SaveChangesAsync();
            var report = SourceRightsServiceTests.ForbiddenReport("barred.example");
            report.RuleVersion = SourceAdmissionPolicy.EarlierScraperRuleVersion;
            var check = new SourceCheckEntity
            {
                Domain = "barred.example", Purpose = SourceAdmissionService.CandidatePurpose, SourceCandidateId = candidate.Id, Status = SourceCheckEntity.Completed,
                StartedUtc = T0.AddDays(-10), FinishedUtc = T0.AddDays(-10), RuleVersion = report.RuleVersion, Verdict = SourceAdmissionPolicy.NotClear, Conclusive = true,
                PolicyVersion = "source-admission/1", ReportJson = JsonSerializer.Serialize(report, Json),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            candidate.LastCheckId = check.Id;
            await db.SaveChangesAsync();
            var scraper = new FakeScraper();

            (await Admission(db, scraper).RunNextCheckAsync()).Should().BeNull("its report under older rules would make it due, but it forbids automated access");

            scraper.Checks.Should().BeEmpty();
        }

        [Fact]
        public async Task An_admin_starts_a_terms_only_check_of_a_forbidden_source_it_reads_no_recipe_page_and_both_ends_are_journaled()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "barred.example", SourceRightsRule.Forbidden, "https://barred.example/terms");
            var facts = await SourceAsync(db, "facts.example", SourceRightsRule.FactsOnly, "https://facts.example/");
            var scraper = new FakeScraper();

            (await Admission(db, scraper).RequestTermsCheckAsync(source.Id, Admin, null)).Error.Should().Contain("TermsOnlyChecks");
            var service = Admission(db, scraper, s => { s.TermsOnlyChecks = true; s.MaxChecksPerDay = 0; });
            (await service.RequestTermsCheckAsync(facts.Id, Admin, null)).Error.Should().Contain("weekly full check");
            var requested = await service.RequestTermsCheckAsync(source.Id, Admin, "the terms page was rewritten");
            requested.Ok.Should().BeTrue(requested.Error);
            requested.Source!.TermsCheck!.Status.Should().Be(SourceAdmissionService.Queued);
            (await service.RequestTermsCheckAsync(source.Id, Admin, null)).Error.Should().Contain("already waiting");
            (await db.AuditLogEntries.SingleAsync(e => e.ChangeType == SourceJournal.TermsCheck)).Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(e =>
                e.EntityId == source.Id && e.ChangedByPersonId == Admin && e.NewValue!.Contains("the terms page was rewritten"));
            (await service.StatusAsync()).TermsChecksWaiting.Should().Be(1);

            var report = TermsOnly(SourceRightsServiceTests.FactsOnlyReport("barred.example"));
            report.Terms!.Pages.Add(new ScraperCheckTermsPage { Url = "https://barred.example/terms", Outcome = "read" });
            CheckJob(scraper, report);
            var check = await service.RunNextCheckAsync();

            scraper.Checks.Should().Equal(("barred.example", SourceAdmissionPolicy.TermsOnlySampleSize));
            check!.Purpose.Should().Be(SourceAdmissionService.TermsOnlyPurpose);
            check.Conclusive.Should().BeFalse("a terms-only check gives no admission verdict");
            check.Facts!.TermsOnly.Should().BeTrue();
            var after = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id);
            after.RightsClass.Should().Be(SourceRightsRule.FactsOnly, "the terms page it was forbidden on was read again and forbids nothing now");
            after.CheckedAtUtc.Should().Be(T0.AddDays(-30), "the weekly full check stays due on its own date");
            after.CheckVerdict.Should().Be(SourceAdmissionPolicy.NotClear);
            var journal = await db.AuditLogEntries.Where(e => e.ChangeType == SourceJournal.TermsCheck).OrderBy(e => e.Id).ToListAsync();
            journal.Should().HaveCount(2);
            journal[1].NewValue.Should().StartWith(SourceCheckEntity.Completed).And.Contain("forbidden -> facts-only");
            (await db.AuditLogEntries.CountAsync(e => e.ChangeType == SourceJournal.Rights && e.EntityId == source.Id)).Should().Be(1);
        }

        [Fact]
        public async Task A_terms_only_check_never_lifts_a_bar_found_on_a_page_it_did_not_read_and_never_clears_an_ai_reservation()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "footer.example", SourceRightsRule.Forbidden, "https://footer.example/a-recipe/", s =>
            {
                s.AiReserved = true;
                s.AiReservedQuote = "https://footer.example/a-recipe/ carries a noai robots meta";
            });
            var scraper = new FakeScraper();
            var service = Admission(db, scraper, s => s.TermsOnlyChecks = true);
            (await service.RequestTermsCheckAsync(source.Id, Admin, null)).Ok.Should().BeTrue();
            var report = TermsOnly(SourceRightsServiceTests.FactsOnlyReport("footer.example"));
            CheckJob(scraper, report);

            await service.RunNextCheckAsync();

            var after = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id);
            after.RightsClass.Should().Be(SourceRightsRule.Forbidden);
            after.AiReserved.Should().BeTrue();
            after.AiReservedQuote.Should().Contain("noai");
            (await db.AuditLogEntries.CountAsync(e => e.ChangeType == SourceJournal.Rights)).Should().Be(0);
            (await db.AuditLogEntries.Where(e => e.ChangeType == SourceJournal.TermsCheck).OrderBy(e => e.Id).LastAsync()).NewValue.Should().Contain("class forbidden stands");

            var verdict = SourceRightsRule.Derive(report, SourceRightsRule.Forbidden, "https://footer.example/a-recipe/");
            verdict.Conclusive.Should().BeFalse();
            verdict.Reason.Should().Contain("does not read https://footer.example/a-recipe/");
            SourceRightsRule.Derive(report, SourceRightsRule.Forbidden, "https://www.footer.example/").Class.Should().Be(SourceRightsRule.FactsOnly, "the homepage is read by a terms-only check");
            SourceRightsRule.Derive(SourceRightsServiceTests.FactsOnlyReport("footer.example"), SourceRightsRule.Forbidden, "https://footer.example/a-recipe/").Class
                .Should().Be(SourceRightsRule.FactsOnly, "a full check reads the recipe pages and can lift it");
        }

        [Fact]
        public async Task A_terms_only_check_the_scraper_answers_with_recipe_pages_is_recorded_as_such()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "barred.example", SourceRightsRule.PermissionNeeded, "https://barred.example/");
            var scraper = new FakeScraper();
            var service = Admission(db, scraper, s => s.TermsOnlyChecks = true);
            await service.RequestTermsCheckAsync(source.Id, Admin, null);
            var full = SourceRightsServiceTests.FactsOnlyReport("barred.example");
            full.Samples = new ScraperCheckSamples { Requested = 1, Attempted = 1 };
            CheckJob(scraper, full);

            var check = await service.RunNextCheckAsync();

            check!.Error.Should().Contain("no terms-only mode");
        }

        [Fact]
        public void A_terms_only_check_whose_terms_link_names_a_recipe_page_has_not_read_the_terms()
        {
            var report = TermsOnly(SourceRightsServiceTests.FactsOnlyReport("held.example"));
            report.Access!.HomepageLinks = 12;
            report.Terms!.Pages.Add(new ScraperCheckTermsPage { Url = "https://held.example/recipes-terms-of-use/", Outcome = "recipe-like" });

            var verdict = SourceRightsRule.Derive(report, SourceRightsRule.PermissionNeeded, "https://held.example/");

            verdict.Class.Should().Be(SourceRightsRule.PermissionNeeded, "the scraper did not fetch a terms link that names a recipe, so the terms are unknown");
            verdict.QuoteUrl.Should().Be("https://held.example/recipes-terms-of-use/");
        }

        [Fact]
        public async Task A_terms_only_check_does_not_lift_a_bar_its_last_full_check_found_on_a_recipe_page_beside_the_quoted_one()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "twice.example", SourceRightsRule.Forbidden, "https://twice.example/robots.txt");
            var full = SourceRightsServiceTests.FactsOnlyReport("twice.example");
            full.Robots!.ProhibitionComments.Add("Scraping of this site is prohibited.");
            full.Terms!.Prohibitions.Add(new ScraperCheckProhibition { Text = "These recipes may not be republished without our written permission.", FoundAt = "https://twice.example/a-recipe/" });
            SourceRightsRule.Derive(full).QuoteUrl.Should().Be("https://twice.example/robots.txt");
            await StoredCheckAsync(db, source, full);
            var scraper = new FakeScraper();
            var service = Admission(db, scraper, s => { s.TermsOnlyChecks = true; s.MaxChecksPerDay = 0; });
            (await service.RequestTermsCheckAsync(source.Id, Admin, null)).Ok.Should().BeTrue();
            CheckJob(scraper, TermsOnly(SourceRightsServiceTests.FactsOnlyReport("twice.example")));

            await service.RunNextCheckAsync();

            var after = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id);
            after.RightsClass.Should().Be(SourceRightsRule.Forbidden, "the recipe page's notice was not read again");
            (await db.AuditLogEntries.CountAsync(e => e.ChangeType == SourceJournal.Rights)).Should().Be(0);

            var stale = await Rights(db).DeriveAsync(apply: false, source.Id);
            stale.Results.Single().NewClass.Should().Be(SourceRightsRule.Forbidden);
            stale.Results.Single().Reason.Should().Contain("does not read https://twice.example/a-recipe/");

            var refused = SourceRightsServiceTests.FactsOnlyReport("twice.example");
            refused.Samples = new ScraperCheckSamples { Requested = 20, Attempted = 20, Refused = 2 };
            SourceRightsRule.Derive(TermsOnly(SourceRightsServiceTests.FactsOnlyReport("twice.example")), SourceRightsRule.PermissionNeeded, "https://twice.example/", refused)
                .Conclusive.Should().BeFalse("refused recipe pages with no page named are never read by a terms-only check");
        }

        [Fact]
        public async Task A_terms_only_check_cut_off_by_a_restart_is_journaled_as_lost_and_never_started_twice()
        {
            await using var db = NewContext();
            var polled = await SourceAsync(db, "polled.example", SourceRightsRule.Forbidden, "https://polled.example/terms");
            db.SourceChecks.Add(new SourceCheckEntity
            {
                Domain = polled.Domain, Purpose = SourceAdmissionService.TermsOnlyPurpose, ScrapingSourceId = polled.Id, JobId = "job0",
                Status = SourceCheckEntity.Running, StartedUtc = T0.AddHours(-3), CreatedDate = T0.AddHours(-3),
            });
            await db.SaveChangesAsync();
            var scraper = new FakeScraper();
            var service = Admission(db, scraper, s => { s.TermsOnlyChecks = true; s.MaxChecksPerDay = 0; });

            (await service.RunNextCheckAsync()).Should().BeNull();

            var lost = await db.AuditLogEntries.SingleAsync(e => e.ChangeType == SourceJournal.TermsCheck && e.EntityId == polled.Id);
            lost.NewValue.Should().StartWith(SourceCheckEntity.Lost).And.Contain("job0");

            var started = await SourceAsync(db, "started.example", SourceRightsRule.Forbidden, "https://started.example/terms");
            (await service.RequestTermsCheckAsync(started.Id, Admin, null)).Ok.Should().BeTrue();
            scraper.FailNextStart = new OperationCanceledException("nom-api stopped");
            await FluentActions.Awaiting(() => service.RunNextCheckAsync()).Should().ThrowAsync<OperationCanceledException>();

            (await service.RunNextCheckAsync()).Should().BeNull("the check was marked running before its job started, so it is not queued again");
            scraper.Checks.Should().Equal(("started.example", SourceAdmissionPolicy.TermsOnlySampleSize));
        }

        [Fact]
        public async Task Terms_only_checks_are_not_counted_against_the_daily_cap()
        {
            await using var db = NewContext();
            var barred = await SourceAsync(db, "barred.example", SourceRightsRule.Forbidden, "https://barred.example/terms");
            await SourceAsync(db, "facts.example", SourceRightsRule.FactsOnly, "https://facts.example/");
            var scraper = new FakeScraper();
            var service = Admission(db, scraper, s => { s.TermsOnlyChecks = true; s.MaxChecksPerDay = 1; });
            (await service.RequestTermsCheckAsync(barred.Id, Admin, null)).Ok.Should().BeTrue();
            CheckJob(scraper, TermsOnly(SourceRightsServiceTests.ForbiddenReport("barred.example")));
            CheckJob(scraper, SourceRightsServiceTests.FactsOnlyReport("facts.example"));

            (await service.RunNextCheckAsync())!.Purpose.Should().Be(SourceAdmissionService.TermsOnlyPurpose);
            (await service.RunNextCheckAsync())!.Purpose.Should().Be(SourceAdmissionService.RecheckPurpose);
            (await service.RunNextCheckAsync()).Should().BeNull("the one automatic check of the day is spent");

            scraper.Checks.Should().Equal(("barred.example", SourceAdmissionPolicy.TermsOnlySampleSize), ("facts.example", SourceAdmissionPolicy.SampleSize));
            (await service.StatusAsync()).ChecksLast24h.Should().Be(1);
        }

        [Fact]
        public void A_source_check_4_report_classes_a_host_with_no_address_as_unknown_and_refusals_as_permission_needed()
        {
            var noAddress = SourceRightsServiceTests.FactsOnlyReport("gone.example");
            noAddress.Access = new ScraperCheckAccess { HomepageUrl = "https://gone.example/", HomepageOutcome = SourceRightsRule.NoAddress };
            SourceRightsRule.Derive(noAddress).Should().Match<SourceRightsVerdict>(v => !v.Conclusive && v.Reason.Contains("has no address"));

            var scripted = SourceRightsServiceTests.FactsOnlyReport("app.example");
            scripted.Access!.HomepageLinks = 0;
            SourceRightsRule.Derive(scripted).Class.Should().Be(SourceRightsRule.PermissionNeeded);

            var sitemap = SourceRightsServiceTests.FactsOnlyReport("map.example");
            sitemap.Enumeration = new ScraperCheckEnumeration { RefusedPages = { "https://map.example/sitemap.xml" } };
            SourceRightsRule.Derive(sitemap).Should().Match<SourceRightsVerdict>(v => v.Class == SourceRightsRule.PermissionNeeded && v.QuoteUrl == "https://map.example/sitemap.xml");

            var parent = SourceRightsServiceTests.FactsOnlyReport("brand.example");
            parent.Terms!.Pages.Add(new ScraperCheckTermsPage { Url = "https://parent.example/terms", Outcome = "off-domain" });
            SourceRightsRule.Derive(parent).Should().Match<SourceRightsVerdict>(v => v.Class == SourceRightsRule.PermissionNeeded && v.Quote!.Contains("another domain"));
        }
    }
}
