using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// Staging checkpoint 4's source findings: full checks are off by default and a source is checked in full only after an
    /// admin approved facts-only use of it (P24); a facts-only class withholds prose as one journaled entry per source, with
    /// the recipes and approved recipes it withholds shown before and after and an undo (P10); a lead no approved source
    /// covers gets a terms-only check and never a full one; the Pipeline page's sources block carries the last run's exclusion
    /// figures and the held leads.
    /// </summary>
    public class SourceLaneCheckpoint4Tests
    {
        private const long Admin = 7;
        private static readonly DateTime T0 = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private sealed class AnsweringScraper : IRecipeScraperClient
        {
            private readonly Dictionary<string, ScraperSourceCheckReport> _jobs = new();
            private readonly Func<DateTime> _now;
            private readonly Func<string, int, ScraperSourceCheckReport> _answer;

            public AnsweringScraper(Func<DateTime> now, Func<string, int, ScraperSourceCheckReport> answer)
            {
                _now = now;
                _answer = answer;
            }

            public List<(string Domain, int SampleSize)> Checks { get; } = new();

            public bool IsConfigured => true;

            public Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, IReadOnlyList<string> excludeDomains, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default)
            {
                Checks.Add((domain, sampleSize));
                var id = $"c{Checks.Count}";
                _jobs[id] = _answer(domain, sampleSize);
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

        private static SourceRightsService Rights(ApplicationDbContext db, Func<DateTime> now) =>
            new(db, Options.Create(new SourceRightsSettings { Enforce = true }), NullLogger<SourceRightsService>.Instance) { Now = now };

        private static ScraperSourceCheckReport TermsOnly(ScraperSourceCheckReport report)
        {
            report.Mode = SourceRightsRule.TermsOnlyMode;
            report.Samples = new ScraperCheckSamples { Requested = SourceAdmissionPolicy.TermsOnlySampleSize };
            return report;
        }

        private static Func<string, int, ScraperSourceCheckReport> FactsOnlySites(params string[] forbidden) => (host, sampleSize) =>
        {
            var bare = SourceAdmissionPolicy.NormalizeDomain(host)!;
            var report = forbidden.Contains(bare) ? SourceRightsServiceTests.ForbiddenReport(bare) : SourceRightsServiceTests.FactsOnlyReport(bare);
            return sampleSize == SourceAdmissionPolicy.TermsOnlySampleSize ? TermsOnly(report) : report;
        };

        private static async Task TicksAsync(SourceAdmissionService lane, Action advance, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                await lane.RunNextCheckAsync();
                advance();
            }
        }

        private static async Task<ScrapingSourceEntity> SourceAsync(ApplicationDbContext db, string domain, Action<ScrapingSourceEntity>? configure = null)
        {
            var source = new ScrapingSourceEntity { Domain = domain, Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0.AddDays(-90) };
            configure?.Invoke(source);
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            return source;
        }

        private static RecipeEntity Recipe(string url, bool approved, bool ownMethod = false) => new()
        {
            AuthorId = Admin, Name = "Chili", Description = "A publisher's description.", SourceUrl = url, ScrapedAtUtc = T0.AddDays(-60),
            CurationStatusId = (long)(approved ? CurationStatusEnum.Curated : CurationStatusEnum.NonCurated),
            ProseLaneVersion = ownMethod ? "prose-lane/2" : null,
        };

        [Fact]
        public void The_code_and_the_shipped_settings_hold_full_checks_off()
        {
            new SourceAdmissionSettings().FullChecks.Should().BeFalse("a first enable must run the terms-only pass alone");
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "Nom.Api", "appsettings.json"))) folder = folder.Parent;
            folder.Should().NotBeNull("the test runs inside the nom-api tree");
            var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(folder!.FullName, "Nom.Api", "appsettings.json")))!;
            settings[SourceAdmissionSettings.SectionName]!["FullChecks"]!.GetValue<bool>().Should().BeFalse();
        }

        [Fact]
        public async Task With_full_checks_on_a_facts_only_source_is_checked_in_full_only_after_an_admin_approves_facts_only_use()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example");
            var now = T0;
            var scraper = new AnsweringScraper(() => now, FactsOnlySites());
            var lane = Lane(db, scraper, () => now);

            await TicksAsync(lane, () => now = now.AddMinutes(30), 4 * 48);
            scraper.Checks.Should().Equal(new[] { ("facts.example", SourceAdmissionPolicy.TermsOnlySampleSize) },
                "the terms-only report allows a full check, but the setting alone never starts one");
            (await db.ScrapingSources.AsNoTracking().SingleAsync()).RightsClass.Should().Be(SourceRightsRule.FactsOnly);

            (await Rights(db, () => now).SetFactsOnlyUseAsync(source.Id, true, Admin, "reviewed")).Ok.Should().BeTrue();
            await TicksAsync(lane, () => now = now.AddMinutes(30), 2);

            scraper.Checks.Should().Equal(("facts.example", SourceAdmissionPolicy.TermsOnlySampleSize), ("facts.example", SourceAdmissionPolicy.SampleSize));
        }

        [Fact]
        public async Task With_full_checks_on_a_clear_class_from_a_terms_only_report_on_a_source_checked_in_full_before_starts_no_full_check()
        {
            await using var db = NewContext();
            SourceCheckEntity TermsCheck(ScrapingSourceEntity source) => new()
            {
                Domain = source.Domain, Purpose = SourceAdmissionService.RecheckPurpose, Mode = SourceRightsRule.TermsOnlyMode, ScrapingSourceId = source.Id,
                Status = SourceCheckEntity.Completed, StartedUtc = T0.AddDays(-1), FinishedUtc = T0.AddDays(-1), RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion,
                ReportJson = JsonSerializer.Serialize(TermsOnly(SourceRightsServiceTests.ClearReport(source.Domain)), Json),
            };
            var unreviewed = await SourceAdmissionTestSource(db, "unreviewed.example");
            var admitted = await SourceAdmissionTestSource(db, "admitted.example");
            db.SourceChecks.AddRange(TermsCheck(unreviewed), TermsCheck(admitted));
            db.SourceCandidates.Add(new SourceCandidateEntity
            {
                Domain = "admitted.example", State = SourceCandidateEntity.Approved, ScrapingSourceId = admitted.Id, FoundVia = "seed.example",
                FirstSeenUtc = T0.AddDays(-60), LastSeenUtc = T0.AddDays(-60), TimesSeen = 1, EvidenceUrl = "https://admitted.example/a-recipe/",
            });
            await db.SaveChangesAsync();
            var now = T0;
            var scraper = new AnsweringScraper(() => now, (host, sampleSize) => SourceRightsServiceTests.ClearReport(SourceAdmissionPolicy.NormalizeDomain(host)!));
            var lane = Lane(db, scraper, () => now);

            await TicksAsync(lane, () => now = now.AddMinutes(30), 2 * 48);

            scraper.Checks.Should().Equal(new[] { ("admitted.example", SourceAdmissionPolicy.SampleSize) },
                "only a source an admin admitted on a clear full check gets its weekly full check; a clear class from a terms-only report on a source a full check once fetched without review approves nothing");
        }

        private static Task<ScrapingSourceEntity> SourceAdmissionTestSource(ApplicationDbContext db, string domain) => SourceAsync(db, domain, s =>
        {
            s.RightsClass = SourceRightsRule.Clear;
            s.CheckedAtUtc = T0.AddDays(-30);
            s.CheckVerdict = SourceAdmissionPolicy.Clear;
        });

        [Fact]
        public async Task A_facts_only_class_withholds_as_one_journaled_entry_per_source_shown_before_and_after_and_its_undo_shows_the_prose_again()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example");
            await SourceAsync(db, "open.example", s => s.CheckedAtUtc = T0);
            db.Recipes.AddRange(
                Recipe("https://facts.example/a/", approved: true),
                Recipe("https://www.facts.example/b/", approved: false),
                Recipe("https://facts.example/c/", approved: true, ownMethod: true),
                Recipe("https://open.example/d/", approved: true));
            await db.SaveChangesAsync();
            var now = T0;
            var scraper = new AnsweringScraper(() => now, FactsOnlySites());
            var lane = Lane(db, scraper, () => now);

            var before = (await lane.ReviewAsync()).Approved.Single(s => s.Id == source.Id).Prose!;
            before.Should().BeEquivalentTo(new { RightsClass = (string?)null, Recipes = 3, Approved = 2, Withheld = 0, WouldWithhold = 2, WouldWithholdApproved = 1, ClassFromTermsOnly = false });
            (await Rights(db, () => now).StatusAsync()).ProseBySource.Should().ContainSingle(f => f.SourceId == source.Id && f.WouldWithhold == 2);

            await lane.RunNextCheckAsync();

            var entries = await db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == SourceJournal.EntityType && a.PropertyName == SourceJournal.FactsOnlyProseField).ToListAsync();
            entries.Should().ContainSingle("the withhold of one source's recipes is one journaled entry");
            entries[0].ChangeType.Should().Be(SourceJournal.FactsOnlyWithhold);
            entries[0].NewValue.Should().StartWith(SourceJournal.Withheld).And.Contain("2 recipes (1 approved)").And.Contain("terms-only check");
            var after = (await lane.ReviewAsync()).Approved.Single(s => s.Id == source.Id).Prose!;
            after.Should().BeEquivalentTo(new { RightsClass = SourceRightsRule.FactsOnly, Recipes = 3, Approved = 2, Withheld = 2, WithheldApproved = 1, WouldWithhold = 0, ClassFromTermsOnly = true });

            var journal = await lane.JournalAsync(50);
            journal.Single(j => j.Id == entries[0].Id).CanUndo.Should().BeTrue();
            var undone = await lane.UndoAsync(entries[0].Id, Admin);
            undone.Ok.Should().BeTrue(undone.Error);

            (await db.Recipes.AsNoTracking().Where(r => r.SourceUrl!.Contains("facts.example")).Select(r => r.ProseWithheld).ToListAsync()).Should().OnlyContain(w => !w);
            var reverted = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id);
            reverted.RightsClass.Should().BeNull("the withhold follows the class, so its undo takes the class back");
            (await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == SourceJournal.EntityType && a.ChangeType == SourceJournal.Undo).Select(a => a.PropertyName).ToListAsync())
                .Should().BeEquivalentTo(new[] { SourceJournal.RightsField, SourceJournal.FactsOnlyProseField });

            var proseUndo = await db.AuditLogEntries.AsNoTracking()
                .SingleAsync(a => a.ChangeType == SourceJournal.Undo && a.PropertyName == SourceJournal.FactsOnlyProseField);
            (await lane.JournalAsync(50)).Single(j => j.Id == proseUndo.Id).CanUndo.Should().BeTrue();
            var redone = await lane.UndoAsync(proseUndo.Id, Admin);
            redone.Ok.Should().BeTrue(redone.Error);
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).RightsClass.Should().Be(SourceRightsRule.FactsOnly,
                "undoing the undo of the prose brings back the class the withhold follows");
            (await db.Recipes.AsNoTracking().CountAsync(r => r.ProseWithheld)).Should().Be(2);
            var again = await db.AuditLogEntries.AsNoTracking()
                .Where(a => a.PropertyName == SourceJournal.FactsOnlyProseField && a.ChangeType == SourceJournal.FactsOnlyWithhold)
                .OrderByDescending(a => a.Id).FirstAsync();
            again.Id.Should().NotBe(entries[0].Id, "the withhold that comes back is journaled again");
            (await lane.UndoAsync(again.Id, Admin)).Ok.Should().BeTrue();
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).RightsClass.Should().BeNull();

            await Rights(db, () => now).DeriveAsync(apply: true, staleOnly: true);
            await TicksAsync(lane, () => now = now.AddMinutes(30), 48);
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).RightsClass.Should().BeNull("neither the stale re-derive nor the lane brings the undone class back from the same report");
            (await db.Recipes.AsNoTracking().CountAsync(r => r.ProseWithheld)).Should().Be(0);
            scraper.Checks.Should().HaveCount(1, "without an admin's approval nothing checks the source again by itself; an admin may start a terms-only check");
        }

        [Fact]
        public async Task A_derive_dry_run_lists_a_terms_only_report_that_would_withhold_prose_with_its_recipes()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example");
            db.Recipes.AddRange(Recipe("https://facts.example/a/", approved: true), Recipe("https://facts.example/b/", approved: false));
            await db.SaveChangesAsync();
            var check = new SourceCheckEntity
            {
                Domain = "facts.example", Purpose = SourceAdmissionService.RecheckPurpose, Mode = SourceRightsRule.TermsOnlyMode, ScrapingSourceId = source.Id,
                Status = SourceCheckEntity.Completed, StartedUtc = T0, FinishedUtc = T0, RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion,
                ReportJson = JsonSerializer.Serialize(TermsOnly(SourceRightsServiceTests.FactsOnlyReport("facts.example")), Json),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();

            var run = await Rights(db, () => T0).DeriveAsync(apply: false);

            run.Results.Should().ContainSingle().Which.Should().BeEquivalentTo(new { NewClass = SourceRightsRule.FactsOnly, TermsOnly = true, WouldWithhold = 2, WouldWithholdApproved = 1 });
            (await db.Recipes.AsNoTracking().CountAsync(r => r.ProseWithheld)).Should().Be(0, "a dry run withholds nothing");
        }

        [Fact]
        public async Task A_lead_no_approved_source_covers_gets_a_terms_only_check_after_the_candidates_and_never_a_full_one()
        {
            await using var db = NewContext();
            await SourceAsync(db, "covered.example", s =>
            {
                s.CheckedAtUtc = T0;
                s.RightsClass = SourceRightsRule.FactsOnly;
                s.FactsOnlyApprovedAt = T0.AddDays(-1);
            });
            await SourceAsync(db, "www.unfetchable.example", s =>
            {
                s.CheckedAtUtc = T0;
                s.RightsClass = SourceRightsRule.FactsOnly;
            });
            SourceCandidateEntity Candidate(string domain, string state) => new()
            {
                Domain = domain, State = state, FoundVia = "seed.example", FirstSeenUtc = T0.AddDays(-5), LastSeenUtc = T0, TimesSeen = 1,
                EvidenceUrl = state == SourceCandidateEntity.Lead ? null : $"https://{domain}/a-recipe/", Signal = state == SourceCandidateEntity.Lead ? null : "RecipeJsonLd",
            };
            db.SourceCandidates.AddRange(
                Candidate("lead.example", SourceCandidateEntity.Lead),
                Candidate("barred-lead.example", SourceCandidateEntity.Lead),
                Candidate("covered.example", SourceCandidateEntity.Lead),
                Candidate("unfetchable.example", SourceCandidateEntity.Lead),
                Candidate("new.example", SourceCandidateEntity.New));
            await db.SaveChangesAsync();
            var now = T0;
            var scraper = new AnsweringScraper(() => now, FactsOnlySites("barred-lead.example"));
            var lane = Lane(db, scraper, () => now);

            var held = await lane.StatusAsync();
            held.LeadsHeld.Should().Be(3, "the lead of a host an approved source that may be fetched has yields to that source; one that may not be fetched does not");
            held.LeadsTermsChecked.Should().Be(0);

            await TicksAsync(lane, () => now = now.AddMinutes(30), 4 * 48);

            scraper.Checks.Take(2).Should().Equal(("new.example", SourceAdmissionPolicy.TermsOnlySampleSize), ("new.example", SourceAdmissionPolicy.SampleSize));
            scraper.Checks.Where(c => c.Domain.Contains("lead.example") || c.Domain == "unfetchable.example").Should().BeEquivalentTo(new[]
            {
                ("lead.example", SourceAdmissionPolicy.TermsOnlySampleSize),
                ("barred-lead.example", SourceAdmissionPolicy.TermsOnlySampleSize),
                ("unfetchable.example", SourceAdmissionPolicy.TermsOnlySampleSize),
            }, "a lead gets one terms-only check and never a recipe page, and a lead discovery holds is checked even where an approved source it may not fetch has the host");
            scraper.Checks.Should().NotContain(c => c.Domain == "covered.example");

            var leads = await db.SourceCandidates.AsNoTracking().ToDictionaryAsync(c => c.Domain);
            leads["lead.example"].State.Should().Be(SourceCandidateEntity.Lead, "a lead whose terms allow automated access waits for discovery to probe it");
            leads["barred-lead.example"].State.Should().Be(SourceCandidateEntity.NotClear);
            var reports = await db.SourceChecks.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ReportJson);
            SourceDiscoveryExclusion.ForCandidate(leads["lead.example"], SourceRightsRule.Derive(JsonSerializer.Deserialize<ScraperSourceCheckReport>(reports[leads["lead.example"].LastCheckId!.Value]!, Json)).Class)
                .Should().BeNull("once its terms allow it, discovery no longer holds the lead");
            SourceDiscoveryExclusion.ForCandidate(leads["barred-lead.example"], SourceRightsRule.Derive(JsonSerializer.Deserialize<ScraperSourceCheckReport>(reports[leads["barred-lead.example"].LastCheckId!.Value]!, Json)).Class)
                .Should().Be(SourceDiscoveryExclusion.Forbidden);
            var after = await lane.StatusAsync();
            after.LeadsHeld.Should().Be(0, "a lead whose terms-only check allows automated access is no longer held, as discovery reads it");
            after.LeadsTermsChecked.Should().Be(0);
        }

        [Fact]
        public async Task The_sources_block_carries_the_exclusion_figures_of_the_last_run_that_sent_a_job()
        {
            await using var db = NewContext();
            db.SourceDiscoveryRuns.AddRange(
                new SourceDiscoveryRunEntity
                {
                    StartedUtc = T0.AddHours(-2), FinishedUtc = T0.AddHours(-2), Status = SourceDiscoveryRunEntity.Completed, Seeds = "facts.example", CreatedDate = T0,
                    JobId = "d1", ExcludeSent = 14, ExcludeLeftOut = 3, SeedsExcluded = "held.example", CandidatesExcluded = 11,
                },
                new SourceDiscoveryRunEntity
                {
                    StartedUtc = T0.AddHours(-1), FinishedUtc = T0.AddHours(-1), Status = SourceDiscoveryRunEntity.Failed, Seeds = "facts.example", CreatedDate = T0,
                    ExcludeSent = 15, Error = "The discovery job did not start: busy",
                },
                new SourceDiscoveryRunEntity { StartedUtc = T0, FinishedUtc = T0, Status = SourceDiscoveryRunEntity.Skipped, Seeds = string.Empty, CreatedDate = T0 });
            await db.SaveChangesAsync();
            var lane = Lane(db, new AnsweringScraper(() => T0, FactsOnlySites()), () => T0);

            var status = await lane.StatusAsync();

            status.LastExclusionRun.Should().BeEquivalentTo(new { ExcludeSent = 14, ExcludeLeftOut = 3, SeedsExcluded = "held.example", CandidatesExcluded = 11 },
                "a failed run that never sent a job is not the last run that sent one");
        }
    }
}
