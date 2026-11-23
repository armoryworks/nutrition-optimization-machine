using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The discovery job's exclusion list (scraper contract 2.5): every host NOM holds as forbidden, permission-needed,
    /// rejected or unclassed is sent on every job, so neither pass fetches it; the scraper's candidatesExcluded never makes
    /// a candidate; a seed the scraper reports as excluded is a NOM fault, logged and shown, and not rotated; past the
    /// scraper's 5,000 entries no forbidden or permission-needed host is ever dropped.
    /// </summary>
    public class SourceDiscoveryExclusionTests
    {
        private static readonly DateTime T0 = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// A scraper on the wire: it records every request NOM's client sends and runs discovery as the contract says. Each
        /// seed links the domains of <see cref="Links"/>; a linked domain on the job's excludeDomains (itself or a parent) is
        /// reported in candidatesExcluded without a request, a seed on it in seedsExcluded, the rest are found. With
        /// <see cref="IgnoresExclusion"/> it is a scraper from before excludeDomains, which probes everything.
        /// </summary>
        private sealed class RecordingScraper : HttpMessageHandler
        {
            public Dictionary<string, string[]> Links { get; } = new(StringComparer.Ordinal);

            public bool IgnoresExclusion { get; set; }

            public List<(string Path, JsonElement? Body)> Requests { get; } = new();

            public List<string> Probed { get; } = new();

            public List<IReadOnlyList<string>> ExcludeLists =>
                Requests.Where(r => r.Path == "/api/discover/jobs").Select(r => (IReadOnlyList<string>)r.Body!.Value.GetProperty("excludeDomains").EnumerateArray().Select(e => e.GetString()!).ToList()).ToList();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var text = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                JsonElement? body = text == null ? null : JsonDocument.Parse(text).RootElement.Clone();
                Requests.Add((request.RequestUri!.AbsolutePath, body));
                if (request.Method != HttpMethod.Post || request.RequestUri.AbsolutePath != "/api/discover/jobs")
                    return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"no such job\"}", Encoding.UTF8, "application/json") };

                var seeds = body!.Value.GetProperty("seedDomains").EnumerateArray().Select(e => e.GetString()!).ToList();
                var exclude = IgnoresExclusion ? new List<string>() : body.Value.GetProperty("excludeDomains").EnumerateArray().Select(e => e.GetString()!).ToList();
                string? By(string host) => exclude.FirstOrDefault(e => host == e || host.EndsWith("." + e, StringComparison.Ordinal));
                var result = new ScraperDiscoveryResult();
                foreach (var seed in seeds)
                {
                    if (By(seed) != null)
                    {
                        result.SeedsExcluded.Add(seed);
                        continue;
                    }

                    result.SeedsVisited.Add(seed);
                    foreach (var linked in Links.GetValueOrDefault(seed, Array.Empty<string>()))
                    {
                        if (By(linked) is { } entry)
                        {
                            if (result.CandidatesExcluded.All(c => c.Domain != linked))
                                result.CandidatesExcluded.Add(new ScraperExcludedDomain { Domain = linked, DiscoveredVia = seed, ExcludedBy = entry });
                            continue;
                        }

                        Probed.Add(linked);
                        result.Candidates.Add(new ScraperDiscoveredSource { Domain = linked, EvidenceUrl = $"https://{linked}/a-recipe/", Signal = "RecipeJsonLd", DiscoveredVia = seed });
                    }
                }

                var job = new ScraperJob
                {
                    JobId = $"d{Requests.Count}", Kind = "discovery", Status = ScraperJob.Completed, StopReason = "none",
                    CreatedUtc = T0, DeadlineUtc = T0.AddMinutes(10), BudgetSeconds = 600, Result = JsonSerializer.SerializeToElement(result, Json),
                };
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(JsonSerializer.Serialize(job, Json), Encoding.UTF8, "application/json") };
            }
        }

        private sealed class ListLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Entries.Add((logLevel, formatter(state, exception)));
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static (SourceAdmissionService Lane, ListLogger<SourceAdmissionService> Log) Lane(ApplicationDbContext db, RecordingScraper scraper, Func<DateTime> now, int seedsPerRun = 1)
        {
            var client = new RecipeScraperClient(new HttpClient(scraper),
                Options.Create(new RecipeScraperSettings { BaseUrl = "http://scraper.test", ApiKey = "test-key" }), NullLogger<RecipeScraperClient>.Instance);
            var log = new ListLogger<SourceAdmissionService>();
            var lane = new SourceAdmissionService(db, client, new RecipeVettingService(),
                Options.Create(new SourceAdmissionSettings { Enabled = true, PollSeconds = 1, SeedsPerRun = seedsPerRun, TermsOnlyChecks = true, FullChecks = true }), log,
                Options.Create(new SourceRightsSettings { Enforce = true }))
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = now,
            };
            return (lane, log);
        }

        private static ScrapingSourceEntity Source(string domain, string? rightsClass, ScrapingSourceStatusEnum status = ScrapingSourceStatusEnum.Approved, string kind = ScrapingSourceEntity.WebKind) => new()
        {
            Domain = domain, Status = status, Kind = kind, RightsClass = rightsClass, CreatedDate = T0.AddDays(-60),
            RightsQuote = rightsClass is SourceRightsRule.Forbidden ? "You may not scrape this website." : null,
        };

        private static async Task<SourceCandidateEntity> CandidateAsync(ApplicationDbContext db, string domain, string state, ScraperSourceCheckReport? lastReport = null)
        {
            var candidate = new SourceCandidateEntity
            {
                Domain = domain, FoundVia = "a.example", Signal = state == SourceCandidateEntity.Lead ? null : "RecipeJsonLd",
                FirstSeenUtc = T0.AddDays(-5), LastSeenUtc = T0.AddDays(-5), TimesSeen = 1, State = state, CreatedDate = T0.AddDays(-5),
            };
            db.SourceCandidates.Add(candidate);
            await db.SaveChangesAsync();
            if (lastReport == null) return candidate;

            var check = new SourceCheckEntity
            {
                Domain = domain, Purpose = SourceAdmissionService.CandidatePurpose, Mode = SourceRightsRule.TermsOnlyMode, Status = SourceCheckEntity.Completed,
                SourceCandidateId = candidate.Id, StartedUtc = T0.AddDays(-4), FinishedUtc = T0.AddDays(-4), RuleVersion = lastReport.RuleVersion,
                Verdict = SourceAdmissionPolicy.NotClear, PolicyVersion = SourceAdmissionPolicy.Version, ReportJson = JsonSerializer.Serialize(lastReport, Json), CreatedDate = T0.AddDays(-4),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            candidate.LastCheckId = check.Id;
            await db.SaveChangesAsync();
            return candidate;
        }

        private static ScraperSourceCheckReport Forbidding(string domain) =>
            SourceAdmissionServiceTests.TermsOnlyReport(domain, r => r.Terms!.Prohibitions.Add(new ScraperCheckProhibition { Category = "automated", Text = "You may not scrape or crawl this website.", FoundAt = $"https://{domain}/terms" }));

        private static ScraperSourceCheckReport Refusing(string domain) =>
            SourceAdmissionServiceTests.TermsOnlyReport(domain, r => r.Access!.RefusedBeforeFetch = "denylist");

        /// <summary>The held rows of every kind: a clear seed links each of them, and a site whose terms allow automated access.</summary>
        private static async Task<RecordingScraper> HeldWorldAsync(ApplicationDbContext db)
        {
            db.ScrapingSources.AddRange(
                Source("www.a.example", SourceRightsRule.Clear),
                Source("b.example", SourceRightsRule.Clear),
                Source("c.example", SourceRightsRule.Clear),
                Source("www.forbidden.example", SourceRightsRule.Forbidden),
                Source("forbidden.example", SourceRightsRule.Forbidden),
                Source("asks.example", SourceRightsRule.PermissionNeeded),
                Source("unclassed.example", null),
                Source("turned-down.example", null, ScrapingSourceStatusEnum.Rejected),
                Source("facts.example", SourceRightsRule.FactsOnly));
            await db.SaveChangesAsync();
            await CandidateAsync(db, "barred-candidate.example", SourceCandidateEntity.NotClear, Forbidding("barred-candidate.example"));
            await CandidateAsync(db, "refused-candidate.example", SourceCandidateEntity.NotClear, Refusing("refused-candidate.example"));
            await CandidateAsync(db, "rejected-candidate.example", SourceCandidateEntity.Rejected, SourceAdmissionServiceTests.TermsOnlyReport("rejected-candidate.example"));
            await CandidateAsync(db, "unchecked.example", SourceCandidateEntity.New);
            await CandidateAsync(db, "lead.example", SourceCandidateEntity.Lead);
            await CandidateAsync(db, "allowed-candidate.example", SourceCandidateEntity.New, SourceAdmissionServiceTests.TermsOnlyReport("allowed-candidate.example"));

            var linked = new[]
            {
                "forbidden.example", "blog.forbidden.example", "asks.example", "unclassed.example", "turned-down.example",
                "barred-candidate.example", "refused-candidate.example", "rejected-candidate.example", "unchecked.example", "lead.example",
                "facts.example", "allowed-candidate.example", "fresh.example",
            };
            var scraper = new RecordingScraper();
            foreach (var seed in new[] { "a.example", "b.example", "c.example" }) scraper.Links[seed] = linked;
            return scraper;
        }

        private static readonly string[] Barred =
        {
            "forbidden.example", "asks.example", "unclassed.example", "turned-down.example",
            "barred-candidate.example", "refused-candidate.example", "rejected-candidate.example", "unchecked.example", "lead.example",
        };

        [Fact]
        public async Task A_barred_host_linked_from_an_approved_seed_is_on_the_list_of_every_job_and_is_never_fetched()
        {
            await using var db = NewContext();
            var scraper = await HeldWorldAsync(db);
            var now = T0;
            var (lane, _) = Lane(db, scraper, () => now);

            for (var i = 0; i < 4; i++)
            {
                await lane.RunDiscoveryAsync();
                now = now.AddDays(1);
            }

            scraper.Requests.Where(r => r.Path == "/api/discover/jobs").Select(r => r.Body!.Value.GetProperty("seedDomains")[0].GetString())
                .Should().Equal(new[] { "a.example", "b.example", "c.example", "a.example" }, "the seeds rotate, one per run");
            scraper.ExcludeLists.Should().HaveCount(4).And.OnlyContain(list => Barred.All(list.Contains),
                "every forbidden, permission-needed, rejected and unclassed host, sources and candidates alike, is on every job's list");
            scraper.ExcludeLists[0].Should().HaveCount(Barred.Length, "the two rows of forbidden.example are one entry, and a host whose terms allow automated access is not held");
            scraper.ExcludeLists.Skip(1).Should().OnlyContain(list => list.Count == Barred.Length + 1 && list.Contains("fresh.example"),
                "a site the first run found is a candidate with no class yet, so later runs leave it alone until its terms-only check");
            scraper.ExcludeLists.Should().OnlyContain(list => list.Distinct().Count() == list.Count);
            scraper.ExcludeLists[0].Take(4).Should().Equal(new[] { "barred-candidate.example", "forbidden.example", "asks.example", "refused-candidate.example" },
                "forbidden and permission-needed hosts go first");
            scraper.Probed.Should().NotContain(Barred).And.NotContain("blog.forbidden.example", "a subdomain of an excluded host is excluded too");
            scraper.Probed.Distinct().Should().BeEquivalentTo(new[] { "facts.example", "allowed-candidate.example", "fresh.example" });
            scraper.Probed.Count(h => h == "fresh.example").Should().Be(1);

            var candidates = await db.SourceCandidates.Select(c => c.Domain).ToListAsync();
            candidates.Should().Contain("fresh.example").And.NotContain("blog.forbidden.example", "candidatesExcluded never makes a candidate");
            (await db.SourceCandidates.SingleAsync(c => c.Domain == "lead.example")).Should().Match<SourceCandidateEntity>(c => c.State == SourceCandidateEntity.Lead && c.TimesSeen == 1);

            var runs = await db.SourceDiscoveryRuns.OrderBy(r => r.Id).ToListAsync();
            runs.Should().OnlyContain(r => r.Status == SourceDiscoveryRunEntity.Completed && r.ExcludeLeftOut == 0 && r.SeedsExcluded == null && r.ExcludeNote == null);
            runs.Select(r => r.ExcludeSent).Should().Equal(Barred.Length, Barred.Length + 1, Barred.Length + 1, Barred.Length + 1);
            runs.Select(r => r.CandidatesExcluded).Should().Equal(new[] { Barred.Length + 1, Barred.Length + 2, Barred.Length + 2, Barred.Length + 2 },
                "every excluded host the seed links, the subdomain included, is listed for the admin");
            var shown = (await lane.ReviewAsync()).Runs.First();
            shown.CandidatesExcludedHosts.Should().ContainSingle(c => c.Domain == "blog.forbidden.example" && c.ExcludedBy == "forbidden.example" && c.DiscoveredVia == "a.example");
        }

        [Fact]
        public async Task A_seed_the_scraper_reports_as_excluded_is_logged_as_a_nom_fault_shown_and_not_rotated()
        {
            await using var db = NewContext();
            db.ScrapingSources.Add(Source("seed.example", SourceRightsRule.Clear));
            await db.SaveChangesAsync();
            var scraper = new RecordingScraper();
            var log = new ListLogger<SourceAdmissionService>();
            var client = new SeedEchoScraper(scraper);
            var faulty = new SourceAdmissionService(db, client, new RecipeVettingService(),
                Options.Create(new SourceAdmissionSettings { Enabled = true, PollSeconds = 1, FullChecks = true }), log, Options.Create(new SourceRightsSettings { Enforce = true }))
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = () => T0,
            };

            var run = await faulty.RunDiscoveryAsync();

            run.SeedsExcluded.Should().Be("seed.example");
            run.ExcludeNote.Should().Contain("NOM fault").And.Contain("seed.example");
            log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("seed.example") && e.Message.Contains("NOM fault"));
            (await db.ScrapingSources.SingleAsync()).LastDiscoveredUtc.Should().BeNull("a seed the scraper did not visit is not rotated");
        }

        /// <summary>A client that sends the job as asked but whose scraper reads every seed as excluded, the answer NOM gets when it sends a seed it also excludes.</summary>
        private sealed class SeedEchoScraper : IRecipeScraperClient
        {
            private readonly RecordingScraper _wire;

            public SeedEchoScraper(RecordingScraper wire) => _wire = wire;

            public bool IsConfigured => true;

            public Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, IReadOnlyList<string> excludeDomains, CancellationToken cancellationToken = default)
            {
                var client = new RecipeScraperClient(new HttpClient(_wire),
                    Options.Create(new RecipeScraperSettings { BaseUrl = "http://scraper.test", ApiKey = "test-key" }), NullLogger<RecipeScraperClient>.Instance);
                return client.StartDiscoveryJobAsync(seedDomains, maxCandidates, budgetSeconds, excludeDomains.Concat(seedDomains).ToList(), cancellationToken);
            }

            public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        [Fact]
        public async Task An_approved_seed_that_another_row_bars_is_held_back_before_the_job_and_shown_as_a_nom_fault()
        {
            await using var db = NewContext();
            db.ScrapingSources.AddRange(
                Source("blog.split.example", SourceRightsRule.Clear),
                Source("ok.example", SourceRightsRule.Clear));
            await db.SaveChangesAsync();
            await CandidateAsync(db, "split.example", SourceCandidateEntity.NotClear, Forbidding("split.example"));
            var scraper = new RecordingScraper();
            var (lane, log) = Lane(db, scraper, () => T0, seedsPerRun: 3);

            var run = await lane.RunDiscoveryAsync();

            scraper.Requests.Single().Body!.Value.GetProperty("seedDomains").EnumerateArray().Select(e => e.GetString()).Should().Equal("ok.example");
            scraper.ExcludeLists.Single().Should().Equal("split.example");
            run.SeedsExcluded.Should().Be("blog.split.example");
            run.ExcludeNote.Should().StartWith("NOM fault");
            log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("blog.split.example"));
        }

        [Fact]
        public async Task A_scraper_that_ignores_the_list_is_flagged_and_what_it_probed_is_never_a_candidate()
        {
            await using var db = NewContext();
            var scraper = await HeldWorldAsync(db);
            scraper.IgnoresExclusion = true;
            var (lane, log) = Lane(db, scraper, () => T0);

            var run = await lane.RunDiscoveryAsync();

            scraper.ExcludeLists.Single().Should().Contain(Barred, "NOM sent the list");
            run.ExcludeNote.Should().Contain("did not apply excludeDomains");
            log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("does not apply excludeDomains"));
            (await db.SourceCandidates.Select(c => c.Domain).ToListAsync()).Should().NotContain("blog.forbidden.example").And.Contain("fresh.example");
            (await db.SourceCandidates.SingleAsync(c => c.Domain == "lead.example")).Should().Match<SourceCandidateEntity>(c => c.State == SourceCandidateEntity.Lead && c.TimesSeen == 1,
                "an excluded host probed anyway is not counted or promoted");
        }

        [Fact]
        public async Task Without_the_rights_gate_an_approved_web_source_is_not_held_by_its_class_but_every_other_barred_or_unclassed_source_is()
        {
            await using var db = NewContext();
            var scraper = await HeldWorldAsync(db);
            db.ScrapingSources.AddRange(
                Source("rows.example", SourceRightsRule.Forbidden, ScrapingSourceStatusEnum.Pending),
                Source("ask-first.example", SourceRightsRule.PermissionNeeded, ScrapingSourceStatusEnum.Pending),
                Source("requested.example", null, ScrapingSourceStatusEnum.Pending));
            await db.SaveChangesAsync();
            foreach (var seed in scraper.Links.Keys.ToList()) scraper.Links[seed] = scraper.Links[seed].Concat(new[] { "rows.example", "ask-first.example", "requested.example" }).ToArray();
            var client = new RecipeScraperClient(new HttpClient(scraper),
                Options.Create(new RecipeScraperSettings { BaseUrl = "http://scraper.test", ApiKey = "test-key" }), NullLogger<RecipeScraperClient>.Instance);
            var lane = new SourceAdmissionService(db, client, new RecipeVettingService(),
                Options.Create(new SourceAdmissionSettings { Enabled = true, PollSeconds = 1, SeedsPerRun = 1, FullChecks = true }), NullLogger<SourceAdmissionService>.Instance)
            {
                Delay = (_, _) => Task.CompletedTask,
                Now = () => T0,
            };

            await lane.RunDiscoveryAsync();

            scraper.ExcludeLists.Single().Should().BeEquivalentTo(new[]
            {
                "rows.example", "ask-first.example", "requested.example",
                "turned-down.example", "barred-candidate.example", "refused-candidate.example", "rejected-candidate.example", "unchecked.example", "lead.example",
            }, "with the gate off every approved web source seeds and is harvested, so only those go unheld; a pending forbidden or permission-needed source is still never probed");
            scraper.Probed.Should().NotContain(new[] { "rows.example", "ask-first.example", "requested.example" });
        }

        [Fact]
        public async Task An_approved_seed_is_not_held_back_by_an_unclassed_or_rejected_candidate_of_its_host_or_a_parent_host()
        {
            await using var db = NewContext();
            db.ScrapingSources.AddRange(
                Source("foo.example", SourceRightsRule.Clear),
                Source("blog.bar.example", SourceRightsRule.Clear),
                Source("www.again.example", SourceRightsRule.Clear),
                Source("blog.kept.example", SourceRightsRule.Clear));
            await db.SaveChangesAsync();
            await CandidateAsync(db, "foo.example", SourceCandidateEntity.Lead);
            await CandidateAsync(db, "bar.example", SourceCandidateEntity.New);
            await CandidateAsync(db, "again.example", SourceCandidateEntity.Rejected, SourceAdmissionServiceTests.TermsOnlyReport("again.example"));
            await CandidateAsync(db, "kept.example", SourceCandidateEntity.NotClear, Forbidding("kept.example"));
            await CandidateAsync(db, "other.example", SourceCandidateEntity.Lead);
            var scraper = new RecordingScraper();
            var now = T0;
            var (lane, log) = Lane(db, scraper, () => now, seedsPerRun: 4);

            var first = await lane.RunDiscoveryAsync();
            now = now.AddDays(1);
            await lane.RunDiscoveryAsync();

            scraper.Requests.Where(r => r.Path == "/api/discover/jobs").Should().HaveCount(2).And.OnlyContain(r =>
                r.Body!.Value.GetProperty("seedDomains").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "foo.example", "blog.bar.example", "again.example" }),
                "a lead, an unchecked candidate and a candidate rejected before the source was approved give way to the approved source, run after run");
            scraper.ExcludeLists.Should().OnlyContain(list => list.SequenceEqual(new[] { "kept.example", "other.example" }),
                "a candidate whose report bars the host still holds it, and a lead of another host is still held");
            first.SeedsExcluded.Should().Be("blog.kept.example");
            first.ExcludeNote.Should().Contain("not sent because an approved source").And.Contain("foo.example").And.Contain("bar.example").And.Contain("again.example");
            log.Entries.Where(e => e.Level == LogLevel.Error).Should().OnlyContain(e => e.Message.Contains("blog.kept.example"), "only the barred parent is a NOM fault");
        }

        [Fact]
        public async Task Past_the_scrapers_limit_every_forbidden_and_permission_needed_host_is_sent_and_the_others_take_turns()
        {
            await using var db = NewContext();
            db.ScrapingSources.Add(Source("seed.example", SourceRightsRule.Clear));
            for (var i = 0; i < 4990; i++) db.ScrapingSources.Add(Source($"barred{i}.example", i % 2 == 0 ? SourceRightsRule.Forbidden : SourceRightsRule.PermissionNeeded));
            for (var i = 0; i < 25; i++) db.ScrapingSources.Add(Source($"waiting{i:00}.example", null, ScrapingSourceStatusEnum.Pending));
            await db.SaveChangesAsync();
            var scraper = new RecordingScraper();
            var now = T0;
            var (lane, log) = Lane(db, scraper, () => now);

            for (var i = 0; i < 3; i++)
            {
                await lane.RunDiscoveryAsync();
                now = now.AddDays(1);
            }

            scraper.ExcludeLists.Should().HaveCount(3).And.OnlyContain(list => list.Count == SourceDiscoveryExclusion.MaxEntries
                && Enumerable.Range(0, 4990).All(i => list.Contains($"barred{i}.example")));
            scraper.ExcludeLists.SelectMany(l => l.Where(h => h.StartsWith("waiting", StringComparison.Ordinal))).Distinct()
                .Should().HaveCount(25, "the unclassed hosts left out of one run are sent in the next ones");
            (await db.SourceDiscoveryRuns.ToListAsync()).Should().OnlyContain(r => r.ExcludeSent == 5000 && r.ExcludeLeftOut == 15 && r.ExcludeNote!.Contains("left out"));
            log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("excludeDomains"));
        }

        [Fact]
        public void Past_the_limit_a_held_host_is_sent_within_a_bounded_number_of_runs_however_many_new_hosts_come_in_before_it_by_name()
        {
            var barred = Enumerable.Range(0, SourceDiscoveryExclusion.MaxEntries - 1).Select(i => ($"barred{i:0000}.example", SourceDiscoveryExclusion.Forbidden, T0)).ToList();
            var rest = new List<(string, string, DateTime)> { ("b.example", SourceDiscoveryExclusion.Unclassed, T0), ("c.example", SourceDiscoveryExclusion.Unclassed, T0) };
            SourceDiscoveryExclusionCursor? cursor = null;
            var sent = new List<string>();
            for (var turn = 0; turn < 6; turn++)
            {
                var list = SourceDiscoveryExclusion.Build(barred.Concat(rest), cursor);
                list.Entries.Should().HaveCount(SourceDiscoveryExclusion.MaxEntries);
                list.Entries.Take(barred.Count).Should().Equal(barred.Select(b => b.Item1));
                sent.Add(list.Entries[^1]);
                cursor = list.Cursor;
                rest.Add(($"a{turn}.example", SourceDiscoveryExclusion.Unclassed, T0.AddDays(turn + 1)));
            }

            sent.Take(2).Should().Equal(new[] { "b.example", "c.example" }, "a host held before the new ones is reached before them, though they sort before it by name");
            sent.Skip(2).Should().Equal("a0.example", "a1.example", "a2.example", "a3.example");
        }

        [Fact]
        public void When_the_barred_hosts_fill_every_entry_the_others_are_left_out_and_shown()
        {
            var held = Enumerable.Range(0, SourceDiscoveryExclusion.MaxEntries).Select(i => ($"barred{i:0000}.example", SourceDiscoveryExclusion.Forbidden, T0))
                .Append(("waiting.example", SourceDiscoveryExclusion.Unclassed, T0));

            var list = SourceDiscoveryExclusion.Build(held, null);

            list.TooManyBarred.Should().BeFalse();
            list.Entries.Should().HaveCount(SourceDiscoveryExclusion.MaxEntries).And.NotContain("waiting.example");
            list.LeftOut.Should().Equal("waiting.example");
        }

        [Fact]
        public async Task When_the_forbidden_and_permission_needed_hosts_alone_pass_the_limit_no_job_starts()
        {
            await using var db = NewContext();
            db.ScrapingSources.Add(Source("seed.example", SourceRightsRule.Clear));
            for (var i = 0; i <= SourceDiscoveryExclusion.MaxEntries; i++) db.ScrapingSources.Add(Source($"barred{i}.example", SourceRightsRule.Forbidden));
            await db.SaveChangesAsync();
            var scraper = new RecordingScraper();
            var (lane, log) = Lane(db, scraper, () => T0);

            var run = await lane.RunDiscoveryAsync();

            scraper.Requests.Should().BeEmpty("no barred host is ever dropped to make the list fit");
            run.Status.Should().Be(SourceDiscoveryRunEntity.Failed);
            run.Error.Should().Contain("5001 forbidden or permission-needed hosts");
            log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
        }

        [Theory]
        [InlineData("budgetbytes.com", "budgetbytes.com")]
        [InlineData("www.budgetbytes.com", "budgetbytes.com")]
        [InlineData("https://WWW.Example.COM/terms?x=1", "example.com")]
        [InlineData("*.example.org", "example.org")]
        [InlineData("example.org.", "example.org")]
        [InlineData("bücher.example", "xn--bcher-kva.example")]
        [InlineData("under_score.example", "under_score.example")]
        [InlineData("localhost", null)]
        [InlineData("[::1]", null)]
        [InlineData("", null)]
        public void An_entry_is_the_host_as_the_scraper_reads_it(string domain, string? expected) =>
            SourceDiscoveryExclusion.Entry(domain).Should().Be(expected);

        [Fact]
        public void Rows_of_one_host_are_one_entry_under_the_strongest_reason_and_a_host_under_a_barred_parent_is_not_repeated()
        {
            var list = SourceDiscoveryExclusion.Build(new[]
            {
                ("www.budgetbytes.com", SourceDiscoveryExclusion.Unclassed, T0),
                ("budgetbytes.com", SourceDiscoveryExclusion.Forbidden, T0),
                ("blog.budgetbytes.com", SourceDiscoveryExclusion.Rejected, T0),
                ("asks.example", SourceDiscoveryExclusion.PermissionNeeded, T0),
                ("later.example", SourceDiscoveryExclusion.Unclassed, T0),
                ("no-dot", SourceDiscoveryExclusion.Rejected, T0),
            }, null);

            list.Entries.Should().Equal("budgetbytes.com", "asks.example", "later.example");
            list.Held["budgetbytes.com"].Should().Be(SourceDiscoveryExclusion.Forbidden);
            list.Barred.Should().Be(2);
            list.NotHostNames.Should().Equal("no-dot");
            list.Covers("shop.blog.budgetbytes.com").Should().BeTrue();
            list.Covers("notbudgetbytes.com").Should().BeFalse();
            list.Covers("budgetbytes.com.other.org").Should().BeFalse();
        }

        [Theory]
        [InlineData(SourceRightsRule.Forbidden, ScrapingSourceStatusEnum.Approved, true, SourceDiscoveryExclusion.Forbidden)]
        [InlineData(SourceRightsRule.PermissionNeeded, ScrapingSourceStatusEnum.Approved, true, SourceDiscoveryExclusion.PermissionNeeded)]
        [InlineData(null, ScrapingSourceStatusEnum.Approved, true, SourceDiscoveryExclusion.Unclassed)]
        [InlineData(null, ScrapingSourceStatusEnum.Pending, true, SourceDiscoveryExclusion.Unclassed)]
        [InlineData(SourceRightsRule.Forbidden, ScrapingSourceStatusEnum.Rejected, true, SourceDiscoveryExclusion.Forbidden)]
        [InlineData(SourceRightsRule.FactsOnly, ScrapingSourceStatusEnum.Rejected, true, SourceDiscoveryExclusion.Rejected)]
        [InlineData(SourceRightsRule.FactsOnly, ScrapingSourceStatusEnum.Approved, true, null)]
        [InlineData(SourceRightsRule.Clear, ScrapingSourceStatusEnum.Approved, true, null)]
        [InlineData(SourceRightsRule.Forbidden, ScrapingSourceStatusEnum.Approved, false, null)]
        [InlineData(null, ScrapingSourceStatusEnum.Rejected, false, SourceDiscoveryExclusion.Rejected)]
        [InlineData(SourceRightsRule.Forbidden, ScrapingSourceStatusEnum.Pending, false, SourceDiscoveryExclusion.Forbidden)]
        [InlineData(SourceRightsRule.PermissionNeeded, ScrapingSourceStatusEnum.Pending, false, SourceDiscoveryExclusion.PermissionNeeded)]
        [InlineData(null, ScrapingSourceStatusEnum.Pending, false, SourceDiscoveryExclusion.Unclassed)]
        [InlineData(SourceRightsRule.Forbidden, ScrapingSourceStatusEnum.Rejected, false, SourceDiscoveryExclusion.Forbidden)]
        public void A_source_is_held_by_its_effective_class_and_by_a_rejection(string? rightsClass, ScrapingSourceStatusEnum status, bool enforce, string? expected) =>
            SourceDiscoveryExclusion.ForSource(Source("x.example", rightsClass, status), enforce).Should().Be(expected);

        [Fact]
        public void A_recorded_permission_lifts_the_hold_and_a_hand_source_with_no_class_is_held()
        {
            var permitted = Source("asks.example", SourceRightsRule.PermissionNeeded);
            permitted.PermissionRecordedAtUtc = T0;
            SourceDiscoveryExclusion.ForSource(permitted, enforce: true).Should().BeNull();
            SourceDiscoveryExclusion.ForSource(Source("www.nhlbi.example", null, kind: ScrapingSourceEntity.HandKind), enforce: true).Should().Be(SourceDiscoveryExclusion.Unclassed, "a hand source is never fetched");
        }

        [Theory]
        [InlineData(SourceCandidateEntity.Lead, null, SourceDiscoveryExclusion.Unclassed)]
        [InlineData(SourceCandidateEntity.New, null, SourceDiscoveryExclusion.Unclassed)]
        [InlineData(SourceCandidateEntity.NotClear, SourceRightsRule.Forbidden, SourceDiscoveryExclusion.Forbidden)]
        [InlineData(SourceCandidateEntity.NotClear, SourceRightsRule.PermissionNeeded, SourceDiscoveryExclusion.PermissionNeeded)]
        [InlineData(SourceCandidateEntity.NotClear, SourceRightsRule.FactsOnly, null)]
        [InlineData(SourceCandidateEntity.Clear, SourceRightsRule.Clear, null)]
        [InlineData(SourceCandidateEntity.Rejected, SourceRightsRule.FactsOnly, SourceDiscoveryExclusion.Rejected)]
        [InlineData(SourceCandidateEntity.Rejected, SourceRightsRule.Forbidden, SourceDiscoveryExclusion.Forbidden)]
        [InlineData(SourceCandidateEntity.Approved, null, null)]
        public void A_candidate_is_held_by_the_class_of_its_last_check_and_by_a_rejection(string state, string? rightsClass, string? expected) =>
            SourceDiscoveryExclusion.ForCandidate(new SourceCandidateEntity { Domain = "x.example", State = state }, rightsClass).Should().Be(expected);
    }
}
