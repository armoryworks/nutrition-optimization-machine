using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Staging checkpoint 6's source findings: hand-recorded classes are not stale and a withdrawn record leaves no derived
    /// time (R21); the staged import's log names the licence its rows get (R23); every source journal undo has a dry run
    /// that writes nothing and answers what the undo does; and a refusing host's hold, release and next run are shown by a
    /// preview of the next run and proven by a run an admin starts, without restarting the API.
    /// </summary>
    public class SourceLaneCheckpoint6Tests
    {
        private const long Admin = 7;
        private static readonly DateTime T0 = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private const string Quote = "you agree that you will not \"scrape,\" \"crawl\" or \"spider\" any web pages or other services contained in the Services";

        private sealed class FakeScraper : IRecipeScraperClient
        {
            private readonly Func<DateTime> _now;

            public FakeScraper(Func<DateTime> now) => _now = now;

            public List<IReadOnlyList<string>> SeedLists { get; } = new();

            public List<IReadOnlyList<string>> ExcludeLists { get; } = new();

            public Queue<List<ScraperRefusingHost>> Refusals { get; } = new();

            public bool IsConfigured { get; set; } = true;

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

            public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        private sealed class ListLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Messages.Add(formatter(state, exception));
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static SourceAdmissionService Lane(ApplicationDbContext db, IRecipeScraperClient scraper, Func<DateTime> now, bool enabled = true, int intervalHours = 24) =>
            new(db, scraper, new RecipeVettingService(),
                Options.Create(new SourceAdmissionSettings { Enabled = enabled, PollSeconds = 1, TermsOnlyChecks = true, FullChecks = true, SeedsPerRun = 5, IntervalHours = intervalHours }),
                NullLogger<SourceAdmissionService>.Instance, Options.Create(new SourceRightsSettings { Enforce = true }))
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = now,
            };

        private static SourceRightsService Rights(ApplicationDbContext db, Func<DateTime> now) =>
            new(db, Options.Create(new SourceRightsSettings { Enforce = true }), NullLogger<SourceRightsService>.Instance) { Now = now };

        private static async Task<ScrapingSourceEntity> SourceAsync(ApplicationDbContext db, string domain, string? rightsClass = null, Action<ScrapingSourceEntity>? configure = null)
        {
            var source = new ScrapingSourceEntity
            {
                Domain = domain, Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0.AddDays(-90), RightsClass = rightsClass,
            };
            configure?.Invoke(source);
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            return source;
        }

        private static SourceRightsByHandRequestModel Forbidding(string host) => new()
        {
            Host = host, RightsClass = SourceRightsRule.Forbidden, Quote = Quote, QuoteUrl = $"https://{host}/terms-conditions/", ReadOn = new DateTime(2026, 11, 21),
        };

        private static RecipeEntity Recipe(string url) => new()
        {
            AuthorId = Admin, Name = "Chili", Description = "A publisher's description.", SourceUrl = url, ScrapedAtUtc = T0.AddDays(-60),
            CurationStatusId = (long)CurationStatusEnum.Curated,
        };

        private static async Task<string> FingerprintAsync(ApplicationDbContext db)
        {
            var sources = await db.ScrapingSources.IgnoreQueryFilters().AsNoTracking().OrderBy(s => s.Id).ToListAsync();
            var records = await db.SourceRightsRecords.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
            var refusing = await db.SourceRefusingHosts.AsNoTracking().OrderBy(h => h.Id).ToListAsync();
            var recipes = await db.Recipes.IgnoreQueryFilters().AsNoTracking().OrderBy(r => r.Id).ToListAsync();
            var journal = await db.AuditLogEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
            return JsonSerializer.Serialize(new object[] { sources, records, refusing, recipes.Select(r => new { r.Id, r.SourceRights, r.ProseWithheld, r.SourceWithheldById }), journal.Select(a => new { a.Id, a.ChangeType, a.NewValue }) }, Json);
        }

        [Fact]
        public async Task A_class_recorded_by_hand_is_not_stale_and_its_withdrawal_leaves_no_derived_time_on_a_source_with_no_class()
        {
            await using var db = NewContext();
            var now = T0;
            var hand = await SourceAsync(db, "budgetbytes.com");
            await SourceAsync(db, "old.example", SourceRightsRule.FactsOnly, s => s.RightsRuleVersion = "source-rights/0");
            var rights = Rights(db, () => now);
            (await rights.RecordByHandAsync(Forbidding("budgetbytes.com"), apply: true, Admin)).Applied.Should().BeTrue();

            var status = await rights.StatusAsync();
            status.SourcesStale.Should().Be(1, "staging checkpoint 6, R21: the ten hand-recorded sources were counted stale; only old.example's derived class is");

            var lane = Lane(db, new FakeScraper(() => now), () => now);
            var recordEntry = (await lane.JournalAsync(50)).Single(e => e.RecordId != null && e.SourceId == 0);
            now = now.AddMinutes(1);
            (await lane.UndoAsync(recordEntry.Id, Admin)).Ok.Should().BeTrue();

            var withdrawn = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == hand.Id);
            withdrawn.RightsClass.Should().BeNull();
            withdrawn.RightsDerivedAtUtc.Should().BeNull("staging checkpoint 6, R21: the withdrawn record left a derived time on the unclassed source");
            (await rights.StatusAsync()).SourcesStale.Should().Be(1);
        }

        [Fact]
        public async Task The_staged_import_log_names_the_licence_the_rows_are_stored_with()
        {
            await using var db = NewContext();
            var hand = new ScrapingSourceEntity
            {
                Domain = "www.nutrition.va.gov", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.HandKind, LicenceKind = "public-domain", CreatedDate = T0,
            };
            db.ScrapingSources.Add(hand);
            await db.SaveChangesAsync();
            var log = new ListLogger<RecipeScrapingService>();
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(u => u.PersonId).Returns(Admin);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.StagedImportGateSetting] = "true" })
                .Build();
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            var import = new RecipeScrapingService(db, user.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object, vetting.Object, log, configuration);

            var result = await import.ImportStagedAsync(new StagedImportRequestModel
            {
                SourceId = hand.Id,
                SourceAttribution = "U.S. Department of Veterans Affairs",
                Recipes =
                {
                    new ScraperRecipe
                    {
                        Name = "Bean Skillet", SourceUrl = "https://www.nutrition.va.gov/docs/bean-skillet.pdf",
                        Ingredients = { new ScraperIngredient { RawLine = "2 cups cooked beans" } },
                        Steps = { new ScraperStep { Order = 1, Instruction = "Warm the beans." } },
                    },
                },
            });

            result.Imported.Should().Be(1);
            result.ImportedPublicDomain.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync()).LicenseStatus.Should().Be(RecipeLicenseStatus.PublicDomain);
            var line = log.Messages.Single(m => m.StartsWith("Staged import:", StringComparison.Ordinal));
            line.Should().Contain("1 stored PublicDomain").And.Contain("0 with the copyright quarantine").And.Contain("www.nutrition.va.gov");
            line.Should().NotContain("publicDomain=False", "staging checkpoint 6, R23: the line named the request's flag, not the licence the rows got");
        }

        [Fact]
        public async Task The_staged_import_log_names_each_rows_own_source_when_the_batch_names_another()
        {
            await using var db = NewContext();
            var hand = new ScrapingSourceEntity
            {
                Domain = "www.nutrition.va.gov", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.HandKind, LicenceKind = "public-domain", CreatedDate = T0,
            };
            db.ScrapingSources.Add(hand);
            await db.SaveChangesAsync();
            await SourceAsync(db, "other.example", SourceRightsRule.Clear);
            var log = new ListLogger<RecipeScrapingService>();
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(u => u.PersonId).Returns(Admin);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.StagedImportGateSetting] = "true" })
                .Build();
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            var import = new RecipeScrapingService(db, user.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object, vetting.Object, log, configuration);

            var result = await import.ImportStagedAsync(new StagedImportRequestModel
            {
                SourceId = hand.Id,
                SourceAttribution = "U.S. Department of Veterans Affairs",
                Recipes =
                {
                    new ScraperRecipe
                    {
                        Name = "Bean Skillet",
                        Ingredients = { new ScraperIngredient { RawLine = "2 cups cooked beans" } },
                        Steps = { new ScraperStep { Order = 1, Instruction = "Warm the beans." } },
                    },
                    new ScraperRecipe
                    {
                        Name = "Rice Bowl", SourceUrl = "https://other.example/rice-bowl/",
                        Ingredients = { new ScraperIngredient { RawLine = "1 cup cooked rice" } },
                        Steps = { new ScraperStep { Order = 1, Instruction = "Fill the bowl." } },
                    },
                },
            });

            result.Imported.Should().Be(2, string.Join("; ", result.Failures.Select(f => f.Error)));
            result.ImportedPublicDomain.Should().Be(1);
            var line = log.Messages.Single(m => m.StartsWith("Staged import:", StringComparison.Ordinal));
            line.Should().Contain("www.nutrition.va.gov: 1 imported, 1 PublicDomain")
                .And.Contain("other.example: 1 imported, 0 PublicDomain", "review of ck11-small: the line named only the batch's source, which did not give the URL row its licence");
        }

        [Fact]
        public async Task A_journal_undo_has_a_dry_run_that_writes_nothing_and_answers_what_the_undo_then_does()
        {
            await using var db = NewContext();
            var now = T0;
            var www = await SourceAsync(db, "www.budgetbytes.com");
            await SourceAsync(db, "budgetbytes.com");
            var facts = await SourceAsync(db, "facts.example", SourceRightsRule.FactsOnly);
            db.Recipes.AddRange(Recipe("https://www.budgetbytes.com/one/"), Recipe("https://budgetbytes.com/two/"));
            await db.SaveChangesAsync();
            var rights = Rights(db, () => now);
            (await rights.RecordByHandAsync(Forbidding("budgetbytes.com"), apply: true, Admin)).Applied.Should().BeTrue();
            (await rights.SetFactsOnlyUseAsync(facts.Id, true, Admin, "read the terms")).Ok.Should().BeTrue();
            var lane = Lane(db, new FakeScraper(() => now), () => now);
            var journal = await lane.JournalAsync(50);
            var recordEntry = journal.Single(e => e.RecordId != null && e.SourceId == 0);
            var approval = journal.Single(e => e.Field == SourceJournal.FactsOnlyField);
            now = now.AddMinutes(1);
            db.ChangeTracker.Clear();
            var before = await FingerprintAsync(db);

            var dry = await lane.PreviewUndoAsync(recordEntry.Id, Admin);

            (await FingerprintAsync(db)).Should().Be(before, "a dry run writes nothing");
            dry.WouldUndo.Should().BeTrue(dry.Refused);
            dry.Changes.Should().Contain(c => c.Entity == "ScrapingSource" && c.Id == www.Id && c.Field == nameof(ScrapingSourceEntity.RightsClass) && c.Before == SourceRightsRule.Forbidden && c.After == null);
            dry.Changes.Should().Contain(c => c.Entity == "SourceRightsRecord" && c.Field == nameof(SourceRightsRecordEntity.WithdrawnAtUtc) && c.After != null);
            dry.Recipes.Should().Be(2);
            dry.RecipeFields.Should().ContainKey(nameof(RecipeEntity.SourceRights));
            dry.Journal.Should().Contain(j => j.ChangeType == SourceJournal.Undo && j.EntityType == SourceJournal.RecordEntityType);
            var dryApproval = await lane.PreviewUndoAsync(approval.Id, Admin);
            dryApproval.Changes.Should().ContainSingle(c => c.Id == facts.Id && c.Field == nameof(ScrapingSourceEntity.FactsOnlyApprovedAt) && c.After == null);
            (await FingerprintAsync(db)).Should().Be(before);

            var applied = await lane.UndoAsync(recordEntry.Id, Admin);
            applied.Ok.Should().BeTrue(applied.Error);
            db.ChangeTracker.Clear();
            var source = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == www.Id);
            source.RightsClass.Should().BeNull();
            (await db.Recipes.AsNoTracking().CountAsync(r => r.SourceRights == null)).Should().Be(dry.Recipes);
            (await db.AuditLogEntries.AsNoTracking().CountAsync(a => a.Timestamp == now)).Should().Be(dry.Journal.Count, "the apply journals what the dry run said it would");

            var again = await lane.PreviewUndoAsync(recordEntry.Id, Admin);
            again.WouldUndo.Should().BeFalse();
            again.Refused.Should().Contain("already withdrawn");
            again.Changes.Should().BeEmpty();
            (await lane.PreviewUndoAsync(999_999, Admin)).NotFound.Should().BeTrue();
        }

        [Fact]
        public async Task The_next_run_preview_shows_a_refusing_host_held_then_released_and_a_run_an_admin_starts_sends_it_and_holds_it_again_when_it_refuses()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            var scraper = new FakeScraper(() => now);
            scraper.Refusals.Enqueue(new List<ScraperRefusingHost> { new() { Domain = "www.bookshop.org", Url = "https://bookshop.org/", HttpStatus = 403, BotChallenge = true } });
            var lane = Lane(db, scraper, () => now, intervalHours: 1);
            (await lane.RunDiscoveryNowAsync(Admin)).Run!.RefusingHosts.Should().Be(1);

            var held = await lane.PreviewDiscoveryAsync();
            held.Seeds.Should().Equal("seed.example");
            held.RefusingHeld.Should().Equal("bookshop.org");
            held.ExcludeDomains.Should().Contain("bookshop.org", "staging checkpoint 6: the hold on the next run could be shown only after another start");
            held.Skipped.Should().BeNull();
            var runs = await db.SourceDiscoveryRuns.CountAsync();

            var host = (await lane.ReviewAsync()).RefusingHosts.Single();
            now = now.AddHours(1);
            (await lane.ReleaseRefusingHostAsync(host.Id, Admin, "looked at the 403"))!.Held.Should().BeFalse();
            var released = await lane.PreviewDiscoveryAsync();
            released.RefusingHeld.Should().BeEmpty();
            released.ExcludeDomains.Should().NotContain("bookshop.org");
            (await db.SourceDiscoveryRuns.CountAsync()).Should().Be(runs, "a preview starts no run");
            scraper.SeedLists.Should().HaveCount(1, "a preview sends nothing to the scraper");

            now = now.AddHours(1);
            scraper.Refusals.Enqueue(new List<ScraperRefusingHost> { new() { Domain = "bookshop.org", Url = "https://bookshop.org/", HttpStatus = 403 } });
            var next = await lane.RunDiscoveryNowAsync(Admin);
            next.Refused.Should().BeNull();
            scraper.ExcludeLists[1].Should().NotContain("bookshop.org", "the release reaches the run after it");
            next.Run!.RefusingHosts.Should().Be(1);
            (await lane.PreviewDiscoveryAsync()).ExcludeDomains.Should().Contain("bookshop.org", "a refusal after the release holds it again");
        }

        [Fact]
        public async Task An_admin_run_keeps_the_lanes_pacing_and_the_timer_counts_it()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            var scraper = new FakeScraper(() => now);
            var lane = Lane(db, scraper, () => now);
            (await lane.RunDiscoveryNowAsync(Admin)).Run.Should().NotBeNull();

            now = T0.AddHours(1);
            var again = await lane.RunDiscoveryNowAsync(Admin);
            again.Run.Should().BeNull("review of ck11-small: an admin could start a full, budgeted run each time the last one ended, past SourceDiscovery:IntervalHours");
            again.Refused.Should().Contain("IntervalHours");
            again.NextRunDueUtc.Should().Be(T0.AddHours(24));
            scraper.SeedLists.Should().HaveCount(1);
            (await lane.NextDiscoveryDueUtcAsync()).Should().Be(T0.AddHours(24), "the timer reads the same time, so an admin's run moves the timer's next run");
            (await lane.PreviewDiscoveryAsync()).NextRunDueUtc.Should().Be(T0.AddHours(24));

            now = T0.AddHours(24);
            (await lane.RunDiscoveryNowAsync(Admin)).Run.Should().NotBeNull();
            scraper.SeedLists.Should().HaveCount(2);
            (await lane.PreviewDiscoveryAsync()).NextRunDueUtc.Should().Be(T0.AddHours(48));
        }

        [Fact]
        public async Task The_next_run_preview_applies_the_classes_recorded_by_hand_first_and_says_when_the_run_would_fail()
        {
            await using var db = NewContext();
            var now = T0;
            await SourceAsync(db, "seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            await SourceAsync(db, "budgetbytes.com");
            (await Rights(db, () => now).RecordByHandAsync(Forbidding("budgetbytes.com"), apply: true, Admin)).Applied.Should().BeTrue();
            await SourceAsync(db, "www.budgetbytes.com", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            db.ChangeTracker.Clear();
            var before = await FingerprintAsync(db);
            var scraper = new FakeScraper(() => now);
            var lane = Lane(db, scraper, () => now);

            var preview = await lane.PreviewDiscoveryAsync();

            (await FingerprintAsync(db)).Should().Be(before, "a preview writes nothing");
            preview.RecordsApplied.Should().Be(1);
            preview.Seeds.Should().Equal("seed.example");
            preview.SeedsHeldBack.Should().BeEmpty("review of ck11-small: the run puts the record on the new row first, so the row is no seed and no fault; the preview skipped that step");
            preview.Fails.Should().BeNull();
            await lane.RunDiscoveryAsync();
            scraper.SeedLists.Single().Should().Equal(preview.Seeds);

            for (var i = 0; i <= SourceDiscoveryExclusion.MaxEntries; i++)
            {
                db.ScrapingSources.Add(new ScrapingSourceEntity
                {
                    Domain = $"barred{i}.example", Status = ScrapingSourceStatusEnum.Pending, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0, RightsClass = SourceRightsRule.Forbidden,
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var full = await lane.PreviewDiscoveryAsync();
            full.Skipped.Should().BeNull();
            full.Fails.Should().Contain("forbidden or permission-needed hosts", "review of ck11-small: the run ends failed without sending anything, and the preview showed a seed list as if it would start");
            now = now.AddDays(2);
            (await lane.RunDiscoveryAsync()).Status.Should().Be(SourceDiscoveryRunEntity.Failed);
            scraper.SeedLists.Should().HaveCount(1);
        }

        [Fact]
        public async Task An_admin_cannot_start_a_discovery_run_while_the_lane_is_off_or_no_scraper_is_configured()
        {
            await using var db = NewContext();
            await SourceAsync(db, "seed.example", SourceRightsRule.Clear, s => s.CheckedAtUtc = T0);
            var scraper = new FakeScraper(() => T0);

            var off = await Lane(db, scraper, () => T0, enabled: false).RunDiscoveryNowAsync(Admin);
            off.Run.Should().BeNull();
            off.Refused.Should().Contain("SourceDiscovery:Enabled=false");
            (await Lane(db, scraper, () => T0, enabled: false).PreviewDiscoveryAsync()).Enabled.Should().BeFalse();
            scraper.IsConfigured = false;
            (await Lane(db, scraper, () => T0).RunDiscoveryNowAsync(Admin)).Refused.Should().Contain("scraper");
            scraper.SeedLists.Should().BeEmpty();
            (await db.SourceDiscoveryRuns.CountAsync()).Should().Be(0);
        }
    }
}
