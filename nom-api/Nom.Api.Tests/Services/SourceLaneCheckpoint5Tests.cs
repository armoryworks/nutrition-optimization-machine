using System;
using System.Collections.Generic;
using System.IO;
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
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Staging checkpoint 5's source findings: a rights class recorded by hand for a source, a candidate or a host with no row,
    /// journaled and undone, which the lane never overwrites and whose barred host is never fetched or checked (Q22); a repeated
    /// titles undo counts the rows already undone as such (Q26); and the scraper's refusingHosts stored on the discovery run and
    /// held from later runs until an admin looks.
    /// </summary>
    public class SourceLaneCheckpoint5Tests
    {
        private const long Admin = 7;
        private static readonly DateTime T0 = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private const string BudgetQuote = "you agree that you will not \"scrape,\" \"crawl\" or \"spider\" any web pages or other services contained in the Services";

        private sealed class FakeScraper : IRecipeScraperClient
        {
            private readonly Func<DateTime> _now;
            private readonly Dictionary<string, ScraperSourceCheckReport> _checks = new();

            public FakeScraper(Func<DateTime> now) => _now = now;

            public List<string> Checks { get; } = new();

            public List<IReadOnlyList<string>> SeedLists { get; } = new();

            public List<IReadOnlyList<string>> ExcludeLists { get; } = new();

            public Queue<List<ScraperRefusingHost>> Refusals { get; } = new();

            public bool IsConfigured => true;

            public Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, IReadOnlyList<string> excludeDomains, CancellationToken cancellationToken = default)
            {
                SeedLists.Add(seedDomains.ToList());
                ExcludeLists.Add(excludeDomains.ToList());
                var result = new ScraperDiscoveryResult { SeedsVisited = seedDomains.ToList(), RefusingHosts = Refusals.Count > 0 ? Refusals.Dequeue() : new List<ScraperRefusingHost>() };
                return Task.FromResult(ScraperJobAnswer.Of(new ScraperJob
                {
                    JobId = $"d{SeedLists.Count}", Kind = "discovery", Status = ScraperJob.Completed, StopReason = "none", CreatedUtc = _now(), DeadlineUtc = _now().AddMinutes(10),
                    BudgetSeconds = 600, Result = JsonSerializer.SerializeToElement(result, Json),
                }));
            }

            public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default)
            {
                Checks.Add(SourceAdmissionPolicy.NormalizeDomain(domain)!);
                var report = SourceRightsServiceTests.FactsOnlyReport(domain);
                if (sampleSize == SourceAdmissionPolicy.TermsOnlySampleSize)
                {
                    report.Mode = SourceRightsRule.TermsOnlyMode;
                    report.Samples = new ScraperCheckSamples { Requested = SourceAdmissionPolicy.TermsOnlySampleSize };
                }

                var id = $"c{Checks.Count}";
                _checks[id] = report;
                return Task.FromResult(ScraperJobAnswer.Of(new ScraperJob
                {
                    JobId = id, Kind = "source-check", Status = ScraperJob.Completed, CreatedUtc = _now(), DeadlineUtc = _now().AddMinutes(20), BudgetSeconds = 1200,
                    Result = JsonSerializer.SerializeToElement(report, Json),
                }));
            }

            public Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static SourceAdmissionService Lane(ApplicationDbContext db, IRecipeScraperClient scraper, Func<DateTime> now, bool enforce = true) =>
            new(db, scraper, new RecipeVettingService(),
                Options.Create(new SourceAdmissionSettings { Enabled = true, PollSeconds = 1, TermsOnlyChecks = true, FullChecks = true, SeedsPerRun = 5 }),
                NullLogger<SourceAdmissionService>.Instance, Options.Create(new SourceRightsSettings { Enforce = enforce }))
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = now,
            };

        private static SourceRightsService Rights(ApplicationDbContext db, Func<DateTime> now, bool enforce = true) =>
            new(db, Options.Create(new SourceRightsSettings { Enforce = enforce }), NullLogger<SourceRightsService>.Instance) { Now = now };

        private static async Task<ScrapingSourceEntity> SourceAsync(ApplicationDbContext db, string domain, string? rightsClass = null, Action<ScrapingSourceEntity>? configure = null)
        {
            var source = new ScrapingSourceEntity
            {
                Domain = domain, Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0.AddDays(-90), RightsClass = rightsClass,
                RightsQuote = rightsClass == SourceRightsRule.Forbidden ? "You may not scrape this website." : null,
            };
            configure?.Invoke(source);
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            return source;
        }

        private static RecipeEntity Recipe(string url, string name = "Chili") => new()
        {
            AuthorId = Admin, Name = name, Description = "A publisher's description.", SourceUrl = url, ScrapedAtUtc = T0.AddDays(-60),
            CurationStatusId = (long)CurationStatusEnum.Curated,
        };

        private static SourceRightsByHandRequestModel Forbidding(string host, string quote = BudgetQuote) => new()
        {
            Host = host, RightsClass = SourceRightsRule.Forbidden, Quote = quote, QuoteUrl = $"https://{host}/terms-conditions/", ReadOn = new DateTime(2026, 11, 21),
        };

        [Fact]
        public async Task A_class_recorded_by_hand_holds_both_rows_of_the_host_is_journaled_and_the_lane_never_fetches_checks_or_changes_it_until_it_is_undone()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "www.seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            var www = await SourceAsync(db, "www.budgetbytes.com");
            var bare = await SourceAsync(db, "budgetbytes.com");
            db.Recipes.AddRange(Recipe("https://www.budgetbytes.com/one/"), Recipe("https://budgetbytes.com/two/"));
            await db.SaveChangesAsync();
            var rights = Rights(db, () => now);

            var dry = await rights.RecordByHandAsync(Forbidding("www.budgetbytes.com"), apply: false, Admin);
            dry.Refused.Should().BeNull();
            dry.Applied.Should().BeFalse();
            dry.Host.Should().Be("budgetbytes.com");
            dry.Sources.Select(s => s.Domain).Should().BeEquivalentTo("www.budgetbytes.com", "budgetbytes.com");
            dry.Sources.Sum(s => s.Recipes).Should().Be(4, "each row counts the recipes of its host with and without www.");
            dry.Excluded.Should().BeTrue();
            (await db.SourceRightsRecords.CountAsync()).Should().Be(0, "a dry run stores nothing");
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == www.Id)).RightsClass.Should().BeNull();

            var applied = await rights.RecordByHandAsync(Forbidding("budgetbytes.com"), apply: true, Admin);
            applied.Applied.Should().BeTrue();
            applied.Record!.Sources.Should().BeEquivalentTo("www.budgetbytes.com", "budgetbytes.com");
            foreach (var id in new[] { www.Id, bare.Id })
            {
                var source = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == id);
                source.RightsClass.Should().Be(SourceRightsRule.Forbidden);
                source.RightsRecordId.Should().Be(applied.Record.Id);
                source.RightsQuote.Should().Be(BudgetQuote);
                source.RightsQuoteUrl.Should().Be("https://budgetbytes.com/terms-conditions/");
                SourceRightsService.HeldByHand(source).Should().BeTrue();
            }

            (await db.Recipes.AsNoTracking().Select(r => r.SourceRights).Distinct().ToListAsync()).Should().Equal(SourceRightsRule.Forbidden);
            var record = await db.SourceRightsRecords.AsNoTracking().SingleAsync();
            record.ReadOn.Should().Be(new DateTime(2026, 11, 21));
            record.RecordedByPersonId.Should().Be(Admin);

            var scraper = new FakeScraper(() => now);
            var lane = Lane(db, scraper, () => now);
            var journal = await lane.JournalAsync(50);
            var recordEntry = journal.Single(e => e.RecordId == record.Id && e.SourceId == 0);
            recordEntry.ChangeType.Should().Be(SourceJournal.RightsByHand);
            recordEntry.Domain.Should().Be("budgetbytes.com");
            recordEntry.CanUndo.Should().BeTrue();
            journal.Count(e => e.ChangeType == SourceJournal.RightsByHand && e.SourceId != 0).Should().Be(2);

            var review = await lane.ReviewAsync();
            review.RightsRecords.Should().ContainSingle(r => r.Host == "budgetbytes.com" && r.JournalEntryId == recordEntry.Id);
            review.Approved.Single(s => s.Id == www.Id).RightsRecord!.Quote.Should().Be(BudgetQuote);

            (await lane.RequestTermsCheckAsync(www.Id, Admin, null)).Ok.Should().BeFalse("a class recorded by hand bars even the admin's terms-only check");
            for (var day = 0; day < 4; day++)
            {
                await lane.RunNextCheckAsync();
                now = now.AddDays(1);
            }

            scraper.Checks.Should().NotContain("budgetbytes.com");
            await lane.RunDiscoveryAsync();
            scraper.ExcludeLists.Single().Should().Contain("budgetbytes.com");
            scraper.SeedLists.Single().Should().Equal("seed.example");

            var check = new SourceCheckEntity
            {
                Domain = "www.budgetbytes.com", Purpose = SourceAdmissionService.RecheckPurpose, ScrapingSourceId = www.Id, Status = SourceCheckEntity.Completed,
                StartedUtc = now, FinishedUtc = now, RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion, Verdict = SourceAdmissionPolicy.NotClear, Conclusive = true,
                ReportJson = JsonSerializer.Serialize(SourceRightsServiceTests.ClearReport("www.budgetbytes.com"), Json), CreatedDate = now,
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            (await db.ScrapingSources.SingleAsync(s => s.Id == www.Id)).LastCheckId = check.Id;
            await db.SaveChangesAsync();
            var derive = await rights.DeriveAsync(apply: true);
            derive.Results.Single(r => r.SourceId == www.Id).NewClass.Should().Be(SourceRightsRule.Forbidden, "the lane never overwrites a class recorded by hand");
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == www.Id)).RightsClass.Should().Be(SourceRightsRule.Forbidden);

            var undo = await lane.UndoAsync(recordEntry.Id, Admin);
            undo.Ok.Should().BeTrue(undo.Error);
            foreach (var id in new[] { www.Id, bare.Id })
            {
                var source = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == id);
                source.RightsClass.Should().BeNull("the undo puts back the class each source had");
                source.RightsRecordId.Should().BeNull();
            }

            (await db.SourceRightsRecords.AsNoTracking().SingleAsync()).WithdrawnAtUtc.Should().NotBeNull();
            (await lane.UndoAsync(recordEntry.Id, Admin)).Ok.Should().BeFalse("a withdrawn record is not withdrawn twice");
            (await lane.JournalAsync(50)).Single(e => e.Id == recordEntry.Id).CanUndo.Should().BeFalse();

            await lane.RunNextCheckAsync();
            scraper.Checks.Should().Contain("budgetbytes.com", "once the record is withdrawn the host is classed by its own terms-only check again");
        }

        [Fact]
        public async Task The_undo_a_withdrawn_record_wrote_on_its_source_cannot_bring_the_class_back_without_its_record()
        {
            await using var db = NewContext();
            var now = T0;
            var source = await SourceAsync(db, "budgetbytes.com");
            var rights = Rights(db, () => now);
            (await rights.RecordByHandAsync(Forbidding("budgetbytes.com"), apply: true, Admin)).Applied.Should().BeTrue();
            var lane = Lane(db, new FakeScraper(() => now), () => now);
            var recordEntry = (await lane.JournalAsync(50)).Single(e => e.RecordId != null && e.SourceId == 0);
            now = now.AddMinutes(1);
            (await lane.UndoAsync(recordEntry.Id, Admin)).Ok.Should().BeTrue();

            var withdrawUndo = (await lane.JournalAsync(50)).First(e => e.SourceId == source.Id && e.Field == SourceJournal.RightsField);
            withdrawUndo.ChangeType.Should().Be(SourceJournal.Undo);
            withdrawUndo.CanUndo.Should().BeFalse("its old value names a record that is withdrawn");
            now = now.AddMinutes(1);
            var again = await lane.UndoAsync(withdrawUndo.Id, Admin);
            again.Ok.Should().BeFalse();
            again.Error.Should().Contain("record the class again");
            var held = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id);
            held.RightsClass.Should().BeNull();
            held.RightsRecordId.Should().BeNull();
            SourceRightsService.HeldByHand(held).Should().BeFalse();
        }

        [Fact]
        public async Task A_class_recorded_by_hand_for_a_candidate_or_a_host_with_no_row_holds_it_from_checks_discovery_and_approval_and_reaches_a_row_added_later()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            var candidate = new SourceCandidateEntity
            {
                Domain = "eatingwell.com", EvidenceUrl = "https://www.eatingwell.com/recipe/1/", FoundVia = "seed.example", Signal = "RecipeJsonLd",
                FirstSeenUtc = T0.AddDays(-3), LastSeenUtc = T0.AddDays(-3), TimesSeen = 3, State = SourceCandidateEntity.New, CreatedDate = T0.AddDays(-3),
            };
            db.SourceCandidates.Add(candidate);
            await db.SaveChangesAsync();
            var rights = Rights(db, () => now);
            const string robots = "Use of any crawler to data mine or scrape the content on this website is prohibited without prior written permission.";

            var onCandidate = await rights.RecordByHandAsync(Forbidding("www.eatingwell.com", robots), apply: true, Admin);
            onCandidate.Applied.Should().BeTrue(onCandidate.Refused);
            onCandidate.Candidate.Should().Be("eatingwell.com");
            onCandidate.Sources.Should().BeEmpty();
            var noRow = await rights.RecordByHandAsync(new SourceRightsByHandRequestModel
            {
                Host = "seriouseats.com", RightsClass = SourceRightsRule.PermissionNeeded, Quote = "The homepage answers automated requests with a bot challenge.",
                QuoteUrl = "https://www.seriouseats.com/", ReadOn = new DateTime(2026, 11, 21),
            }, apply: true, Admin);
            noRow.Applied.Should().BeTrue(noRow.Refused);

            var scraper = new FakeScraper(() => now);
            var lane = Lane(db, scraper, () => now);
            for (var day = 0; day < 3; day++)
            {
                await lane.RunNextCheckAsync();
                now = now.AddDays(1);
            }

            scraper.Checks.Should().NotContain("eatingwell.com", "a candidate barred by hand gets no check, not even a terms-only one");
            await lane.RunDiscoveryAsync();
            scraper.ExcludeLists.Single().Should().Contain(new[] { "eatingwell.com", "seriouseats.com" });
            (await lane.ApproveCandidateAsync(candidate.Id, Admin, null)).Error.Should().Contain("recorded by hand");

            var later = await SourceAsync(db, "www.seriouseats.com");
            await lane.RunNextCheckAsync();
            var held = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == later.Id);
            held.RightsClass.Should().Be(SourceRightsRule.PermissionNeeded, "a row added after the record takes it before anything could fetch it");
            held.RightsRecordId.Should().Be(noRow.Record!.Id);
            scraper.Checks.Should().NotContain("seriouseats.com");
        }

        [Fact]
        public async Task The_rule_lets_a_class_recorded_by_hand_be_as_strict_or_stricter_never_laxer_and_clear_only_beside_a_written_permission()
        {
            await using var db = NewContext();
            var rights = Rights(db, () => T0);
            await SourceAsync(db, "davidlebovitz.example", SourceRightsRule.Forbidden);
            var facts = await SourceAsync(db, "facts.example", SourceRightsRule.FactsOnly);

            SourceRightsByHandRequestModel Ask(string host, string rightsClass, string quote = "Content from this website may not be reprinted or republished in any manner.") => new()
            {
                Host = host, RightsClass = rightsClass, Quote = quote, QuoteUrl = $"https://{host}/terms/", ReadOn = new DateTime(2026, 11, 20),
            };

            (await rights.RecordByHandAsync(Ask("davidlebovitz.example", SourceRightsRule.FactsOnly), true, Admin)).Refused.Should().Contain("never laxer");
            (await rights.RecordByHandAsync(Ask("davidlebovitz.example", SourceRightsRule.PermissionNeeded), false, Admin)).Refused.Should().Contain("never laxer");
            (await rights.RecordByHandAsync(Ask("facts.example", SourceRightsRule.Clear), true, Admin)).Refused.Should().Contain("written permission");
            (await rights.RecordByHandAsync(Ask("facts.example", SourceRightsRule.Forbidden, "All rights reserved."), true, Admin)).Refused.Should().Contain("sentence reader");
            (await rights.RecordByHandAsync(Ask("facts.example", "unknown"), true, Admin)).Refused.Should().NotBeNull();
            (await rights.RecordByHandAsync(Ask("not a host", SourceRightsRule.Forbidden), true, Admin)).Refused.Should().NotBeNull();
            var future = Ask("facts.example", SourceRightsRule.PermissionNeeded);
            future.ReadOn = T0.AddDays(5);
            (await rights.RecordByHandAsync(future, true, Admin)).Refused.Should().Contain("date");
            var noPage = Ask("facts.example", SourceRightsRule.PermissionNeeded);
            noPage.QuoteUrl = "terms page";
            (await rights.RecordByHandAsync(noPage, true, Admin)).Refused.Should().Contain("page");
            (await db.SourceRightsRecords.CountAsync()).Should().Be(0);

            (await rights.RecordByHandAsync(Ask("davidlebovitz.example", SourceRightsRule.Forbidden), true, Admin)).Applied.Should().BeTrue("as strict as the check is allowed");
            (await rights.RecordByHandAsync(Ask("davidlebovitz.example", SourceRightsRule.Forbidden), true, Admin)).AlreadyRecorded.Should().BeTrue();
            (await rights.RecordByHandAsync(Ask("davidlebovitz.example", SourceRightsRule.PermissionNeeded), true, Admin)).Refused.Should().Contain("already holds");

            (await rights.RecordPermissionAsync(facts.Id, new SourcePermissionRequestModel { GrantedBy = "The publisher", GrantedOn = new DateTime(2026, 11, 19), Text = "You may use our recipes in NOM." }, Admin)).Ok.Should().BeTrue();
            var clear = await rights.RecordByHandAsync(Ask("facts.example", SourceRightsRule.Clear, "You may use our recipes in NOM."), true, Admin);
            clear.Applied.Should().BeTrue(clear.Refused);
            clear.Excluded.Should().BeFalse();
        }

        [Fact]
        public async Task Without_the_rights_gate_a_source_barred_by_hand_is_neither_a_seed_nor_harvestable_nor_left_off_the_exclusion_list()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "seed.example");
            await SourceAsync(db, "pinchofyum.com");
            var rights = Rights(db, () => now, enforce: false);
            (await rights.RecordByHandAsync(Forbidding("pinchofyum.com", "You must not conduct any systematic or automated data collection activities, including scraping, without Company's express written permission."), true, Admin))
                .Applied.Should().BeTrue();

            var scraper = new FakeScraper(() => now);
            var lane = Lane(db, scraper, () => now, enforce: false);
            (await lane.HarvestableAsync()).Select(h => h.Domain).Should().Equal("seed.example");
            await lane.RunDiscoveryAsync();
            scraper.SeedLists.Single().Should().Equal("seed.example");
            scraper.ExcludeLists.Single().Should().Contain("pinchofyum.com");
        }

        [Fact]
        public async Task Discovery_stores_the_refusing_hosts_on_the_run_and_holds_each_from_later_runs_until_an_admin_looks_and_again_after_a_new_refusal()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            await SourceAsync(db, "other-seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            var scraper = new FakeScraper(() => now);
            scraper.Refusals.Enqueue(new List<ScraperRefusingHost>
            {
                new() { Domain = "booksamillion.com", Url = "https://booksamillion.com/robots.txt", HttpStatus = 403 },
                new() { Domain = "www.bookshop.org", Url = "https://bookshop.org/", HttpStatus = 403, BotChallenge = true },
                new() { Domain = "other-seed.example", Url = "https://other-seed.example/sitemap.xml", HttpStatus = 429 },
            });
            var lane = Lane(db, scraper, () => now);

            var first = await lane.RunDiscoveryAsync();
            first.RefusingHosts.Should().Be(3);
            first.RefusingHostsList.Select(h => h.Domain).Should().Equal("booksamillion.com", "www.bookshop.org", "other-seed.example");
            first.RefusingHostsList[1].BotChallenge.Should().BeTrue();
            scraper.ExcludeLists[0].Should().NotContain("booksamillion.com");
            var review = await lane.ReviewAsync();
            review.Runs.Single().RefusingHostsList.Should().HaveCount(3);
            review.RefusingHosts.Should().HaveCount(3).And.OnlyContain(h => h.Held);

            now = now.AddDays(1);
            var second = await lane.RunDiscoveryAsync();
            scraper.ExcludeLists[1].Should().Contain(new[] { "booksamillion.com", "bookshop.org", "other-seed.example" });
            scraper.SeedLists[1].Should().Equal("seed.example");
            second.ExcludeNote.Should().Contain("other-seed.example");

            var books = review.RefusingHosts.Single(h => h.Host == "booksamillion.com");
            (await lane.ReleaseRefusingHostAsync(books.Id, Admin, "robots.txt 403 to everyone; nothing to do"))!.Held.Should().BeFalse();
            (await lane.ReleaseRefusingHostAsync(999, Admin, null)).Should().BeNull();
            now = now.AddDays(1);
            scraper.Refusals.Enqueue(new List<ScraperRefusingHost> { new() { Domain = "booksamillion.com", Url = "https://booksamillion.com/", HttpStatus = 403 } });
            await lane.RunDiscoveryAsync();
            scraper.ExcludeLists[2].Should().NotContain("booksamillion.com", "an admin looked at it");
            scraper.ExcludeLists[2].Should().Contain("bookshop.org");

            now = now.AddDays(1);
            await lane.RunDiscoveryAsync();
            scraper.ExcludeLists[3].Should().Contain("booksamillion.com", "it refused again after the admin looked");
            (await db.SourceRefusingHosts.AsNoTracking().SingleAsync(h => h.Host == "booksamillion.com")).TimesRefused.Should().Be(2);
            (await db.SourceCandidates.CountAsync()).Should().Be(0, "a refusal makes no candidate");
        }

        [Fact]
        public async Task Releasing_a_refusing_host_is_in_the_sources_journal_and_its_undo_holds_the_host_again()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            var scraper = new FakeScraper(() => now);
            scraper.Refusals.Enqueue(new List<ScraperRefusingHost> { new() { Domain = "www.bookshop.org", Url = "https://bookshop.org/", HttpStatus = 403, BotChallenge = true } });
            var lane = Lane(db, scraper, () => now);
            await lane.RunDiscoveryAsync();
            var host = (await lane.ReviewAsync()).RefusingHosts.Single();

            now = now.AddHours(1);
            (await lane.ReleaseRefusingHostAsync(host.Id, Admin, "pressed by mistake"))!.Held.Should().BeFalse();
            var release = (await lane.JournalAsync(50)).Single(e => e.ChangeType == SourceAdmissionService.RefusingReleaseChangeType);
            release.Domain.Should().Be("bookshop.org");
            release.CanUndo.Should().BeTrue();

            now = now.AddMinutes(1);
            var undo = await lane.UndoAsync(release.Id, Admin);
            undo.Ok.Should().BeTrue(undo.Error);
            (await lane.ReviewAsync()).RefusingHosts.Single().Held.Should().BeTrue("the undo holds the host again");
            var journal = await lane.JournalAsync(50);
            journal.Single(e => e.Id == release.Id).CanUndo.Should().BeFalse();
            journal.Should().Contain(e => e.ChangeType == SourceJournal.Undo && e.Domain == "bookshop.org" && !e.CanUndo);
            (await lane.UndoAsync(release.Id, Admin)).Ok.Should().BeFalse("a release already undone is not undone twice");

            now = now.AddDays(1);
            await lane.RunDiscoveryAsync();
            scraper.ExcludeLists[1].Should().Contain("bookshop.org");

            (await lane.ReleaseRefusingHostAsync(host.Id, Admin, null))!.Held.Should().BeFalse();
            var second = (await lane.JournalAsync(50)).First(e => e.ChangeType == SourceAdmissionService.RefusingReleaseChangeType);
            now = now.AddDays(1);
            scraper.Refusals.Enqueue(new List<ScraperRefusingHost> { new() { Domain = "bookshop.org", Url = "https://bookshop.org/", HttpStatus = 403 } });
            await lane.RunDiscoveryAsync();
            (await lane.JournalAsync(50)).Single(e => e.Id == second.Id).CanUndo.Should().BeFalse("a later refusal already holds it again");
            (await lane.UndoAsync(second.Id, Admin)).Ok.Should().BeFalse();
        }

        [Fact]
        public async Task A_repeated_titles_undo_counts_the_rows_already_undone_as_such_and_an_older_stamp_never_undoes_a_newer_run()
        {
            await using var db = NewContext();
            var now = T0;
            var recipe = Recipe("https://facts.example/bbq/", "The Best Slow Cooker BBQ Chicken");
            recipe.SourceRights = SourceRightsRule.FactsOnly;
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            var rights = Rights(db, () => now);

            var first = await rights.TitlesAsync(apply: true);
            first.Changed.Should().Be(1);
            now = now.AddMinutes(1);
            (await rights.UndoStepAsync(SourceRightsService.TitlesStep, first.Stamp, apply: true)).Changed.Should().Be(1);
            now = now.AddMinutes(1);
            var again = await rights.UndoStepAsync(SourceRightsService.TitlesStep, first.Stamp, apply: false);
            again.Changed.Should().Be(0);
            again.AlreadyUndone.Should().Be(1);
            again.SkippedByReason.Should().ContainKey(SourceRightsService.AlreadyUndoneReason).And.NotContainKey("the title changed since");

            now = now.AddMinutes(1);
            var second = await rights.TitlesAsync(apply: true);
            second.Changed.Should().Be(1);
            now = now.AddMinutes(1);
            var stale = await rights.UndoStepAsync(SourceRightsService.TitlesStep, first.Stamp, apply: false);
            stale.Changed.Should().Be(0, "the first run was undone already; its stamp does not undo the second");
            stale.AlreadyUndone.Should().Be(1);
            (await rights.UndoStepAsync(SourceRightsService.TitlesStep, second.Stamp, apply: false)).Changed.Should().Be(1);
        }

        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ops"))) dir = dir.Parent;
            dir.Should().NotBeNull("the tests run inside the repository");
            return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        }

        [Fact]
        public async Task Every_row_of_the_forbidding_hosts_file_is_a_short_quote_that_the_rule_records_by_hand()
        {
            var lines = File.ReadAllLines(RepoFile("ops", "source-rights", "forbidding-hosts.tsv")).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            lines[0].Split('\t').Should().Equal("host", "class", "quote", "page", "date");
            var rows = lines.Skip(1).Select(l => l.Split('\t')).ToList();
            rows.Should().OnlyContain(r => r.Length == 5);
            rows.Select(r => SourceAdmissionPolicy.NormalizeDomain(r[0])).Should().OnlyHaveUniqueItems();
            await using var db = NewContext();
            var rights = Rights(db, () => T0);
            foreach (var row in rows)
            {
                row[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeLessThan(25, row[0]);
                var result = await rights.RecordByHandAsync(new SourceRightsByHandRequestModel
                {
                    Host = row[0], RightsClass = row[1], Quote = row[2], QuoteUrl = row[3], ReadOn = DateTime.Parse(row[4], System.Globalization.CultureInfo.InvariantCulture),
                }, apply: false, Admin);
                result.Refused.Should().BeNull(row[0]);
                result.Excluded.Should().BeTrue(row[0]);
            }
        }
    }
}
