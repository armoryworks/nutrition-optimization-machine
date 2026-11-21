using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <inheritdoc cref="IWordingTierLaneService"/>
    public class WordingTierLaneService : IWordingTierLaneService
    {
        public const string Lane = AiLanes.WordingTiers;
        public const string AbstainTier = "abstain";
        public const string BatchEntityType = "WordingTierLane";
        public const string BatchChangeType = "WordingTierBatch";
        public const string BatchPrefix = "wt-";

        /// <summary>Lines of one wording read for the gate's unit, grams and alcohol checks.</summary>
        public const int MaxLinesPerWording = 200;

        /// <summary>Lines of one wording kept with its decision for the auditor.</summary>
        private const int SampleLines = 5;

        /// <summary>Raw lines longer than this are not read as a bare head word when the head-default table is counted.</summary>
        private const int HeadLineLength = 60;

        /// <summary>Grams on curated lines a USDA category needs before its 99th percentile bounds the gate's grams check.</summary>
        public const int MinimumBoundLines = 100;

        /// <summary>Would-be writes of a tier in one pass before the loader's refusals can count as a fault.</summary>
        public const int MinimumForRefusalShare = 20;

        public const double RefusedShareFault = 0.05;

        private static readonly TimeSpan FactsLifetime = TimeSpan.FromHours(24);
        private const string FactsKey = nameof(WordingTierLaneService) + ":facts";

        /// <summary>
        /// The verified wording, trusted alias and staple tiers are links design T1: they qualify on a
        /// Wilson lower bound of 99 % and pause when a full window shows more than 1.5 % confirmed errors.
        /// </summary>
        public static readonly LanePolicy FirstTierPolicy = new(QualifyBar: 0.99, PauseBar: 0.015, ResumeNeedsNewSignature: true);

        /// <summary>
        /// The name key, alcohol context and head default tiers are links design T2 and
        /// T3d: they qualify on 98 % and pause above 3 %. (The alcohol design's 2 % pause bar is not above
        /// the 2 % its 98 % qualify bar allows, which the frame refuses.)
        /// </summary>
        public static readonly LanePolicy SecondTierPolicy = new(QualifyBar: 0.98, PauseBar: 0.03, ResumeNeedsNewSignature: true);

        public static readonly LaneSamplePlan Sample = new(Minimum: 30, Rate: 0.25);

        public static LanePolicy Policy(string tier) =>
            tier is WordingTiers.VerifiedWording or WordingTiers.Alias or WordingTiers.Staple ? FirstTierPolicy : SecondTierPolicy;

        private readonly ApplicationDbContext _db;
        private readonly IVerifiedWordingService _wordings;
        private readonly ILaneFrameService _frame;
        private readonly IWordingTierJudges _judges;
        private readonly ICurrentUserService _currentUser;
        private readonly IMemoryCache _cache;
        private readonly ILogger<WordingTierLaneService> _logger;

        public WordingTierLaneService(ApplicationDbContext db, IVerifiedWordingService wordings, ILaneFrameService frame, IWordingTierJudges judges,
            ICurrentUserService currentUser, IMemoryCache cache, ILogger<WordingTierLaneService> logger)
        {
            _db = db;
            _wordings = wordings;
            _frame = frame;
            _judges = judges;
            _currentUser = currentUser;
            _cache = cache;
            _logger = logger;
        }

        public string Signature(string tier) => WordingTiers.Hash(string.Join('|', WordingTiers.Signature, tier, _judges.AuditorModel, _judges.CheckModel,
            OllamaIngredientLinkVerifier.CurrentPromptVersion.ToString(CultureInfo.InvariantCulture)));

        /// <summary>
        /// The evidence a row is loaded on: the wording's line count, whatever the tier or rules, so a
        /// pair a retract took back is written again by no tier until the wording has twice the lines
        /// it had when it was loaded (or an admin lifts it); see <see cref="StillRetracted"/>.
        /// </summary>
        public static string EvidenceKey(int lines) => string.Create(CultureInfo.InvariantCulture, $"{EvidencePrefix}{Math.Max(1, lines)}");

        public const string EvidencePrefix = "wording-tiers:lines-";

        /// <summary>
        /// Whether a retract on <paramref name="rejectedEvidence"/> still holds the pair for a wording
        /// that now has <paramref name="lines"/> lines: a retract with no evidence key holds for good,
        /// one on this lane's evidence until the lines reach twice the count it was loaded on, and one
        /// on another evidence key only on that same key.
        /// </summary>
        public static bool StillRetracted(string? rejectedEvidence, int lines)
        {
            if (rejectedEvidence == null || rejectedEvidence == EvidenceKey(lines)) return true;
            if (!rejectedEvidence.StartsWith(EvidencePrefix, StringComparison.Ordinal)
                || !int.TryParse(rejectedEvidence.AsSpan(EvidencePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var loaded)) return false;
            return Math.Max(1, lines) < 2L * loaded;
        }

        /// <summary>The power of two a line count falls in; a wording is asked again when it moves up one.</summary>
        public static int Band(int lines) => (int)Math.Floor(Math.Log2(Math.Max(1, lines)));

        public static string Subject(string key) => "wording:" + key;

        private sealed record Candidate(long Id, string Name, int Lines);

        private sealed record LineRow(long IngredientId, long RecipeId, string RawLine, decimal Quantity, long MeasurementId);

        /// <summary>What the lane reads from the approved recipes: the head-default table and the grams bounds by USDA category.</summary>
        internal sealed record CuratedFacts(WordingHeadDefaults HeadDefaults, IReadOnlyDictionary<int, decimal> GramBounds);

        private sealed record Pending(Candidate Candidate, string Wording, WordingTierDecision Decision, List<LineRow> Lines, string Stratum);

        public async Task<WordingTierBatchResult> RunBatchAsync(IReadOnlyDictionary<string, string> modes, int max, IReadOnlyDictionary<string, int>? remaining = null, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var batch = new WordingTierBatchResult { Stamp = now };
            foreach (var tier in WordingTiers.All) batch.Modes[tier] = modes.TryGetValue(tier, out var m) ? m : LaneMode.Shadow;
            var allowance = WordingTiers.All.ToDictionary(t => t, t => remaining != null && remaining.TryGetValue(t, out var left) ? left : int.MaxValue);
            var signatures = WordingTiers.All.ToDictionary(t => t, Signature);
            signatures[AbstainTier] = WordingTiers.Signature;

            await RecoverAsync(batch, signatures, cancellationToken);
            var candidates = await CandidatesAsync(signatures, batch.Modes, Math.Max(0, max), batch, cancellationToken);
            if (candidates.Count == 0) return batch;

            var catalog = await CatalogAsync(cancellationToken);
            var units = await _db.Measurements.AsNoTracking().ToDictionaryAsync(m => m.Id, m => (MeasurementEntity)m, cancellationToken);
            var foodIds = catalog.Foods.Keys.ToList();
            var conversions = new Dictionary<long, IngredientEntity>();
            foreach (var chunk in foodIds.Chunk(2000))
            {
                foreach (var c in await _db.Ingredients.AsNoTracking().Where(i => chunk.Contains(i.Id))
                    .Select(i => new { i.Id, i.GramsPerMilliliter, i.GramsPerItem, i.ReferenceServingGrams }).ToListAsync(cancellationToken))
                    conversions[c.Id] = new IngredientEntity { GramsPerMilliliter = c.GramsPerMilliliter, GramsPerItem = c.GramsPerItem, ReferenceServingGrams = c.ReferenceServingGrams };
            }
            var ids = candidates.Select(c => c.Id).ToList();
            var lines = (await _db.RecipeIngredients.AsNoTracking()
                    .Where(ri => ids.Contains(ri.IngredientId) && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient) && !ri.Recipe.IsDeleted)
                    .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder)
                    .Select(ri => new LineRow(ri.IngredientId, ri.RecipeId, ri.RawLine, ri.Quantity, ri.MeasurementId))
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.IngredientId)
                .ToDictionary(g => g.Key, g => g.Take(MaxLinesPerWording).ToList());
            var contexts = await ContextsAsync(candidates, lines, cancellationToken);

            var decided = new List<Pending>();
            foreach (var candidate in candidates)
            {
                var wording = IngredientNameNormalizer.WordingKey(candidate.Name);
                var own = lines.GetValueOrDefault(candidate.Id) ?? new List<LineRow>();
                var tierLines = own.Select(l => new TierLine(l.RawLine, UnitClass(l, units), contexts.GetValueOrDefault(l.RecipeId))).ToList();
                var decision = WordingTiers.Decide(wording, candidate.Id, tierLines, catalog, target => GateLines(own, conversions.GetValueOrDefault(target.Id), units, contexts));
                decided.Add(new Pending(candidate, wording, decision, own, Stratum(candidate.Lines)));
            }

            var keys = decided.Select(d => d.Wording).ToList();
            var rejections = (await _db.IngredientWordingRejections.AsNoTracking()
                    .Where(r => keys.Contains(r.Wording) && r.Verdict == IngredientWordingVerdict.Food)
                    .Select(r => new { r.Wording, r.IngredientId, r.EvidenceKey })
                    .ToListAsync(cancellationToken))
                .ToLookup(r => r.Wording, StringComparer.Ordinal);
            var family = await FamilyHoldsAsync(decided.Where(d => d.Decision.Writes).ToList(), catalog, cancellationToken);
            var personId = _currentUser.PersonIdOrSystem;
            foreach (var tierGroup in decided.Where(d => d.Decision.Answer != null).GroupBy(d => d.Decision.Answer!.Tier))
            {
                var tier = tierGroup.Key;
                var mode = batch.Modes[tier];
                var live = mode == LaneMode.Live;
                var rows = new List<(Pending Item, VerifiedWordingRow Row, string Evidence)>();
                foreach (var item in tierGroup)
                {
                    var evidence = EvidenceKey(item.Candidate.Lines);
                    var hold = item.Decision.Hold
                        ?? (rejections[item.Wording].FirstOrDefault(r => r.IngredientId == item.Decision.Answer!.FoodId && StillRetracted(r.EvidenceKey, item.Candidate.Lines)) is { } retract
                            ? $"{RetractedHold}: the pair was retracted on {retract.EvidenceKey ?? "no evidence key"} and the wording has {item.Candidate.Lines} lines" : null)
                        ?? (family.TryGetValue(item.Wording, out var familyHold) ? familyHold : null);
                    if (hold != null)
                    {
                        await RecordAsync(item, catalog, tier, mode, writes: false, signatures[tier], "held: " + hold, null, null, cancellationToken);
                        batch.Held++;
                        var rule = hold.Split(':', 2)[0];
                        batch.HeldByRule[rule] = batch.HeldByRule.GetValueOrDefault(rule) + 1;
                        batch.Tiers.Add(tier);
                        continue;
                    }
                    if (allowance[tier] <= 0)
                    {
                        await RecordAsync(item, catalog, tier, mode, writes: false, signatures[tier], $"{CappedDecision}: the tier's writes for 24 hours are spent", evidence, null, cancellationToken);
                        batch.Capped++;
                        continue;
                    }
                    allowance[tier]--;
                    var food = catalog.Foods[item.Decision.Answer!.FoodId];
                    rows.Add((item, new VerifiedWordingRow
                    {
                        Row = item.Wording,
                        Wording = item.Wording,
                        Verdict = IngredientWordingVerdict.Food,
                        FoodId = food.Id,
                        FoodName = food.Name,
                        Note = Clip($"{tier}: {item.Decision.Answer.Detail}", 1023),
                        EvidenceKey = evidence,
                    }, evidence));
                }
                if (rows.Count == 0) continue;

                var name = $"{BatchPrefix}{tier}-{now:yyyyMMddHHmmss}";
                var loaded = await _wordings.LoadAsync(rows.Select(r => r.Row).ToList(), name, apply: live, source: WordingTiers.Source(tier));
                var refused = loaded.Problems.GroupBy(p => p.Wording, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Reason, StringComparer.Ordinal);
                foreach (var (item, row, evidence) in rows)
                {
                    batch.Tiers.Add(tier);
                    if (refused.TryGetValue(item.Wording, out var reason))
                    {
                        await RecordAsync(item, catalog, tier, mode, writes: false, signatures[tier], "refused by the loader: " + reason, evidence, name, cancellationToken);
                        batch.RefusedByTier[tier] = batch.RefusedByTier.GetValueOrDefault(tier) + 1;
                        batch.RefusedByReason[reason] = batch.RefusedByReason.GetValueOrDefault(reason) + 1;
                        continue;
                    }
                    await RecordAsync(item, catalog, tier, mode, writes: true, signatures[tier], $"{row.FoodId} {row.FoodName}", evidence, name, cancellationToken);
                    batch.WritesByTier[tier] = batch.WritesByTier.GetValueOrDefault(tier) + 1;
                    if (live) batch.WrittenByTier[tier] = batch.WrittenByTier.GetValueOrDefault(tier) + 1;
                }

                var refusedCount = batch.RefusedByTier.GetValueOrDefault(tier);
                if (rows.Count >= MinimumForRefusalShare && (double)refusedCount / rows.Count > RefusedShareFault)
                    Fault(batch, tier, string.Create(CultureInfo.InvariantCulture, $"the verified wording loader refused {refusedCount} of {rows.Count} rows the tier would write, over {RefusedShareFault:P0}"));

                if (live && batch.WrittenByTier.GetValueOrDefault(tier) > 0)
                {
                    var relinked = await new LineRelinkService(_db, _currentUser).LinkVerifiedWordingsAsync(apply: true, batch: name);
                    batch.LinesMoved += relinked.Moved;
                    batch.Batches.Add(name);
                }
            }

            foreach (var item in decided.Where(d => d.Decision.Answer == null))
            {
                await RecordAsync(item, catalog, AbstainTier, LaneMode.Shadow, writes: false, signatures[AbstainTier], item.Decision.Abstain, null, null, cancellationToken);
                batch.Abstained++;
                var reason = item.Decision.Abstain ?? WordingTiers.NoTier;
                var label = reason.StartsWith("the verified wording table says", StringComparison.Ordinal) ? "verified none or unclear" : reason;
                batch.AbstainedByReason[label] = batch.AbstainedByReason.GetValueOrDefault(label) + 1;
            }

            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = BatchEntityType, EntityId = 0, ChangeType = BatchChangeType, PropertyName = Lane,
                NewValue = Clip(LaneJournal.Write(batch), 4000), Timestamp = DateTime.UtcNow, ChangedByPersonId = personId,
            });
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Wording tiers: {Read} wordings, {Abstained} no tier, {Held} held, {Writes} would write ({Written} written), {Refused} refused by the loader, {Lines} lines moved",
                batch.Read, batch.Abstained, batch.Held, batch.WritesByTier.Values.Sum(), batch.WrittenByTier.Values.Sum(), batch.RefusedByTier.Values.Sum(), batch.LinesMoved);
            return batch;
        }

        /// <summary>
        /// Rows a tier loaded that no decision records as written (the process stopped between the
        /// load and its decisions or its relink): the lane's undo finds rows only by its written
        /// decisions, so each is recorded now as written live on its wording's present line count and
        /// its batch is relinked; a row of a tier that is paused is retracted instead, since that
        /// tier's own undo has already run without it.
        /// </summary>
        private async Task RecoverAsync(WordingTierBatchResult batch, IReadOnlyDictionary<string, string> signatures, CancellationToken cancellationToken)
        {
            var orphans = await OrphanQuery(_db, DateTime.UtcNow - RecoveryWindow).ToListAsync(cancellationToken);
            if (orphans.Count == 0) return;

            var keys = orphans.Select(o => o.Wording).Distinct().ToList();
            var sources = (await _db.Ingredients.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.FdcId == null && i.CurationStatusId != (long)CurationStatusEnum.Curated && keys.Contains(i.Name.ToLower()))
                    .Select(i => new { i.Id, i.Name, Lines = _db.RecipeIngredients.Count(ri => ri.IngredientId == i.Id) })
                    .ToListAsync(cancellationToken))
                .Where(i => keys.Contains(IngredientNameNormalizer.WordingKey(i.Name)))
                .GroupBy(i => IngredientNameNormalizer.WordingKey(i.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.Lines).ThenBy(i => i.Id).First(), StringComparer.Ordinal);
            var foodIds = orphans.Where(o => o.IngredientId != null).Select(o => o.IngredientId!.Value).Distinct().ToList();
            var foods = await _db.Ingredients.AsNoTracking().Where(i => foodIds.Contains(i.Id))
                .Select(i => new
                {
                    i.Id, i.Name,
                    Description = i.Aliases.Where(a => !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext).Select(a => a.AliasName).FirstOrDefault(),
                })
                .ToDictionaryAsync(i => i.Id, i => new GateFood(i.Id, i.Name, i.Description), cancellationToken);

            var relink = new HashSet<string>(StringComparer.Ordinal);
            foreach (var orphan in orphans)
            {
                if (WordingTiers.TierOf(orphan.Source) is not { } tier) continue;
                var source = sources.GetValueOrDefault(orphan.Wording);
                var lines = source?.Lines ?? 0;
                var evidence = EvidenceKey(lines);
                if (batch.Modes.GetValueOrDefault(tier) == LaneMode.Paused)
                {
                    await _wordings.RetractAsync(orphan.Id, $"lane {Lane}/{tier} is paused and its load of this row was never recorded", apply: true, evidenceKey: evidence);
                    batch.RecoveredRetracted++;
                    continue;
                }

                var food = orphan.IngredientId is { } id ? foods.GetValueOrDefault(id) : null;
                await _frame.RecordAsync(new LaneDecisionInput(
                    Lane, tier, Subject(orphan.Wording), Clip($"{RecoveredDecision}: {orphan.IngredientId} {food?.Name}", 255), LaneMode.Live, true, signatures[tier], Stratum(lines),
                    new WordingTierSignals(orphan.Wording, IngredientNameNormalizer.TierKey(orphan.Wording), source?.Id ?? 0, lines, tier, orphan.IngredientId, food?.Name, food?.Text,
                        evidence, orphan.Batch, new List<string>(), null, new List<string>())),
                    cancellationToken);
                batch.Recovered++;
                relink.Add(orphan.Batch);
            }

            foreach (var name in relink)
            {
                var relinked = await new LineRelinkService(_db, _currentUser).LinkVerifiedWordingsAsync(apply: true, batch: name);
                batch.LinesMoved += relinked.Moved;
                batch.Batches.Add(name);
            }
            _db.ChangeTracker.Clear();
        }

        /// <summary>The decision a row <see cref="RecoverAsync"/> found loaded but unrecorded is recorded with.</summary>
        public const string RecoveredDecision = "recovered";

        private static void Fault(WordingTierBatchResult batch, string tier, string fault)
        {
            if (!batch.FaultsByTier.TryGetValue(tier, out var list)) batch.FaultsByTier[tier] = list = new List<string>();
            list.Add(fault);
        }

        private Task RecordAsync(Pending item, WordingTierCatalog catalog, string tier, string mode, bool writes, string signature, string? decision, string? evidence, string? batchName, CancellationToken cancellationToken)
        {
            var answer = item.Decision.Answer;
            var food = answer == null ? null : catalog.Foods.GetValueOrDefault(answer.FoodId);
            return _frame.RecordAsync(new LaneDecisionInput(
                Lane, tier, Subject(item.Wording), decision, mode, writes, signature, item.Stratum,
                new WordingTierSignals(item.Wording, item.Decision.Key, item.Candidate.Id, item.Candidate.Lines, tier, answer?.FoodId, food?.Name, food?.Text,
                    evidence, batchName, item.Decision.Answers.Select(a => $"{a.Tier} #{a.FoodId} ({a.Detail})").ToList(), item.Decision.Hold,
                    item.Lines.Select(l => l.RawLine).Distinct(StringComparer.Ordinal).Take(SampleLines).ToList())),
                cancellationToken);
        }

        /// <summary>
        /// The line-count band a decision is counted under (a power of two), so the audit sample
        /// reads wordings of every size, and a wording decided under the current signature is asked
        /// again when its lines double.
        /// </summary>
        public static string Stratum(int lines) => string.Create(CultureInfo.InvariantCulture, $"lines-2^{Band(lines)}");

        public const string RetractedHold = "retracted";

        /// <summary>The decision a wording is recorded with when its tier's daily allowance is spent; it is set aside for <see cref="CapWindow"/>, then asked again.</summary>
        public const string CappedDecision = "capped";

        /// <summary>The window the daily caps count writes over.</summary>
        public static readonly TimeSpan CapWindow = TimeSpan.FromHours(24);

        /// <summary>How far back a pass looks for rows a tier loaded but never recorded (<see cref="RecoverAsync"/>).</summary>
        public static readonly TimeSpan RecoveryWindow = TimeSpan.FromDays(2);

        /// <summary>
        /// In-use uncurated wordings (no USDA id, not curated, live lines) with no live row in the
        /// verified wording table, most lines first, whose name is in key form (the relink finds a
        /// wording's lines by it) and that this lane has not decided under its current signature at
        /// its present line-count band. A would-be write recorded while its tier was not live does not
        /// count once the tier is live, so the backlog a tier qualified on in shadow is written; a
        /// wording its tier's cap set aside waits out the cap window.
        /// </summary>
        private async Task<List<Candidate>> CandidatesAsync(IReadOnlyDictionary<string, string> signatures, IReadOnlyDictionary<string, string> modes, int max, WordingTierBatchResult batch, CancellationToken cancellationToken)
        {
            var waiting = await WaitingQuery(_db).ToListAsync(cancellationToken);
            var decided = (await DecidedQuery(_db).ToListAsync(cancellationToken))
                .Where(d => d.SignatureHash != null && signatures.TryGetValue(d.Tier, out var current) && current == d.SignatureHash)
                .Where(d => !(d.Writes && !d.Written && modes.TryGetValue(d.Tier, out var mode) && mode == LaneMode.Live))
                .Select(d => (d.Subject, d.Stratum))
                .ToHashSet();
            foreach (var capped in await CappedQuery(_db, DateTime.UtcNow - CapWindow).ToListAsync(cancellationToken))
            {
                if (signatures.TryGetValue(capped.Tier, out var current) && current == capped.SignatureHash) decided.Add((capped.Subject, capped.Stratum));
            }

            var taken = new List<Candidate>();
            foreach (var w in waiting)
            {
                if (taken.Count >= max) break;
                var key = IngredientNameNormalizer.WordingKey(w.Name);
                if (!string.Equals(key, w.Name.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    batch.NotKeyForm++;
                    continue;
                }
                if (decided.Contains((Subject(key), Stratum(w.Lines)))) continue;
                taken.Add(new Candidate(w.Id, w.Name, w.Lines));
            }
            batch.Read = taken.Count;
            return taken;
        }

        /// <summary>An in-use uncurated wording as the lane selects it: its id, its name and its live lines.</summary>
        internal sealed record WaitingRow(long Id, string Name, int Lines);

        /// <summary>A decision the lane recorded, as the selection reads it.</summary>
        internal sealed record DecidedRow(string Subject, string Tier, string? SignatureHash, string? Stratum, bool Writes, bool Written);

        /// <summary>A wording the lane set aside because its tier's cap was spent, as the selection reads it.</summary>
        internal sealed record CappedRow(string Subject, string Tier, string? SignatureHash, string? Stratum);

        /// <summary>A row a tier loaded with no decision recording it as written since, as <see cref="RecoverAsync"/> reads it.</summary>
        internal sealed record OrphanRow(long Id, string Wording, string Source, string Batch, long? IngredientId);

        /// <summary>One line of an approved recipe on a curated food, as the head-default table and the grams bounds read it.</summary>
        internal sealed record CuratedLineRow(long IngredientId, string? Raw, decimal Quantity, long MeasurementId, decimal? GramsPerMilliliter, decimal? GramsPerItem, decimal? ReferenceServingGrams, int? FdcCategoryId);

        internal static IQueryable<WaitingRow> WaitingQuery(ApplicationDbContext db) =>
            db.Ingredients.AsNoTracking()
                .Where(i => i.FdcId == null && !i.IsDeleted && i.CurationStatusId != (long)CurationStatusEnum.Curated
                    && !db.IngredientWordings.Any(w => w.Wording == i.Name.ToLower()))
                .Select(i => new { i.Id, i.Name, Lines = db.RecipeIngredients.Count(ri => ri.IngredientId == i.Id) })
                .Where(x => x.Lines > 0)
                .OrderByDescending(x => x.Lines).ThenBy(x => x.Id)
                .Select(x => new WaitingRow(x.Id, x.Name, x.Lines));

        internal static IQueryable<DecidedRow> DecidedQuery(ApplicationDbContext db) =>
            db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == Lane && (d.Decision == null || !d.Decision.StartsWith(CappedDecision)))
                .Select(d => new { d.Subject, d.Tier, d.SignatureHash, d.Stratum, d.Writes, d.Written })
                .Distinct()
                .Select(d => new DecidedRow(d.Subject, d.Tier, d.SignatureHash, d.Stratum, d.Writes, d.Written));

        internal static IQueryable<CappedRow> CappedQuery(ApplicationDbContext db, DateTime sinceUtc) =>
            db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == Lane && d.Decision != null && d.Decision.StartsWith(CappedDecision) && d.CreatedDate >= sinceUtc)
                .Select(d => new { d.Subject, d.Tier, d.SignatureHash, d.Stratum })
                .Distinct()
                .Select(d => new CappedRow(d.Subject, d.Tier, d.SignatureHash, d.Stratum));

        internal static IQueryable<OrphanRow> OrphanQuery(ApplicationDbContext db, DateTime sinceUtc)
        {
            var sources = WordingTiers.Sources.ToList();
            return db.IngredientWordings.AsNoTracking()
                .Where(w => sources.Contains(w.Source) && (w.LastModifiedDate ?? w.CreatedDate) >= sinceUtc
                    && !db.LaneDecisions.Any(d => d.Lane == Lane && d.Written && d.Subject == "wording:" + w.Wording
                        && d.CreatedDate >= (w.LastModifiedDate ?? w.CreatedDate)))
                .OrderBy(w => w.Id)
                .Select(w => new OrphanRow(w.Id, w.Wording, w.Source, w.Batch, w.IngredientId));
        }

        internal static IQueryable<CuratedLineRow> CuratedLinesQuery(ApplicationDbContext db) =>
            db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.Recipe.IsDeleted && ri.Recipe.CurationStatusId == (long)CurationStatusEnum.Curated
                    && ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated && !ri.Ingredient.IsDeleted
                    && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient))
                .Select(ri => new CuratedLineRow(
                    ri.IngredientId,
                    ri.RawLine.Length <= HeadLineLength ? ri.RawLine : null,
                    ri.Quantity,
                    ri.MeasurementId,
                    ri.Ingredient.GramsPerMilliliter,
                    ri.Ingredient.GramsPerItem,
                    ri.Ingredient.ReferenceServingGrams,
                    ri.Ingredient.FdcCategoryId));

        /// <summary>The tiers' catalog: the gate's foods, matcher, staples and trusted aliases, the live verified rows no tier wrote, and the head defaults and grams bounds.</summary>
        internal async Task<WordingTierCatalog> CatalogAsync(CancellationToken cancellationToken)
        {
            var parts = await new LinkAutoApplyService(_db, _cache).CatalogPartsAsync(cancellationToken);
            var tierSources = WordingTiers.Sources.ToList();
            var verified = (await _db.IngredientWordings.AsNoTracking()
                    .Where(w => !tierSources.Contains(w.Source))
                    .Select(w => new { w.Wording, w.Verdict, w.IngredientId })
                    .ToListAsync(cancellationToken))
                .Select(w => (w.Wording, w.Verdict, w.IngredientId));
            var facts = await CuratedFactsAsync(cancellationToken);
            return WordingTierCatalog.Build(parts.Foods, parts.Matcher, parts.Staples, parts.TrustedAliases, verified, facts.HeadDefaults, facts.GramBounds);
        }

        /// <summary>
        /// Reads the lines of approved recipes on curated foods once a day: each short line whose
        /// harder key is one word counts toward that head's default in its unit class, and every line
        /// that weighs counts toward the 99th percentile of grams per line of its food's USDA category.
        /// </summary>
        internal async Task<CuratedFacts> CuratedFactsAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue<CuratedFacts>(FactsKey, out var cached) && cached != null) return cached;
            var units = await _db.Measurements.AsNoTracking().ToDictionaryAsync(m => m.Id, m => (MeasurementEntity)m, cancellationToken);
            var rows = await CuratedLinesQuery(_db).ToListAsync(cancellationToken);

            var heads = new List<HeadLine>();
            var grams = new Dictionary<int, List<decimal>>();
            foreach (var r in rows)
            {
                units.TryGetValue(r.MeasurementId, out var unit);
                if (r.Raw != null && WordingHeadDefaults.HeadOf(IngredientNameNormalizer.TierKey(r.Raw)) is { } head)
                    heads.Add(new HeadLine(head, UnitClass(unit, r.Quantity), r.IngredientId));
                if (r.FdcCategoryId is { } category && r.Quantity > 0
                    && RecipeNutritionService.GramsFor(r.Quantity, unit, new IngredientEntity { GramsPerMilliliter = r.GramsPerMilliliter, GramsPerItem = r.GramsPerItem, ReferenceServingGrams = r.ReferenceServingGrams }) is > 0 and var g)
                {
                    if (!grams.TryGetValue(category, out var list)) grams[category] = list = new List<decimal>();
                    list.Add(g);
                }
            }

            var facts = new CuratedFacts(WordingHeadDefaults.Build(heads), GramBounds(grams));
            _cache.Set(FactsKey, facts, FactsLifetime);
            return facts;
        }

        /// <summary>The 99th percentile of grams per line by USDA category, for categories with at least <see cref="MinimumBoundLines"/> weighed lines.</summary>
        internal static Dictionary<int, decimal> GramBounds(IReadOnlyDictionary<int, List<decimal>> grams) =>
            grams.Where(g => g.Value.Count >= MinimumBoundLines).ToDictionary(g => g.Key, g =>
            {
                var sorted = g.Value.OrderBy(v => v).ToList();
                return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(0.99 * sorted.Count) - 1)];
            });

        private static string UnitClass(LineRow line, IReadOnlyDictionary<long, MeasurementEntity> units) =>
            UnitClass(units.GetValueOrDefault(line.MeasurementId), line.Quantity);

        private static string UnitClass(MeasurementEntity? unit, decimal quantity) =>
            WordingHeadDefaults.UnitClass(unit?.Name, unit?.MeasurementCategoryId == RecipeNutritionService.CategoryMass,
                unit?.MeasurementCategoryId == RecipeNutritionService.CategoryVolume, unit?.IsContainer == true, quantity);

        /// <summary>The wording's lines as the gate reads them against one food: raw text, unit, grams through that food's conversions (0 for a line with no amount) and the recipe's context.</summary>
        private static List<GateLine> GateLines(List<LineRow> lines, IngredientEntity? food, IReadOnlyDictionary<long, MeasurementEntity> units, IReadOnlyDictionary<long, AlcoholRecipeContext> contexts)
        {
            return lines.Select(l =>
            {
                var unit = units.GetValueOrDefault(l.MeasurementId);
                var grams = l.Quantity <= 0 ? 0m : RecipeNutritionService.GramsFor(l.Quantity, unit, food);
                return new GateLine(l.RawLine, unit?.Name, grams, contexts.GetValueOrDefault(l.RecipeId));
            }).ToList();
        }

        /// <summary>
        /// What the alcohol rules read about each recipe of a wording whose lines hold an alcohol
        /// word: its title, its other lines, its steps, and whether it came from a web source (not a
        /// public-domain book). Other wordings need no context.
        /// </summary>
        private async Task<Dictionary<long, AlcoholRecipeContext>> ContextsAsync(List<Candidate> candidates, Dictionary<long, List<LineRow>> lines, CancellationToken cancellationToken)
        {
            var recipeIds = candidates
                .Where(c => AlcoholLineContext.Read(c.Name).Kind != AlcoholSenseKind.None
                    || (lines.TryGetValue(c.Id, out var own) && own.Any(l => AlcoholLineContext.Read(l.RawLine).Kind != AlcoholSenseKind.None)))
                .SelectMany(c => lines.GetValueOrDefault(c.Id) ?? new List<LineRow>())
                .Select(l => l.RecipeId)
                .Distinct()
                .ToList();
            if (recipeIds.Count == 0) return new Dictionary<long, AlcoholRecipeContext>();

            var recipes = await _db.Recipes.AsNoTracking().Where(r => recipeIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name, Web = r.SourceSite != null && r.LicenseStatus != RecipeLicenseStatus.PublicDomain })
                .ToListAsync(cancellationToken);
            var others = (await _db.RecipeIngredients.AsNoTracking().Where(ri => recipeIds.Contains(ri.RecipeId))
                    .Select(ri => new { ri.RecipeId, ri.RawLine }).ToListAsync(cancellationToken))
                .ToLookup(ri => ri.RecipeId, ri => ri.RawLine);
            var steps = (await _db.RecipeSteps.AsNoTracking().Where(s => recipeIds.Contains(s.RecipeId)).OrderBy(s => s.StepNumber)
                    .Select(s => new { s.RecipeId, s.Description }).ToListAsync(cancellationToken))
                .ToLookup(s => s.RecipeId, s => s.Description ?? string.Empty);
            return recipes.ToDictionary(r => r.Id, r => new AlcoholRecipeContext(r.Name, others[r.Id].ToList(), string.Join(" ", steps[r.Id]), r.Web));
        }

        /// <summary>
        /// The family check over the pass's answers and the table's live food rows: an answer whose
        /// family (wordings folding to one key) settles on another food, or on none, is held. The
        /// lane never re-points a wording itself; the next pass reads the table again.
        /// </summary>
        private async Task<Dictionary<string, string>> FamilyHoldsAsync(List<Pending> writes, WordingTierCatalog catalog, CancellationToken cancellationToken)
        {
            var holds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (writes.Count == 0) return holds;
            var keys = writes.Select(w => LinkGate.FamilyKey(w.Wording)).ToHashSet(StringComparer.Ordinal);
            var table = (await _db.IngredientWordings.AsNoTracking()
                    .Where(w => w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null)
                    .Select(w => new { w.Wording, Food = w.IngredientId!.Value })
                    .ToListAsync(cancellationToken))
                .Where(w => keys.Contains(LinkGate.FamilyKey(w.Wording)) && catalog.Foods.ContainsKey(w.Food))
                .Select(w => (w.Wording, catalog.Foods[w.Food]));
            var answers = writes.Select(w => (w.Wording, catalog.Foods[w.Decision.Answer!.FoodId])).ToList();
            var verdicts = LinkGate.Family(answers.Concat(table), catalog.Gate);
            foreach (var verdict in verdicts.Take(answers.Count).Where(v => !v.Keeps))
                holds[verdict.Wording] = verdict.Winner is { } winner
                    ? $"family: wordings of \"{verdict.Key}\" settle on #{winner}"
                    : $"family: wordings of \"{verdict.Key}\" tie between two foods";
            return holds;
        }

        public async Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string tier, IReadOnlyList<string> faults, CancellationToken cancellationToken = default)
        {
            var reasons = faults.ToList();
            var (score, golden) = WordingTierAuditRecord.Evaluate(WordingTierAuditRecord.Embedded(), _judges.AuditorModel, _judges.CheckModel);
            if (!_judges.IsConfigured)
            {
                reasons.Add("no model host or model is set for the auditor and reader B (Ai:BatchOllamaUrl or Ai:OllamaUrl, Ai:Roster)");
            }
            else
            {
                try
                {
                    var audit = await _frame.AuditAsync(Lane, tier, DateTime.UtcNow.AddDays(-30), Sample, _judges.Auditor, _judges.Check, null, cancellationToken);
                    if (audit.Sampled >= 10 && audit.Unreadable * 5 > audit.Sampled)
                        reasons.Add($"the auditor {_judges.AuditorModel} or reader B {_judges.CheckModel} gave no readable answer on {audit.Unreadable} of {audit.Sampled} sampled wordings");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Wording tiers {Tier}: the audit could not run", tier);
                    reasons.Add($"the auditor {_judges.AuditorModel} could not be reached: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return await _frame.EvaluateAsync(new LaneEvaluateRequest(Lane, tier, Signature(tier), Policy(tier), score, golden, reasons), cancellationToken);
        }

        public Task<int> WritesTodayAsync(string tier, CancellationToken cancellationToken = default)
        {
            var since = DateTime.UtcNow.AddHours(-24);
            return _db.LaneDecisions.AsNoTracking().CountAsync(d => d.Lane == Lane && d.Tier == tier && d.Writes && d.CreatedDate >= since, cancellationToken);
        }

        private static string? Clip(string? value, int length) => value == null || value.Length <= length ? value : value[..length];
    }

    /// <summary>
    /// The wording tier lane's undo for the lane frame: every row a tier wrote live since a time,
    /// found by the tier's own decisions and the row's source, is retracted through the verified
    /// wording loader's per-wording retract (its relinked lines, retired ingredients, withdrawn
    /// proposals and picks and import landings go back), with the evidence key it was loaded on, so
    /// the rejection memory keeps the lane from writing the pair again on the same evidence. A
    /// retract never demotes a food the row's load curated (lead decision).
    /// </summary>
    public sealed class WordingTierUndo : ILaneUndo
    {
        private readonly ApplicationDbContext _db;
        private readonly IVerifiedWordingService _wordings;

        public WordingTierUndo(ApplicationDbContext db, IVerifiedWordingService wordings)
        {
            _db = db;
            _wordings = wordings;
        }

        public string Lane => WordingTierLaneService.Lane;

        public async Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken)
        {
            var written = (await _db.LaneDecisions.AsNoTracking()
                    .Where(d => d.Lane == Lane && d.Tier == tier && d.Written && d.CreatedDate >= sinceUtc)
                    .OrderByDescending(d => d.Id)
                    .Select(d => d.SignalsJson)
                    .ToListAsync(cancellationToken))
                .Select(LaneJournal.Read<WordingTierSignals>)
                .Where(s => s?.FoodId != null)
                .ToList();
            var source = WordingTiers.Source(tier);
            var keys = written.Select(s => s!.Wording).Distinct().ToList();
            var rows = await _db.IngredientWordings.AsNoTracking()
                .Where(w => keys.Contains(w.Wording) && w.Source == source)
                .Select(w => new { w.Id, w.Wording, w.IngredientId })
                .ToListAsync(cancellationToken);

            int undone = 0, skipped = 0;
            var done = new HashSet<long>();
            foreach (var signals in written)
            {
                var row = rows.FirstOrDefault(r => r.Wording == signals!.Wording && r.IngredientId == signals.FoodId);
                if (row == null || !done.Add(row.Id))
                {
                    skipped++;
                    continue;
                }
                if (apply)
                {
                    var reason = $"lane {Lane}/{tier} undone its writes since {sinceUtc.ToString("u", CultureInfo.InvariantCulture)}";
                    if (await _wordings.RetractAsync(row.Id, reason, apply: true, evidenceKey: signals!.EvidenceKey) == null)
                    {
                        skipped++;
                        continue;
                    }
                }
                undone++;
            }
            return new LaneUndoResult(undone, skipped, 0, $"wording tiers {tier}: rows written since {sinceUtc.ToString("u", CultureInfo.InvariantCulture)}, retracted with their evidence");
        }
    }
}
