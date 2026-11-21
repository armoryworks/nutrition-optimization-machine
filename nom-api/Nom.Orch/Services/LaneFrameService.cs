using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <inheritdoc cref="ILaneFrameService"/>
    public class LaneFrameService : ILaneFrameService
    {
        private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string[] Audited = { LaneAuditOutcome.Pass, LaneAuditOutcome.Flagged, LaneAuditOutcome.Error };

        private readonly ApplicationDbContext _db;
        private readonly IEnumerable<ILaneUndo> _undos;
        private readonly ILogger<LaneFrameService> _logger;

        public LaneFrameService(ApplicationDbContext db, IEnumerable<ILaneUndo> undos, ILogger<LaneFrameService> logger)
        {
            _db = db;
            _undos = undos;
            _logger = logger;
        }

        public static LaneFrameService Create(IServiceProvider services) =>
            new(services.GetRequiredService<ApplicationDbContext>(), Deferred(services), services.GetRequiredService<ILogger<LaneFrameService>>());

        private static IEnumerable<ILaneUndo> Deferred(IServiceProvider services)
        {
            foreach (var undo in services.GetServices<ILaneUndo>()) yield return undo;
        }

        public async Task<string> ModeAsync(string lane, string tier = LaneTiers.Whole, CancellationToken cancellationToken = default)
        {
            var rows = await _db.LaneStates.AsNoTracking()
                .Where(s => s.Lane == lane && (s.Tier == tier || s.Tier == LaneTiers.Whole))
                .Select(s => new { s.Tier, s.Mode })
                .ToListAsync(cancellationToken);
            if (rows.Any(r => r.Tier == LaneTiers.Whole && r.Mode == LaneMode.Paused)) return LaneMode.Paused;
            return rows.FirstOrDefault(r => r.Tier == tier)?.Mode ?? LaneMode.Shadow;
        }

        public async Task<long> RecordAsync(LaneDecisionInput decision, CancellationToken cancellationToken = default)
        {
            var row = new LaneDecisionEntity
            {
                Lane = decision.Lane,
                Tier = decision.Tier,
                Subject = Clip(decision.Subject, 255)!,
                Decision = Clip(decision.Decision, 255),
                Mode = decision.Mode,
                Writes = decision.Writes,
                Written = decision.Writes && decision.Mode == LaneMode.Live,
                SignatureHash = decision.Signature,
                Stratum = Clip(decision.Stratum, 128),
                SignalsJson = decision.Signals == null ? null : LaneJournal.Write(decision.Signals),
                VerdictsJson = decision.Verdicts == null ? null : LaneJournal.Write(decision.Verdicts),
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = SystemConstants.SystemPersonId,
            };
            _db.LaneDecisions.Add(row);
            await _db.SaveChangesAsync(cancellationToken);
            return row.Id;
        }

        public async Task<LaneAuditPassResult> AuditAsync(string lane, string tier, DateTime sinceUtc, LaneSamplePlan plan, ILaneAuditor auditor, ILaneAuditCheck check, Random? random = null, CancellationToken cancellationToken = default)
        {
            var pool = await AuditPoolQuery(_db, lane, tier, sinceUtc).ToListAsync(cancellationToken);
            var sample = LaneAuditSampler.Pick(pool, d => d.Stratum, plan, random ?? Random.Shared);

            int passed = 0, flagged = 0, errors = 0, unreadable = 0;
            foreach (var decision in sample)
            {
                var answer = await auditor.AuditAsync(decision, cancellationToken);
                bool? confirmed = null;
                string outcome;
                if (answer.Wrong == null) outcome = LaneAuditOutcome.Unreadable;
                else if (answer.Wrong == false) outcome = LaneAuditOutcome.Pass;
                else
                {
                    confirmed = await check.ConfirmsWrongAsync(decision, cancellationToken);
                    outcome = confirmed switch
                    {
                        true => LaneAuditOutcome.Error,
                        false => LaneAuditOutcome.Flagged,
                        null => LaneAuditOutcome.Unreadable,
                    };
                }

                switch (outcome)
                {
                    case LaneAuditOutcome.Pass: passed++; break;
                    case LaneAuditOutcome.Flagged: flagged++; break;
                    case LaneAuditOutcome.Error: errors++; break;
                    default: unreadable++; break;
                }

                decision.AuditOutcome = outcome;
                decision.AuditedAt = DateTime.UtcNow;
                decision.LastModifiedDate = decision.AuditedAt;
                decision.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                decision.AuditJson = LaneJournal.Write(new
                {
                    auditor = auditor.Model,
                    wrong = answer.Wrong,
                    note = answer.Note,
                    check = answer.Wrong == true ? check.Name : null,
                    confirmed,
                });
            }
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Lane {Lane}/{Tier}: audited {Sampled} of {Available} decisions with {Auditor}: {Passed} right, {Flagged} flags not confirmed by {Check}, {Errors} confirmed errors, {Unreadable} unreadable",
                lane, tier, sample.Count, pool.Count, auditor.Model, passed, flagged, check.Name, errors, unreadable);
            return new LaneAuditPassResult(pool.Count, sample.Count, passed, flagged, errors, unreadable);
        }

        public async Task<LaneQualifyVerdict> EvaluateAsync(LaneEvaluateRequest request, CancellationToken cancellationToken = default)
        {
            var problems = request.Policy.Problems();
            if (problems.Count > 0)
                throw new ArgumentException($"Lane {request.Lane}/{request.Tier}: the policy is refused: {string.Join("; ", problems)}.", nameof(request));

            var now = DateTime.UtcNow;
            var state = await _db.LaneStates.FirstOrDefaultAsync(s => s.Lane == request.Lane && s.Tier == request.Tier, cancellationToken);
            if (state == null)
            {
                state = new LaneStateEntity
                {
                    Lane = request.Lane,
                    Tier = request.Tier,
                    Mode = LaneMode.Shadow,
                    Since = now,
                    WindowStart = Epoch,
                    CreatedDate = now,
                    CreatedByPersonId = SystemConstants.SystemPersonId,
                };
                _db.LaneStates.Add(state);
            }

            var previousSignature = state.SignatureHash;
            var signatureChanged = previousSignature != null && previousSignature != request.Signature;
            if (signatureChanged) state.WindowStart = state.EvaluatedAt ?? state.Since;

            var qualify = await CountAsync(request, request.Signature, state.WindowStart, null, cancellationToken);
            var auditorScore = request.Auditor;
            var window = auditorScore?.Problem == null && auditorScore != null
                ? LaneQualifier.PauseWindow(request.Policy, auditorScore)
                : request.Policy.PauseWindow;
            var windowSignature = signatureChanged ? previousSignature! : request.Signature;
            var recent = state.Mode == LaneMode.Live
                ? await CountAsync(request, windowSignature, state.Since, window, cancellationToken)
                : LaneCounts.None;

            var verdict = LaneQualifier.Decide(new LaneQualifyInput(
                state.Mode, request.Signature, signatureChanged, state.PausedSignatureHash, request.Policy, request.Auditor,
                request.GoldenProblem, request.PauseReasons ?? Array.Empty<string>(), qualify, recent), now);

            var old = new LaneJournal.ModeValue(state.Mode, state.Reason);
            state.SignatureHash = request.Signature;
            state.FiguresJson = LaneJournal.Write(verdict.Figures);
            state.EvaluatedAt = now;
            state.Reason = Clip(verdict.Reason, 1023);
            state.LastModifiedDate = now;
            state.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            if (verdict.Clean && recent.NewestAudited is { } clean) state.LastCleanAt = clean;

            var undoSince = state.LastCleanAt ?? state.Since;
            if (verdict.Journal != null)
            {
                state.Mode = verdict.Mode;
                state.Since = now;
                if (verdict.Mode == LaneMode.Live) state.LastCleanAt = now;
                if (verdict.Mode == LaneMode.Paused)
                {
                    state.PausedSignatureHash = request.PauseReasons is { Count: > 0 } ? request.Signature : windowSignature;
                    state.WindowStart = now;
                }
            }
            await _db.SaveChangesAsync(cancellationToken);

            if (verdict.Journal != null)
            {
                _db.AuditLogEntries.Add(LaneJournal.Entry(state.Id, state.Lane, state.Tier, verdict.Journal,
                    LaneJournal.Write(old), LaneJournal.Write(new LaneJournal.ModeValue(verdict.Mode, verdict.Reason, verdict.Figures)),
                    now, SystemConstants.SystemPersonId));
                await _db.SaveChangesAsync(cancellationToken);
                if (verdict.Journal == LaneJournal.Pause)
                    _logger.LogWarning("Lane {Lane}/{Tier}: {Reason}", state.Lane, state.Tier, verdict.Reason);
                else
                    _logger.LogInformation("Lane {Lane}/{Tier}: {Reason}", state.Lane, state.Tier, verdict.Reason);
            }

            if (verdict.Undo) await SelfUndoAsync(state, undoSince, cancellationToken);
            return verdict;
        }

        public async Task<IReadOnlyList<LaneFrameStatusModel>> StatusAsync(CancellationToken cancellationToken = default)
        {
            var states = await _db.LaneStates.AsNoTracking().OrderBy(s => s.Lane).ThenBy(s => s.Tier).ToListAsync(cancellationToken);
            var since = DateTime.UtcNow.AddHours(-24);
            var counts = await RecentCountsQuery(_db, since).ToListAsync(cancellationToken);
            var undoLanes = _undos.Select(u => u.Lane).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return states.Select(s =>
            {
                var count = counts.FirstOrDefault(c => c.Lane == s.Lane && c.Tier == s.Tier);
                return new LaneFrameStatusModel
                {
                    Lane = s.Lane,
                    Tier = s.Tier,
                    Mode = s.Mode,
                    Reason = s.Reason,
                    Since = s.Since,
                    SignatureHash = s.SignatureHash,
                    EvaluatedAt = s.EvaluatedAt,
                    LastCleanAt = s.LastCleanAt,
                    Figures = LaneJournal.Read<LaneFigures>(s.FiguresJson),
                    DecisionsLast24h = count?.Decisions ?? 0,
                    WrittenLast24h = count?.Written ?? 0,
                    CanUndo = undoLanes.Contains(s.Lane),
                };
            }).ToList();
        }

        public async Task<LaneUndoReport?> UndoWindowAsync(string lane, string tier, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            var state = await _db.LaneStates.AsNoTracking().FirstOrDefaultAsync(s => s.Lane == lane && s.Tier == tier, cancellationToken);
            var undo = Undo(lane);
            if (state == null || undo == null) return null;

            var since = state.LastCleanAt ?? state.Since;
            var result = await undo.UndoSinceAsync(tier, since, apply, cancellationToken);
            if (apply) await JournalUndoAsync(state, true, since, result, personId, cancellationToken);
            return new LaneUndoReport(lane, tier, since, apply, result);
        }

        private async Task SelfUndoAsync(LaneStateEntity state, DateTime since, CancellationToken cancellationToken)
        {
            var undo = Undo(state.Lane);
            if (undo == null)
            {
                _logger.LogWarning("Lane {Lane}/{Tier} paused with no undo registered; its writes since {Since:u} stay", state.Lane, state.Tier, since);
                await JournalUndoAsync(state, false, since, new LaneUndoResult(0, 0, 0, "no undo registered for the lane"), SystemConstants.SystemPersonId, cancellationToken);
                return;
            }

            try
            {
                var dry = await undo.UndoSinceAsync(state.Tier, since, false, cancellationToken);
                _logger.LogWarning("Lane {Lane}/{Tier} paused; dry run of the undo since {Since:u}: {Undone} to undo, {Skipped} skipped, {Remaining} not reached",
                    state.Lane, state.Tier, since, dry.Undone, dry.Skipped, dry.Remaining);
                await JournalUndoAsync(state, false, since, dry, SystemConstants.SystemPersonId, cancellationToken);

                var applied = await undo.UndoSinceAsync(state.Tier, since, true, cancellationToken);
                _logger.LogWarning("Lane {Lane}/{Tier} paused; undid its writes since {Since:u}: {Undone} undone, {Skipped} skipped, {Remaining} not reached",
                    state.Lane, state.Tier, since, applied.Undone, applied.Skipped, applied.Remaining);
                await JournalUndoAsync(state, true, since, applied, SystemConstants.SystemPersonId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Lane {Lane}/{Tier} paused; its undo since {Since:u} failed and the lane stays paused", state.Lane, state.Tier, since);
                await JournalUndoAsync(state, false, since, new LaneUndoResult(0, 0, 0, "undo failed: " + ex.GetType().Name + ": " + ex.Message), SystemConstants.SystemPersonId, cancellationToken);
            }
        }

        private async Task JournalUndoAsync(LaneStateEntity state, bool applied, DateTime since, LaneUndoResult result, long personId, CancellationToken cancellationToken)
        {
            _db.AuditLogEntries.Add(LaneJournal.Entry(state.Id, state.Lane, state.Tier, LaneJournal.Undo, null,
                LaneJournal.Write(new LaneJournal.UndoValue(applied, since, result.Undone, result.Skipped, result.Remaining, result.Note)),
                DateTime.UtcNow, personId));
            await _db.SaveChangesAsync(cancellationToken);
        }

        private ILaneUndo? Undo(string lane) => _undos.FirstOrDefault(u => string.Equals(u.Lane, lane, StringComparison.OrdinalIgnoreCase));

        private async Task<LaneCounts> CountAsync(LaneEvaluateRequest request, string signature, DateTime since, int? lastAudited, CancellationToken cancellationToken)
        {
            if (lastAudited is { } take)
            {
                var window = await WindowQuery(_db, request.Lane, request.Tier, signature, since, take).ToListAsync(cancellationToken);
                return new LaneCounts(window.Count, window.Count, window.Count,
                    window.Count(w => w.AuditOutcome == LaneAuditOutcome.Error),
                    window.Count(w => w.AuditOutcome == LaneAuditOutcome.Flagged),
                    window.Count == 0 ? null : window.Max(w => w.CreatedDate));
            }

            var totals = await TotalsQuery(_db, request.Lane, request.Tier, signature, since).FirstOrDefaultAsync(cancellationToken);
            return totals ?? LaneCounts.None;
        }

        internal sealed record WindowRow(string? AuditOutcome, DateTime CreatedDate);

        internal sealed record RecentCount(string Lane, string Tier, int Decisions, int Written);

        internal static IQueryable<LaneDecisionEntity> DecisionsQuery(ApplicationDbContext db, string lane, string tier, string signature, DateTime since) =>
            db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == lane && d.Tier == tier && d.SignatureHash == signature && d.CreatedDate >= since);

        internal static IQueryable<LaneCounts> TotalsQuery(ApplicationDbContext db, string lane, string tier, string signature, DateTime since) =>
            DecisionsQuery(db, lane, tier, signature, since)
                .GroupBy(d => d.Lane)
                .Select(g => new LaneCounts(
                    g.Count(),
                    g.Count(d => d.Written),
                    g.Count(d => d.Writes && d.AuditOutcome != null && Audited.Contains(d.AuditOutcome)),
                    g.Count(d => d.Writes && d.AuditOutcome == LaneAuditOutcome.Error),
                    g.Count(d => d.Writes && d.AuditOutcome == LaneAuditOutcome.Flagged),
                    null));

        internal static IQueryable<WindowRow> WindowQuery(ApplicationDbContext db, string lane, string tier, string signature, DateTime since, int take) =>
            DecisionsQuery(db, lane, tier, signature, since)
                .Where(d => d.Written && d.AuditOutcome != null && Audited.Contains(d.AuditOutcome))
                .OrderByDescending(d => d.CreatedDate).ThenByDescending(d => d.Id)
                .Take(take)
                .Select(d => new WindowRow(d.AuditOutcome, d.CreatedDate));

        internal static IQueryable<LaneDecisionEntity> AuditPoolQuery(ApplicationDbContext db, string lane, string tier, DateTime since) =>
            db.LaneDecisions
                .Where(d => d.Lane == lane && d.Tier == tier && d.Writes && d.AuditOutcome == null && d.CreatedDate >= since)
                .OrderBy(d => d.Id);

        internal static IQueryable<RecentCount> RecentCountsQuery(ApplicationDbContext db, DateTime since) =>
            db.LaneDecisions.AsNoTracking()
                .Where(d => d.CreatedDate >= since)
                .GroupBy(d => new { d.Lane, d.Tier })
                .Select(g => new RecentCount(g.Key.Lane, g.Key.Tier, g.Count(), g.Count(d => d.Written)));

        private static string? Clip(string? value, int length) => value == null || value.Length <= length ? value : value[..length];
    }
}
