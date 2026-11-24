using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrition;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <inheritdoc cref="ILinkRecheckService"/>
    public class LinkRecheckService : ILinkRecheckService
    {
        public const string Lane = AiLanes.LinkRecheck;
        public const string EntityType = "LinkRecheck";
        public const string RunChangeType = "LinkRecheckRun";
        public const string UndoChangeType = "LinkRecheckUndo";
        public const string WithdrawChangeType = "LinkRecheckWithdraw";
        public const string AliasChangeType = "LinkRecheckAlias";
        public const string FoodChangeType = "LinkRecheckFood";
        public const string LineChangeType = "LinkRecheckLine";
        public const string WithdrawRun = "withdraw";
        public const string NamesRun = "names";
        public const string CreatedProperty = "Created";
        public const string AliasProperty = "Alias";

        /// <summary>The context of a trusted alias the re-check gives a wording on the food it moved the wording's lines to.</summary>
        public const string MovedAliasPrefix = IngredientAliasTrust.DeterministicPrefix + "recheck2:";

        private const string LinkAiPrefix = "link:ai:";

        /// <summary>The outcome a sample shows for a refutation of a class that is not proven: kept, and handed to the model audit as a candidate.</summary>
        public const string CandidateOutcome = "candidate";
        private const int ExampleLines = 3;
        private const int LineChunk = 500;
        private const int WriteChunk = 200;
        private static readonly TimeSpan DecidedFor = TimeSpan.FromDays(30);
        private static readonly ConcurrentDictionary<string, byte> RunningStamps = new(StringComparer.Ordinal);

        /// <summary>
        /// A set qualifies on a Wilson lower bound of 98 % of audited moves right (at most one wrong in
        /// 300) and pauses when a full window of 300 shows more than 3 % confirmed wrong, as the
        /// reprocess rebuild tier does (observed-error rule of LQ-4).
        /// </summary>
        public static readonly LanePolicy Policy = new(QualifyBar: 0.98, PauseBar: 0.03, PauseWindow: 300);

        public static readonly LaneSamplePlan Sample = new(Minimum: 30, Rate: 0.1);

        /// <summary>The share of a live batch's moves whose line was no longer where the batch found it that pauses the set.</summary>
        public const double SkippedShareToPause = 0.05;
        public const int MinimumLinesForShare = 50;

        public const string AuditorProblem =
            "no link auditor and confirming reader are set, so the link-audit record cannot be checked; a set stays in shadow and its sets are applied by an operator, one dry run at a time";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly ILinkAutoApplyService _gate;
        private readonly ILineRelinkService _relink;
        private readonly ILaneFrameService _frame;
        private readonly ILogger<LinkRecheckService> _logger;
        private readonly Func<WordingTierAuditRecord.Record?> _auditRecord;

        public LinkRecheckService(ApplicationDbContext db, ICurrentUserService currentUser, ILinkAutoApplyService gate, ILineRelinkService relink,
            ILaneFrameService frame, ILogger<LinkRecheckService> logger, Func<WordingTierAuditRecord.Record?>? auditRecord = null)
        {
            _auditRecord = auditRecord ?? WordingTierAuditRecord.Embedded;
            _db = db;
            _currentUser = currentUser;
            _gate = gate;
            _relink = relink;
            _frame = frame;
            _logger = logger;
        }

        private sealed record RLine(long RecipeId, long FoodId, int SortOrder, string Raw);

        private sealed class Group
        {
            public required long FoodId { get; init; }
            public required string Wording { get; init; }
            public string? Sense { get; init; }
            public bool Drained { get; init; }
            public List<RLine> Lines { get; } = new();
            public HashSet<long> Proposals { get; } = new();
            public HashSet<string> Aliases { get; } = new(StringComparer.Ordinal);
            public long? WordingRowId { get; set; }
            public bool KeepWithoutLines { get; set; }

            public string Subject =>
                string.Create(CultureInfo.InvariantCulture, $"{FoodId}:{Wording}{(Sense == null ? string.Empty : ":" + Sense)}{(Drained ? ":drained" : string.Empty)}");
        }

        private sealed record Decided(Group Group, GateFood Food, RecheckVerdict Verdict, string? Skip, bool Qualified = false);

        /// <summary>The signals a re-check decision keeps on the lane frame, which its auditor reads.</summary>
        public sealed record LinkRecheckSignals(
            string Set, long FoodId, string FoodName, string FoodText, string Wording, string? Sense, int Lines, IReadOnlyList<string> Examples,
            IReadOnlyList<string> Holds, string Outcome, string? Reason, string? Tier, long? TargetId, string? TargetName, string? TargetText, DateTime? Stamp, string? Class = null);

        public static DateTime NewStamp()
        {
            var now = DateTime.UtcNow;
            return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        }

        private static string StampText(DateTime stamp) => stamp.ToString("O", CultureInfo.InvariantCulture);

        /// <summary>Marks a run's stamp as going in this process until disposed, so the status tells a run still going from one cut off (whose process threw or ended before its finish).</summary>
        private static RunningMark MarkRunning(DateTime stamp)
        {
            var text = StampText(stamp);
            RunningStamps[text] = 0;
            return new RunningMark(text);
        }

        private sealed class RunningMark(string stamp) : IDisposable
        {
            public void Dispose() => RunningStamps.TryRemove(stamp, out _);
        }

        internal static readonly Expression<Func<FoodCatalogProposalEntity, bool>> WaitingModelPick = p =>
            p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Pending && p.Source.StartsWith("ai:")
            && (p.ReviewedByPersonId == null || p.ReviewedByPersonId == SystemConstants.SystemPersonId);

        public async Task<LinkRecheckWithdrawResult> WithdrawPicksAsync(bool apply = false, int sampleSize = 50, CancellationToken cancellationToken = default)
        {
            var picks = await _db.FoodCatalogProposals.AsNoTracking().Where(WaitingModelPick)
                .OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.IngredientId, p.CurrentValue, p.ProposedValue, p.Source })
                .ToListAsync(cancellationToken);
            var result = new LinkRecheckWithdrawResult { Examined = picks.Count, Applied = apply };
            foreach (var group in picks.GroupBy(p => p.Source)) result.BySource[group.Key] = group.Count();
            var step = Math.Max(1, picks.Count / Math.Max(1, Math.Clamp(sampleSize, 0, 5000)));
            result.Samples = picks.Where((_, i) => i % step == 0).Take(Math.Clamp(sampleSize, 0, 5000))
                .Select(p => new LinkRecheckPickSample { ProposalId = p.Id, IngredientId = p.IngredientId, Wording = p.CurrentValue ?? string.Empty, TargetId = p.ProposedValue, Source = p.Source })
                .ToList();
            if (!apply)
            {
                result.Withdrawn = picks.Count;
                return result;
            }

            var stamp = NewStamp();
            var personId = _currentUser.PersonIdOrSystem;
            var summary = new LinkRecheckRunSummary { Set = WithdrawRun, Stamp = stamp, Signature = LinkRecheckRules.Signature };
            using var running = MarkRunning(stamp);
            var header = await StartAsync(WithdrawRun, stamp, summary, personId, cancellationToken);
            foreach (var chunk in picks.Select(p => p.Id).Chunk(1000))
            {
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                foreach (var proposal in await _db.FoodCatalogProposals.Where(p => chunk.Contains(p.Id)).Where(WaitingModelPick).ToListAsync(cancellationToken))
                {
                    proposal.IsDeleted = true;
                    proposal.DeletedAt = stamp;
                    proposal.DeletedByPersonId = personId;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = LineRelinkService.PickWithdrawEntityType,
                        EntityId = proposal.Id,
                        ChangeType = WithdrawChangeType,
                        PropertyName = nameof(FoodCatalogProposalEntity.IsDeleted),
                        OldValue = bool.FalseString,
                        NewValue = bool.TrueString,
                        Timestamp = stamp,
                        ChangedByPersonId = personId,
                    });
                    result.Withdrawn++;
                }
                await _db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            result.Stamp = stamp;
            summary.Withdrawn = result.Withdrawn;
            await FinishAsync(header, summary, cancellationToken);
            _logger.LogInformation("Link re-check: withdrew {Count} waiting model picks under stamp {Stamp:O}", result.Withdrawn, stamp);
            return result;
        }

        public async Task<LinkRecheckWithdrawUndoResult> UndoWithdrawAsync(DateTime stamp, bool apply = false, CancellationToken cancellationToken = default)
        {
            stamp = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
            var result = new LinkRecheckWithdrawUndoResult { Stamp = stamp, Applied = apply };
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == LineRelinkService.PickWithdrawEntityType && a.ChangeType == WithdrawChangeType && a.Timestamp == stamp)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            result.Entries = entries.Count;
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(1000))
            {
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                var ids = chunk.Select(e => e.EntityId).ToList();
                var proposals = await _db.FoodCatalogProposals.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
                var holders = proposals.Values.Where(p => p.IngredientId != null).Select(p => p.IngredientId!.Value).Distinct().ToList();
                var asked = (await _db.FoodCatalogProposals.AsNoTracking()
                        .Where(p => p.IngredientId != null && holders.Contains(p.IngredientId.Value) && p.Field == FoodCatalogReviewService.FdcLinkField && !ids.Contains(p.Id)
                            && (p.LastModifiedDate ?? p.CreatedDate) > stamp)
                        .Select(p => p.IngredientId!.Value)
                        .ToListAsync(cancellationToken))
                    .ToHashSet();
                foreach (var entry in chunk)
                {
                    if (!proposals.TryGetValue(entry.EntityId, out var pick) || pick is not { IsDeleted: true, Status: FoodProposalStatus.Pending })
                    {
                        result.Skipped++;
                        continue;
                    }
                    if (pick.IngredientId is { } holder && asked.Contains(holder))
                    {
                        result.Skipped++;
                        result.SkippedAskedAgain++;
                        continue;
                    }

                    result.Restored++;
                    if (!apply) continue;
                    pick.IsDeleted = false;
                    pick.DeletedAt = null;
                    pick.DeletedByPersonId = null;
                    _db.AuditLogEntries.Remove(entry);
                }
                if (apply)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    if (tx != null) await tx.CommitAsync(cancellationToken);
                }
                _db.ChangeTracker.Clear();
            }

            if (apply && entries.Count > 0)
            {
                _db.AuditLogEntries.Add(UndoEntry(WithdrawRun, stamp, result, personId));
                await _db.SaveChangesAsync(cancellationToken);
            }
            return result;
        }

        public async Task<LinkRecheckResult?> RunAsync(string set, bool apply = false, int max = 2000, int sampleSize = 50, CancellationToken cancellationToken = default)
        {
            if (!LinkRecheckRules.AllSets.Contains(set)) return null;
            var writes = apply && set != LinkRecheckRules.Wordings;
            return await RunCoreAsync(set, writes, Math.Clamp(max, 1, 50000), Math.Clamp(sampleSize, 0, 5000), writes ? LaneMode.Live : null, lane: false, cancellationToken);
        }

        public Task<LinkRecheckResult> RunLaneBatchAsync(string set, string mode, int max, CancellationToken cancellationToken = default) =>
            RunCoreAsync(set, mode == LaneMode.Live && set != LinkRecheckRules.Wordings, Math.Max(0, max), 0, mode, lane: true, cancellationToken);

        private async Task<LinkRecheckResult> RunCoreAsync(string set, bool apply, int max, int sampleSize, string? mode, bool lane, CancellationToken cancellationToken)
        {
            var signature = LinkRecheckRules.SetSignature(set);
            var result = new LinkRecheckResult { Set = set, Applied = apply, Signature = signature, Lane = lane, Mode = mode };
            if (max == 0) return result;

            var catalog = await _gate.GateCatalogAsync(cancellationToken);
            var groups = await GroupsAsync(set, catalog, cancellationToken);
            var promotes = !lane || (mode != LaneMode.Paused && set != LinkRecheckRules.Wordings);
            var classModes = new Dictionary<string, string>(StringComparer.Ordinal);
            async Task<bool> QualifiedAsync(string reClass)
            {
                if (!classModes.TryGetValue(reClass, out var classMode))
                    classModes[reClass] = classMode = await _frame.ModeAsync(Lane, LinkRecheckRules.CandidateTier(reClass), cancellationToken);
                return classMode == LaneMode.Live;
            }

            if (lane)
            {
                var since = DateTime.UtcNow - DecidedFor;
                var rows = await _db.LaneDecisions.AsNoTracking()
                    .Where(d => d.Lane == Lane && d.CreatedDate >= since && (d.Tier == set || (d.Tier.StartsWith(LinkRecheckRules.CandidateTierPrefix) && d.Stratum == set)))
                    .Select(d => new { d.Subject, d.Tier, d.Mode, d.SignatureHash })
                    .ToListAsync(cancellationToken);
                var decided = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    if (row.SignatureHash != (row.Tier == set ? signature : LinkRecheckRules.TierSignature(row.Tier))) continue;
                    if (LinkRecheckRules.ClassOfTier(row.Tier) is { } reClass && row.Mode == LaneMode.Shadow && promotes && await QualifiedAsync(reClass)) continue;
                    decided.Add(row.Subject);
                }
                groups = groups.Where(g => !decided.Contains(Clip(g.Subject, 255)!)).ToList();
            }

            groups = groups.OrderByDescending(g => g.Lines.Count).ThenBy(g => g.FoodId).ThenBy(g => g.Wording, StringComparer.Ordinal).ThenBy(g => g.Sense, StringComparer.Ordinal).ToList();
            result.Remaining = Math.Max(0, groups.Count - max);
            groups = groups.Take(max).ToList();
            result.Groups = groups.Count;
            result.Lines = groups.Sum(g => g.Lines.Count);
            if (groups.Count == 0) return result;

            var foods = new Dictionary<long, GateFood>(await _gate.GateFoodsAsync(groups.Select(g => g.FoodId).ToList(), cancellationToken));
            var wordings = groups.Select(g => g.Wording).Distinct().ToList();
            var verified = new Dictionary<string, (long Id, long? Food)>(StringComparer.Ordinal);
            foreach (var chunk in wordings.Chunk(1000))
            {
                foreach (var row in await _db.IngredientWordings.AsNoTracking().Where(w => chunk.Contains(w.Wording) && w.Verdict == IngredientWordingVerdict.Food)
                    .Select(w => new { w.Id, w.Wording, w.IngredientId }).ToListAsync(cancellationToken))
                    verified.TryAdd(row.Wording, (row.Id, row.IngredientId));
            }
            var admin = await AdminAliasesAsync(groups, cancellationToken);
            var alternatives = await AlternativePlacesAsync(groups, cancellationToken);
            var contexts = await ContextsAsync(groups.SelectMany(g => g.Lines).Where(l => AlcoholLineContext.Read(l.Raw).Kind != AlcoholSenseKind.None).Select(l => l.RecipeId).ToHashSet(), cancellationToken);

            var plans = new List<(Group Group, GateFood Food, IReadOnlyList<GateLine>? Lines, List<(string Tier, long Id)> Candidates)>();
            var skipped = new List<Decided>();
            var allowed = AlcoholLineContext.SenseCodes.SelectMany(AlcoholLineContext.Allowed).Distinct().ToList();
            var byFdc = await _gate.GateFoodsByFdcAsync(allowed, cancellationToken);
            foreach (var group in groups)
            {
                if (!foods.TryGetValue(group.FoodId, out var food)) continue;
                var before = group.Lines.Count;
                group.Lines.RemoveAll(l => alternatives.Contains((l.RecipeId, l.FoodId, l.SortOrder)));
                var prepared = set == LinkRecheckRules.Prepared;
                if (set != LinkRecheckRules.Wordings && verified.TryGetValue(group.Wording, out var row) && row.Food == food.Id)
                {
                    group.WordingRowId = row.Id;
                    if (!prepared)
                    {
                        skipped.Add(new Decided(group, food, new RecheckVerdict(LinkRecheckRules.Verified, "verified wording row #" + row.Id, null, Array.Empty<GateHold>()), LinkRecheckRules.Verified));
                        continue;
                    }
                }
                if (!prepared && admin.Contains((food.Id, group.Wording)))
                {
                    skipped.Add(new Decided(group, food, new RecheckVerdict(LinkRecheckRules.Verified, "a person's alias", null, Array.Empty<GateHold>()), LinkRecheckRules.Verified));
                    continue;
                }
                if (group.Lines.Count == 0 && (before > 0 || !group.KeepWithoutLines))
                {
                    if (before > 0) skipped.Add(new Decided(group, food, new RecheckVerdict(LinkRecheckRules.Alternative, "lines on an alternative the compound lane picked", null, Array.Empty<GateHold>()), LinkRecheckRules.Alternative));
                    continue;
                }

                var lines = group.Lines.Count == 0 ? null : (IReadOnlyList<GateLine>)group.Lines.Select(l => new GateLine(l.Raw, null, null, contexts.GetValueOrDefault(l.RecipeId))).ToList();
                plans.Add((group, food, lines, LinkRecheckRules.CandidateIds(set, group.Wording, group.Sense, group.Drained, food, lines, catalog,
                    verified.TryGetValue(group.Wording, out var named) ? named.Food : null, byFdc)));
            }

            foreach (var (id, food) in await _gate.GateFoodsAsync(plans.SelectMany(p => p.Candidates.Select(c => c.Id)).Where(id => !foods.ContainsKey(id)).Distinct().ToList(), cancellationToken))
                foods[id] = food;

            var decisions = new List<Decided>(skipped);
            foreach (var (group, food, lines, candidateIds) in plans)
            {
                var verdict = LinkRecheckRules.Judge(set, group.Wording, food, lines, catalog,
                    candidateIds.Where(c => foods.ContainsKey(c.Id)).Select(c => (c.Tier, foods[c.Id])));
                var qualified = promotes && verdict is { Outcome: LinkRecheckRules.Unproven, Candidate: not null, Class: not null } && await QualifiedAsync(verdict.Class);
                if (qualified) verdict = verdict with { Outcome = verdict.Candidate! };
                decisions.Add(new Decided(group, food, verdict, null, qualified));
            }
            var guard = await DietGuardAsync(cancellationToken);
            for (var i = 0; i < decisions.Count; i++)
            {
                var d = decisions[i];
                if (d.Skip != null || d.Group.Lines.Count == 0) continue;
                var target = d.Verdict.Outcome == LinkRecheckRules.Move ? d.Verdict.Target?.Food : null;
                if (d.Verdict.Outcome != LinkRecheckRules.Unlink && target == null) continue;
                var raws = d.Group.Lines.Select(l => l.Raw);
                var lowered = target == null ? guard.Lowered(d.Food, d.Group.Wording, raws) : guard.Lowered(d.Food, target, raws);
                if (lowered.Count > 0)
                    decisions[i] = d with
                    {
                        Verdict = d.Verdict with { Outcome = LinkRecheckRules.Held, Target = null, Reason = target == null ? RecheckDietGuard.Reason(lowered) : RecheckDietGuard.Reason(lowered, target) },
                        Qualified = false,
                    };
            }

            Tally(result, decisions, sampleSize);
            var stamp = NewStamp();
            var summary = new LinkRecheckRunSummary
            {
                Set = set, Stamp = stamp, Lane = lane, Mode = mode, Signature = signature, Groups = result.Groups, Kept = result.Kept + result.Verified + result.Alternatives, Held = result.Held,
                Unproven = result.Unproven, UnprovenLines = result.UnprovenLines, Candidates = result.Candidates, CandidateLines = result.CandidateLines,
            };
            long? header = null;
            using var running = apply ? MarkRunning(stamp) : null;
            var acting = decisions.Where(d => d.Skip == null && d.Verdict.Outcome is LinkRecheckRules.Move or LinkRecheckRules.Unlink).ToList();
            foreach (var d in acting.Where(d => d.Qualified)) result.ByQualified[d.Verdict.Class!] = result.ByQualified.GetValueOrDefault(d.Verdict.Class!) + 1;
            var stamps = new Dictionary<Decided, DateTime>();
            if (lane)
            {
                var byClass = acting.Where(d => d.Qualified).GroupBy(d => d.Verdict.Class!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                acting = acting.Where(d => !d.Qualified).ToList();
                var last = stamp;
                foreach (var group in byClass)
                {
                    var next = NewStamp();
                    var classStamp = last = next > last ? next : last.AddMilliseconds(1);
                    foreach (var d in group) stamps[d] = classStamp;
                    await WriteClassAsync(set, group.Key, group.ToList(), classStamp, result, catalog, cancellationToken);
                    result.ClassStamps.Add(classStamp);
                    result.Applied = true;
                }
                if (byClass.Count > 0 && !apply) acting.Clear();
            }
            if (apply)
            {
                result.Stamp = stamp;
                header = await StartAsync(set, stamp, summary, _currentUser.PersonIdOrSystem, cancellationToken);
                foreach (var d in acting) stamps[d] = stamp;
            }
            await WriteAsync(acting, stamp, result, apply, catalog, cancellationToken);
            if (mode != null) await RecordAsync(set, signature, mode, lane, lane ? decisions : decisions.Where(d => d.Skip == null).ToList(), stamps, cancellationToken);
            if (header != null)
            {
                summary.LinesMoved = result.LinesMoved;
                summary.LinesUnlinked = result.LinesUnlinked;
                summary.AliasesChanged = result.AliasesMoved + result.AliasesUntrusted + result.AliasesRemoved + result.AliasesAdded;
                await FinishAsync(header.Value, summary, cancellationToken);
            }

            _logger.LogInformation(
                "Link re-check {Set} ({Mode}): {Groups} wordings, {Kept} kept, {Held} held, {Unproven} not proven ({UnprovenLines} lines kept), {Candidates} refuted by a class not proven ({CandidateLines} lines kept for the model audit), {Moved} lines {MoveVerb}, {Unlinked} lines {UnlinkVerb}",
                set, mode ?? "dry run", result.Groups, result.Kept, result.Held, result.Unproven, result.UnprovenLines, result.Candidates, result.CandidateLines, result.LinesMoved, apply ? "moved" : "would move",
                result.LinesUnlinked, apply ? "taken off their food" : "would be taken off their food");
            return result;
        }

        /// <summary>The diet guard over the default restriction types and every stored type's pattern criteria (<see cref="RecheckDietGuard"/>).</summary>
        private Task<RecheckDietGuard> DietGuardAsync(CancellationToken cancellationToken) => LineMoveDietGuard.RecheckGuardAsync(_db, cancellationToken);

        /// <summary>The line entries whose undo the diet guard would hold (<see cref="LineMoveDietGuard"/>): what the apply's undo sweep holds, read without writing.</summary>
        private async Task<HashSet<long>> HeldTakingBackAsync(List<AuditLogEntryEntity> entries, CancellationToken cancellationToken)
        {
            var held = new HashSet<long>();
            if (entries.Count == 0) return held;
            var guard = await LineMoveDietGuard.LoadAsync(_db, cancellationToken);
            var trail = await RecipeOrderTrail.LoadAsync(_db, entries.Select(a => a.EntityId).Distinct());
            foreach (var entry in entries)
            {
                if (LineRelinkService.ReadPlace(entry.OldValue) is not { } was || LineRelinkService.ReadPlace(entry.NewValue) is not { } now
                    || trail.Follow(entry.EntityId, now.IngredientId, now.SortOrder, entry.Timestamp) is not { } from) continue;
                if (await guard.HoldsAsync(_db, entry.EntityId, now.IngredientId, from, was.IngredientId)) held.Add(entry.Id);
            }
            return held;
        }

        private static void Tally(LinkRecheckResult result, List<Decided> decisions, int sampleSize)
        {
            var perOutcome = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in decisions.OrderByDescending(d => d.Group.Lines.Count))
            {
                var lines = d.Group.Lines.Count;
                switch (d.Verdict.Outcome)
                {
                    case LinkRecheckRules.Verified: result.Verified++; break;
                    case LinkRecheckRules.Alternative: result.Alternatives++; break;
                    case LinkRecheckRules.Keep: result.Kept++; break;
                    case LinkRecheckRules.Held: result.Held++; break;
                    case LinkRecheckRules.Unproven when d.Verdict.Candidate != null:
                        result.Candidates++;
                        result.CandidateLines += lines;
                        result.ByCandidate[d.Verdict.Class!] = result.ByCandidate.GetValueOrDefault(d.Verdict.Class!) + 1;
                        break;
                    case LinkRecheckRules.Unproven:
                        result.Unproven++;
                        result.UnprovenLines += lines;
                        break;
                    case LinkRecheckRules.Move:
                        result.MoveGroups++;
                        result.RefutedLines += lines;
                        result.ByTier[d.Verdict.Target!.Tier] = result.ByTier.GetValueOrDefault(d.Verdict.Target.Tier) + 1;
                        break;
                    case LinkRecheckRules.Unlink:
                        result.UnlinkGroups++;
                        result.RefutedLines += lines;
                        break;
                }
                if (d.Verdict.Reason != null && d.Verdict.Outcome is LinkRecheckRules.Move or LinkRecheckRules.Unlink or LinkRecheckRules.Held)
                {
                    var rule = d.Verdict.Reason.Split(" (", 2)[0];
                    result.ByReason[rule] = result.ByReason.GetValueOrDefault(rule) + 1;
                }
                if (d.Verdict.Class != null && d.Verdict.Outcome is LinkRecheckRules.Move or LinkRecheckRules.Unlink)
                    result.ByClass[d.Verdict.Class] = result.ByClass.GetValueOrDefault(d.Verdict.Class) + 1;
                if (d.Verdict.Reason != null && d.Verdict.Outcome == LinkRecheckRules.Unproven && d.Verdict.Candidate == null)
                {
                    var rule = d.Verdict.Reason.Split(" (", 2)[0];
                    result.ByUnproven[rule] = result.ByUnproven.GetValueOrDefault(rule) + 1;
                }

                var sampleOutcome = d.Verdict.Candidate != null && d.Verdict.Outcome == LinkRecheckRules.Unproven ? CandidateOutcome : d.Verdict.Outcome;
                var seen = perOutcome[sampleOutcome] = perOutcome.GetValueOrDefault(sampleOutcome) + 1;
                if (seen > sampleSize) continue;
                result.Samples.Add(new LinkRecheckSample
                {
                    Outcome = sampleOutcome,
                    Class = d.Verdict.Class,
                    Candidate = d.Verdict.Candidate,
                    FoodId = d.Food.Id,
                    FoodName = d.Food.Name,
                    Wording = d.Group.Wording,
                    Sense = d.Group.Sense,
                    Lines = lines,
                    Reason = d.Verdict.Reason,
                    Tier = d.Verdict.Target?.Tier,
                    TargetId = d.Verdict.Target?.Food.Id,
                    TargetName = d.Verdict.Target?.Food.Name,
                    WordingRowId = d.Group.WordingRowId,
                    Holds = d.Verdict.Holds.Select(h => h.ToString()).ToList(),
                    ExampleLines = d.Group.Lines.Take(ExampleLines).Select(l => l.Raw).ToList(),
                });
            }
        }

        private async Task<List<Group>> GroupsAsync(string set, GateCatalog catalog, CancellationToken cancellationToken)
        {
            switch (set)
            {
                case LinkRecheckRules.Alcohol:
                {
                    var lines = (await LinesAsync(ScrapedLines(_db, curatedOnly: true).Where(ContainsAny(LinkRecheckRules.AlcoholStems)), cancellationToken))
                        .Where(l => AlcoholLineContext.Read(l.Raw).Kind != AlcoholSenseKind.None).ToList();
                    var foods = await _gate.GateFoodsAsync(lines.Select(l => l.FoodId).Distinct().ToList(), cancellationToken);
                    var contexts = await ContextsAsync(lines.Select(l => l.RecipeId).ToHashSet(), cancellationToken);
                    var groups = new Dictionary<(long, string, string), Group>();
                    foreach (var line in lines)
                    {
                        if (!foods.TryGetValue(line.FoodId, out var food)) continue;
                        var reading = AlcoholLineContext.Read(line.Raw, contexts.GetValueOrDefault(line.RecipeId));
                        if (reading.Kind == AlcoholSenseKind.None || AlcoholLineContext.Refuse(reading, food.FdcId, food.Text) == null) continue;
                        var key = (line.FoodId, WordingOf(line.Raw), reading.ToString());
                        if (!groups.TryGetValue(key, out var group)) groups[key] = group = new Group { FoodId = line.FoodId, Wording = key.Item2, Sense = reading.Sense == AlcoholLineContext.ListSense ? reading.ToString() : reading.Sense };
                        group.Lines.Add(line);
                    }

                    var aliases = (await _db.IngredientAliases.AsNoTracking()
                            .Where(a => !a.IsDeleted && !a.Ingredient.IsDeleted && a.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated
                                && (a.SourceContext == null || a.SourceContext.StartsWith(LinkAiPrefix) || a.SourceContext.StartsWith(MovedAliasPrefix)))
                            .Select(a => new { a.IngredientId, a.AliasName })
                            .ToListAsync(cancellationToken))
                        .Select(a => (a.IngredientId, a.AliasName, Reading: AlcoholLineContext.Read(a.AliasName)))
                        .Where(a => a.Reading.Kind != AlcoholSenseKind.None)
                        .ToList();
                    var owners = await _gate.GateFoodsAsync(aliases.Select(a => a.IngredientId).Distinct().ToList(), cancellationToken);
                    foreach (var (owner, name, reading) in aliases)
                    {
                        if (!owners.TryGetValue(owner, out var food) || AlcoholLineContext.Refuse(reading, food.FdcId, food.Text) == null) continue;
                        var key = (owner, IngredientNameNormalizer.WordingKey(name), reading.ToString());
                        if (!groups.TryGetValue(key, out var group)) groups[key] = group = new Group { FoodId = owner, Wording = key.Item2, Sense = reading.Sense == AlcoholLineContext.ListSense ? reading.ToString() : reading.Sense };
                        group.Aliases.Add(name);
                        group.KeepWithoutLines = true;
                    }
                    return groups.Values.Where(g => g.Wording.Length > 0).ToList();
                }
                case LinkRecheckRules.ContainerForm:
                {
                    var lines = await LinesAsync(ScrapedLines(_db, curatedOnly: true).Where(ContainsAny(LinkRecheckRules.ContainerStems)), cancellationToken);
                    var foods = await _gate.GateFoodsAsync(lines.Select(l => l.FoodId).Distinct().ToList(), cancellationToken);
                    var groups = new Dictionary<(long, string, bool), Group>();
                    foreach (var line in lines)
                    {
                        if (!foods.TryGetValue(line.FoodId, out var food) || !LinkRecheckRules.WrongForm(line.Raw, food)) continue;
                        var key = (line.FoodId, WordingOf(line.Raw), LinkRecheckRules.IsDrainedLine(line.Raw));
                        if (!groups.TryGetValue(key, out var group)) groups[key] = group = new Group { FoodId = line.FoodId, Wording = key.Item2, Drained = key.Item3 };
                        group.Lines.Add(line);
                    }
                    return groups.Values.Where(g => g.Wording.Length > 0).ToList();
                }
                case LinkRecheckRules.Prepared:
                {
                    var categories = CatalogAdmissionRule.PreparedCategories.Keys.ToList();
                    var lines = await LinesAsync(ScrapedLines(_db, curatedOnly: false).Where(ri => ri.Ingredient.FdcCategoryId != null && categories.Contains(ri.Ingredient.FdcCategoryId.Value)), cancellationToken);
                    return lines.GroupBy(l => (l.FoodId, WordingOf(l.Raw)))
                        .Where(g => g.Key.Item2.Length > 0)
                        .Select(g =>
                        {
                            var group = new Group { FoodId = g.Key.FoodId, Wording = g.Key.Item2 };
                            group.Lines.AddRange(g);
                            return group;
                        })
                        .ToList();
                }
                default:
                    return await LinkGroupsAsync(set, cancellationToken);
            }
        }

        /// <summary>
        /// The wordings of applied model links (by the day they were applied), of trusted aliases a
        /// rule gave (recheck and deterministic link contexts) or of verified wording rows, each with
        /// the scraped lines on its food whose own text names the wording.
        /// </summary>
        private async Task<List<Group>> LinkGroupsAsync(string set, CancellationToken cancellationToken)
        {
            var groups = new Dictionary<(long, string), Group>();
            Group Get(long food, string wording)
            {
                if (!groups.TryGetValue((food, wording), out var group)) groups[(food, wording)] = group = new Group { FoodId = food, Wording = wording };
                return group;
            }

            if (set is LinkRecheckRules.PartHerb or LinkRecheckRules.Compound or LinkRecheckRules.AppliedNovember or LinkRecheckRules.Applied)
            {
                var applied = await _db.FoodCatalogProposals.AsNoTracking()
                    .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Applied && p.Source.StartsWith("ai:")
                        && p.ProposedValue != null && p.CurrentValue != null)
                    .Select(p => new { p.Id, p.CurrentValue, p.ProposedValue, p.ReviewedAt })
                    .ToListAsync(cancellationToken);
                var day = LinkRecheckRules.NovemberThird;
                foreach (var p in applied)
                {
                    var onTheDay = p.ReviewedAt >= day && p.ReviewedAt < day.AddDays(1);
                    if (set == LinkRecheckRules.AppliedNovember && !onTheDay) continue;
                    if (set == LinkRecheckRules.Applied && onTheDay) continue;
                    if (!long.TryParse(p.ProposedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var target)) continue;
                    var wording = IngredientNameNormalizer.WordingKey(p.CurrentValue!);
                    if (wording.Length > 0) Get(target, wording).Proposals.Add(p.Id);
                }
            }

            if (set is LinkRecheckRules.PartHerb or LinkRecheckRules.Compound or LinkRecheckRules.Aliases)
            {
                var aliases = await _db.IngredientAliases.AsNoTracking()
                    .Where(a => !a.IsDeleted && !a.Ingredient.IsDeleted && a.SourceContext != null
                        && (a.SourceContext.StartsWith(IngredientAliasTrust.RecheckPrefix) || a.SourceContext.StartsWith(IngredientAliasTrust.DeterministicLinkPrefix)
                            || a.SourceContext.StartsWith(MovedAliasPrefix)))
                    .Select(a => new { a.IngredientId, a.AliasName })
                    .ToListAsync(cancellationToken);
                foreach (var a in aliases)
                {
                    var wording = IngredientNameNormalizer.WordingKey(a.AliasName);
                    if (wording.Length == 0) continue;
                    var group = Get(a.IngredientId, wording);
                    group.Aliases.Add(a.AliasName);
                    group.KeepWithoutLines = true;
                }
            }

            if (set == LinkRecheckRules.Wordings)
            {
                foreach (var row in await _db.IngredientWordings.AsNoTracking().Where(w => w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null)
                    .Select(w => new { w.Id, w.Wording, w.IngredientId }).ToListAsync(cancellationToken))
                {
                    var group = Get(row.IngredientId!.Value, row.Wording);
                    group.WordingRowId = row.Id;
                    group.KeepWithoutLines = true;
                }
            }

            var byFood = groups.Values.GroupBy(g => g.FoodId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var chunk in byFood.Keys.Chunk(LineChunk))
            {
                var lines = await LinesAsync(ScrapedLines(_db, curatedOnly: true).Where(ri => chunk.Contains(ri.IngredientId)), cancellationToken);
                foreach (var food in lines.GroupBy(l => l.FoodId))
                {
                    var byWording = byFood[food.Key].ToDictionary(g => g.Wording, StringComparer.Ordinal);
                    var byExact = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
                    foreach (var g in byFood[food.Key])
                        foreach (var key in IngredientNameNormalizer.ExactKeys(g.Wording)) byExact.TryAdd(key, g);
                    foreach (var line in food)
                    {
                        var first = FirstSegment(line.Raw);
                        var owner = IngredientNameNormalizer.ExactKeys(first).Select(k => byWording.GetValueOrDefault(k.ToLowerInvariant())).FirstOrDefault(g => g != null)
                            ?? byExact.GetValueOrDefault(IngredientNameNormalizer.WordingKey(first));
                        owner?.Lines.Add(line);
                    }
                }
            }
            return groups.Values.Where(g => g.Lines.Count > 0 || g.KeepWithoutLines).ToList();
        }

        internal static IQueryable<RecipeIngredientEntity> ScrapedLines(ApplicationDbContext db, bool curatedOnly) =>
            db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RawLine != ""
                    && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient)
                    && !ri.Recipe.IsDeleted && ri.Recipe.ScrapedAtUtc != null && !ri.Ingredient.IsDeleted
                    && (!curatedOnly || ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated));

        private static async Task<List<RLine>> LinesAsync(IQueryable<RecipeIngredientEntity> query, CancellationToken cancellationToken) =>
            await query.OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new RLine(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine))
                .ToListAsync(cancellationToken);

        internal static Expression<Func<RecipeIngredientEntity, bool>> ContainsAny(IEnumerable<string> stems)
        {
            var line = Expression.Parameter(typeof(RecipeIngredientEntity), "ri");
            var lowered = Expression.Call(Expression.Property(line, nameof(RecipeIngredientEntity.RawLine)), typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
            var contains = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
            Expression? body = null;
            foreach (var stem in stems)
            {
                var test = Expression.Call(lowered, contains, Expression.Constant(stem));
                body = body == null ? test : Expression.OrElse(body, test);
            }
            return Expression.Lambda<Func<RecipeIngredientEntity, bool>>(body ?? Expression.Constant(false), line);
        }

        private static string FirstSegment(string raw) => raw.Split(" + ", 2)[0];

        private static string WordingOf(string raw) => IngredientNameNormalizer.WordingKey(FirstSegment(raw));

        /// <summary>What the rest of each recipe says, for the alcohol sense rules: title, other lines, steps, and whether it came from a modern web source.</summary>
        private async Task<Dictionary<long, AlcoholRecipeContext>> ContextsAsync(IReadOnlyCollection<long> recipeIds, CancellationToken cancellationToken)
        {
            var contexts = new Dictionary<long, AlcoholRecipeContext>();
            foreach (var chunk in recipeIds.Chunk(1000))
            {
                var recipes = await _db.Recipes.AsNoTracking().Where(r => chunk.Contains(r.Id))
                    .Select(r => new { r.Id, r.Name, r.SourceSite, r.LicenseStatus }).ToListAsync(cancellationToken);
                var lines = (await _db.RecipeIngredients.AsNoTracking().Where(ri => chunk.Contains(ri.RecipeId) && !ri.IsDeleted)
                        .Select(ri => new { ri.RecipeId, ri.RawLine }).ToListAsync(cancellationToken))
                    .ToLookup(l => l.RecipeId, l => l.RawLine);
                var steps = (await _db.RecipeSteps.AsNoTracking().Where(s => chunk.Contains(s.RecipeId))
                        .OrderBy(s => s.StepNumber).Select(s => new { s.RecipeId, s.Description }).ToListAsync(cancellationToken))
                    .ToLookup(s => s.RecipeId, s => s.Description ?? string.Empty);
                foreach (var r in recipes)
                {
                    contexts[r.Id] = new AlcoholRecipeContext(r.Name, lines[r.Id].ToList(), string.Join(" ", steps[r.Id]),
                        !string.IsNullOrWhiteSpace(r.SourceSite) && r.LicenseStatus != RecipeLicenseStatus.PublicDomain);
                }
            }
            return contexts;
        }

        /// <summary>Lines the compound lane put on the first of their alternatives on purpose (a live LineAlternative entry for that place): never the re-check's to move.</summary>
        private async Task<HashSet<(long RecipeId, long FoodId, int SortOrder)>> AlternativePlacesAsync(List<Group> groups, CancellationToken cancellationToken)
        {
            var places = new HashSet<(long, long, int)>();
            foreach (var chunk in groups.SelectMany(g => g.Lines).Select(l => l.RecipeId).Distinct().Chunk(1000))
            {
                foreach (var entry in await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == LineRelinkService.RelinkEntityType && a.ChangeType == CompoundLineService.AlternativeChangeType && chunk.Contains(a.EntityId))
                    .Select(a => new { a.EntityId, a.NewValue }).ToListAsync(cancellationToken))
                {
                    if (LineRelinkService.ReadPlace(entry.NewValue) is { } place) places.Add((entry.EntityId, place.IngredientId, place.SortOrder));
                }
            }
            return places;
        }

        /// <summary>Wordings a person approved for the food they are on (an admin: alias): never the re-check's to undo.</summary>
        private async Task<HashSet<(long FoodId, string Wording)>> AdminAliasesAsync(List<Group> groups, CancellationToken cancellationToken)
        {
            var found = new HashSet<(long, string)>();
            foreach (var chunk in groups.Select(g => g.FoodId).Distinct().Chunk(1000))
            {
                foreach (var alias in await _db.IngredientAliases.AsNoTracking()
                    .Where(a => chunk.Contains(a.IngredientId) && !a.IsDeleted && a.SourceContext != null && a.SourceContext.StartsWith(IngredientAliasTrust.AdminPrefix))
                    .Select(a => new { a.IngredientId, a.AliasName }).ToListAsync(cancellationToken))
                    found.Add((alias.IngredientId, IngredientNameNormalizer.WordingKey(alias.AliasName)));
            }
            return found;
        }

        /// <summary>
        /// Writes the moves and take-backs, or with <paramref name="apply"/> false counts what they
        /// would write by the same steps (homes, created foods, freed and changed names, aliases,
        /// wording rows, approved recipes), so a dry run reads as its apply: each line to its new food
        /// at its own position (the next free one when taken), journaled as a line relink under the
        /// run's stamp; the wording's rule-given and model aliases on the old food removed; on a move
        /// a trusted alias of the wording on the new food, so a later import follows it; on a
        /// take-back an uncurated food of the wording's name, created when none is live, so a later
        /// import lands there and the link lane asks about it again through the gate. Every alias
        /// and food change is journaled under the stamp too.
        /// </summary>
        private async Task WriteAsync(List<Decided> decisions, DateTime stamp, LinkRecheckResult result, bool apply, GateCatalog catalog, CancellationToken cancellationToken)
        {
            var personId = _currentUser.PersonIdOrSystem;
            var homes = new Dictionary<string, long?>(StringComparer.Ordinal);
            var touched = new HashSet<long>();
            var retired = new HashSet<(long, string)>();
            var trusted = new HashSet<(long, string)>();
            foreach (var chunk in decisions.Chunk(WriteChunk))
            {
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                foreach (var d in chunk)
                {
                    var move = d.Verdict.Outcome == LinkRecheckRules.Move;
                    long target;
                    if (move)
                    {
                        target = d.Verdict.Target!.Food.Id;
                    }
                    else if (d.Group.Lines.Count == 0)
                    {
                        target = 0;
                    }
                    else
                    {
                        if (!homes.TryGetValue(d.Group.Wording, out var home))
                            homes[d.Group.Wording] = home = await HomeAsync(d.Group.Wording, d.Food, stamp, personId, result, apply, cancellationToken);
                        if (home is not { } found || found == d.Food.Id)
                        {
                            result.NoHome++;
                            continue;
                        }
                        target = found;
                    }

                    var moved = 0;
                    foreach (var line in d.Group.Lines)
                    {
                        if (!apply)
                        {
                            touched.Add(line.RecipeId);
                            moved++;
                            continue;
                        }
                        var position = await RecipeLineRowWriter.MoveAsync(_db, LineMoveDietGuard.Decided, line.RecipeId, line.FoodId, line.SortOrder, target, line.SortOrder, personId);
                        if (position == null)
                        {
                            result.LinesSkipped++;
                            continue;
                        }
                        _db.AuditLogEntries.Add(new AuditLogEntryEntity
                        {
                            EntityType = LineRelinkService.RelinkEntityType,
                            EntityId = line.RecipeId,
                            ChangeType = LineChangeType,
                            PropertyName = nameof(RecipeIngredientEntity.IngredientId),
                            OldValue = LineRelinkService.Place(line.FoodId, line.SortOrder),
                            NewValue = LineRelinkService.Place(target, position.Value),
                            Timestamp = stamp,
                            ChangedByPersonId = personId,
                        });
                        touched.Add(line.RecipeId);
                        moved++;
                    }
                    if (move) result.LinesMoved += moved;
                    else result.LinesUnlinked += moved;
                    if (moved > 0 && d.Group.WordingRowId is { } wordingRow && !result.WordingRows.Contains(wordingRow)) result.WordingRows.Add(wordingRow);

                    await RetireAliasesAsync(d, retired, stamp, personId, result, apply, cancellationToken);
                    if (move) await TrustOnTargetAsync(d, target, trusted, stamp, personId, result, apply, catalog, cancellationToken);
                    if (apply) await _db.SaveChangesAsync(cancellationToken);
                }
                if (tx != null) await tx.CommitAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            foreach (var chunk in touched.Chunk(1000))
                result.ApprovedRecipes += await _db.Recipes.AsNoTracking().CountAsync(r => chunk.Contains(r.Id) && r.CurationStatusId == (long)CurationStatusEnum.Curated, cancellationToken);
        }

        /// <summary>
        /// The uncurated food a wording's lines go back to: the live one of its name that no USDA
        /// entry backs, or a new one, created after any retired row holding the name is renamed out
        /// of the way (both journaled). It may sit beside a curated or USDA food whose name differs
        /// only in case, as every wording food does. When the food the lines leave holds the very
        /// same name (a prepared baby food named "zwieback"), that food is renamed to its USDA
        /// description first, journaled, so the lines can leave it and later imports stop landing
        /// on it. Null only when another curated or USDA food holds the very same name, which the
        /// unique index on the name refuses to repeat (the lines stay, counted as without a home).
        /// With <paramref name="apply"/> false nothing is written: the counts are made and a planned
        /// new food is <see cref="PlannedHome"/>.
        /// </summary>
        private async Task<long?> HomeAsync(string wording, GateFood source, DateTime stamp, long personId, LinkRecheckResult result, bool apply, CancellationToken cancellationToken)
        {
            var name = wording.Length > 2000 ? wording[..2000] : wording;
            var live = await _db.Ingredients.AsNoTracking().Where(i => !i.IsDeleted && i.Name.ToLower() == name)
                .Select(i => new { i.Id, i.Name, i.CurationStatusId, i.FdcId }).ToListAsync(cancellationToken);
            if (live.FirstOrDefault(i => i.FdcId == null && i.CurationStatusId != (long)CurationStatusEnum.Curated) is { } uncurated) return uncurated.Id;
            if (live.FirstOrDefault(i => i.Name == name) is { } same)
            {
                if (same.Id != source.Id || string.IsNullOrWhiteSpace(source.Description)) return null;
                var renamed = await FreeNameAsync(RenameFor(source, name), source.Id, cancellationToken);
                result.FoodsRenamed++;
                if (apply)
                {
                    var renaming = await _db.Ingredients.SingleAsync(i => i.Id == source.Id, cancellationToken);
                    _db.AuditLogEntries.Add(FoodEntry(renaming.Id, nameof(IngredientEntity.Name), renaming.Name, renamed, stamp, personId));
                    renaming.Name = renamed;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            var holders = await _db.Ingredients.IgnoreQueryFilters().Where(i => i.IsDeleted && i.Name == name).ToListAsync(cancellationToken);
            result.NamesFreed += holders.Count;
            if (!apply)
            {
                _db.ChangeTracker.Clear();
                result.FoodsCreated++;
                return PlannedHome;
            }
            foreach (var holder in holders)
            {
                var freed = $"{holder.Name} (deleted #{holder.Id})";
                if (freed.Length > 2000) freed = freed[..2000];
                _db.AuditLogEntries.Add(FoodEntry(holder.Id, nameof(IngredientEntity.Name), holder.Name, freed, stamp, personId));
                holder.Name = freed;
            }
            await _db.SaveChangesAsync(cancellationToken);

            var food = new IngredientEntity
            {
                Name = name,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                CreatedDate = stamp,
                CreatedByPersonId = personId,
            };
            _db.Ingredients.Add(food);
            await _db.SaveChangesAsync(cancellationToken);
            _db.AuditLogEntries.Add(FoodEntry(food.Id, CreatedProperty, null, name, stamp, personId));
            result.FoodsCreated++;
            return food.Id;
        }

        /// <summary>
        /// The name a food holding a wording's very name takes so the wording's lines can leave it:
        /// its USDA description, or, when that differs from the wording only in case (#93510
        /// "zwieback", described "Zwieback"), the description with the food's USDA category, or its
        /// id when it has none, so the two names differ beyond case.
        /// </summary>
        internal static string RenameFor(GateFood source, string wording)
        {
            var description = (source.Description ?? source.Name).Trim();
            if (!string.Equals(description, wording.Trim(), StringComparison.OrdinalIgnoreCase)) return description;
            return source.CategoryId is { } category && CatalogAdmissionRule.PreparedCategories.TryGetValue(category, out var kind)
                ? $"{description} ({kind})"
                : string.Create(CultureInfo.InvariantCulture, $"{description} (#{source.Id})");
        }

        /// <summary>The id a dry run gives a wording food it would create.</summary>
        private const long PlannedHome = -1;

        /// <summary>A name no other food holds: the name itself, or the name with the food's id after it.</summary>
        private async Task<string> FreeNameAsync(string name, long foodId, CancellationToken cancellationToken)
        {
            name = name.Length > 1980 ? name[..1980] : name;
            return await _db.Ingredients.IgnoreQueryFilters().AnyAsync(i => i.Id != foodId && i.Name == name, cancellationToken) ? $"{name} (#{foodId})" : name;
        }

        private static AuditLogEntryEntity FoodEntry(long foodId, string property, string? oldValue, string? newValue, DateTime stamp, long personId) => new()
        {
            EntityType = LineRelinkService.RetireEntityType,
            EntityId = foodId,
            ChangeType = FoodChangeType,
            PropertyName = property,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = stamp,
            ChangedByPersonId = personId,
        };

        private static bool Retirable(string? context) =>
            context == null || context.StartsWith(LinkAiPrefix, StringComparison.Ordinal)
            || context.StartsWith(IngredientAliasTrust.RecheckPrefix, StringComparison.Ordinal)
            || context.StartsWith(IngredientAliasTrust.DeterministicLinkPrefix, StringComparison.Ordinal)
            || context.StartsWith(MovedAliasPrefix, StringComparison.Ordinal);

        /// <summary>Removes the wording's aliases on the food its lines left that a rule or a model pick put there (never a USDA description, a person's or a retired food's name), each once in a run (<paramref name="retired"/>, by food and name, the alias key), so a dry run counts an alias two groups share as its apply does, and every alias of the wording goes, in name order.</summary>
        private async Task RetireAliasesAsync(Decided d, HashSet<(long, string)> retired, DateTime stamp, long personId, LinkRecheckResult result, bool apply, CancellationToken cancellationToken)
        {
            var aliases = (await _db.IngredientAliases.Where(a => a.IngredientId == d.Food.Id && !a.IsDeleted).ToListAsync(cancellationToken))
                .OrderBy(a => a.AliasName, StringComparer.Ordinal)
                .Where(a => Retirable(a.SourceContext) && IngredientNameNormalizer.WordingKey(a.AliasName) == d.Group.Wording && retired.Add((a.IngredientId, a.AliasName)))
                .ToList();
            foreach (var alias in aliases)
            {
                if (!apply)
                {
                    if (IngredientAliasTrust.IsTrusted(alias.SourceContext)) result.AliasesUntrusted++;
                    else result.AliasesRemoved++;
                    continue;
                }
                var before = new LinkApplyJournal.AliasValue(alias.AliasName, alias.SourceContext, false);
                alias.IsDeleted = true;
                alias.DeletedAt = stamp;
                alias.DeletedByPersonId = personId;
                _db.AuditLogEntries.Add(AliasEntry(alias.IngredientId, before, before with { Deleted = true }, stamp, personId));
                if (IngredientAliasTrust.IsTrusted(before.Context)) result.AliasesUntrusted++;
                else result.AliasesRemoved++;
            }
        }

        /// <summary>
        /// Gives the wording a trusted alias on the food its lines moved to, unless the food already
        /// has a trusted one of that name or the run gave it one (<paramref name="trusted"/>), so a
        /// dry run counts it once as its apply does; and only when the gate accepts the wording for
        /// that food and it adds no restricted food's word to it (<see cref="LinkRecheckRules.MayAlias"/>),
        /// counted <c>aliasesRefused</c> otherwise.
        /// </summary>
        private async Task TrustOnTargetAsync(Decided d, long target, HashSet<(long, string)> trusted, DateTime stamp, long personId, LinkRecheckResult result, bool apply, GateCatalog catalog, CancellationToken cancellationToken)
        {
            var name = d.Group.Wording.Length > 511 ? d.Group.Wording[..511] : d.Group.Wording;
            if (!trusted.Add((target, name))) return;
            if (!LinkRecheckRules.MayAlias(name, d.Verdict.Target!.Food, catalog))
            {
                result.AliasesRefused++;
                return;
            }
            var context = MovedAliasPrefix + d.Verdict.Target!.Tier;
            var existing = await _db.IngredientAliases.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.IngredientId == target && a.AliasName == name, cancellationToken);
            if (existing is { IsDeleted: false } && IngredientAliasTrust.IsTrusted(existing.SourceContext)) return;
            if (!apply)
            {
                if (d.Group.Aliases.Count > 0) result.AliasesMoved++;
                else result.AliasesAdded++;
                return;
            }

            var before = existing == null ? null : new LinkApplyJournal.AliasValue(existing.AliasName, existing.SourceContext, existing.IsDeleted);
            if (existing == null)
            {
                _db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = target, AliasName = name, SourceContext = context, CreatedDate = stamp, CreatedByPersonId = personId });
            }
            else
            {
                existing.IsDeleted = false;
                existing.DeletedAt = null;
                existing.DeletedByPersonId = null;
                existing.SourceContext = context;
                existing.LastModifiedDate = stamp;
                existing.LastModifiedByPersonId = personId;
            }
            _db.AuditLogEntries.Add(AliasEntry(target, before, new LinkApplyJournal.AliasValue(name, context, false), stamp, personId));
            if (d.Group.Aliases.Count > 0) result.AliasesMoved++;
            else result.AliasesAdded++;
        }

        private static AuditLogEntryEntity AliasEntry(long foodId, LinkApplyJournal.AliasValue? before, LinkApplyJournal.AliasValue after, DateTime stamp, long personId) => new()
        {
            EntityType = IngredientLinkService.AliasEntityType,
            EntityId = foodId,
            ChangeType = AliasChangeType,
            PropertyName = AliasProperty,
            OldValue = before == null ? null : LinkApplyJournal.Write(before),
            NewValue = LinkApplyJournal.Write(after),
            Timestamp = stamp,
            ChangedByPersonId = personId,
        };

        /// <summary>
        /// Records a run's decisions on the lane frame. A candidate, and a candidate written because its
        /// class's tier is live, goes on that class's tier (<see cref="LinkRecheckRules.CandidateTier"/>)
        /// with the set as its stratum, so the auditor samples and qualifies each class on its own and a
        /// set's record never carries a class that is not proven; the rest go on the set's tier.
        /// </summary>
        private async Task RecordAsync(string set, string signature, string mode, bool lane, List<Decided> decisions, IReadOnlyDictionary<Decided, DateTime> stamps, CancellationToken cancellationToken)
        {
            foreach (var d in decisions)
            {
                var candidate = d.Verdict.Outcome == LinkRecheckRules.Unproven ? d.Verdict.Candidate : null;
                var writes = d.Verdict.Outcome is LinkRecheckRules.Move or LinkRecheckRules.Unlink;
                if (!lane && !writes && candidate == null) continue;
                var target = d.Verdict.Target?.Food;
                var outcome = candidate ?? d.Verdict.Outcome;
                DateTime? stamp = stamps.TryGetValue(d, out var at) ? at : null;
                var signals = new LinkRecheckSignals(set, d.Food.Id, d.Food.Name, d.Food.Text, d.Group.Wording, d.Group.Sense, d.Group.Lines.Count,
                    d.Group.Lines.Take(ExampleLines).Select(l => l.Raw).ToList(), d.Verdict.Holds.Select(h => h.ToString()).ToList(),
                    outcome, d.Verdict.Reason, d.Verdict.Target?.Tier, target?.Id, target?.Name, target?.Text, candidate == null ? stamp : null, d.Verdict.Class);
                var decision = outcome + (d.Verdict.Reason == null ? string.Empty : ": " + d.Verdict.Reason) + (target == null ? string.Empty : " -> " + target.Name);
                var input = candidate != null || d.Qualified
                    ? new LaneDecisionInput(Lane, LinkRecheckRules.CandidateTier(d.Verdict.Class!), Clip(d.Group.Subject, 255)!, decision,
                        candidate != null || stamp == null ? LaneMode.Shadow : LaneMode.Live, true, LinkRecheckRules.TierSignature(LinkRecheckRules.CandidateTier(d.Verdict.Class!)), set, signals)
                    : new LaneDecisionInput(Lane, set, Clip(d.Group.Subject, 255)!, decision,
                        mode, writes, signature, d.Verdict.Target?.Tier ?? d.Verdict.Reason?.Split(" (", 2)[0], signals);
                await _frame.RecordAsync(input, cancellationToken);
            }
        }

        /// <summary>
        /// Writes a lane batch's candidates of one class whose tier is live, as a run of that tier under
        /// a stamp of its own, so the tier's pause undoes only its own writes.
        /// </summary>
        private async Task WriteClassAsync(string set, string reClass, List<Decided> decisions, DateTime stamp, LinkRecheckResult result, GateCatalog catalog, CancellationToken cancellationToken)
        {
            var tier = LinkRecheckRules.CandidateTier(reClass);
            var summary = new LinkRecheckRunSummary { Set = tier, Stamp = stamp, Lane = true, Mode = LaneMode.Live, Signature = LinkRecheckRules.TierSignature(tier), Groups = decisions.Count };
            using var running = MarkRunning(stamp);
            var header = await StartAsync(tier, stamp, summary, _currentUser.PersonIdOrSystem, cancellationToken);
            var part = new LinkRecheckResult { Set = set, Applied = true, Stamp = stamp };
            await WriteAsync(decisions, stamp, part, true, catalog, cancellationToken);
            result.LinesMoved += part.LinesMoved;
            result.LinesUnlinked += part.LinesUnlinked;
            result.LinesSkipped += part.LinesSkipped;
            result.NoHome += part.NoHome;
            result.AliasesMoved += part.AliasesMoved;
            result.AliasesUntrusted += part.AliasesUntrusted;
            result.AliasesRemoved += part.AliasesRemoved;
            result.AliasesAdded += part.AliasesAdded;
            result.AliasesRefused += part.AliasesRefused;
            result.FoodsCreated += part.FoodsCreated;
            result.FoodsRenamed += part.FoodsRenamed;
            result.NamesFreed += part.NamesFreed;
            result.ApprovedRecipes += part.ApprovedRecipes;
            result.WordingRows.AddRange(part.WordingRows.Where(id => !result.WordingRows.Contains(id)));
            summary.LinesMoved = part.LinesMoved;
            summary.LinesUnlinked = part.LinesUnlinked;
            summary.AliasesChanged = part.AliasesMoved + part.AliasesUntrusted + part.AliasesRemoved + part.AliasesAdded;
            await FinishAsync(header, summary, cancellationToken);
        }

        /// <summary>Writes a run's header before its first write, so a run cut off partway still has the stamp its undo takes; returns the header's id.</summary>
        private async Task<long> StartAsync(string set, DateTime stamp, LinkRecheckRunSummary summary, long personId, CancellationToken cancellationToken)
        {
            var header = Header(set, stamp, summary, personId);
            _db.AuditLogEntries.Add(header);
            await _db.SaveChangesAsync(cancellationToken);
            return header.Id;
        }

        /// <summary>Marks a run's header complete with its final counts.</summary>
        private async Task FinishAsync(long headerId, LinkRecheckRunSummary summary, CancellationToken cancellationToken)
        {
            summary.Complete = true;
            var header = await _db.AuditLogEntries.SingleAsync(a => a.Id == headerId, cancellationToken);
            header.NewValue = Clip(LaneJournal.Write(summary), 4000);
            await _db.SaveChangesAsync(cancellationToken);
        }

        private static AuditLogEntryEntity Header(string set, DateTime stamp, LinkRecheckRunSummary summary, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = 0,
            ChangeType = RunChangeType,
            PropertyName = set,
            OldValue = StampText(stamp),
            NewValue = Clip(LaneJournal.Write(summary), 4000),
            Timestamp = stamp,
            ChangedByPersonId = personId,
        };

        private static AuditLogEntryEntity UndoEntry(string set, DateTime stamp, object result, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = 0,
            ChangeType = UndoChangeType,
            PropertyName = set,
            OldValue = StampText(stamp),
            NewValue = Clip(LaneJournal.Write(result), 4000),
            Timestamp = DateTime.UtcNow,
            ChangedByPersonId = personId,
        };

        public async Task<LinkRecheckUndoResult> UndoAsync(DateTime stamp, bool apply = false, CancellationToken cancellationToken = default)
        {
            stamp = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
            var result = new LinkRecheckUndoResult { Stamp = stamp, Applied = apply };
            var header = await _db.AuditLogEntries.AsNoTracking()
                .FirstOrDefaultAsync(a => a.EntityType == EntityType && a.ChangeType == RunChangeType && a.Timestamp == stamp, cancellationToken);
            result.Found = header != null;
            result.Set = header?.PropertyName;
            if (header?.PropertyName == WithdrawRun)
            {
                var withdrawn = await UndoWithdrawAsync(stamp, apply, cancellationToken);
                result.PickEntries = withdrawn.Entries;
                result.PicksRestored = withdrawn.Restored;
                result.SkippedAskedAgain = withdrawn.SkippedAskedAgain;
                result.Skipped = withdrawn.Skipped;
                return result;
            }

            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.Timestamp == stamp
                    && ((a.EntityType == LineRelinkService.RelinkEntityType && a.ChangeType == LineChangeType)
                        || (a.EntityType == LineRelinkService.RetireEntityType && a.ChangeType == LineRelinkService.RetireChangeType)
                        || a.ChangeType == AliasChangeType || a.ChangeType == FoodChangeType))
                .ToListAsync(cancellationToken);
            var lineEntries = entries.Where(a => a.ChangeType is LineChangeType or LineRelinkService.RetireChangeType).ToList();
            var aliasEntries = entries.Where(a => a.ChangeType == AliasChangeType).OrderByDescending(a => a.Id).ToList();
            var foodEntries = entries.Where(a => a.ChangeType == FoodChangeType).ToList();
            result.LineEntries = lineEntries.Count(a => a.ChangeType == LineChangeType);
            result.AliasEntries = aliasEntries.Count;
            var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_db, lineEntries.Where(a => a.EntityType == LineRelinkService.RelinkEntityType).Select(a => a.EntityId).ToList());
            result.OnRebuiltRecipes = lineEntries.Count(a => a.EntityType == LineRelinkService.RelinkEntityType && rebuilt.TryGetValue(a.EntityId, out var at) && at > a.Timestamp);

            var personId = _currentUser.PersonIdOrSystem;
            var held = new HashSet<long>();
            if (!apply)
            {
                held = await HeldTakingBackAsync(lineEntries.Where(a => a.ChangeType == LineChangeType && !(rebuilt.TryGetValue(a.EntityId, out var at) && at > a.Timestamp)).ToList(), cancellationToken);
                result.HeldByDiet = held.Count;
                result.LinesRestored = result.LineEntries - result.OnRebuiltRecipes - held.Count;
                result.NamesRestored = lineEntries.Count(a => a.ChangeType == LineRelinkService.RetireChangeType);
            }
            else if (lineEntries.Count > 0)
            {
                var undone = await _relink.UndoEntriesAsync(lineEntries.Select(a => a.Id).ToList());
                result.LinesRestored = undone.Restored;
                result.SkippedRebuilt = undone.SkippedRebuilt;
                result.Skipped = undone.Skipped;
                result.HeldByDiet = undone.HeldByDiet;
                var left = lineEntries.Select(a => a.Id).ToList();
                result.NamesRestored = lineEntries.Count(a => a.ChangeType == LineRelinkService.RetireChangeType) - await _db.AuditLogEntries.CountAsync(a => left.Contains(a.Id) && a.ChangeType == LineRelinkService.RetireChangeType, cancellationToken);
                _db.ChangeTracker.Clear();
            }

            var restoredLines = lineEntries.Where(a => a.ChangeType == LineChangeType && !(rebuilt.TryGetValue(a.EntityId, out var at) && at > a.Timestamp) && !held.Contains(a.Id))
                .Select(a => LineRelinkService.ReadPlace(a.NewValue)).Where(p => p != null).GroupBy(p => p!.Value.IngredientId).ToDictionary(g => g.Key, g => g.Count());

            await using (var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null)
            {
                foreach (var entry in aliasEntries)
                {
                    var before = LinkApplyJournal.Read<LinkApplyJournal.AliasValue>(entry.OldValue);
                    var after = LinkApplyJournal.Read<LinkApplyJournal.AliasValue>(entry.NewValue);
                    if (after == null) continue;
                    var alias = await _db.IngredientAliases.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.IngredientId == entry.EntityId && a.AliasName == after.Name, cancellationToken);
                    if (alias == null || alias.IsDeleted != after.Deleted || alias.SourceContext != after.Context)
                    {
                        result.Skipped++;
                        continue;
                    }
                    result.AliasesRestored++;
                    if (!apply) continue;
                    if (before == null)
                    {
                        _db.AuditLogEntries.Remove(await _db.AuditLogEntries.SingleAsync(a => a.Id == entry.Id, cancellationToken));
                        await _db.SaveChangesAsync(cancellationToken);
                        await _db.HardDeleteAsync(new[] { alias }, cancellationToken);
                        continue;
                    }
                    else
                    {
                        alias.SourceContext = before.Context;
                        alias.IsDeleted = before.Deleted;
                        alias.DeletedAt = before.Deleted ? alias.DeletedAt ?? stamp : null;
                        alias.DeletedByPersonId = before.Deleted ? alias.DeletedByPersonId ?? personId : null;
                        alias.LastModifiedDate = DateTime.UtcNow;
                        alias.LastModifiedByPersonId = personId;
                    }
                    _db.AuditLogEntries.Remove(await _db.AuditLogEntries.SingleAsync(a => a.Id == entry.Id, cancellationToken));
                    await _db.SaveChangesAsync(cancellationToken);
                }
                if (!apply) _db.ChangeTracker.Clear();

                var removed = new HashSet<long>();
                foreach (var created in foodEntries.Where(a => a.PropertyName == CreatedProperty))
                {
                    var id = created.EntityId;
                    var lines = await _db.RecipeIngredients.IgnoreQueryFilters().CountAsync(ri => ri.IngredientId == id, cancellationToken);
                    var empty = lines == (apply ? 0 : restoredLines.GetValueOrDefault(id))
                        && !await _db.FoodCatalogProposals.IgnoreQueryFilters().AnyAsync(p => p.IngredientId == id, cancellationToken)
                        && !await _db.IngredientAliases.IgnoreQueryFilters().AnyAsync(a => a.IngredientId == id, cancellationToken)
                        && !await _db.IngredientNutrients.IgnoreQueryFilters().AnyAsync(n => n.IngredientId == id, cancellationToken)
                        && !await _db.IngredientWordings.IgnoreQueryFilters().AnyAsync(w => w.IngredientId == id, cancellationToken);
                    var food = await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
                    if (!empty || food == null || (apply && !await RemoveFoodAsync(id, tx, cancellationToken)))
                    {
                        result.FoodsKept++;
                        continue;
                    }
                    removed.Add(id);
                    result.FoodsRemoved++;
                    if (!apply) continue;
                    _db.AuditLogEntries.Remove(await _db.AuditLogEntries.SingleAsync(a => a.Id == created.Id, cancellationToken));
                    await _db.SaveChangesAsync(cancellationToken);
                }

                foreach (var freed in foodEntries.Where(a => a.PropertyName == nameof(IngredientEntity.Name)).OrderByDescending(a => a.Id))
                {
                    var holder = await _db.Ingredients.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == freed.EntityId, cancellationToken);
                    var others = holder == null || freed.OldValue == null ? new List<long>()
                        : await _db.Ingredients.IgnoreQueryFilters().Where(i => i.Id != holder.Id && i.Name == freed.OldValue).Select(i => i.Id).ToListAsync(cancellationToken);
                    var leftovers = await LeftoverFoodsAsync(others.Where(id => !removed.Contains(id)).ToList(), stamp, cancellationToken);
                    if (holder == null || holder.Name != freed.NewValue || freed.OldValue == null || leftovers == null)
                    {
                        result.Skipped++;
                        continue;
                    }
                    if (apply && leftovers.Count > 0)
                    {
                        var kept = false;
                        foreach (var id in leftovers) kept |= !await RemoveFoodAsync(id, tx, cancellationToken);
                        if (kept)
                        {
                            result.Skipped++;
                            continue;
                        }
                        holder = await _db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == freed.EntityId, cancellationToken);
                    }
                    result.FoodsRemoved += leftovers.Count;
                    foreach (var id in leftovers) removed.Add(id);
                    result.NamesRestored++;
                    if (!apply) continue;
                    holder.Name = freed.OldValue;
                    _db.AuditLogEntries.Remove(await _db.AuditLogEntries.SingleAsync(a => a.Id == freed.Id, cancellationToken));
                    await _db.SaveChangesAsync(cancellationToken);
                }

                if (apply)
                {
                    _db.AuditLogEntries.Add(UndoEntry(header?.PropertyName ?? "unknown", stamp, result, personId));
                    await _db.SaveChangesAsync(cancellationToken);
                    if (tx != null) await tx.CommitAsync(cancellationToken);
                }
            }
            _db.ChangeTracker.Clear();
            if (apply)
                _logger.LogInformation("Link re-check undo of {Set} {Stamp:O}: {Lines} lines back, {Rebuilt} left on rebuilt recipes, {Aliases} aliases, {Foods} created foods and {Names} names taken back",
                    result.Set, stamp, result.LinesRestored, result.SkippedRebuilt, result.AliasesRestored, result.FoodsRemoved, result.NamesRestored);
            return result;
        }

        /// <summary>
        /// The foods that hold a name the run freed and may be deleted so the name comes back: each an
        /// uncurated food with no FDC id created within a day after the stamp (a wording food the run
        /// created) that an earlier undo soft-deleted and that holds nothing, as the undo of
        /// link-recheck/1 left them; null when any other food holds the name.
        /// </summary>
        private async Task<List<long>?> LeftoverFoodsAsync(List<long> ids, DateTime stamp, CancellationToken cancellationToken)
        {
            var leftovers = new List<long>();
            foreach (var id in ids)
            {
                var food = await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == id).Select(i => new { i.IsDeleted, i.CreatedDate, i.FdcId, i.CurationStatusId }).FirstOrDefaultAsync(cancellationToken);
                if (food is not { IsDeleted: true, FdcId: null } || food.CurationStatusId != (long)CurationStatusEnum.NonCurated || food.CreatedDate < stamp || food.CreatedDate >= stamp.AddDays(1)
                    || await _db.RecipeIngredients.IgnoreQueryFilters().AnyAsync(ri => ri.IngredientId == id, cancellationToken)
                    || await _db.FoodCatalogProposals.IgnoreQueryFilters().AnyAsync(p => p.IngredientId == id, cancellationToken)
                    || await _db.IngredientAliases.IgnoreQueryFilters().AnyAsync(a => a.IngredientId == id, cancellationToken)
                    || await _db.IngredientNutrients.IgnoreQueryFilters().AnyAsync(n => n.IngredientId == id, cancellationToken)
                    || await _db.IngredientWordings.IgnoreQueryFilters().AnyAsync(w => w.IngredientId == id, cancellationToken))
                    return null;
                leftovers.Add(id);
            }
            return leftovers;
        }

        /// <summary>
        /// Deletes a food the re-check created and that holds nothing again, as a row and not a
        /// soft delete, so its name is free for the name the run freed; under a savepoint on a
        /// relational database, so a row elsewhere that still points at it keeps it (false) without
        /// ending the undo's transaction.
        /// </summary>
        private async Task<bool> RemoveFoodAsync(long id, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx, CancellationToken cancellationToken)
        {
            if (!_db.Database.IsRelational())
            {
                var food = await _db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == id, cancellationToken);
                await _db.HardDeleteAsync(new[] { food }, cancellationToken);
                return true;
            }
            const string savepoint = "recheck_food";
            if (tx != null) await tx.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                await _db.Ingredients.IgnoreQueryFilters().Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
                if (tx != null) await tx.ReleaseSavepointAsync(savepoint, cancellationToken);
                return true;
            }
            catch (Exception ex) when (ex is DbUpdateException || ex.GetType().Name == "PostgresException")
            {
                if (tx != null) await tx.RollbackToSavepointAsync(savepoint, cancellationToken);
                _logger.LogWarning(ex, "Link re-check undo: created food #{Id} is still referenced and is kept", id);
                return false;
            }
        }

        public async Task<LinkRecheckNameResult> CheckNamesAsync(bool apply = false, CancellationToken cancellationToken = default)
        {
            var foods = await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated && i.FdcId != null)
                .Select(i => new
                {
                    i.Id, i.Name, i.FdcId,
                    Description = i.Aliases.Where(a => !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext).Select(a => a.AliasName).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);
            var result = new LinkRecheckNameResult { Applied = apply };
            foreach (var food in foods.Where(f => f.Description != null).OrderBy(f => f.Id))
            {
                var fix = LinkRecheckRules.NameFix(food.FdcId, food.Name);
                if (!LinkRecheckRules.NameContradicts(food.Name, food.Description) && fix == null) continue;
                result.Listed.Add(new LinkRecheckNameRow { IngredientId = food.Id, Name = food.Name, Description = food.Description!, FdcId = food.FdcId, NewName = fix });
            }
            if (!apply || result.Listed.All(r => r.NewName == null)) return result;

            var stamp = NewStamp();
            var summary = new LinkRecheckRunSummary { Set = NamesRun, Stamp = stamp, Signature = LinkRecheckRules.Signature };
            using var running = MarkRunning(stamp);
            var header = await StartAsync(NamesRun, stamp, summary, _currentUser.PersonIdOrSystem, cancellationToken);
            foreach (var row in result.Listed.Where(r => r.NewName != null))
            {
                var renamed = await _relink.RenameAsync(row.IngredientId, row.NewName!, stamp);
                row.Renamed = renamed?.Renamed == true;
                if (row.Renamed) result.Renamed++;
            }
            result.Stamp = stamp;
            summary.Renamed = result.Renamed;
            await FinishAsync(header, summary, cancellationToken);
            return result;
        }

        public async Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string set, IReadOnlyList<string> faults, ILaneAuditor? auditor, ILaneAuditCheck? check, CancellationToken cancellationToken = default)
        {
            var reasons = faults.ToList();
            if (auditor != null && check != null)
            {
                try
                {
                    var audit = await _frame.AuditAsync(Lane, set, DateTime.UtcNow - DecidedFor, Sample, auditor, check, null, cancellationToken);
                    if (audit.Sampled >= 10 && audit.Unreadable * 5 > audit.Sampled)
                        reasons.Add($"the link auditor {auditor.Model} gave no readable answer on {audit.Unreadable} of {audit.Sampled} sampled decisions");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Link re-check {Set}: the audit could not run", set);
                    reasons.Add($"the link auditor {auditor.Model} could not be reached: {ex.GetType().Name}: {ex.Message}");
                }
            }

            var (score, golden) = auditor != null && check is ILaneAuditor confirming ? WordingTierAuditRecord.Evaluate(_auditRecord(), auditor.Model, confirming.Model) : (null, AuditorProblem);
            return await _frame.EvaluateAsync(new LaneEvaluateRequest(Lane, set, LinkRecheckRules.TierSignature(set), Policy, score, golden, reasons), cancellationToken);
        }

        public Task<int> DecidedTodayAsync(string set, CancellationToken cancellationToken = default)
        {
            var since = DateTime.UtcNow.AddHours(-24);
            return _db.LaneDecisions.AsNoTracking().CountAsync(d => d.Lane == Lane && d.CreatedDate >= since
                && (d.Tier == set || (d.Tier.StartsWith(LinkRecheckRules.CandidateTierPrefix) && d.Stratum == set)), cancellationToken);
        }

        public async Task<LinkRecheckStatusModel> StatusAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            var status = new LinkRecheckStatusModel
            {
                Enabled = enabled,
                Version = LinkRecheckRules.Version,
                Signature = LinkRecheckRules.Signature,
                Sets = LinkRecheckRules.AllSets.ToList(),
                ProvenClasses = LinkRecheckRules.ProvenClasses.OrderBy(c => c, StringComparer.Ordinal).ToList(),
                CandidateClasses = LinkRecheckRules.CandidateClasses.ToList(),
                ProvenProblem = LinkRecheckRules.ProvenProblem,
                WaitingPicks = await _db.FoodCatalogProposals.AsNoTracking().CountAsync(WaitingModelPick, cancellationToken),
            };
            var undone = (await _db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == EntityType && a.ChangeType == UndoChangeType)
                    .Select(a => a.OldValue).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var set in LinkRecheckRules.AllSets.Concat(LinkRecheckRules.CandidateClasses.Select(LinkRecheckRules.CandidateTier)).Append(WithdrawRun).Append(NamesRun))
            {
                var last = await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == EntityType && a.ChangeType == RunChangeType && a.PropertyName == set)
                    .OrderByDescending(a => a.Id)
                    .Select(a => new { a.OldValue, a.NewValue })
                    .FirstOrDefaultAsync(cancellationToken);
                if (LaneJournal.Read<LinkRecheckRunSummary>(last?.NewValue) is not { } summary) continue;
                summary.Undone = last!.OldValue != null && undone.Contains(last.OldValue);
                summary.Running = !summary.Complete && last.OldValue != null && RunningStamps.ContainsKey(last.OldValue);
                if (set == WithdrawRun) status.LastWithdraw = summary;
                else status.LastRuns.Add(summary);
            }
            return status;
        }

        private static string? Clip(string? value, int length) => value == null || value.Length <= length ? value : value[..length];
    }

    /// <summary>
    /// The link re-check's undo for the lane frame: takes back the lane's own runs of a set since a
    /// time (never an operator's), newest first, each by its stamp, skipping lines on recipes
    /// rebuilt since.
    /// </summary>
    public sealed class LinkRecheckLaneUndo : ILaneUndo
    {
        private readonly ILinkRecheckService _recheck;
        private readonly ApplicationDbContext _db;

        public LinkRecheckLaneUndo(ILinkRecheckService recheck, ApplicationDbContext db)
        {
            _recheck = recheck;
            _db = db;
        }

        public string Lane => LinkRecheckService.Lane;

        public async Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken)
        {
            var headers = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == LinkRecheckService.EntityType && a.ChangeType == LinkRecheckService.RunChangeType && a.Timestamp >= sinceUtc
                    && (tier == LaneTiers.Whole || a.PropertyName == tier))
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.Timestamp, a.OldValue, a.NewValue })
                .ToListAsync(cancellationToken);
            var undone = (await _db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == LinkRecheckService.EntityType && a.ChangeType == LinkRecheckService.UndoChangeType)
                    .Select(a => a.OldValue).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            int restored = 0, skipped = 0;
            foreach (var header in headers)
            {
                if (LaneJournal.Read<LinkRecheckRunSummary>(header.NewValue) is not { Lane: true, Mode: LaneMode.Live } summary
                    || (summary.Complete && summary.LinesMoved + summary.LinesUnlinked + summary.AliasesChanged == 0)) continue;
                if (header.OldValue != null && undone.Contains(header.OldValue)) continue;
                var result = await _recheck.UndoAsync(header.Timestamp, apply, cancellationToken);
                restored += apply ? result.LinesRestored + result.AliasesRestored : result.LineEntries + result.AliasEntries;
                skipped += apply ? result.Skipped + result.SkippedRebuilt : result.OnRebuiltRecipes;
            }
            return new LaneUndoResult(restored, skipped, 0, $"link-recheck {tier}: the lane's own runs since {sinceUtc:u}");
        }
    }
}
