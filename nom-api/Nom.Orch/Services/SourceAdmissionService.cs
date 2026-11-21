using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Discovery, source checks, the approval queue, probation, the weekly re-check and the source
    /// actions (see <see cref="ISourceAdmissionService"/>). Every fetch goes through the scraper's job
    /// routes; every change of a source's state is journaled (<see cref="SourceJournal"/>).
    /// </summary>
    public class SourceAdmissionService : ISourceAdmissionService
    {
        public const string CandidatePurpose = "candidate";
        public const string RecheckPurpose = "recheck";
        public const string RequestPurpose = "request";
        public const string DiscoveryKind = "discovery";
        public const string SourceCheckKind = "source-check";
        public const double ProbationMaxRequiresRevision = 0.10;
        public const double ProbationMaxUnquantified = 0.20;
        public const int StaleGraceSeconds = 300;
        public const int FailedRetryHours = 24;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly IRecipeScraperClient _scraper;
        private readonly IRecipeVettingService _vetting;
        private readonly SourceAdmissionSettings _settings;
        private readonly ILogger<SourceAdmissionService> _logger;

        public SourceAdmissionService(
            ApplicationDbContext db,
            IRecipeScraperClient scraper,
            IRecipeVettingService vetting,
            IOptions<SourceAdmissionSettings> settings,
            ILogger<SourceAdmissionService> logger)
        {
            _db = db;
            _scraper = scraper;
            _vetting = vetting;
            _settings = settings.Value;
            _logger = logger;
        }

        internal Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

        public async Task<SourceDiscoveryRunModel> RunDiscoveryAsync(CancellationToken cancellationToken = default)
        {
            await ExpireStaleAsync(cancellationToken);
            var seeds = await _db.ScrapingSources
                .Where(s => s.Status == ScrapingSourceStatusEnum.Approved && s.Kind == ScrapingSourceEntity.WebKind && s.HarvestPausedAt == null)
                .OrderBy(s => s.LastDiscoveredUtc ?? DateTime.MinValue).ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);
            var seedDomains = seeds
                .Select(s => SourceAdmissionPolicy.NormalizeDomain(s.Domain))
                .Where(d => d != null).Select(d => d!)
                .Distinct(StringComparer.Ordinal)
                .Take(Math.Clamp(_settings.SeedsPerRun, 1, 20))
                .ToList();

            var run = new SourceDiscoveryRunEntity
            {
                StartedUtc = Now(),
                Seeds = Clip(string.Join(",", seedDomains), 2047)!,
                Status = SourceDiscoveryRunEntity.Running,
                CreatedDate = Now(),
            };
            _db.SourceDiscoveryRuns.Add(run);

            if (seedDomains.Count == 0)
            {
                return await EndRunAsync(run, SourceDiscoveryRunEntity.Skipped, null, "No approved web source to seed from.", null, cancellationToken);
            }

            var start = await _scraper.StartDiscoveryJobAsync(seedDomains, Math.Clamp(_settings.MaxCandidatesPerRun, 1, 50), _settings.BudgetSeconds, cancellationToken);
            if (start.Outcome == ScraperJobOutcome.Busy)
            {
                return await EndRunAsync(run, SourceDiscoveryRunEntity.Skipped, null, start.Error, null, cancellationToken);
            }

            if (start.Job == null)
            {
                return await EndRunAsync(run, SourceDiscoveryRunEntity.Failed, null, $"The discovery job did not start: {start.Error}", null, cancellationToken);
            }

            run.JobId = start.Job.JobId;
            await _db.SaveChangesAsync(cancellationToken);

            var (job, failure) = await PollAsync(DiscoveryKind, start.Job, cancellationToken);
            var result = Read<ScraperDiscoveryResult>(job.Result);
            if (result != null)
            {
                await KeepCandidatesAsync(result, seeds, cancellationToken);
                run.Candidates = result.Candidates.Count;
                run.Leads = result.CandidatesNotProbed.Count;
                run.SeedsVisited = Clip(string.Join(",", result.SeedsVisited), 2047);
                run.SeedsNotVisited = Clip(string.Join(",", result.SeedsNotVisited), 2047);
                run.SeedsRefused = Clip(string.Join(",", result.SeedsRefused), 2047);
                run.PagesFetched = result.PagesFetched;
            }

            if (failure != null)
            {
                return await EndRunAsync(run, SourceDiscoveryRunEntity.Failed, job.StopReason, failure, result, cancellationToken);
            }

            return job.Status switch
            {
                ScraperJob.Completed => await EndRunAsync(run, SourceDiscoveryRunEntity.Completed, job.StopReason, null, result, cancellationToken),
                ScraperJob.Partial => await EndRunAsync(run, SourceDiscoveryRunEntity.Partial, job.StopReason, null, result, cancellationToken),
                _ => await EndRunAsync(run, SourceDiscoveryRunEntity.Failed, job.StopReason, job.Error ?? $"The discovery job ended {job.Status}.", result, cancellationToken),
            };
        }

        private async Task<SourceDiscoveryRunModel> EndRunAsync(SourceDiscoveryRunEntity run, string status, string? stopReason, string? error, ScraperDiscoveryResult? result, CancellationToken cancellationToken)
        {
            run.Status = status;
            run.StopReason = Clip(stopReason, 16);
            run.Error = Clip(error, 1023);
            run.FinishedUtc = Now();
            await _db.SaveChangesAsync(cancellationToken);
            if (status == SourceDiscoveryRunEntity.Failed)
            {
                _logger.LogWarning("Source discovery run {RunId} failed: {Error} ({Candidates} candidates kept)", run.Id, error, result?.Candidates.Count ?? 0);
            }
            else
            {
                _logger.LogInformation("Source discovery run {RunId} {Status}: {Candidates} candidates, {Leads} leads", run.Id, status, run.Candidates, run.Leads);
            }

            return Map(run);
        }

        private async Task KeepCandidatesAsync(ScraperDiscoveryResult result, List<ScrapingSourceEntity> seeds, CancellationToken cancellationToken)
        {
            var now = Now();
            var visited = result.SeedsVisited.Concat(result.SeedsRefused).Select(SourceAdmissionPolicy.NormalizeDomain).Where(d => d != null).ToHashSet(StringComparer.Ordinal);
            foreach (var seed in seeds.Where(s => visited.Contains(SourceAdmissionPolicy.NormalizeDomain(s.Domain))))
            {
                seed.LastDiscoveredUtc = now;
            }

            var known = await KnownSourcesAsync(cancellationToken);
            var found = result.Candidates.Select(c => (Source: c, Lead: false))
                .Concat(result.CandidatesNotProbed.Select(c => (Source: c, Lead: true)));
            foreach (var (source, lead) in found)
            {
                var domain = SourceAdmissionPolicy.NormalizeDomain(source.Domain);
                if (domain == null || known.ContainsKey(domain)) continue;

                var candidate = await _db.SourceCandidates.FirstOrDefaultAsync(c => c.Domain == domain, cancellationToken)
                    ?? _db.SourceCandidates.Local.FirstOrDefault(c => c.Domain == domain);
                if (candidate == null)
                {
                    _db.SourceCandidates.Add(new SourceCandidateEntity
                    {
                        Domain = domain,
                        EvidenceUrl = lead ? null : Clip(source.EvidenceUrl, 2047),
                        FoundVia = Clip(source.DiscoveredVia, 255),
                        Signal = lead ? null : Clip(source.Signal, 32),
                        FirstSeenUtc = now,
                        LastSeenUtc = now,
                        TimesSeen = 1,
                        State = lead ? SourceCandidateEntity.Lead : SourceCandidateEntity.New,
                        CreatedDate = now,
                    });
                    continue;
                }

                candidate.LastSeenUtc = now;
                candidate.TimesSeen++;
                if (!lead && candidate.State == SourceCandidateEntity.Lead)
                {
                    candidate.State = SourceCandidateEntity.New;
                    candidate.Signal = Clip(source.Signal, 32);
                    candidate.EvidenceUrl = Clip(source.EvidenceUrl, 2047);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<SourceCheckModel?> RunNextCheckAsync(CancellationToken cancellationToken = default)
        {
            await ExpireStaleAsync(cancellationToken);
            var now = Now();
            var started = await _db.SourceChecks.CountAsync(c => c.StartedUtc > now.AddHours(-24), cancellationToken);
            if (started >= _settings.MaxChecksPerDay) return null;

            await AdoptRequestsAsync(cancellationToken);
            var target = await NextTargetAsync(cancellationToken);
            if (target == null) return null;

            var (domain, purpose, candidate, source) = target.Value;
            var start = await _scraper.StartSourceCheckJobAsync(domain, SourceAdmissionPolicy.SampleSize, _settings.CheckBudgetSeconds, cancellationToken);
            if (start.Outcome == ScraperJobOutcome.Busy) return null;

            var check = new SourceCheckEntity
            {
                Domain = Clip(domain, 255)!,
                Purpose = purpose,
                SourceCandidateId = candidate?.Id,
                ScrapingSourceId = source?.Id,
                JobId = start.Job?.JobId,
                StartedUtc = now,
                Status = SourceCheckEntity.Running,
                CreatedDate = now,
            };
            _db.SourceChecks.Add(check);
            await _db.SaveChangesAsync(cancellationToken);

            if (start.Job == null)
            {
                return await EndCheckAsync(check, SourceCheckEntity.Failed, $"The source check did not start: {start.Error}", cancellationToken);
            }

            var (job, failure) = await PollAsync(SourceCheckKind, start.Job, cancellationToken);
            check.StopReason = Clip(job.StopReason, 16);
            if (failure != null)
            {
                return await EndCheckAsync(check, failure.StartsWith("lost", StringComparison.Ordinal) ? SourceCheckEntity.Lost : SourceCheckEntity.Failed, failure, cancellationToken);
            }

            var report = Read<ScraperSourceCheckReport>(job.Result);
            if (report == null)
            {
                return await EndCheckAsync(check, SourceCheckEntity.Failed, job.Error ?? $"The source check ended {job.Status} with no report.", cancellationToken);
            }

            var judgement = await JudgeAsync(report, domain, cancellationToken);
            check.Status = job.Status == ScraperJob.Completed ? SourceCheckEntity.Completed : job.Status == ScraperJob.Partial ? SourceCheckEntity.Partial : SourceCheckEntity.Failed;
            check.Error = Clip(job.Error, 1023);
            check.FinishedUtc = Now();
            check.ScraperVerdict = Clip(report.Verdict, 16);
            check.RuleVersion = Clip(report.RuleVersion, 32);
            check.Verdict = judgement.Verdict;
            check.Conclusive = judgement.Conclusive;
            check.PolicyVersion = SourceAdmissionPolicy.Version;
            check.ReasonsJson = JsonSerializer.Serialize(judgement.Reasons, JsonOptions);
            check.ReportJson = job.Result?.GetRawText();
            await _db.SaveChangesAsync(cancellationToken);

            if (candidate != null) await SettleCandidateAsync(candidate, check, judgement, cancellationToken);
            if (source != null) await SettleSourceAsync(source, check, judgement, report, cancellationToken);
            return MapCheck(check);
        }

        private async Task<SourceCheckModel> EndCheckAsync(SourceCheckEntity check, string status, string error, CancellationToken cancellationToken)
        {
            check.Status = status;
            check.Error = Clip(error, 1023);
            check.FinishedUtc = Now();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Source check {CheckId} of {Domain} {Status}: {Error}", check.Id, check.Domain, status, error);
            return MapCheck(check);
        }

        private async Task<SourceJudgement> JudgeAsync(ScraperSourceCheckReport report, string domain, CancellationToken cancellationToken)
        {
            var checkedCount = 0;
            var passed = 0;
            foreach (var page in report.Samples?.Pages.Where(p => p.Recipe != null) ?? Enumerable.Empty<ScraperCheckSamplePage>())
            {
                checkedCount++;
                if ((await _vetting.VetAsync(page.Recipe!)).Count == 0) passed++;
            }

            var key = SourceAdmissionPolicy.NormalizeDomain(domain);
            var known = await KnownSourcesAsync(cancellationToken);
            var rejected = key != null && known.TryGetValue(key, out var status) && status == ScrapingSourceStatusEnum.Rejected;
            return SourceAdmissionPolicy.Judge(SourceAdmissionPolicy.Facts(report, checkedCount, passed, rejected));
        }

        private async Task SettleCandidateAsync(SourceCandidateEntity candidate, SourceCheckEntity check, SourceJudgement judgement, CancellationToken cancellationToken)
        {
            candidate.LastCheckId = check.Id;
            if (candidate.State is not (SourceCandidateEntity.Approved or SourceCandidateEntity.Rejected))
            {
                candidate.State = judgement.Clear ? SourceCandidateEntity.Clear : SourceCandidateEntity.NotClear;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Stores the re-judging of an approved source beside it. Harvesting is paused only when a
        /// conclusive verdict turns from clear to not clear; a first verdict, an inconclusive check
        /// or a source that was never clear changes nothing but what is shown, because what happens
        /// to a source that is not clear is the owner's decision.
        /// </summary>
        private async Task SettleSourceAsync(ScrapingSourceEntity source, SourceCheckEntity check, SourceJudgement judgement, ScraperSourceCheckReport report, CancellationToken cancellationToken)
        {
            var now = Now();
            source.CheckedAtUtc = now;
            source.LastCheckId = check.Id;
            if (!judgement.Conclusive)
            {
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var before = source.CheckVerdict;
            source.CheckVerdict = judgement.Verdict;
            if (source.Kind == ScrapingSourceEntity.WebKind)
            {
                var statement = report.Licence?.Statements.FirstOrDefault(s => s.Kind == report.Licence.Kind) ?? report.Licence?.Statements.FirstOrDefault();
                source.LicenceKind = Clip(report.Licence?.Kind, 32);
                source.LicenceStatement = Clip(statement?.Text, 2047);
                source.LicenceFoundAt = Clip(statement?.FoundAt, 2047);
            }

            if (before != judgement.Verdict)
            {
                _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Recheck, SourceJournal.VerdictField,
                    SourceJournal.Value(before ?? SourceJournal.None), SourceJournal.Value(judgement.Verdict, $"check {check.Id}"), now, SystemConstants.SystemPersonId));
            }

            if (before == SourceAdmissionPolicy.Clear && !judgement.Clear && source.HarvestPausedAt == null)
            {
                var against = string.Join(", ", judgement.Against.Select(r => r.Code));
                SetPaused(source, $"re-check {check.Id} turned not clear: {against}", null, now, SourceJournal.HarvestPause);
                _logger.LogWarning("Harvesting of {Domain} paused: its re-check turned not clear ({Against})", source.Domain, against);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task AdoptRequestsAsync(CancellationToken cancellationToken)
        {
            var pending = await _db.ScrapingSources
                .Where(s => s.Status == ScrapingSourceStatusEnum.Pending && s.Kind == ScrapingSourceEntity.WebKind)
                .ToListAsync(cancellationToken);
            if (pending.Count == 0) return;

            var domains = await _db.SourceCandidates.Select(c => c.Domain).ToListAsync(cancellationToken);
            var have = domains.ToHashSet(StringComparer.Ordinal);
            var now = Now();
            foreach (var source in pending)
            {
                var domain = SourceAdmissionPolicy.NormalizeDomain(source.Domain);
                if (domain == null || !have.Add(domain)) continue;
                _db.SourceCandidates.Add(new SourceCandidateEntity
                {
                    Domain = domain,
                    EvidenceUrl = source.SampleUrl ?? $"https://{source.Domain}/",
                    FoundVia = RequestPurpose,
                    Signal = RequestPurpose,
                    FirstSeenUtc = source.CreatedDate == default ? now : source.CreatedDate,
                    LastSeenUtc = now,
                    TimesSeen = 1,
                    State = SourceCandidateEntity.New,
                    ScrapingSourceId = source.Id,
                    CreatedDate = now,
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task<(string Domain, string Purpose, SourceCandidateEntity? Candidate, ScrapingSourceEntity? Source)?> NextTargetAsync(CancellationToken cancellationToken)
        {
            var now = Now();
            var recheckBefore = now.AddDays(-Math.Max(1, _settings.RecheckDays));
            var retryAfter = now.AddHours(-FailedRetryHours);
            var failedLately = await _db.SourceChecks
                .Where(c => c.Verdict == null && c.Status != SourceCheckEntity.Running && c.StartedUtc > retryAfter)
                .Select(c => new { c.ScrapingSourceId, c.SourceCandidateId })
                .ToListAsync(cancellationToken);
            var sourcesFailed = failedLately.Where(c => c.ScrapingSourceId != null).Select(c => c.ScrapingSourceId!.Value).ToHashSet();
            var candidatesFailed = failedLately.Where(c => c.SourceCandidateId != null).Select(c => c.SourceCandidateId!.Value).ToHashSet();

            var dueSource = (await _db.ScrapingSources
                .Where(s => s.Status == ScrapingSourceStatusEnum.Approved && s.Kind == ScrapingSourceEntity.WebKind
                    && (s.CheckedAtUtc == null || s.CheckedAtUtc < recheckBefore))
                .OrderBy(s => s.CheckedAtUtc ?? DateTime.MinValue).ThenBy(s => s.Id)
                .ToListAsync(cancellationToken))
                .FirstOrDefault(s => !sourcesFailed.Contains(s.Id));
            if (dueSource != null)
            {
                return (dueSource.Domain, RecheckPurpose, null, dueSource);
            }

            var lastChecks = await _db.SourceChecks
                .Where(c => c.SourceCandidateId != null && c.Verdict != null)
                .Select(c => new { c.Id, c.FinishedUtc, c.Conclusive, c.RuleVersion, c.PolicyVersion })
                .ToListAsync(cancellationToken);
            var byId = lastChecks.ToDictionary(c => c.Id);
            var candidates = await _db.SourceCandidates
                .Where(c => c.State == SourceCandidateEntity.New || c.State == SourceCandidateEntity.Clear || c.State == SourceCandidateEntity.NotClear)
                .ToListAsync(cancellationToken);
            var due = candidates.Where(c =>
            {
                if (candidatesFailed.Contains(c.Id)) return false;
                if (c.State == SourceCandidateEntity.New || c.LastCheckId == null || !byId.TryGetValue(c.LastCheckId.Value, out var last)) return true;
                if (last.PolicyVersion != SourceAdmissionPolicy.Version || last.RuleVersion != SourceAdmissionPolicy.ScraperRuleVersion) return true;
                if (!last.Conclusive || c.State == SourceCandidateEntity.Clear) return last.FinishedUtc < recheckBefore;
                return false;
            });
            var next = due
                .OrderBy(c => c.FoundVia == RequestPurpose ? 0 : 1)
                .ThenBy(c => c.State == SourceCandidateEntity.New ? 0 : 1)
                .ThenByDescending(c => c.TimesSeen)
                .ThenBy(c => c.FirstSeenUtc)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (next == null) return null;

            var host = Uri.TryCreate(next.EvidenceUrl, UriKind.Absolute, out var evidence) ? evidence.Host : next.Domain;
            return (host, next.FoundVia == RequestPurpose ? RequestPurpose : CandidatePurpose, next, null);
        }

        /// <summary>
        /// Ends the runs and checks still marked running past their budget plus StaleGraceSeconds: their
        /// poll was cut off (nom-api stopped or the tick failed), so a run is stored as failed and a check
        /// as lost, and both are shown with the failed ones.
        /// </summary>
        private async Task ExpireStaleAsync(CancellationToken cancellationToken)
        {
            var now = Now();
            var runBefore = now.AddSeconds(-(Math.Max(_settings.BudgetSeconds, 60) + 60 + StaleGraceSeconds));
            var checkBefore = now.AddSeconds(-(Math.Max(_settings.CheckBudgetSeconds, 60) + 60 + StaleGraceSeconds));
            var runs = await _db.SourceDiscoveryRuns
                .Where(r => r.Status == SourceDiscoveryRunEntity.Running && r.StartedUtc < runBefore)
                .ToListAsync(cancellationToken);
            var checks = await _db.SourceChecks
                .Where(c => c.Status == SourceCheckEntity.Running && c.StartedUtc < checkBefore)
                .ToListAsync(cancellationToken);
            if (runs.Count == 0 && checks.Count == 0) return;

            foreach (var run in runs)
            {
                run.Status = SourceDiscoveryRunEntity.Failed;
                run.Error = Clip($"lost: the run was still running past its budget, so its poll of job {run.JobId ?? "(none)"} was cut off", 1023);
                run.FinishedUtc = now;
            }

            foreach (var check in checks)
            {
                check.Status = SourceCheckEntity.Lost;
                check.Error = Clip($"lost: the check was still running past its budget, so its poll of job {check.JobId ?? "(none)"} was cut off", 1023);
                check.FinishedUtc = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Source lane: {Runs} discovery runs and {Checks} source checks were cut off while polling and are stored as failed", runs.Count, checks.Count);
        }

        /// <summary>
        /// Polls a job every PollSeconds until it is no longer running. A 404, a failed answer, a
        /// transport error or no end by its deadline plus 60 s is a failure (the reason is returned),
        /// with the last job read, so the caller keeps what it had found.
        /// </summary>
        private async Task<(ScraperJob Job, string? Failure)> PollAsync(string kind, ScraperJob job, CancellationToken cancellationToken)
        {
            var created = job.CreatedUtc?.UtcDateTime ?? Now();
            var deadline = (job.DeadlineUtc?.UtcDateTime ?? created.AddSeconds(Math.Max(job.BudgetSeconds, 60))).AddSeconds(60);
            while (job.Status == ScraperJob.Running)
            {
                if (Now() > deadline)
                {
                    return (job, $"lost: the {kind} job {job.JobId} did not end by its deadline plus 60 s");
                }

                await Delay(TimeSpan.FromSeconds(Math.Clamp(_settings.PollSeconds, 1, 60)), cancellationToken);
                var answer = await _scraper.GetJobAsync(kind, job.JobId, cancellationToken);
                switch (answer.Outcome)
                {
                    case ScraperJobOutcome.NotFound:
                        return (job, $"lost: the {kind} job {job.JobId} is gone from the scraper ({answer.Error})");
                    case ScraperJobOutcome.Ok when answer.Job != null:
                        job = answer.Job;
                        break;
                    default:
                        return (job, $"failed: reading the {kind} job {job.JobId}: {answer.Error}");
                }
            }

            return job.Status == ScraperJob.Failed ? (job, $"failed: {job.Error ?? "the scraper reported the job failed"}") : (job, null);
        }

        public async Task<int> WatchAsync(CancellationToken cancellationToken = default)
        {
            await ExpireStaleAsync(cancellationToken);
            var now = Now();
            var sources = await _db.ScrapingSources
                .Where(s => s.Status == ScrapingSourceStatusEnum.Approved && s.ProbationUntilUtc != null)
                .ToListAsync(cancellationToken);
            var ended = 0;
            foreach (var source in sources)
            {
                var recipes = await RecipesOf(source).Select(r => new { r.Id, r.CurationStatusId }).ToListAsync(cancellationToken);
                var limit = source.ProbationRecipeLimit ?? _settings.ProbationRecipes;
                if (recipes.Count < limit && now < source.ProbationUntilUtc) continue;

                var ids = recipes.Select(r => r.Id).ToList();
                var lines = await _db.RecipeIngredients.Where(l => ids.Contains(l.RecipeId))
                    .Select(l => new { l.Quantity, l.RawLine, l.LineKind }).ToListAsync(cancellationToken);
                var revision = recipes.Count == 0 ? 0 : recipes.Count(r => r.CurationStatusId == (long)CurationStatusEnum.RequiresRevision) / (double)recipes.Count;
                var unquantified = lines.Count == 0 ? 0 : lines.Count(l => l.Quantity <= 0 && !RecipeVettingService.IsAcceptablyUnquantified(l.RawLine, l.LineKind)) / (double)lines.Count;
                var figures = $"{recipes.Count} recipes, {revision:P1} needing revision (at most {ProbationMaxRequiresRevision:P0}), {unquantified:P1} of {lines.Count} lines without a quantity they need (at most {ProbationMaxUnquantified:P0})";
                var failed = recipes.Count == 0 ? "no recipe was harvested in probation"
                    : revision > ProbationMaxRequiresRevision ? "too many recipes need revision"
                    : unquantified > ProbationMaxUnquantified ? "too many lines lack a quantity"
                    : null;

                source.ProbationUntilUtc = null;
                source.ProbationResult = Clip(failed == null ? $"passed {now:yyyy-MM-dd}: {figures}" : $"failed {now:yyyy-MM-dd}: {failed}; {figures}", 1023);
                _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, failed == null ? SourceJournal.ProbationPass : SourceJournal.ProbationFail, SourceJournal.ProbationField,
                    SourceJournal.Value("probation"), SourceJournal.Value(failed == null ? "passed" : "failed", source.ProbationResult), now, SystemConstants.SystemPersonId));
                if (failed != null && source.HarvestPausedAt == null)
                {
                    SetPaused(source, $"probation failed: {failed}", null, now, SourceJournal.HarvestPause);
                }

                ended++;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return ended;
        }

        public async Task<SourceActionResult> ApproveCandidateAsync(long candidateId, long personId, string? notes)
        {
            var candidate = await _db.SourceCandidates.FirstOrDefaultAsync(c => c.Id == candidateId);
            if (candidate == null) return SourceActionResult.Missing("No such candidate.");

            var check = candidate.LastCheckId == null ? null : await _db.SourceChecks.FirstOrDefaultAsync(c => c.Id == candidate.LastCheckId);
            var blocked = ApproveBlocked(candidate, check);
            if (blocked != null) return SourceActionResult.Refused(blocked);

            var now = Now();
            var host = Uri.TryCreate(candidate.EvidenceUrl, UriKind.Absolute, out var evidence) ? evidence.Host.ToLowerInvariant() : candidate.Domain;
            var source = await FindSourceAsync(candidate.Domain);
            var before = source == null ? SourceJournal.None : source.Status.ToString();
            if (source == null)
            {
                source = new ScrapingSourceEntity
                {
                    Domain = Clip(host, 255)!,
                    Kind = ScrapingSourceEntity.WebKind,
                    SampleUrl = candidate.EvidenceUrl,
                    CreatedDate = now,
                    CreatedByPersonId = personId,
                };
                _db.ScrapingSources.Add(source);
            }
            else if (source.Status == ScrapingSourceStatusEnum.Approved)
            {
                return SourceActionResult.Refused($"{source.Domain} is already approved.");
            }
            else if (source.Status == ScrapingSourceStatusEnum.Rejected)
            {
                return SourceActionResult.Refused("An admin rejected this domain.");
            }

            var report = Read<ScraperSourceCheckReport>(check!.ReportJson);
            var statement = report?.Licence?.Statements.FirstOrDefault(s => s.Kind == report.Licence.Kind) ?? report?.Licence?.Statements.FirstOrDefault();
            source.Status = ScrapingSourceStatusEnum.Approved;
            source.ReviewedByPersonId = personId;
            source.ReviewedDate = now;
            source.Notes = Clip(notes ?? source.Notes, 2047);
            source.CheckVerdict = check.Verdict;
            source.CheckedAtUtc = check.FinishedUtc;
            source.LastCheckId = check.Id;
            source.LicenceKind = Clip(report?.Licence?.Kind, 32);
            source.LicenceStatement = Clip(statement?.Text, 2047);
            source.LicenceFoundAt = Clip(statement?.FoundAt, 2047);
            source.ProbationUntilUtc = now.AddDays(Math.Max(1, _settings.ProbationDays));
            source.ProbationRecipeLimit = Math.Max(1, _settings.ProbationRecipes);
            source.ProbationResult = null;
            source.LastModifiedDate = now;
            source.LastModifiedByPersonId = personId;
            await _db.SaveChangesAsync();

            candidate.State = SourceCandidateEntity.Approved;
            candidate.ScrapingSourceId = source.Id;
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Approve, SourceJournal.StatusField,
                SourceJournal.Value(before), SourceJournal.Value(nameof(ScrapingSourceStatusEnum.Approved), $"candidate {candidate.Id}; check {check.Id} clear; probation until {source.ProbationUntilUtc:yyyy-MM-dd}"),
                now, personId));
            await _db.SaveChangesAsync();
            _logger.LogInformation("Source {Domain} approved by person {PersonId} on clear check {CheckId}", source.Domain, personId, check.Id);
            return new SourceActionResult { Ok = true, Source = await SourceModelAsync(source) };
        }

        private string? ApproveBlocked(SourceCandidateEntity candidate, SourceCheckEntity? check)
        {
            if (candidate.State == SourceCandidateEntity.Approved) return "This candidate is already approved.";
            if (candidate.State == SourceCandidateEntity.Rejected) return "An admin rejected this domain.";
            if (check == null || check.Verdict == null) return "This candidate has no check yet.";
            if (check.Verdict != SourceAdmissionPolicy.Clear || !check.Conclusive) return "Its last check is not clear.";
            if (check.PolicyVersion != SourceAdmissionPolicy.Version || check.RuleVersion != SourceAdmissionPolicy.ScraperRuleVersion) return "Its last check is under older rules; it will be checked again.";
            if (check.FinishedUtc == null || check.FinishedUtc < Now().AddDays(-Math.Max(1, _settings.ClearValidDays))) return $"Its clear check is older than {_settings.ClearValidDays} days; it will be checked again.";
            return null;
        }

        public async Task<SourceActionResult> PauseHarvestAsync(long sourceId, long personId, string? reason)
        {
            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == sourceId);
            if (source == null) return SourceActionResult.Missing("No such source.");
            if (source.HarvestPausedAt != null) return SourceActionResult.Refused("Harvesting of this source is already paused.");
            SetPaused(source, string.IsNullOrWhiteSpace(reason) ? "paused by an admin" : reason.Trim(), personId, Now(), SourceJournal.HarvestPause);
            await _db.SaveChangesAsync();
            return new SourceActionResult { Ok = true, Source = await SourceModelAsync(source) };
        }

        public async Task<SourceActionResult> ResumeHarvestAsync(long sourceId, long personId, string? reason)
        {
            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == sourceId);
            if (source == null) return SourceActionResult.Missing("No such source.");
            if (source.HarvestPausedAt == null) return SourceActionResult.Refused("Harvesting of this source is not paused.");
            SetRunning(source, reason, personId, Now(), SourceJournal.HarvestResume);
            await _db.SaveChangesAsync();
            return new SourceActionResult { Ok = true, Source = await SourceModelAsync(source) };
        }

        private void SetPaused(ScrapingSourceEntity source, string reason, long? personId, DateTime now, string changeType)
        {
            source.HarvestPausedAt = now;
            source.HarvestPausedReason = Clip(reason, 1023);
            source.HarvestPausedByPersonId = personId;
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, changeType, SourceJournal.HarvestField,
                SourceJournal.Value(SourceJournal.Running), SourceJournal.Value(SourceJournal.Paused, reason), now, personId ?? SystemConstants.SystemPersonId));
        }

        private void SetRunning(ScrapingSourceEntity source, string? reason, long personId, DateTime now, string changeType)
        {
            var old = SourceJournal.Value(SourceJournal.Paused, source.HarvestPausedReason);
            source.HarvestPausedAt = null;
            source.HarvestPausedReason = null;
            source.HarvestPausedByPersonId = null;
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, changeType, SourceJournal.HarvestField,
                old, SourceJournal.Value(SourceJournal.Running, reason), now, personId));
        }

        public async Task<SourceWithholdReport> WithholdRecipesAsync(long sourceId, long personId, string? reason, bool apply)
        {
            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == sourceId);
            if (source == null) return new SourceWithholdReport { Error = "No such source.", NotFound = true };
            if (source.RecipesWithheldAt != null) return new SourceWithholdReport { Error = "This source's recipes are already withheld.", Source = await SourceModelAsync(source) };

            var recipes = await RecipesOf(source).Where(r => r.SourceWithheldById == null).ToListAsync();
            if (!apply) return new SourceWithholdReport { Recipes = recipes.Count, Source = await SourceModelAsync(source) };

            var now = Now();
            var why = string.IsNullOrWhiteSpace(reason) ? "withheld by an admin" : reason.Trim();
            foreach (var recipe in recipes)
            {
                recipe.SourceWithheldById = source.Id;
            }

            source.RecipesWithheldAt = now;
            source.RecipesWithheldReason = Clip(why, 1023);
            source.RecipesWithheldByPersonId = personId;
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Withhold, SourceJournal.RecipesField,
                SourceJournal.Value(SourceJournal.Shown), SourceJournal.Value(SourceJournal.Withheld, $"{recipes.Count} recipes; {why}"), now, personId));
            await _db.SaveChangesAsync();
            return new SourceWithholdReport { Applied = true, Recipes = recipes.Count, Source = await SourceModelAsync(source) };
        }

        public async Task<SourceWithholdReport> ReleaseRecipesAsync(long sourceId, long personId, bool apply)
        {
            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == sourceId);
            if (source == null) return new SourceWithholdReport { Error = "No such source.", NotFound = true };
            if (source.RecipesWithheldAt == null) return new SourceWithholdReport { Error = "This source's recipes are not withheld.", Source = await SourceModelAsync(source) };

            var recipes = await _db.Recipes.IgnoreQueryFilters().Where(r => r.SourceWithheldById == source.Id).ToListAsync();
            if (!apply) return new SourceWithholdReport { Recipes = recipes.Count, Source = await SourceModelAsync(source) };

            var now = Now();
            foreach (var recipe in recipes)
            {
                recipe.SourceWithheldById = null;
            }

            var old = SourceJournal.Value(SourceJournal.Withheld, source.RecipesWithheldReason);
            source.RecipesWithheldAt = null;
            source.RecipesWithheldReason = null;
            source.RecipesWithheldByPersonId = null;
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Release, SourceJournal.RecipesField,
                old, SourceJournal.Value(SourceJournal.Shown, $"{recipes.Count} recipes"), now, personId));
            await _db.SaveChangesAsync();
            return new SourceWithholdReport { Applied = true, Recipes = recipes.Count, Source = await SourceModelAsync(source) };
        }

        public async Task<SourceActionResult> RecordHandSourceAsync(HandSourceRequestModel request, long personId)
        {
            var domain = SourceAdmissionPolicy.NormalizeDomain(request.Domain);
            if (domain == null) return SourceActionResult.Refused("A hand source needs the domain where its licence statement was found.");
            if (string.IsNullOrWhiteSpace(request.Title)) return SourceActionResult.Refused("A hand source needs a title.");
            if (string.IsNullOrWhiteSpace(request.LicenceKind) || string.IsNullOrWhiteSpace(request.LicenceStatement) || string.IsNullOrWhiteSpace(request.LicenceFoundAt))
            {
                return SourceActionResult.Refused("A hand source needs its licence kind, the statement as the source words it, and where it was found.");
            }

            var kind = request.LicenceKind.Trim().ToLowerInvariant();
            if (!SourceAdmissionPolicy.ClearLicences.Contains(kind))
            {
                return SourceActionResult.Refused($"Only a licence that allows commercial reuse and adaptation can be recorded ({string.Join(", ", SourceAdmissionPolicy.ClearLicences.OrderBy(k => k))}).");
            }

            var host = Uri.TryCreate(request.Domain.Contains("://", StringComparison.Ordinal) ? request.Domain.Trim() : "https://" + request.Domain.Trim(), UriKind.Absolute, out var given)
                ? given.Host.ToLowerInvariant() : domain;
            var source = await FindSourceAsync(domain);
            if (source != null && source.Kind == ScrapingSourceEntity.WebKind)
            {
                return SourceActionResult.Refused($"{source.Domain} is already a web source ({source.Status}).");
            }

            var now = Now();
            var before = source == null ? SourceJournal.None : source.Status.ToString();
            var oldLicence = source == null ? null : LicenceValue(source);
            if (source == null)
            {
                source = new ScrapingSourceEntity { Domain = Clip(host, 255)!, Kind = ScrapingSourceEntity.HandKind, CreatedDate = now, CreatedByPersonId = personId };
                _db.ScrapingSources.Add(source);
            }

            source.Status = ScrapingSourceStatusEnum.Approved;
            source.Title = Clip(request.Title.Trim(), 255);
            source.LicenceKind = kind;
            source.LicenceStatement = Clip(request.LicenceStatement.Trim(), 2047);
            source.LicenceFoundAt = Clip(request.LicenceFoundAt.Trim(), 2047);
            source.LicenceEnteredByPersonId = personId;
            source.Notes = Clip(request.Notes ?? source.Notes, 2047);
            source.ReviewedByPersonId = personId;
            source.ReviewedDate = now;
            await _db.SaveChangesAsync();
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.HandRecord, SourceJournal.StatusField,
                SourceJournal.Value(before), SourceJournal.Value(nameof(ScrapingSourceStatusEnum.Approved), $"hand source; licence {kind} at {source.LicenceFoundAt}"), now, personId));
            var newLicence = LicenceValue(source);
            if (oldLicence != newLicence)
            {
                _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.HandRecord, SourceJournal.LicenceField,
                    oldLicence ?? SourceJournal.None, newLicence, now, personId));
            }

            await _db.SaveChangesAsync();
            return new SourceActionResult { Ok = true, Source = await SourceModelAsync(source) };
        }

        private sealed record LicenceFacts(string? Statement, string? FoundAt, long? EnteredByPersonId);

        /// <summary>A source's licence facts as a journal value: the kind as the state, the statement, its URL and who entered them as the detail.</summary>
        private static string LicenceValue(ScrapingSourceEntity source) =>
            SourceJournal.Value(source.LicenceKind ?? SourceJournal.None,
                JsonSerializer.Serialize(new LicenceFacts(source.LicenceStatement, source.LicenceFoundAt, source.LicenceEnteredByPersonId), JsonOptions));

        public async Task<List<HarvestableSourceModel>> HarvestableAsync(CancellationToken cancellationToken = default)
        {
            var sources = await _db.ScrapingSources.AsNoTracking()
                .Where(s => s.Status == ScrapingSourceStatusEnum.Approved && s.Kind == ScrapingSourceEntity.WebKind
                    && s.HarvestPausedAt == null && s.RecipesWithheldAt == null)
                .OrderBy(s => s.Domain)
                .ToListAsync(cancellationToken);
            var checkIds = sources.Where(s => s.LastCheckId != null).Select(s => s.LastCheckId!.Value).ToList();
            var reports = await _db.SourceChecks.AsNoTracking().Where(c => checkIds.Contains(c.Id))
                .Select(c => new { c.Id, c.ReportJson }).ToListAsync(cancellationToken);
            var delays = reports.ToDictionary(r => r.Id, r => Read<ScraperSourceCheckReport>(r.ReportJson)?.Robots?.NomBot?.CrawlDelaySeconds);

            var list = new List<HarvestableSourceModel>();
            foreach (var source in sources)
            {
                int? limit = null;
                if (source.ProbationUntilUtc != null)
                {
                    var taken = await RecipesOf(source).CountAsync(cancellationToken);
                    limit = Math.Max(0, (source.ProbationRecipeLimit ?? _settings.ProbationRecipes) - taken);
                }

                list.Add(new HarvestableSourceModel
                {
                    Domain = source.Domain,
                    Probation = source.ProbationUntilUtc != null,
                    RecipeLimit = limit,
                    CrawlDelaySeconds = source.LastCheckId is { } id && delays.TryGetValue(id, out var delay) ? delay : null,
                });
            }

            return list;
        }

        public async Task<List<SourceJournalEntryModel>> JournalAsync(int limit, CancellationToken cancellationToken = default)
        {
            var rows = await _db.AuditLogEntries.AsNoTracking()
                .Where(e => e.EntityType == SourceJournal.EntityType)
                .OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id)
                .Take(Math.Clamp(limit, 1, 500))
                .ToListAsync(cancellationToken);
            var ids = rows.Select(r => r.EntityId).Distinct().ToList();
            var sources = await _db.ScrapingSources.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
            var latest = await LatestEntriesAsync(ids, cancellationToken);
            return rows.Select(r => new SourceJournalEntryModel
            {
                Id = r.Id,
                SourceId = r.EntityId,
                Domain = sources.TryGetValue(r.EntityId, out var s) ? s.Domain : null,
                ChangeType = r.ChangeType,
                Field = r.PropertyName,
                OldValue = r.OldValue,
                NewValue = r.NewValue,
                Timestamp = r.Timestamp,
                ChangedByPersonId = r.ChangedByPersonId,
                CanUndo = s != null && SourceJournal.Undoable(r.PropertyName) && latest.Contains(r.Id) && CurrentState(s, r.PropertyName!) == SourceJournal.State(r.NewValue),
            }).ToList();
        }

        public async Task<SourceActionResult> UndoAsync(long entryId, long personId)
        {
            var entry = await _db.AuditLogEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entryId && e.EntityType == SourceJournal.EntityType);
            if (entry == null) return SourceActionResult.Missing("No such source journal entry.");
            if (!SourceJournal.Undoable(entry.PropertyName)) return SourceActionResult.Refused($"A {entry.ChangeType} entry cannot be undone.");

            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == entry.EntityId);
            if (source == null) return SourceActionResult.Missing("The source of this entry is gone.");

            var field = entry.PropertyName!;
            var target = SourceJournal.State(entry.OldValue);
            if (!(await LatestEntriesAsync(new[] { source.Id }, CancellationToken.None)).Contains(entry.Id) || CurrentState(source, field) != SourceJournal.State(entry.NewValue))
            {
                return SourceActionResult.Refused("The source changed since; undo the later entries first.");
            }

            var now = Now();
            var note = $"undo of entry {entry.Id}";
            switch (field)
            {
                case SourceJournal.HarvestField when target == SourceJournal.Paused:
                    SetPaused(source, SourceJournal.Detail(entry.OldValue) ?? note, personId, now, SourceJournal.Undo);
                    break;
                case SourceJournal.HarvestField:
                    SetRunning(source, note, personId, now, SourceJournal.Undo);
                    break;
                case SourceJournal.RecipesField:
                    {
                        var report = target == SourceJournal.Withheld
                            ? await WithholdRecipesAsync(source.Id, personId, SourceJournal.Detail(entry.OldValue) ?? note, apply: true)
                            : await ReleaseRecipesAsync(source.Id, personId, apply: true);
                        if (!report.Applied) return SourceActionResult.Refused(report.Error ?? "Nothing to undo.");
                        var last = _db.AuditLogEntries.Local.LastOrDefault(e => e.EntityId == source.Id && e.PropertyName == SourceJournal.RecipesField);
                        if (last != null)
                        {
                            last.ChangeType = SourceJournal.Undo;
                            last.NewValue = SourceJournal.Value(SourceJournal.State(last.NewValue), $"{SourceJournal.Detail(last.NewValue)}; {note}");
                        }

                        break;
                    }

                case SourceJournal.LicenceField:
                    {
                        var detail = SourceJournal.Detail(entry.OldValue);
                        var facts = target == SourceJournal.None && detail == null ? new LicenceFacts(null, null, null) : Read<LicenceFacts>(detail);
                        if (facts == null) return SourceActionResult.Refused("This entry does not hold the old licence facts whole, so it cannot be undone.");
                        var old = LicenceValue(source);
                        source.LicenceKind = target == SourceJournal.None ? null : target;
                        source.LicenceStatement = facts.Statement;
                        source.LicenceFoundAt = facts.FoundAt;
                        source.LicenceEnteredByPersonId = facts.EnteredByPersonId;
                        source.LastModifiedDate = now;
                        source.LastModifiedByPersonId = personId;
                        _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Undo, SourceJournal.LicenceField, old, LicenceValue(source), now, personId));
                        break;
                    }

                case SourceJournal.StatusField:
                    {
                        var old = source.Status.ToString();
                        source.Status = target == SourceJournal.None ? ScrapingSourceStatusEnum.Pending : Enum.Parse<ScrapingSourceStatusEnum>(target);
                        source.ReviewedByPersonId = personId;
                        source.ReviewedDate = now;
                        if (source.Status != ScrapingSourceStatusEnum.Approved)
                        {
                            source.ProbationUntilUtc = null;
                        }

                        var domain = SourceAdmissionPolicy.NormalizeDomain(source.Domain);
                        var candidate = domain == null ? null : await _db.SourceCandidates.FirstOrDefaultAsync(c => c.Domain == domain);
                        if (candidate != null && candidate.State == SourceCandidateEntity.Approved && source.Status != ScrapingSourceStatusEnum.Approved)
                        {
                            candidate.State = SourceCandidateEntity.New;
                        }

                        _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Undo, SourceJournal.StatusField,
                            SourceJournal.Value(old), SourceJournal.Value(source.Status.ToString(), note), now, personId));
                        break;
                    }
            }

            await _db.SaveChangesAsync();
            return new SourceActionResult { Ok = true, Source = await SourceModelAsync(source) };
        }

        /// <summary>The newest journal row of each field of the given sources: only it can be undone.</summary>
        private async Task<HashSet<long>> LatestEntriesAsync(IEnumerable<long> sourceIds, CancellationToken cancellationToken)
        {
            var ids = sourceIds.ToList();
            var rows = await _db.AuditLogEntries.AsNoTracking()
                .Where(e => e.EntityType == SourceJournal.EntityType && ids.Contains(e.EntityId))
                .Select(e => new { e.Id, e.EntityId, e.PropertyName })
                .ToListAsync(cancellationToken);
            return rows.GroupBy(r => (r.EntityId, r.PropertyName)).Select(g => g.Max(r => r.Id)).ToHashSet();
        }

        private static string CurrentState(ScrapingSourceEntity source, string field) => field switch
        {
            SourceJournal.HarvestField => source.HarvestPausedAt == null ? SourceJournal.Running : SourceJournal.Paused,
            SourceJournal.RecipesField => source.RecipesWithheldAt == null ? SourceJournal.Shown : SourceJournal.Withheld,
            SourceJournal.StatusField => source.Status.ToString(),
            SourceJournal.LicenceField => source.LicenceKind ?? SourceJournal.None,
            _ => SourceJournal.None,
        };

        public async Task<SourceReviewModel> ReviewAsync(CancellationToken cancellationToken = default)
        {
            var candidates = await _db.SourceCandidates.AsNoTracking()
                .Where(c => c.State != SourceCandidateEntity.Lead && c.State != SourceCandidateEntity.Approved)
                .OrderByDescending(c => c.TimesSeen).ThenBy(c => c.Domain)
                .ToListAsync(cancellationToken);
            var sources = await _db.ScrapingSources.AsNoTracking()
                .Include(s => s.RequestedByPerson).Include(s => s.ReviewedByPerson)
                .OrderBy(s => s.Domain)
                .ToListAsync(cancellationToken);
            var checkIds = candidates.Where(c => c.LastCheckId != null).Select(c => c.LastCheckId!.Value)
                .Concat(sources.Where(s => s.LastCheckId != null).Select(s => s.LastCheckId!.Value)).Distinct().ToList();
            var checks = await _db.SourceChecks.AsNoTracking().Where(c => checkIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);

            SourceCandidateModel CandidateModel(SourceCandidateEntity c)
            {
                var check = c.LastCheckId is { } id && checks.TryGetValue(id, out var found) ? found : null;
                var blocked = ApproveBlocked(c, check);
                return new SourceCandidateModel
                {
                    Id = c.Id,
                    Domain = c.Domain,
                    EvidenceUrl = c.EvidenceUrl,
                    FoundVia = c.FoundVia,
                    Signal = c.Signal,
                    FirstSeenUtc = c.FirstSeenUtc,
                    LastSeenUtc = c.LastSeenUtc,
                    TimesSeen = c.TimesSeen,
                    State = c.State,
                    Check = check == null ? null : MapCheck(check),
                    CanApprove = blocked == null,
                    ApproveBlockedReason = blocked,
                };
            }

            ScrapingSourceModel SourceModel(ScrapingSourceEntity s)
            {
                var model = ScrapingSourceService.Map(s);
                model.Check = s.LastCheckId is { } id && checks.TryGetValue(id, out var check) ? MapCheck(check) : null;
                return model;
            }

            var runs = await _db.SourceDiscoveryRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).ThenByDescending(r => r.Id).Take(10).ToListAsync(cancellationToken);
            var failed = await _db.SourceChecks.AsNoTracking()
                .Where(c => c.Verdict == null && c.Status != SourceCheckEntity.Running)
                .OrderByDescending(c => c.StartedUtc).Take(10).ToListAsync(cancellationToken);

            return new SourceReviewModel
            {
                Enabled = _settings.Enabled,
                Queue = candidates.Where(c => c.State == SourceCandidateEntity.Clear).Select(CandidateModel).Where(c => c.CanApprove).ToList(),
                NotClear = candidates.Where(c => c.State == SourceCandidateEntity.NotClear || (c.State == SourceCandidateEntity.Clear && ApproveBlocked(c, c.LastCheckId is { } id && checks.TryGetValue(id, out var k) ? k : null) != null))
                    .Select(CandidateModel).ToList(),
                Waiting = candidates.Where(c => c.State == SourceCandidateEntity.New).Select(CandidateModel).ToList(),
                Leads = await _db.SourceCandidates.CountAsync(c => c.State == SourceCandidateEntity.Lead, cancellationToken),
                Approved = sources.Where(s => s.Status == ScrapingSourceStatusEnum.Approved && s.Kind == ScrapingSourceEntity.WebKind).Select(SourceModel).ToList(),
                Hand = sources.Where(s => s.Kind == ScrapingSourceEntity.HandKind).Select(SourceModel).ToList(),
                Rejected = sources.Where(s => s.Status == ScrapingSourceStatusEnum.Rejected && s.Kind == ScrapingSourceEntity.WebKind).Select(SourceModel).ToList(),
                Runs = runs.Select(Map).ToList(),
                FailedChecks = failed.Select(MapCheck).ToList(),
                Journal = await JournalAsync(50, cancellationToken),
            };
        }

        public async Task<SourceAdmissionStatusModel> StatusAsync(CancellationToken cancellationToken = default)
        {
            var now = Now();
            var runs = await _db.SourceDiscoveryRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).ThenByDescending(r => r.Id).Take(20).ToListAsync(cancellationToken);
            var candidates = await _db.SourceCandidates.AsNoTracking().Select(c => c.State).ToListAsync(cancellationToken);
            var sources = await _db.ScrapingSources.AsNoTracking()
                .Select(s => new { s.Status, s.Kind, s.CheckVerdict, s.HarvestPausedAt, s.RecipesWithheldAt, s.ProbationUntilUtc })
                .ToListAsync(cancellationToken);
            var web = sources.Where(s => s.Status == ScrapingSourceStatusEnum.Approved && s.Kind == ScrapingSourceEntity.WebKind).ToList();
            var review = await ReviewAsync(cancellationToken);
            return new SourceAdmissionStatusModel
            {
                Enabled = _settings.Enabled,
                Runs = runs.Take(5).Select(Map).ToList(),
                RunsFailedInRow = runs.Where(r => r.Status != SourceDiscoveryRunEntity.Skipped && r.Status != SourceDiscoveryRunEntity.Running)
                    .TakeWhile(r => r.Status == SourceDiscoveryRunEntity.Failed).Count(),
                LastGoodRunUtc = runs.FirstOrDefault(r => r.Status is SourceDiscoveryRunEntity.Completed or SourceDiscoveryRunEntity.Partial)?.FinishedUtc,
                ChecksLast24h = await _db.SourceChecks.CountAsync(c => c.StartedUtc > now.AddHours(-24), cancellationToken),
                MaxChecksPerDay = _settings.MaxChecksPerDay,
                ChecksFailedLast7d = await _db.SourceChecks.CountAsync(c => c.StartedUtc > now.AddDays(-7) && c.Verdict == null && c.Status != SourceCheckEntity.Running, cancellationToken),
                Queue = review.Queue.Count,
                NotClear = review.NotClear.Count,
                Waiting = candidates.Count(s => s == SourceCandidateEntity.New),
                Leads = candidates.Count(s => s == SourceCandidateEntity.Lead),
                Approved = web.Count,
                ApprovedClear = web.Count(s => s.CheckVerdict == SourceAdmissionPolicy.Clear),
                ApprovedNotClear = web.Count(s => s.CheckVerdict == SourceAdmissionPolicy.NotClear),
                ApprovedUnjudged = web.Count(s => s.CheckVerdict == null),
                HarvestPaused = web.Count(s => s.HarvestPausedAt != null),
                Withheld = sources.Count(s => s.RecipesWithheldAt != null),
                InProbation = web.Count(s => s.ProbationUntilUtc != null),
                Harvestable = web.Count(s => s.HarvestPausedAt == null && s.RecipesWithheldAt == null),
                Hand = sources.Count(s => s.Kind == ScrapingSourceEntity.HandKind && s.Status == ScrapingSourceStatusEnum.Approved),
            };
        }

        /// <summary>Recipes imported from the source's domain, with or without "www.".</summary>
        internal IQueryable<RecipeEntity> RecipesOf(ScrapingSourceEntity source) => RecipesFrom(_db.Recipes, source.Domain);

        /// <summary>The recipes of the query imported from the domain, with or without "www.".</summary>
        internal static IQueryable<RecipeEntity> RecipesFrom(IQueryable<RecipeEntity> recipes, string domain)
        {
            var bare = SourceAdmissionPolicy.NormalizeDomain(domain) ?? domain;
            var www = "www." + bare;
            string a = $"https://{bare}/", b = $"http://{bare}/", c = $"https://{www}/", d = $"http://{www}/";
            return recipes.Where(r => r.SourceUrl != null
                && (r.SourceUrl.StartsWith(a) || r.SourceUrl.StartsWith(b) || r.SourceUrl.StartsWith(c) || r.SourceUrl.StartsWith(d)));
        }

        private async Task<Dictionary<string, ScrapingSourceStatusEnum>> KnownSourcesAsync(CancellationToken cancellationToken)
        {
            var rows = await _db.ScrapingSources.AsNoTracking().Select(s => new { s.Domain, s.Status }).ToListAsync(cancellationToken);
            var known = new Dictionary<string, ScrapingSourceStatusEnum>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var key = SourceAdmissionPolicy.NormalizeDomain(row.Domain);
                if (key == null) continue;
                if (!known.TryGetValue(key, out var have) || row.Status == ScrapingSourceStatusEnum.Rejected || (row.Status == ScrapingSourceStatusEnum.Approved && have != ScrapingSourceStatusEnum.Rejected))
                {
                    known[key] = row.Status;
                }
            }

            return known;
        }

        private async Task<ScrapingSourceEntity?> FindSourceAsync(string normalizedDomain)
        {
            var www = "www." + normalizedDomain;
            return await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Domain == normalizedDomain)
                ?? await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Domain == www);
        }

        private async Task<ScrapingSourceModel> SourceModelAsync(ScrapingSourceEntity source)
        {
            var model = ScrapingSourceService.Map(source);
            if (source.LastCheckId is { } id && await _db.SourceChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id) is { } check)
            {
                model.Check = MapCheck(check);
            }

            return model;
        }

        internal static SourceCheckModel MapCheck(SourceCheckEntity check)
        {
            var report = Read<ScraperSourceCheckReport>(check.ReportJson);
            return new SourceCheckModel
            {
                Id = check.Id,
                Domain = check.Domain,
                Purpose = check.Purpose,
                Status = check.Status,
                Error = check.Error,
                StartedUtc = check.StartedUtc,
                FinishedUtc = check.FinishedUtc,
                Verdict = check.Verdict,
                Conclusive = check.Conclusive,
                RuleVersion = check.RuleVersion,
                PolicyVersion = check.PolicyVersion,
                Reasons = Read<List<SourceReason>>(check.ReasonsJson) ?? new List<SourceReason>(),
                Facts = report == null ? null : Facts(report),
            };
        }

        internal static SourceFactsModel Facts(ScraperSourceCheckReport report) => new()
        {
            LicenceKind = report.Licence?.Kind,
            LicenceBasis = report.Licence?.Basis,
            LicenceStatements = report.Licence?.Statements.Select(s => new SourceStatementModel
            {
                Kind = s.Kind, Text = s.Text, FoundAt = s.FoundAt, Where = s.Where, LicenceUrl = s.LicenceUrl,
            }).ToList() ?? new List<SourceStatementModel>(),
            SampleLicences = report.Licence?.SampleLicences ?? new Dictionary<string, int>(),
            TermsPages = report.Terms?.Pages.Select(p => new SourceTermsPageModel { Url = p.Url, Outcome = p.Outcome }).ToList() ?? new List<SourceTermsPageModel>(),
            Prohibitions = report.Terms?.Prohibitions.Select(p => new SourceProhibitionModel { Category = p.Category, Text = p.Text, FoundAt = p.FoundAt }).ToList() ?? new List<SourceProhibitionModel>(),
            RobotsUrl = report.Robots?.Url,
            RobotsStatus = report.Robots?.Status,
            GeneralAllowsHomepage = report.Robots?.General?.AllowsHomepage,
            GeneralSamplePathsDisallowed = report.Robots?.General?.SamplePathsDisallowed.Count ?? 0,
            NomBotAllowsHomepage = report.Robots?.NomBot?.AllowsHomepage,
            NomBotSamplePathsDisallowed = report.Robots?.NomBot?.SamplePathsDisallowed.Count ?? 0,
            AiCrawlersBlocked = report.Robots?.AiCrawlers.Where(a => a.Rule is "blocked" or "partly-blocked").Select(a => $"{a.Agent} ({a.Rule})").ToList() ?? new List<string>(),
            AiCrawlersAllowed = report.Robots?.AiCrawlers.Where(a => a.Rule is "allowed" or "not-named").Select(a => a.Agent).ToList() ?? new List<string>(),
            CrawlDelaySeconds = report.Robots?.NomBot?.CrawlDelaySeconds,
            RecipeUrlCount = report.Enumeration?.RecipeUrlCount ?? 0,
            SampleRequested = report.Samples?.Requested ?? 0,
            SampleFetched = report.Samples?.Fetched ?? 0,
            SampleWithRecipe = report.Samples?.WithRecipeJsonLd ?? 0,
            SampleComplete = report.Samples?.Complete ?? 0,
            SampleEnglish = report.Samples?.English ?? 0,
            IngredientLines = report.Samples?.IngredientLines ?? 0,
            LinesWithQuantity = report.Samples?.LinesWithQuantity ?? 0,
        };

        private static SourceDiscoveryRunModel Map(SourceDiscoveryRunEntity run) => new()
        {
            Id = run.Id,
            StartedUtc = run.StartedUtc,
            FinishedUtc = run.FinishedUtc,
            Status = run.Status,
            StopReason = run.StopReason,
            Error = run.Error,
            Seeds = run.Seeds,
            Candidates = run.Candidates,
            Leads = run.Leads,
            SeedsVisited = run.SeedsVisited,
            SeedsNotVisited = run.SeedsNotVisited,
            SeedsRefused = run.SeedsRefused,
            PagesFetched = run.PagesFetched,
        };

        private static T? Read<T>(JsonElement? element) where T : class
        {
            if (element is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            try
            {
                return value.Deserialize<T>(JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static T? Read<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Clip(string? value, int length) =>
            value == null || value.Length <= length ? value : value[..length];
    }
}
