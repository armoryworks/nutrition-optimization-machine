using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The container size lane. Sizes come only from deterministic sources (a USDA portion on the
    /// food, the branded mode, the cited can table, a standard of fill, and for old and undated
    /// books the sized lines of the same book or era, <see cref="ContainerCorpusSizes"/>) and are
    /// written by <c>Nom.Import --container-sizes</c> (and <c>--corpus</c>) in shadow. Lines of old
    /// and undated books are scored apart, under the old-book tier that sized them
    /// (<see cref="OldBookTiers"/>), a corpus tier with the line left out. The hold-out scorer hides the size each
    /// sized container line states, asks which size the lane would assume for that food, kind and
    /// era, and sets each key active, shadow or paused by <see cref="ContainerSizeQualifier"/>; the
    /// whole lane runs on the lane frame (lane container-sizes, tier *), whose decisions are the
    /// hold-out comparisons, audited by the rule itself. A row is read for weighing only when it is
    /// active and the lane is live. Every change is one audit.AuditLogEntry and can be undone.
    /// </summary>
    public class ContainerSizeService : IContainerSizeService
    {
        public const string Lane = AiLanes.ContainerSizes;

        /// <summary>The old-book tier of lines sized by the same book's sized lines of the food and container.</summary>
        public const string SameBookTier = "same-book";

        /// <summary>The old-book tier of lines sized by the sized lines of the books of the same era.</summary>
        public const string SameEraTier = "same-era";

        /// <summary>The old-book tier of lines sized by a cited period source (a historic size for an undated book, a period candidate can).</summary>
        public const string PeriodTier = "period";

        /// <summary>The old-book tier of lines sized by today's most common size, the last tier.</summary>
        public const string ModernTier = "modern-fallback";

        /// <summary>The tiers an old or undated book's unsized line is weighed under, in the order they are tried (<see cref="ContainerBasis.OldBookTier"/>); each qualifies and pauses on its own hold-out with <see cref="Policy"/>.</summary>
        public static readonly IReadOnlyList<string> OldBookTiers = new[] { SameBookTier, SameEraTier, PeriodTier, ModernTier };

        public const string RuleVersion = "container-sizes/2";
        public const string EntityType = "IngredientContainerSize";
        public const string SetChange = "ContainerSizeSet";
        public const string RetireChange = "ContainerSizeRetire";
        public const string StatusChange = "ContainerSizeStatus";
        public const string PauseChange = "ContainerSizePause";
        public const string UndoChange = "ContainerSizeUndo";

        /// <summary>Qualifies at a Wilson lower bound of 93 % and pauses when a full window of 200 hold-out lines agrees under 92 %.</summary>
        public static readonly LanePolicy Policy = new(QualifyBar: 0.93, PauseBar: 0.08, PauseWindow: 200);

        private static readonly LaneSamplePlan AuditEverything = new(Minimum: int.MaxValue, Rate: 1.0);

        private readonly ApplicationDbContext _db;
        private readonly ILaneFrameService _frame;
        private readonly ILogger<ContainerSizeService> _logger;

        public ContainerSizeService(ApplicationDbContext db, ILaneFrameService frame, ILogger<ContainerSizeService> logger)
        {
            _db = db;
            _frame = frame;
            _logger = logger;
        }

        /// <summary>A row as a journal entry keeps it.</summary>
        public sealed record RowValue(long IngredientId, string Kind, string Era, string SizeWord, decimal NetGrams, decimal? DrainedGrams, string SourceRef, string Status, long? Replaces = null, string? Book = null);

        /// <summary>An undo entry: the entry it reverses and the value it restored.</summary>
        public sealed record UndoValue(long Undoes, string RestoredStatus);

        /// <summary>The signals of one hold-out decision.</summary>
        public sealed record HoldoutSignals(long RecipeId, long IngredientId, int SortOrder, string Line, string Era, decimal Stated, decimal Assumed, long RowId, string Class, bool Hit);

        public static string Key(long ingredientId, string kind, string era, string sizeWord, string? book = null) =>
            string.Create(CultureInfo.InvariantCulture, $"{ingredientId}/{kind}/{era}/{(sizeWord.Length == 0 ? "-" : sizeWord)}")
            + (book == null ? string.Empty : "/book:" + ContainerCorpusSizes.BookToken(book));

        private static string Key(IngredientContainerSizeEntity r) => Key(r.IngredientId, r.ContainerKind, r.Era, r.SizeWord, r.SourceAttribution);

        private static RowValue ValueOf(IngredientContainerSizeEntity r, long? replaces = null) =>
            new(r.IngredientId, r.ContainerKind, r.Era, r.SizeWord, r.NetGrams, r.DrainedGrams, r.SourceRef, r.Status, replaces, r.SourceAttribution);

        private static AuditLogEntryEntity Entry(long rowId, string changeType, string key, string? oldValue, string? newValue, DateTime at, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = rowId,
            ChangeType = changeType,
            PropertyName = key,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        public async Task<ContainerSizeWriteReport> WriteAsync(IReadOnlyList<ContainerSizeProposal> proposals, bool dryRun, DateTime stamp, bool retireMissing, long personId, IReadOnlyCollection<string>? unreadSources = null, CancellationToken cancellationToken = default)
        {
            var report = new ContainerSizeWriteReport { DryRun = dryRun, Stamp = stamp, Proposed = proposals.Count };
            var standing = await _db.IngredientContainerSizes.Where(r => r.Status != ContainerSizeStatus.Retired).ToListAsync(cancellationToken);
            var byKey = standing.GroupBy(Key).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).ToList());
            var proposed = new HashSet<string>(StringComparer.Ordinal);
            var pending = new List<(IngredientContainerSizeEntity Row, string Key, long? Replaces)>();

            foreach (var p in proposals.OrderBy(p => p.IngredientId).ThenBy(p => p.Kind, StringComparer.Ordinal).ThenBy(p => p.Era, StringComparer.Ordinal).ThenBy(p => p.SizeWord, StringComparer.Ordinal).ThenBy(p => p.Book, StringComparer.Ordinal))
            {
                var key = Key(p.IngredientId, p.Kind, p.Era, p.SizeWord, p.Book);
                if (!proposed.Add(key))
                {
                    report.Lines.Add($"skipped {key}: proposed twice");
                    continue;
                }
                var old = byKey.GetValueOrDefault(key)?.FirstOrDefault();
                if (old != null && old.NetGrams == Math.Round(p.NetGrams, 2) && old.DrainedGrams == (p.DrainedGrams is { } d ? Math.Round(d, 2) : null) && old.SourceRef == p.SourceRef)
                {
                    report.Unchanged++;
                    continue;
                }

                var row = new IngredientContainerSizeEntity
                {
                    IngredientId = p.IngredientId,
                    ContainerKind = p.Kind,
                    Era = p.Era,
                    SizeWord = p.SizeWord,
                    NetGrams = Math.Round(p.NetGrams, 2),
                    DrainedGrams = p.DrainedGrams is { } dg ? Math.Round(dg, 2) : null,
                    SizeText = Clip(p.SizeText, 255),
                    SourceRef = Clip(p.SourceRef, 255),
                    Class = Clip(p.Class, 64),
                    SourcesAgreeing = p.SourcesAgreeing,
                    ModeShare = p.ModeShare is { } s ? Math.Round(s, 4) : null,
                    Products = p.Products,
                    SourceAttribution = p.Book,
                    Evidence = p.Evidence is { } evidence ? Clip(evidence, 1023) : null,
                    Status = ContainerSizeStatus.Shadow,
                    StatusReason = "written by an import run; not scored yet",
                    CreatedDate = stamp,
                    CreatedByPersonId = personId,
                };
                if (old != null)
                {
                    report.Replaced++;
                    report.Lines.Add(string.Create(CultureInfo.InvariantCulture, $"replace {key}: {old.NetGrams} g ({old.SourceRef}) -> {row.NetGrams} g ({row.SourceRef})"));
                    if (!dryRun)
                    {
                        _db.AuditLogEntries.Add(Entry(old.Id, RetireChange, key, LaneJournal.Write(ValueOf(old)), LaneJournal.Write(ValueOf(old) with { Status = ContainerSizeStatus.Retired }), stamp, personId));
                        old.Status = ContainerSizeStatus.Retired;
                        old.StatusReason = "replaced by an import run";
                        old.LastModifiedDate = stamp;
                        old.LastModifiedByPersonId = personId;
                    }
                }
                else
                {
                    report.Added++;
                    report.AddedByClass[p.Class] = report.AddedByClass.GetValueOrDefault(p.Class) + 1;
                }
                pending.Add((row, key, old?.Id));
            }

            if (retireMissing)
            {
                var unread = new HashSet<string>(unreadSources ?? Array.Empty<string>(), StringComparer.Ordinal);
                foreach (var (key, rows) in byKey.Where(k => !proposed.Contains(k.Key)))
                {
                    foreach (var old in rows.Where(r => !unread.Contains(SourceKind(r.SourceRef))))
                    {
                        report.Retired++;
                        report.Lines.Add(string.Create(CultureInfo.InvariantCulture, $"retire {key}: {old.NetGrams} g ({old.SourceRef}), no source gives it any more"));
                        if (dryRun) continue;
                        _db.AuditLogEntries.Add(Entry(old.Id, RetireChange, key, LaneJournal.Write(ValueOf(old)), LaneJournal.Write(ValueOf(old) with { Status = ContainerSizeStatus.Retired }), stamp, personId));
                        old.Status = ContainerSizeStatus.Retired;
                        old.StatusReason = "no source gives it any more";
                        old.LastModifiedDate = stamp;
                        old.LastModifiedByPersonId = personId;
                    }
                }
            }

            if (dryRun) return report;
            _db.IngredientContainerSizes.AddRange(pending.Select(p => p.Row));
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var (row, key, replaces) in pending)
                _db.AuditLogEntries.Add(Entry(row.Id, SetChange, key, null, LaneJournal.Write(ValueOf(row, replaces)), stamp, personId));
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Container sizes written: {Added} added, {Replaced} replaced, {Retired} retired, {Unchanged} unchanged (stamp {Stamp:O})",
                report.Added, report.Replaced, report.Retired, report.Unchanged, stamp);
            return report;
        }

        public async Task<ContainerSizeScoreReport> ScoreAsync(bool apply, long personId, IReadOnlyList<ContainerSizeProposal>? proposals = null, CancellationToken cancellationToken = default)
        {
            apply &= proposals == null;
            var report = new ContainerSizeScoreReport { Applied = apply };
            var rows = proposals == null
                ? await _db.IngredientContainerSizes.Where(r => r.Status != ContainerSizeStatus.Retired).ToListAsync(cancellationToken)
                : proposals.Select((p, i) => new IngredientContainerSizeEntity
                {
                    Id = -(i + 1), IngredientId = p.IngredientId, ContainerKind = p.Kind, Era = p.Era, SizeWord = p.SizeWord,
                    NetGrams = Math.Round(p.NetGrams, 2), DrainedGrams = p.DrainedGrams, SizeText = p.SizeText, SourceRef = p.SourceRef,
                    Class = p.Class, Status = ContainerSizeStatus.Shadow, SourceAttribution = p.Book, Evidence = p.Evidence,
                }).ToList();
            report.Rows = rows.Count;
            report.Signature = Signature(rows);
            var byFood = rows.GroupBy(r => r.IngredientId).ToDictionary(g => g.Key, g => g.ToList());
            var byId = rows.ToDictionary(r => r.Id);
            var foodIds = byFood.Keys.ToList();
            var bookRows = rows.Where(r => ContainerCorpusSizes.TierOf(r.SourceRef) == ContainerCorpusSizes.BookTier && r.SourceAttribution != null)
                .GroupBy(r => (r.SourceAttribution!, r.IngredientId, r.ContainerKind, r.SizeWord)).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());
            var eraRows = rows.Where(r => ContainerCorpusSizes.TierOf(r.SourceRef) == ContainerCorpusSizes.EraTier)
                .GroupBy(r => (r.Era, r.IngredientId, r.ContainerKind, r.SizeWord)).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());

            var lines = await HoldoutLinesAsync(foodIds, cancellationToken);
            var corpusOf = new Dictionary<HoldoutLine, CorpusLine>();
            foreach (var line in lines)
            {
                if (ContainerCorpusSizes.Read(line.Book, line.Era, line.IngredientId, line.RawLine, line.GramsPerMilliliter) is { } read) corpusOf[line] = read;
            }
            var corpusByKey = corpusOf.Values.GroupBy(c => (c.IngredientId, c.Kind, c.SizeWord)).ToDictionary(g => g.Key, g => g.ToList());

            var judged = new List<HoldoutSignals>();
            var byTier = OldBookTiers.ToDictionary(t => t, _ => new List<HoldoutSignals>(), StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (ContainerSizeResolver.StatedPerContainer(line.RawLine, line.GramsPerMilliliter) is not { Reason: null } stated) continue;
                var candidates = byFood[line.IngredientId].Where(r => ContainerCorpusSizes.TierOf(r.SourceRef) == null)
                    .Select(r => new ContainerSizeCandidate(r.ContainerKind, r.Era, r.SizeWord, r.NetGrams, r.DrainedGrams, r.SourceRef, r.Status, r.Id))
                    .ToList();
                if (corpusOf.TryGetValue(line, out var own))
                {
                    var group = corpusByKey[(own.IngredientId, own.Kind, own.SizeWord)];
                    if (bookRows.TryGetValue((own.Book, own.IngredientId, own.Kind, own.SizeWord), out var bookRow)
                        && ContainerCorpusSizes.LeaveOut(ContainerCorpusSizes.BookTier, own, group) is { } fromBook)
                        candidates.Add(new ContainerSizeCandidate(own.Kind, bookRow.Era, own.SizeWord, fromBook.Grams, null, bookRow.SourceRef, bookRow.Status, bookRow.Id, own.Book));
                    if (eraRows.TryGetValue((own.Era, own.IngredientId, own.Kind, own.SizeWord), out var eraRow)
                        && ContainerCorpusSizes.LeaveOut(ContainerCorpusSizes.EraTier, own, group) is { } fromEra)
                        candidates.Add(new ContainerSizeCandidate(own.Kind, eraRow.Era, own.SizeWord, fromEra.Grams, null, eraRow.SourceRef, eraRow.Status, eraRow.Id));
                }
                var assumed = ContainerSizeResolver.Resolve(line.RawLine, line.Era, candidates, null, line.GramsPerMilliliter, hideStatedSize: true, activeOnly: false, netOnly: true,
                    usdaCategoryId: line.UsdaCategoryId, book: line.Book);
                if (assumed.GramsPerContainer is not { } grams || assumed.RowId == 0)
                {
                    report.LinesWithoutKey++;
                    continue;
                }
                var row = byId[assumed.RowId];
                var signals = new HoldoutSignals(line.RecipeId, line.IngredientId, line.SortOrder, line.RawLine, line.Era, stated.Grams, grams, row.Id, row.Class,
                    ContainerSizeAgreement.Hit(grams, stated.Grams));
                if (ContainerBasis.OldBookTier(assumed.Basis, line.Era) is { } tier) byTier[tier].Add(signals);
                else judged.Add(signals);
            }
            report.OtherEraLines = byTier.Values.Sum(t => t.Count);
            report.OtherEraAgree = byTier.Values.Sum(t => t.Count(j => j.Hit));
            report.HoldoutLines = judged.Count;
            report.HoldoutAgree = judged.Count(j => j.Hit);

            var at = lines.ToDictionary(l => (l.RecipeId, l.IngredientId, l.SortOrder), l => l.CreatedDate);
            ContainerHoldoutLine Holdout(HoldoutSignals j) => new(j.Hit, at[(j.RecipeId, j.IngredientId, j.SortOrder)]);
            var keyedLines = judged.Concat(byTier[SameBookTier]).Concat(byTier[SameEraTier]).ToList();
            var byRow = keyedLines.GroupBy(j => j.RowId).ToDictionary(g => g.Key, g => g.Select(Holdout).ToList());
            var byClass = keyedLines.GroupBy(j => j.Class).ToDictionary(g => g.Key, g => g.Select(Holdout).ToList());

            var now = DateTime.UtcNow;
            var changes = new List<(IngredientContainerSizeEntity Row, string Old, ContainerKeyVerdict Verdict)>();
            foreach (var row in rows)
            {
                var verdict = ContainerSizeQualifier.Decide(row.Status, byRow.GetValueOrDefault(row.Id) ?? new List<ContainerHoldoutLine>(),
                    byClass.GetValueOrDefault(row.Class) ?? new List<ContainerHoldoutLine>(), sourcePasses: true);
                if (verdict.Status != row.Status)
                {
                    changes.Add((row, row.Status, verdict));
                    report.Changes.Add($"{Key(row)} ({row.Class}): {row.Status} -> {verdict.Status}: {verdict.Reason}");
                }
                report.ByStatus[verdict.Status] = report.ByStatus.GetValueOrDefault(verdict.Status) + 1;
                if (!apply) continue;
                row.HoldoutLines = verdict.Lines;
                row.HoldoutAgree = verdict.Agree;
                row.StatusReason = Clip(verdict.Reason, 1023);
                row.ScoredAt = now;
            }

            var changed = changes.ToDictionary(c => c.Row.Id, c => c.Verdict.Status);
            foreach (var cls in rows.GroupBy(r => r.Class).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var holdout = byClass.GetValueOrDefault(cls.Key) ?? new List<ContainerHoldoutLine>();
                var verdicts = cls.Select(r => changed.TryGetValue(r.Id, out var v) ? v : r.Status).ToList();
                report.Classes.Add(new ContainerSizeClassFigures(cls.Key, holdout.Count, holdout.Count(h => h.Hit),
                    holdout.Count == 0 ? 0 : (double)holdout.Count(h => h.Hit) / holdout.Count, cls.Count(),
                    verdicts.Count(s => s == ContainerSizeStatus.Active), verdicts.Count(s => s == ContainerSizeStatus.Shadow), verdicts.Count(s => s == ContainerSizeStatus.Paused)));
            }

            if (!apply)
            {
                report.Tiers.AddRange(OldBookTiers.Select(t => new ContainerSizeTierFigures(t, byTier[t].Count, byTier[t].Count(j => j.Hit), null, null)));
                return report;
            }
            foreach (var (row, old, verdict) in changes)
            {
                var before = ValueOf(row) with { Status = old };
                row.Status = verdict.Status;
                row.LastModifiedDate = now;
                row.LastModifiedByPersonId = personId;
                _db.AuditLogEntries.Add(Entry(row.Id, verdict.Status == ContainerSizeStatus.Paused ? PauseChange : StatusChange,
                    Key(row), LaneJournal.Write(before), LaneJournal.Write(ValueOf(row)), now, personId));
                if (verdict.Status == ContainerSizeStatus.Paused)
                    _logger.LogWarning("Container size {Key} paused: {Reason}", Key(row), verdict.Reason);
            }
            await _db.SaveChangesAsync(cancellationToken);

            var mode = await _frame.ModeAsync(Lane, LaneTiers.Whole, cancellationToken);
            var seen = (await _db.LaneDecisions.AsNoTracking()
                    .Where(d => d.Lane == Lane && d.SignatureHash == report.Signature)
                    .Select(d => d.Tier + "|" + d.Subject)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            await RecordAsync(LaneTiers.Whole, judged, mode, seen);
            foreach (var tier in OldBookTiers)
                await RecordAsync(tier, byTier[tier], await _frame.ModeAsync(Lane, tier, cancellationToken), seen);
            await _frame.AuditAsync(Lane, LaneTiers.Whole, DateTime.UtcNow.AddDays(-400), AuditEverything, new HoldoutRule(), new HoldoutRule(), null, cancellationToken);
            var lane = await _frame.EvaluateAsync(new LaneEvaluateRequest(Lane, LaneTiers.Whole, report.Signature, Policy, AuditorScore.ExactRule), cancellationToken);
            report.LaneMode = lane.Mode;
            report.LaneReason = lane.Reason;
            foreach (var tier in OldBookTiers)
            {
                await _frame.AuditAsync(Lane, tier, DateTime.UtcNow.AddDays(-400), AuditEverything, new HoldoutRule(), new HoldoutRule(), null, cancellationToken);
                var verdict = await _frame.EvaluateAsync(new LaneEvaluateRequest(Lane, tier, report.Signature, Policy, AuditorScore.ExactRule), cancellationToken);
                report.Tiers.Add(new ContainerSizeTierFigures(tier, byTier[tier].Count, byTier[tier].Count(j => j.Hit), verdict.Mode, verdict.Reason));
            }
            _logger.LogInformation("Container sizes scored: {Agree}/{Lines} hold-out lines agree; {Changes} status changes; lane {Mode} ({Reason}); old-book tiers {Tiers}",
                report.HoldoutAgree, report.HoldoutLines, changes.Count, lane.Mode, lane.Reason,
                string.Join("; ", report.Tiers.Select(t => $"{t.Tier} {t.Agree}/{t.Lines} {t.Mode}")));
            return report;

            async Task RecordAsync(string tier, IEnumerable<HoldoutSignals> decisions, string tierMode, HashSet<string> recorded)
            {
                foreach (var j in decisions.OrderBy(j => at[(j.RecipeId, j.IngredientId, j.SortOrder)]))
                {
                    var subject = string.Create(CultureInfo.InvariantCulture, $"line:{j.RecipeId}:{j.IngredientId}:{j.SortOrder}");
                    if (!recorded.Add(tier + "|" + subject)) continue;
                    await _frame.RecordAsync(new LaneDecisionInput(Lane, tier, subject,
                        string.Create(CultureInfo.InvariantCulture, $"assume {j.Assumed} g, states {j.Stated} g"),
                        tierMode, true, report.Signature, j.Class, j), cancellationToken);
                    report.DecisionsRecorded++;
                }
            }
        }

        /// <summary>A line in a weight or volume unit on one of the foods, with what the hold-out reads of it and of its recipe.</summary>
        public sealed record HoldoutRow(long RecipeId, long IngredientId, int SortOrder, string RawLine, DateTime CreatedDate, decimal? GramsPerMilliliter, string? SourceSite, string? SourceAttribution, int? UsdaCategoryId = null);

        /// <summary>The lines stated in a weight or volume unit on the given foods, in live recipes; the container word is read after.</summary>
        public static IQueryable<HoldoutRow> HoldoutQuery(ApplicationDbContext db, IReadOnlyCollection<long> foodIds) =>
            db.RecipeIngredients.AsNoTracking()
                .Where(ri => foodIds.Contains(ri.IngredientId) && !ri.Recipe.IsDeleted && ri.Measurement != null
                    && (ri.Measurement.MeasurementCategoryId == RecipeNutritionService.CategoryMass || ri.Measurement.MeasurementCategoryId == RecipeNutritionService.CategoryVolume))
                .Select(ri => new HoldoutRow(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine, ri.CreatedDate, ri.Ingredient.GramsPerMilliliter, ri.Recipe.SourceSite, ri.Recipe.SourceAttribution, ri.Ingredient.FdcCategoryId));

        /// <summary>The standing container sizes with their food's name, for the Pipeline block.</summary>
        public static IQueryable<StatusRow> StatusQuery(ApplicationDbContext db) =>
            db.IngredientContainerSizes.AsNoTracking()
                .Where(r => r.Status != ContainerSizeStatus.Retired)
                .Select(r => new StatusRow(r, db.Ingredients.Where(i => i.Id == r.IngredientId).Select(i => i.Name).FirstOrDefault()));

        /// <summary>A standing container size and its food's name.</summary>
        public sealed record StatusRow(IngredientContainerSizeEntity Row, string? Name);

        /// <summary>A sized container line on a food that has a size, with the era of its recipe and, for a book recipe, its book.</summary>
        public sealed record HoldoutLine(long RecipeId, long IngredientId, int SortOrder, string RawLine, DateTime CreatedDate, decimal? GramsPerMilliliter, string Era, int? UsdaCategoryId = null, string? Book = null);

        private async Task<List<HoldoutLine>> HoldoutLinesAsync(IReadOnlyList<long> foodIds, CancellationToken cancellationToken)
        {
            var result = new List<HoldoutLine>();
            if (foodIds.Count == 0) return result;
            var eras = await _db.RecipeSourceEras.AsNoTracking().ToDictionaryAsync(e => e.SourceAttribution, e => e.Era, cancellationToken);
            foreach (var chunk in foodIds.Chunk(2000))
            {
                var rows = await HoldoutQuery(_db, chunk).ToListAsync(cancellationToken);
                foreach (var r in rows)
                {
                    if (ContainerKinds.FromText(r.RawLine) == null) continue;
                    var era = ContainerEras.ForRecipe(r.SourceSite, r.SourceAttribution, eras);
                    result.Add(new HoldoutLine(r.RecipeId, r.IngredientId, r.SortOrder, r.RawLine, r.CreatedDate, r.GramsPerMilliliter, era, r.UsdaCategoryId, BookOf(r.SourceSite, r.SourceAttribution)));
                }
            }
            return result;
        }

        /// <summary>A standing row as a proposal, so a dry run of one source scores it beside the other sources' standing rows.</summary>
        public static ContainerSizeProposal AsProposal(IngredientContainerSizeEntity r) =>
            new(r.IngredientId, r.ContainerKind, r.Era, r.SizeWord, r.NetGrams, r.DrainedGrams, r.SizeText, r.SourceRef, r.Class, r.SourcesAgreeing, r.ModeShare, r.Products, r.SourceAttribution, r.Evidence);

        /// <summary>The book a recipe comes from: its attribution when it has no web source; null for a web recipe or one written in the app.</summary>
        public static string? BookOf(string? sourceSite, string? sourceAttribution) =>
            string.IsNullOrWhiteSpace(sourceSite) && !string.IsNullOrWhiteSpace(sourceAttribution) ? sourceAttribution : null;

        /// <summary>A hash of the rule version and every standing size, so a new import or a rule change starts the lane's count again.</summary>
        public static string Signature(IEnumerable<IngredientContainerSizeEntity> rows)
        {
            var text = new StringBuilder(RuleVersion);
            foreach (var r in rows.OrderBy(r => r.Id))
                text.Append(CultureInfo.InvariantCulture, $"|{r.Id}:{r.NetGrams}:{r.DrainedGrams}:{r.SourceRef}");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16].ToLowerInvariant();
        }

        /// <summary>The audit of a hold-out decision is the rule itself: wrong when the assumed size misses the stated one by more than 10 %.</summary>
        public sealed class HoldoutRule : ILaneAuditor, ILaneAuditCheck
        {
            public string Model => RuleVersion;

            public string Name => RuleVersion;

            public Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var s = LaneJournal.Read<HoldoutSignals>(decision.SignalsJson);
                return Task.FromResult(s == null
                    ? new LaneAuditAnswer(null, "the decision has no signals")
                    : ContainerSizeAgreement.Hit(s.Assumed, s.Stated) ? new LaneAuditAnswer(false, null)
                    : new LaneAuditAnswer(true, string.Create(CultureInfo.InvariantCulture, $"assumed {s.Assumed} g, the line states {s.Stated} g")));
            }

            public Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var s = LaneJournal.Read<HoldoutSignals>(decision.SignalsJson);
                return Task.FromResult<bool?>(s == null ? null : !ContainerSizeAgreement.Hit(s.Assumed, s.Stated));
            }
        }

        public async Task<ContainerSizeUndoReport> UndoSinceAsync(DateTime sinceUtc, bool apply, long personId, bool statusOnly = false, CancellationToken cancellationToken = default)
        {
            var report = new ContainerSizeUndoReport { DryRun = !apply, SinceUtc = sinceUtc };
            var types = statusOnly ? new[] { StatusChange } : new[] { SetChange, RetireChange, StatusChange, PauseChange };
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == EntityType && types.Contains(a.ChangeType) && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == EntityType && a.ChangeType == UndoChange && a.Timestamp >= sinceUtc)
                    .Select(a => a.NewValue)
                    .ToListAsync(cancellationToken))
                .Select(v => LaneJournal.Read<UndoValue>(v)?.Undoes ?? 0)
                .ToHashSet();
            var ids = entries.Select(e => e.EntityId).Distinct().ToList();
            var rows = await _db.IngredientContainerSizes.IgnoreQueryFilters().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
            var stamp = DateTime.UtcNow;
            var state = rows.Values.ToDictionary(r => r.Id, r => (r.Status, r.IsDeleted));

            foreach (var entry in entries.Where(e => !undone.Contains(e.Id)))
            {
                var after = LaneJournal.Read<RowValue>(entry.NewValue);
                var before = LaneJournal.Read<RowValue>(entry.OldValue);
                if (statusOnly && after != null && after.Status != ContainerSizeStatus.Active) continue;
                if (after == null || !rows.TryGetValue(entry.EntityId, out var row))
                {
                    report.Skipped++;
                    report.Lines.Add($"skip #{entry.Id} {entry.ChangeType} {entry.PropertyName}: the row or its journal value is missing");
                    continue;
                }
                var (status, deleted) = state[row.Id];
                var unchanged = !deleted && status == after.Status && row.NetGrams == after.NetGrams && row.SourceRef == after.SourceRef;
                if (!unchanged)
                {
                    report.Skipped++;
                    report.Lines.Add($"skip #{entry.Id} {entry.ChangeType} {entry.PropertyName}: changed since ({status}{(deleted ? ", deleted" : string.Empty)})");
                    continue;
                }
                var restored = entry.ChangeType == SetChange ? ContainerSizeStatus.Retired : before?.Status ?? ContainerSizeStatus.Shadow;
                report.Undone++;
                report.Lines.Add($"undo #{entry.Id} {entry.ChangeType} {entry.PropertyName}: {status} -> {restored}{(entry.ChangeType == SetChange ? " (row removed)" : string.Empty)}");
                state[row.Id] = (restored, entry.ChangeType == SetChange);
                if (!apply) continue;
                var old = LaneJournal.Write(ValueOf(row));
                row.Status = restored;
                row.StatusReason = $"undone: {entry.ChangeType} of {entry.Timestamp:u}";
                row.LastModifiedDate = stamp;
                row.LastModifiedByPersonId = personId;
                if (entry.ChangeType == SetChange)
                {
                    row.IsDeleted = true;
                    row.DeletedAt = stamp;
                    row.DeletedByPersonId = personId;
                }
                _db.AuditLogEntries.Add(Entry(row.Id, UndoChange, entry.PropertyName ?? string.Empty, old, LaneJournal.Write(new UndoValue(entry.Id, restored)), stamp, personId));
            }

            if (apply)
            {
                await _db.SaveChangesAsync(cancellationToken);
                report.UndoStamp = stamp;
            }
            return report;
        }

        public async Task<ContainerSizeStatusModel> StatusAsync(int keys = 50, CancellationToken cancellationToken = default)
        {
            var rows = await StatusQuery(_db).ToListAsync(cancellationToken);
            var model = new ContainerSizeStatusModel
            {
                Lane = Lane,
                LaneMode = await _frame.ModeAsync(Lane, LaneTiers.Whole, cancellationToken),
                Rows = rows.Count,
                LastScoredAt = rows.Count == 0 ? null : rows.Max(r => r.Row.ScoredAt),
                HoldoutLines = rows.Sum(r => r.Row.HoldoutLines ?? 0),
                HoldoutAgree = rows.Sum(r => r.Row.HoldoutAgree ?? 0),
            };
            foreach (var g in rows.GroupBy(r => r.Row.Status)) model.ByStatus[g.Key] = g.Count();
            foreach (var g in rows.GroupBy(r => SourceKind(r.Row.SourceRef))) model.BySource[g.Key] = g.Count();
            model.Keys = rows
                .OrderBy(r => r.Row.Status == ContainerSizeStatus.Paused ? 0 : r.Row.Status == ContainerSizeStatus.Active ? 1 : 2)
                .ThenByDescending(r => r.Row.HoldoutLines ?? 0)
                .ThenBy(r => r.Row.Id)
                .Take(Math.Clamp(keys, 0, 500))
                .Select(r => new ContainerSizeKeyFigures(r.Row.Id, r.Row.IngredientId, r.Name ?? string.Empty, r.Row.ContainerKind, r.Row.Era, r.Row.SizeWord,
                    r.Row.NetGrams, r.Row.DrainedGrams, r.Row.SizeText, r.Row.SourceRef, r.Row.Class, r.Row.Status, r.Row.StatusReason, r.Row.HoldoutLines ?? 0, r.Row.HoldoutAgree ?? 0))
                .ToList();
            return model;
        }

        /// <summary>The kind of source a row came from: portion, branded-mode, can-table, standard-of-fill or other.</summary>
        public static string SourceKind(string sourceRef) =>
            sourceRef.Contains(":portion:", StringComparison.Ordinal) ? "portion"
            : sourceRef.StartsWith("deterministic:fdc-branded-mode", StringComparison.Ordinal) ? "branded-mode"
            : sourceRef.StartsWith("deterministic:can-table", StringComparison.Ordinal) ? "can-table"
            : sourceRef.StartsWith("deterministic:standard-of-fill", StringComparison.Ordinal) ? "standard-of-fill"
            : sourceRef.StartsWith(ContainerCorpusSizes.BookSourcePrefix, StringComparison.Ordinal) ? CorpusBookSource
            : sourceRef.StartsWith(ContainerCorpusSizes.EraSourcePrefix, StringComparison.Ordinal) ? CorpusEraSource
            : "other";

        public const string CorpusBookSource = "corpus-book";
        public const string CorpusEraSource = "corpus-era";

        /// <summary>The source kinds the corpus run writes; the USDA import passes them as unread, so it never retires a corpus row.</summary>
        public static readonly IReadOnlyList<string> CorpusSources = new[] { CorpusBookSource, CorpusEraSource };

        /// <summary>The source kinds the USDA import writes; the corpus run passes them as unread, so it never retires an import's row.</summary>
        public static readonly IReadOnlyList<string> ImportSources = new[] { "portion", "branded-mode", "can-table", "standard-of-fill", "other" };

        private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];
    }

    /// <summary>
    /// The container size lane's undo for the lane frame: on a pause it takes back the scorer's
    /// status changes that made a key active since the last clean window, so no key it made active
    /// since stays active, and a key it paused or demoted stays so. Sizes an import wrote stay;
    /// they are undone by the operator's undo.
    /// </summary>
    public sealed class ContainerSizeLaneUndo : ILaneUndo
    {
        private readonly IContainerSizeService _sizes;

        public ContainerSizeLaneUndo(IContainerSizeService sizes) => _sizes = sizes;

        public string Lane => ContainerSizeService.Lane;

        public async Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken)
        {
            if (ContainerSizeService.OldBookTiers.Contains(tier))
                return new LaneUndoResult(0, 0, 0, $"container sizes, old-book tier {tier}: it writes no key; its lines stop being weighed while it is not live");
            var report = await _sizes.UndoSinceAsync(sinceUtc, apply, SystemConstants.SystemPersonId, statusOnly: true, cancellationToken);
            return new LaneUndoResult(report.Undone, report.Skipped, 0, $"container sizes: keys made active since {sinceUtc:u}");
        }
    }
}
