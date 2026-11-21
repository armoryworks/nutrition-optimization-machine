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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Extensions;
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
    /// The NOM side of recipe sources: discovery and checks through the scraper's jobs (a lost or
    /// failed job is a failed run that keeps what it found), the approval queue that holds only
    /// clear candidates, the re-judging of approved sources (harvesting paused only when a clear
    /// verdict turns not clear), probation, the harvestable list, the journaled actions and their
    /// undo, and the staged-import gate.
    /// </summary>
    public class SourceAdmissionServiceTests
    {
        private const long Admin = 7;
        private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private sealed class FakeScraper : IRecipeScraperClient
        {
            public readonly Queue<ScraperJobAnswer> Starts = new();
            public readonly Queue<ScraperJobAnswer> Polls = new();
            public readonly List<(string Kind, IReadOnlyList<string> Seeds, string? Domain)> Started = new();

            public bool IsConfigured => true;

            public Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, CancellationToken cancellationToken = default)
            {
                Started.Add(("discovery", seedDomains, null));
                return Task.FromResult(Starts.Dequeue());
            }

            public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default)
            {
                Started.Add(("source-check", Array.Empty<string>(), domain));
                return Task.FromResult(Starts.Dequeue());
            }

            public Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Polls.Dequeue());
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static SourceAdmissionService Service(ApplicationDbContext db, FakeScraper scraper, Func<DateTime>? now = null, Action<SourceAdmissionSettings>? configure = null)
        {
            var settings = new SourceAdmissionSettings { Enabled = true, PollSeconds = 1 };
            configure?.Invoke(settings);
            var service = new SourceAdmissionService(db, scraper, new RecipeVettingService(), Options.Create(settings), NullLogger<SourceAdmissionService>.Instance)
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = now ?? (() => T0),
            };
            return service;
        }

        private static ScraperJob Job(string kind, string status, object? result, string id = "job1") => new()
        {
            JobId = id,
            Kind = kind,
            Status = status,
            CreatedUtc = T0,
            DeadlineUtc = T0.AddMinutes(10),
            BudgetSeconds = 600,
            Result = result == null ? null : JsonSerializer.SerializeToElement(result, Json),
        };

        private static async Task<ScrapingSourceEntity> SourceAsync(ApplicationDbContext db, string domain, ScrapingSourceStatusEnum status = ScrapingSourceStatusEnum.Approved, string? verdict = null)
        {
            var source = new ScrapingSourceEntity { Domain = domain, Status = status, CheckVerdict = verdict, CreatedDate = T0.AddDays(-60) };
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            return source;
        }

        private static ScraperRecipe SampleRecipe(int i) => new()
        {
            Name = $"Garden soup {i}",
            Ingredients =
            {
                new ScraperIngredient { RawLine = "2 cups stock", Name = "stock", Quantity = 2, Unit = "cup" },
                new ScraperIngredient { RawLine = "1 onion", Name = "onion", Quantity = 1 },
                new ScraperIngredient { RawLine = "salt to taste", Name = "salt" },
            },
            Steps =
            {
                new ScraperStep { Order = 1, Instruction = "Chop the onion and soften it in a pot." },
                new ScraperStep { Order = 2, Instruction = "Add the stock and simmer for twenty minutes." },
            },
        };

        private static ScraperSourceCheckReport Report(string domain, bool clear = true, Action<ScraperSourceCheckReport>? change = null)
        {
            var report = new ScraperSourceCheckReport
            {
                Domain = domain,
                RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion,
                Verdict = clear ? "clear" : "not-clear",
                Complete = true,
                Reasons = SourceAdmissionPolicy.ScraperReasons.Select(code => new ScraperCheckReason
                {
                    Code = code,
                    Outcome = code is "crawl-delay" or "sample-yield" ? "info" : !clear && code == "licence" ? "fail" : "pass",
                    Detail = "fixture",
                }).ToList(),
                Robots = new ScraperCheckRobots { Status = "found", NomBot = new ScraperCheckRobotsGroup { AllowsHomepage = true, CrawlDelaySeconds = 20 } },
                Licence = new ScraperCheckLicence
                {
                    Kind = clear ? "cc0" : null,
                    Statements = { new ScraperCheckStatement { Kind = clear ? "cc0" : "all-rights-reserved", FoundAt = $"https://{domain}/terms", Text = clear ? "Recipes on this site are CC0." : "All rights reserved." } },
                },
                Samples = new ScraperCheckSamples
                {
                    Requested = 20,
                    Fetched = 20,
                    Pages = Enumerable.Range(1, 20).Select(i => new ScraperCheckSamplePage { Url = $"https://{domain}/r{i}", HasRecipeJsonLd = true, Recipe = SampleRecipe(i) }).ToList(),
                },
            };
            change?.Invoke(report);
            return report;
        }

        private static ScraperDiscoveryResult Found(params string[] domains) => new()
        {
            Candidates = domains.Select(d => new ScraperDiscoveredSource { Domain = d, EvidenceUrl = $"https://www.{d}/a-recipe/", Signal = "RecipeJsonLd", DiscoveredVia = "seed.example" }).ToList(),
        };

        private static async Task<SourceCandidateEntity> CandidateAsync(ApplicationDbContext db, string domain)
        {
            var candidate = new SourceCandidateEntity
            {
                Domain = domain, EvidenceUrl = $"https://www.{domain}/a-recipe/", FoundVia = "seed.example", Signal = "RecipeJsonLd",
                FirstSeenUtc = T0, LastSeenUtc = T0, TimesSeen = 1, State = SourceCandidateEntity.New,
            };
            db.SourceCandidates.Add(candidate);
            await db.SaveChangesAsync();
            return candidate;
        }

        private static void CheckJob(FakeScraper scraper, ScraperSourceCheckReport? report, string final = ScraperJob.Completed)
        {
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job("source-check", ScraperJob.Running, null)));
            scraper.Polls.Enqueue(ScraperJobAnswer.Of(Job("source-check", ScraperJob.Running, null)));
            scraper.Polls.Enqueue(ScraperJobAnswer.Of(Job("source-check", final, report)));
        }

        [Fact]
        public async Task A_discovery_run_polls_its_job_keeps_candidates_and_rotates_only_the_seeds_it_visited()
        {
            using var db = NewContext();
            var a = await SourceAsync(db, "www.a.example");
            var b = await SourceAsync(db, "b.example");
            await SourceAsync(db, "rejected.example", ScrapingSourceStatusEnum.Rejected);
            var scraper = new FakeScraper();
            var partial = Found("new.example");
            var final = Found("new.example", "rejected.example", "b.example");
            final.SeedsVisited = new() { "a.example" };
            final.SeedsNotVisited = new() { "b.example" };
            final.CandidatesNotProbed = new() { new ScraperDiscoveredSource { Domain = "lead.example", DiscoveredVia = "a.example" } };
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Running, null)));
            scraper.Polls.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Running, partial)));
            scraper.Polls.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Partial, final)));

            var run = await Service(db, scraper).RunDiscoveryAsync();

            run.Status.Should().Be(SourceDiscoveryRunEntity.Partial);
            scraper.Started.Single().Seeds.Should().BeEquivalentTo(new[] { "a.example", "b.example" });
            var candidates = await db.SourceCandidates.OrderBy(c => c.Domain).ToListAsync();
            candidates.Select(c => (c.Domain, c.State)).Should().Equal(("lead.example", SourceCandidateEntity.Lead), ("new.example", SourceCandidateEntity.New));
            (await db.ScrapingSources.FindAsync(a.Id))!.LastDiscoveredUtc.Should().Be(T0);
            (await db.ScrapingSources.FindAsync(b.Id))!.LastDiscoveredUtc.Should().BeNull();
        }

        [Fact]
        public async Task A_lost_discovery_job_is_a_failed_run_that_keeps_what_it_had_found()
        {
            using var db = NewContext();
            await SourceAsync(db, "seed.example");
            var scraper = new FakeScraper();
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Running, null)));
            scraper.Polls.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Running, Found("kept.example"))));
            scraper.Polls.Enqueue(ScraperJobAnswer.Fail(ScraperJobOutcome.NotFound, "gone"));

            var run = await Service(db, scraper).RunDiscoveryAsync();

            run.Status.Should().Be(SourceDiscoveryRunEntity.Failed);
            run.Error.Should().StartWith("lost");
            run.Candidates.Should().Be(1);
            (await db.SourceCandidates.SingleAsync()).Domain.Should().Be("kept.example");
            (await db.SourceDiscoveryRuns.SingleAsync()).Status.Should().Be(SourceDiscoveryRunEntity.Failed);
        }

        [Fact]
        public async Task A_job_that_never_ends_is_a_failed_run_not_an_empty_one()
        {
            using var db = NewContext();
            await SourceAsync(db, "seed.example");
            var scraper = new FakeScraper();
            var clock = T0;
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Running, null)));
            for (var i = 0; i < 5; i++) scraper.Polls.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Running, null)));
            var service = Service(db, scraper, () => clock);
            service.Delay = (_, _) => { clock = clock.AddMinutes(4); return Task.CompletedTask; };

            var run = await service.RunDiscoveryAsync();

            run.Status.Should().Be(SourceDiscoveryRunEntity.Failed);
            run.Error.Should().Contain("deadline");
        }

        [Fact]
        public async Task A_discovery_job_that_cannot_start_is_failed_and_a_busy_scraper_skips_the_run()
        {
            using var db = NewContext();
            await SourceAsync(db, "seed.example");
            var scraper = new FakeScraper();
            scraper.Starts.Enqueue(ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, "connection refused"));
            scraper.Starts.Enqueue(ScraperJobAnswer.Fail(ScraperJobOutcome.Busy, "busy"));
            var service = Service(db, scraper);

            (await service.RunDiscoveryAsync()).Status.Should().Be(SourceDiscoveryRunEntity.Failed);
            (await service.RunDiscoveryAsync()).Status.Should().Be(SourceDiscoveryRunEntity.Skipped);
            var status = await service.StatusAsync();
            status.RunsFailedInRow.Should().Be(1);
            status.LastGoodRunUtc.Should().BeNull();
        }

        [Fact]
        public async Task Only_a_clear_candidate_is_queued_and_its_one_click_approval_starts_a_probation()
        {
            using var db = NewContext();
            var open = await CandidateAsync(db, "open.example");
            var closed = await CandidateAsync(db, "closed.example");
            var scraper = new FakeScraper();
            CheckJob(scraper, Report("open.example"));
            CheckJob(scraper, Report("closed.example", clear: false));
            var service = Service(db, scraper);

            (await service.RunNextCheckAsync())!.Verdict.Should().Be(SourceAdmissionPolicy.Clear);
            (await service.RunNextCheckAsync())!.Verdict.Should().Be(SourceAdmissionPolicy.NotClear);
            scraper.Started.Select(s => s.Domain).Should().BeEquivalentTo(new[] { "www.open.example", "www.closed.example" });

            var review = await service.ReviewAsync();
            review.Queue.Select(c => c.Domain).Should().Equal("open.example");
            review.Queue.Single().Check!.Facts!.LicenceStatements.Single().FoundAt.Should().Be("https://open.example/terms");
            review.Queue.Single().Check!.Facts!.CrawlDelaySeconds.Should().Be(20);
            review.NotClear.Select(c => c.Domain).Should().Equal("closed.example");
            review.NotClear.Single().Check!.Reasons.Where(r => r.Outcome == "fail").Select(r => r.Code).Should().Contain(new[] { "licence", "licence-kind" });

            var refused = await service.ApproveCandidateAsync(closed.Id, Admin, null);
            refused.Ok.Should().BeFalse();
            (await db.ScrapingSources.CountAsync()).Should().Be(0);

            var approved = await service.ApproveCandidateAsync(open.Id, Admin, null);
            approved.Ok.Should().BeTrue();
            var source = await db.ScrapingSources.SingleAsync();
            source.Domain.Should().Be("www.open.example");
            source.Status.Should().Be(ScrapingSourceStatusEnum.Approved);
            source.ProbationUntilUtc.Should().Be(T0.AddDays(21));
            source.LicenceKind.Should().Be("cc0");
            (await db.AuditLogEntries.SingleAsync(e => e.ChangeType == SourceJournal.Approve)).ChangedByPersonId.Should().Be(Admin);

            var harvest = await service.HarvestableAsync();
            harvest.Single().Should().BeEquivalentTo(new HarvestableSourceModel { Domain = "www.open.example", Probation = true, RecipeLimit = 50, CrawlDelaySeconds = 20 });
        }

        [Fact]
        public async Task A_clear_check_that_is_too_old_or_inconclusive_cannot_be_approved()
        {
            using var db = NewContext();
            var candidate = await CandidateAsync(db, "open.example");
            var scraper = new FakeScraper();
            CheckJob(scraper, Report("open.example"));
            await Service(db, scraper).RunNextCheckAsync();

            var later = Service(db, scraper, () => T0.AddDays(31));
            (await later.ApproveCandidateAsync(candidate.Id, Admin, null)).Error.Should().Contain("older than 30 days");
            (await later.ReviewAsync()).Queue.Should().BeEmpty();

            using var db2 = NewContext();
            var partial = await CandidateAsync(db2, "half.example");
            var scraper2 = new FakeScraper();
            CheckJob(scraper2, Report("half.example", change: r => { r.Complete = false; r.Verdict = "clear"; }), ScraperJob.Partial);
            var service2 = Service(db2, scraper2);
            var check = await service2.RunNextCheckAsync();
            check!.Conclusive.Should().BeFalse();
            (await service2.ApproveCandidateAsync(partial.Id, Admin, null)).Ok.Should().BeFalse();
        }

        [Fact]
        public async Task A_lost_check_job_is_stored_as_lost_and_changes_no_candidate()
        {
            using var db = NewContext();
            var candidate = await CandidateAsync(db, "open.example");
            var scraper = new FakeScraper();
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job("source-check", ScraperJob.Running, null)));
            scraper.Polls.Enqueue(ScraperJobAnswer.Fail(ScraperJobOutcome.NotFound, "gone"));
            var service = Service(db, scraper);

            var check = await service.RunNextCheckAsync();

            check!.Status.Should().Be(SourceCheckEntity.Lost);
            check.Verdict.Should().BeNull();
            (await db.SourceCandidates.FindAsync(candidate.Id))!.State.Should().Be(SourceCandidateEntity.New);
            (await service.ReviewAsync()).FailedChecks.Single().Status.Should().Be(SourceCheckEntity.Lost);
            (await service.StatusAsync()).ChecksFailedLast7d.Should().Be(1);
        }

        [Fact]
        public async Task Approved_sources_are_rechecked_first_and_a_first_not_clear_verdict_only_shows()
        {
            using var db = NewContext();
            await CandidateAsync(db, "open.example");
            var current = await SourceAsync(db, "www.blog.example");
            var scraper = new FakeScraper();
            CheckJob(scraper, Report("blog.example", clear: false));
            var service = Service(db, scraper);

            var check = await service.RunNextCheckAsync();

            check!.Purpose.Should().Be(SourceAdmissionService.RecheckPurpose);
            scraper.Started.Single().Domain.Should().Be("www.blog.example");
            var source = (await db.ScrapingSources.FindAsync(current.Id))!;
            source.CheckVerdict.Should().Be(SourceAdmissionPolicy.NotClear);
            source.Status.Should().Be(ScrapingSourceStatusEnum.Approved);
            source.HarvestPausedAt.Should().BeNull();
            (await service.HarvestableAsync()).Select(h => h.Domain).Should().Equal("www.blog.example");
            (await service.ReviewAsync()).Approved.Single().Check!.Verdict.Should().Be(SourceAdmissionPolicy.NotClear);
        }

        [Fact]
        public async Task The_weekly_recheck_pauses_harvesting_when_a_clear_verdict_turns_not_clear()
        {
            using var db = NewContext();
            var source = await SourceAsync(db, "open.example", verdict: SourceAdmissionPolicy.Clear);
            source.CheckedAtUtc = T0.AddDays(-8);
            await db.SaveChangesAsync();
            var scraper = new FakeScraper();
            CheckJob(scraper, Report("open.example", clear: false));
            var service = Service(db, scraper);

            await service.RunNextCheckAsync();

            var paused = (await db.ScrapingSources.FindAsync(source.Id))!;
            paused.HarvestPausedAt.Should().Be(T0);
            paused.HarvestPausedReason.Should().Contain("turned not clear").And.Contain("licence");
            paused.Status.Should().Be(ScrapingSourceStatusEnum.Approved);
            (await service.HarvestableAsync()).Should().BeEmpty();
            (await db.AuditLogEntries.Select(e => e.ChangeType).ToListAsync()).Should().BeEquivalentTo(new[] { SourceJournal.Recheck, SourceJournal.HarvestPause });
        }

        [Fact]
        public async Task An_inconclusive_recheck_of_a_clear_source_pauses_nothing()
        {
            using var db = NewContext();
            var source = await SourceAsync(db, "open.example", verdict: SourceAdmissionPolicy.Clear);
            source.CheckedAtUtc = T0.AddDays(-8);
            await db.SaveChangesAsync();
            var scraper = new FakeScraper();
            CheckJob(scraper, Report("open.example", clear: false, r => r.Complete = false), ScraperJob.Partial);

            await Service(db, scraper).RunNextCheckAsync();

            var after = (await db.ScrapingSources.FindAsync(source.Id))!;
            after.HarvestPausedAt.Should().BeNull();
            after.CheckVerdict.Should().Be(SourceAdmissionPolicy.Clear);
            after.CheckedAtUtc.Should().Be(T0);
        }

        [Fact]
        public async Task Checks_stop_at_the_daily_cap_and_a_source_checked_this_week_is_not_due()
        {
            using var db = NewContext();
            var source = await SourceAsync(db, "open.example", verdict: SourceAdmissionPolicy.Clear);
            source.CheckedAtUtc = T0.AddDays(-2);
            await db.SaveChangesAsync();
            var scraper = new FakeScraper();
            (await Service(db, scraper).RunNextCheckAsync()).Should().BeNull();

            await CandidateAsync(db, "new.example");
            (await Service(db, scraper, configure: s => s.MaxChecksPerDay = 0).RunNextCheckAsync()).Should().BeNull();
            scraper.Started.Should().BeEmpty();
        }

        [Fact]
        public async Task A_pending_request_is_checked_as_a_candidate_and_a_rejected_domain_is_never_clear()
        {
            using var db = NewContext();
            await SourceAsync(db, "asked.example", ScrapingSourceStatusEnum.Pending);
            var scraper = new FakeScraper();
            CheckJob(scraper, Report("asked.example"));
            var service = Service(db, scraper);

            var check = await service.RunNextCheckAsync();

            check!.Purpose.Should().Be(SourceAdmissionService.RequestPurpose);
            var candidate = await db.SourceCandidates.SingleAsync();
            candidate.State.Should().Be(SourceCandidateEntity.Clear);
            (await service.ApproveCandidateAsync(candidate.Id, Admin, null)).Ok.Should().BeTrue();
            (await db.ScrapingSources.SingleAsync()).Status.Should().Be(ScrapingSourceStatusEnum.Approved);

            var facts = SourceAdmissionPolicy.Facts(Report("x.example"), 20, 20, rejectedBefore: true);
            SourceAdmissionPolicy.Judge(facts).Clear.Should().BeFalse();
        }

        [Fact]
        public async Task Pausing_resuming_and_undoing_are_journaled_and_the_harvestable_list_follows()
        {
            using var db = NewContext();
            var source = await SourceAsync(db, "open.example");
            var service = Service(db, new FakeScraper());

            (await service.PauseHarvestAsync(source.Id, Admin, "owner asked")).Ok.Should().BeTrue();
            (await service.HarvestableAsync()).Should().BeEmpty();
            (await service.PauseHarvestAsync(source.Id, Admin, null)).Ok.Should().BeFalse();

            var pause = await db.AuditLogEntries.SingleAsync();
            (await service.JournalAsync(10)).Single().CanUndo.Should().BeTrue();
            (await service.UndoAsync(pause.Id, Admin)).Ok.Should().BeTrue();
            (await service.HarvestableAsync()).Select(h => h.Domain).Should().Equal("open.example");

            var journal = await service.JournalAsync(10);
            journal.Select(j => j.ChangeType).Should().Equal(SourceJournal.Undo, SourceJournal.HarvestPause);
            journal.Single(j => j.ChangeType == SourceJournal.HarvestPause).CanUndo.Should().BeFalse();
            (await service.UndoAsync(pause.Id, Admin)).Ok.Should().BeFalse();

            var undoRow = journal.First();
            (await service.UndoAsync(undoRow.Id, Admin)).Ok.Should().BeTrue();
            (await db.ScrapingSources.FindAsync(source.Id))!.HarvestPausedReason.Should().Be("owner asked");
        }

        [Fact]
        public async Task Withholding_a_sources_recipes_hides_them_from_everyone_and_undo_brings_them_back()
        {
            using var db = NewContext();
            var source = await SourceAsync(db, "www.blog.example");
            db.Set<Nom.Data.Reference.ReferenceEntity>().Add(new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" });
            RecipeEntity Mine(string name, string url) => new() { Name = name, SourceUrl = url, AuthorId = Admin, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Recipes.AddRange(
                Mine("Blog soup", "https://www.blog.example/soup/"),
                Mine("Bare-host stew", "https://blog.example/stew/"),
                Mine("Other pie", "https://other.example/pie/"),
                Mine("Lookalike", "https://notblog.example/x/"));
            await db.SaveChangesAsync();
            (await db.Recipes.VisibleTo(db, Admin).CountAsync()).Should().Be(4);
            await db.SaveChangesAsync();
            var service = Service(db, new FakeScraper());

            var dry = await service.WithholdRecipesAsync(source.Id, Admin, "owner's decision", apply: false);
            dry.Applied.Should().BeFalse();
            dry.Recipes.Should().Be(2);
            (await db.Recipes.CountAsync(r => r.SourceWithheldById != null)).Should().Be(0);

            (await service.WithholdRecipesAsync(source.Id, Admin, "owner's decision", apply: true)).Recipes.Should().Be(2);
            (await db.Recipes.VisibleTo(db, Admin).Select(r => r.Name).ToListAsync()).Should().BeEquivalentTo(new[] { "Other pie", "Lookalike" });
            (await service.HarvestableAsync()).Should().BeEmpty();

            var withhold = await db.AuditLogEntries.SingleAsync(e => e.ChangeType == SourceJournal.Withhold);
            (await service.UndoAsync(withhold.Id, Admin)).Ok.Should().BeTrue();
            (await db.Recipes.VisibleTo(db, Admin).CountAsync()).Should().Be(4);
            (await db.ScrapingSources.FindAsync(source.Id))!.RecipesWithheldAt.Should().BeNull();
            (await db.AuditLogEntries.CountAsync(e => e.ChangeType == SourceJournal.Undo && e.PropertyName == SourceJournal.RecipesField)).Should().Be(1);
        }

        [Fact]
        public async Task A_recipe_imported_from_a_withheld_source_is_withheld_too()
        {
            using var db = NewContext();
            var source = await SourceAsync(db, "blog.example");
            source.RecipesWithheldAt = T0;
            await db.SaveChangesAsync();
            var import = Staged(db, gate: false);

            await import.ImportStagedAsync(new StagedImportRequestModel { Recipes = { Record("Late soup", "https://www.blog.example/late/") } });

            (await db.Recipes.SingleAsync()).SourceWithheldById.Should().Be(source.Id);
        }

        [Fact]
        public async Task A_probation_passes_on_good_figures_and_a_failed_one_pauses_harvesting()
        {
            using var db = NewContext();
            var good = await SourceAsync(db, "good.example");
            var bad = await SourceAsync(db, "bad.example");
            var idle = await SourceAsync(db, "idle.example");
            foreach (var s in new[] { good, bad, idle })
            {
                s.ProbationUntilUtc = T0.AddDays(1);
                s.ProbationRecipeLimit = 2;
            }

            idle.ProbationUntilUtc = T0.AddDays(-1);
            var food = new IngredientEntity { Name = "stock" };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            void Add(string host, int i, long status, params string[] lines) => db.Recipes.Add(new RecipeEntity
            {
                Name = $"{host} {i}", SourceUrl = $"https://{host}/{i}/", CurationStatusId = status,
                RecipeIngredients = lines.Select((l, n) => new RecipeIngredientEntity { IngredientId = food.Id, SortOrder = n, RawLine = l, Quantity = l.StartsWith('2') ? 2 : 0, MeasurementId = 1 }).ToList(),
            });
            Add("good.example", 1, (long)CurationStatusEnum.NonCurated, "2 cups stock", "salt to taste");
            Add("good.example", 2, (long)CurationStatusEnum.NonCurated, "2 cups stock");
            Add("bad.example", 1, (long)CurationStatusEnum.RequiresRevision, "2 cups stock");
            Add("bad.example", 2, (long)CurationStatusEnum.NonCurated, "stock");
            await db.SaveChangesAsync();

            (await Service(db, new FakeScraper()).WatchAsync()).Should().Be(3);

            var g = (await db.ScrapingSources.FindAsync(good.Id))!;
            g.ProbationUntilUtc.Should().BeNull();
            g.ProbationResult.Should().StartWith("passed");
            g.HarvestPausedAt.Should().BeNull();
            var b = (await db.ScrapingSources.FindAsync(bad.Id))!;
            b.ProbationResult.Should().StartWith("failed");
            b.HarvestPausedReason.Should().StartWith("probation failed");
            (await db.ScrapingSources.FindAsync(idle.Id))!.ProbationResult.Should().Contain("no recipe was harvested");
            (await db.AuditLogEntries.CountAsync(e => e.ChangeType == SourceJournal.ProbationFail)).Should().Be(2);
        }

        [Fact]
        public async Task A_hand_source_needs_its_licence_facts_and_records_them()
        {
            using var db = NewContext();
            var service = Service(db, new FakeScraper());

            (await service.RecordHandSourceAsync(new HandSourceRequestModel { Domain = "www.cancer.gov", Title = "NCI Down Home Healthy Cooking", LicenceKind = "public-domain" }, Admin))
                .Ok.Should().BeFalse();
            (await service.RecordHandSourceAsync(new HandSourceRequestModel
            {
                Domain = "www.cancer.gov", Title = "NCI", LicenceKind = "cc-by-sa", LicenceStatement = "x", LicenceFoundAt = "https://www.cancer.gov/policies/copyright-reuse",
            }, Admin)).Ok.Should().BeFalse();

            var result = await service.RecordHandSourceAsync(new HandSourceRequestModel
            {
                Domain = "www.cancer.gov",
                Title = "NCI Down Home Healthy Cooking",
                LicenceKind = "public-domain",
                LicenceStatement = "all text within National Cancer Institute (NCI) products is free of copyright",
                LicenceFoundAt = "https://www.cancer.gov/policies/copyright-reuse",
            }, Admin);

            result.Ok.Should().BeTrue();
            var source = await db.ScrapingSources.SingleAsync();
            source.Kind.Should().Be(ScrapingSourceEntity.HandKind);
            source.LicenceEnteredByPersonId.Should().Be(Admin);
            (await service.HarvestableAsync()).Should().BeEmpty();
            (await db.AuditLogEntries.Select(e => new { e.ChangeType, e.PropertyName }).ToListAsync()).Should().BeEquivalentTo(new[]
            {
                new { ChangeType = SourceJournal.HandRecord, PropertyName = (string?)SourceJournal.StatusField },
                new { ChangeType = SourceJournal.HandRecord, PropertyName = (string?)SourceJournal.LicenceField },
            });
        }

        [Fact]
        public async Task The_staged_import_gate_takes_only_approved_sources_and_the_licence_from_their_record()
        {
            using var db = NewContext();
            var hand = await SourceAsync(db, "www.cancer.gov");
            hand.Kind = ScrapingSourceEntity.HandKind;
            hand.LicenceKind = "public-domain";
            await SourceAsync(db, "www.blog.example");
            await SourceAsync(db, "pending.example", ScrapingSourceStatusEnum.Pending);
            await db.SaveChangesAsync();
            var import = Staged(db, gate: true);

            var refused = await import.ImportStagedAsync(new StagedImportRequestModel
            {
                PublicDomain = true,
                Recipes = { Record("Book soup", null), Record("Pending pie", "https://pending.example/pie/"), Record("Blog stew", "https://blog.example/stew/") },
            });

            refused.Imported.Should().Be(1);
            refused.Refused.Should().Be(2);
            refused.PublicDomainFlagIgnored.Should().BeTrue();
            var blog = await db.Recipes.SingleAsync();
            blog.Name.Should().Be("Blog stew");
            blog.LicenseStatus.Should().Be(RecipeLicenseStatus.Unknown);

            var book = await import.ImportStagedAsync(new StagedImportRequestModel { SourceId = hand.Id, Recipes = { Record("Book soup", null) } });

            book.Imported.Should().Be(1);
            (await db.Recipes.SingleAsync(r => r.Name == "Book soup")).LicenseStatus.Should().Be(RecipeLicenseStatus.PublicDomain);

            var other = await import.ImportStagedAsync(new StagedImportRequestModel { SourceId = hand.Id, Recipes = { Record("Elsewhere", "https://other.example/x/") } });
            other.Refused.Should().Be(1);
        }

        [Fact]
        public async Task Without_the_gate_the_staged_import_keeps_its_old_behaviour()
        {
            using var db = NewContext();
            var import = Staged(db, gate: false);

            var result = await import.ImportStagedAsync(new StagedImportRequestModel { PublicDomain = true, Recipes = { Record("Book soup", null) } });

            result.Imported.Should().Be(1);
            (await db.Recipes.SingleAsync()).LicenseStatus.Should().Be(RecipeLicenseStatus.PublicDomain);
        }

        [Fact]
        public async Task The_api_approves_a_source_only_through_the_queue_of_clear_candidates()
        {
            var approveRoutes = typeof(Nom.Api.Controllers.ScrapingSourceController).GetMethods()
                .SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false).Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>())
                .Select(a => a.Template)
                .Where(t => t != null && t.EndsWith("approve", StringComparison.Ordinal));
            approveRoutes.Should().Equal("candidates/{id:long}/approve");
            typeof(IScrapingSourceService).GetMethods().Select(m => m.Name).Should().NotContain("ApproveAsync");

            using var db = NewContext();
            await SourceAsync(db, "rejected.example", ScrapingSourceStatusEnum.Rejected);
            var candidate = await CandidateAsync(db, "rejected.example");
            var check = new SourceCheckEntity
            {
                Domain = "rejected.example", Purpose = SourceAdmissionService.CandidatePurpose, StartedUtc = T0, FinishedUtc = T0, Status = SourceCheckEntity.Completed,
                Verdict = SourceAdmissionPolicy.Clear, Conclusive = true, PolicyVersion = SourceAdmissionPolicy.Version, RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion,
                ReportJson = JsonSerializer.Serialize(Report("rejected.example"), Json),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            candidate.LastCheckId = check.Id;
            candidate.State = SourceCandidateEntity.Clear;
            await db.SaveChangesAsync();

            (await Service(db, new FakeScraper()).ApproveCandidateAsync(candidate.Id, Admin, null)).Error.Should().Contain("rejected");
            (await db.ScrapingSources.SingleAsync()).Status.Should().Be(ScrapingSourceStatusEnum.Rejected);
        }

        [Fact]
        public async Task The_staged_import_refuses_a_paused_source_and_a_full_probation()
        {
            using var db = NewContext();
            var paused = await SourceAsync(db, "paused.example");
            paused.HarvestPausedAt = T0;
            paused.HarvestPausedReason = "re-check 4 turned not clear: licence";
            var trial = await SourceAsync(db, "trial.example");
            trial.ProbationUntilUtc = T0.AddDays(10);
            trial.ProbationRecipeLimit = 2;
            await db.SaveChangesAsync();
            var import = Staged(db, gate: true);

            var result = await import.ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes =
                {
                    Record("Paused soup", "https://paused.example/soup/"),
                    Record("Trial one", "https://trial.example/1/"),
                    Record("Trial one", "https://trial.example/1/"),
                    Record("Trial two", "https://www.trial.example/2/"),
                    Record("Trial three", "https://trial.example/3/"),
                },
            });

            result.Imported.Should().Be(2);
            result.SkippedDuplicates.Should().Be(1);
            result.Refused.Should().Be(2);
            result.Failures.Select(f => f.Error).Should().Contain(e => e.Contains("paused")).And.Contain(e => e.Contains("probation"));
            (await db.Recipes.Select(r => r.Name).ToListAsync()).Should().BeEquivalentTo(new[] { "Trial one", "Trial two" });
        }

        [Fact]
        public async Task A_run_or_check_cut_off_while_polling_is_stored_as_failed_and_shown()
        {
            using var db = NewContext();
            db.SourceDiscoveryRuns.Add(new SourceDiscoveryRunEntity { StartedUtc = T0.AddHours(-2), Seeds = "a.example", Status = SourceDiscoveryRunEntity.Running, JobId = "d1" });
            db.SourceChecks.Add(new SourceCheckEntity { Domain = "b.example", Purpose = SourceAdmissionService.CandidatePurpose, StartedUtc = T0.AddHours(-2), Status = SourceCheckEntity.Running, JobId = "c1" });
            db.SourceDiscoveryRuns.Add(new SourceDiscoveryRunEntity { StartedUtc = T0.AddMinutes(-1), Seeds = "c.example", Status = SourceDiscoveryRunEntity.Running });
            await db.SaveChangesAsync();
            var service = Service(db, new FakeScraper());

            await service.WatchAsync();

            var runs = await db.SourceDiscoveryRuns.OrderBy(r => r.StartedUtc).ToListAsync();
            runs[0].Status.Should().Be(SourceDiscoveryRunEntity.Failed);
            runs[0].Error.Should().StartWith("lost");
            runs[0].FinishedUtc.Should().Be(T0);
            runs[1].Status.Should().Be(SourceDiscoveryRunEntity.Running, "a run inside its budget may still be polling");
            (await db.SourceChecks.SingleAsync()).Status.Should().Be(SourceCheckEntity.Lost);
            (await service.ReviewAsync()).FailedChecks.Single().Domain.Should().Be("b.example");
            var status = await service.StatusAsync();
            status.RunsFailedInRow.Should().Be(1);
            status.ChecksFailedLast7d.Should().Be(1);
        }

        [Fact]
        public async Task A_target_whose_check_failed_waits_a_day_so_it_cannot_take_the_daily_cap()
        {
            using var db = NewContext();
            await SourceAsync(db, "broken.example");
            var other = await SourceAsync(db, "other.example");
            var candidate = await CandidateAsync(db, "new.example");
            candidate.TimesSeen = 9;
            await db.SaveChangesAsync();
            var scraper = new FakeScraper();
            scraper.Starts.Enqueue(ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, "connection reset"));
            scraper.Starts.Enqueue(ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, "connection reset"));
            scraper.Starts.Enqueue(ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, "connection reset"));
            CheckJob(scraper, Report("broken.example"));
            var service = Service(db, scraper);

            for (var i = 0; i < 3; i++) (await service.RunNextCheckAsync())!.Status.Should().Be(SourceCheckEntity.Failed);
            (await service.RunNextCheckAsync()).Should().BeNull("every target failed within the day");

            scraper.Started.Select(s => s.Domain).Should().Equal("broken.example", "other.example", "www.new.example");
            var tomorrow = Service(db, scraper, () => T0.AddHours(FailedRetryHoursPlusOne));
            (await tomorrow.RunNextCheckAsync())!.Domain.Should().Be("broken.example");
            (await db.ScrapingSources.FindAsync(other.Id))!.CheckedAtUtc.Should().BeNull();
        }

        private const int FailedRetryHoursPlusOne = SourceAdmissionService.FailedRetryHours + 1;

        [Fact]
        public async Task A_seed_the_scraper_refuses_rotates_to_the_back_like_a_visited_one()
        {
            using var db = NewContext();
            await SourceAsync(db, "platform.example");
            await SourceAsync(db, "real.example");
            var scraper = new FakeScraper();
            var refused = Found();
            refused.SeedsRefused = new() { "platform.example" };
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Completed, refused)));
            scraper.Starts.Enqueue(ScraperJobAnswer.Of(Job("discovery", ScraperJob.Completed, Found(), "job2")));
            var clock = T0;
            var service = Service(db, scraper, () => clock, s => s.SeedsPerRun = 1);

            await service.RunDiscoveryAsync();
            clock = T0.AddDays(1);
            await service.RunDiscoveryAsync();

            scraper.Started.Select(s => s.Seeds.Single()).Should().Equal("platform.example", "real.example");
        }

        [Fact]
        public async Task Recording_a_hand_source_again_journals_its_old_licence_facts_and_undo_restores_them()
        {
            using var db = NewContext();
            var service = Service(db, new FakeScraper());
            HandSourceRequestModel Hand(string kind, string statement) => new()
            {
                Domain = "www.loc.gov", Title = "Library cookbooks", LicenceKind = kind, LicenceStatement = statement, LicenceFoundAt = $"https://www.loc.gov/{kind}",
            };

            (await service.RecordHandSourceAsync(Hand("cc-by", "Reuse with attribution."), Admin)).Ok.Should().BeTrue();
            (await service.RecordHandSourceAsync(Hand("public-domain", "No known restrictions."), 8)).Ok.Should().BeTrue();
            var source = await db.ScrapingSources.SingleAsync();
            source.LicenceKind.Should().Be("public-domain");

            var licenceRow = (await service.JournalAsync(20)).First(j => j.Field == SourceJournal.LicenceField);
            licenceRow.OldValue.Should().StartWith("cc-by").And.Contain("Reuse with attribution.");
            licenceRow.CanUndo.Should().BeTrue();
            (await service.UndoAsync(licenceRow.Id, Admin)).Ok.Should().BeTrue();

            source = await db.ScrapingSources.SingleAsync();
            source.LicenceKind.Should().Be("cc-by");
            source.LicenceStatement.Should().Be("Reuse with attribution.");
            source.LicenceFoundAt.Should().Be("https://www.loc.gov/cc-by");
            source.LicenceEnteredByPersonId.Should().Be(Admin);
            (await ImportedLicenceAsync(db)).Should().Be(RecipeLicenseStatus.Unknown);

            static async Task<string?> ImportedLicenceAsync(ApplicationDbContext db)
            {
                var hand = await db.ScrapingSources.SingleAsync();
                await Staged(db, gate: true).ImportStagedAsync(new StagedImportRequestModel { SourceId = hand.Id, Recipes = { Record("Library soup", null) } });
                return (await db.Recipes.SingleAsync()).LicenseStatus;
            }
        }

        private static ScraperRecipe Record(string name, string? url) => new()
        {
            Name = name,
            SourceUrl = url,
            Ingredients = { new ScraperIngredient { RawLine = "2 cups stock", Name = "stock", Quantity = 2, Unit = "cup" }, new ScraperIngredient { RawLine = "1 onion", Name = "onion", Quantity = 1 } },
            Steps = { new ScraperStep { Order = 1, Instruction = "Simmer everything together for twenty minutes." } },
        };

        private static RecipeScrapingService Staged(ApplicationDbContext db, bool gate)
        {
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(u => u.PersonId).Returns(Admin);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.StagedImportGateSetting] = gate ? "true" : "false" })
                .Build();
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            return new RecipeScrapingService(db, user.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object,
                vetting.Object, NullLogger<RecipeScrapingService>.Instance, configuration);
        }
    }
}
