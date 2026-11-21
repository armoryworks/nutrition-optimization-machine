using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <inheritdoc cref="IWordingReaderLaneService"/>
    public class WordingReaderLaneService : IWordingReaderLaneService
    {
        public const string Lane = AiLanes.WordingReaders;
        public const string AbstainTier = "abstain";
        public const string BatchEntityType = "WordingReaderLane";
        public const string BatchChangeType = "WordingReaderBatch";
        public const string LexiconEntityType = "WordingLexicon";
        public const string LexiconAddChangeType = "WordingLexiconAdd";
        public const string LexiconUndoChangeType = "WordingLexiconUndo";

        /// <summary>The decision a wording is recorded with when a reader gave no readable answer on it; it is asked again after <see cref="RetryAfter"/>.</summary>
        public const string UnreadableDecision = "unreadable";

        /// <summary>The decision a wording is recorded with when its tier's daily allowance is spent; it is asked again after <see cref="RetryAfter"/>.</summary>
        public const string CappedDecision = "capped";

        public const string RetractedHold = "retracted";
        public const string SizeGateHold = "size-gate";
        public const string NoFoodHold = "no-food";

        public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(24);

        /// <summary>Lines of one wording read for the gate's unit, grams and alcohol checks.</summary>
        public const int MaxLinesPerWording = 200;

        private const int SampleLines = 5;
        private const int SentinelLines = 100;
        private const int SentinelBatch = 12;

        /// <summary>Sampled lines of one wording the sentinel finds another food that retract it (wordings design 2.6).</summary>
        public const int RetractAtWrongLines = 2;

        /// <summary>Wrong lines per 100 sentinel lines of one batch that pause the tier.</summary>
        public const int PauseAtWrongPer100 = 6;

        /// <summary>Moved lines that weigh nothing on their new food, in one batch, that pause the tier.</summary>
        public const int PauseAtWeightless = 3;

        private static readonly TimeSpan BlockersLifetime = TimeSpan.FromHours(6);
        private const string BlockersKey = nameof(WordingReaderLaneService) + ":blockers";

        /// <summary>The read tier qualifies on a Wilson lower bound of 98 % and pauses when a full window of 500 shows more than 3 % confirmed wrong (wordings design 3.2).</summary>
        public static readonly LanePolicy ReadPolicy = new(QualifyBar: 0.98, PauseBar: 0.03, PauseWindow: 500);

        /// <summary>The lexicon and the alcohol fixed readings qualify on 97 % and pause above 4 % (wordings design 3.1, alcohol design 3).</summary>
        public static readonly LanePolicy LexiconPolicy = new(QualifyBar: 0.97, PauseBar: 0.04);

        public static readonly LanePolicy AlcoholPolicy = new(QualifyBar: 0.97, PauseBar: 0.04, PauseWindow: 200);

        public static readonly LaneSamplePlan Sample = new(Minimum: 30, Rate: 0.25);

        public static LanePolicy Policy(string tier) => tier switch
        {
            WordingReaders.Lexicon => LexiconPolicy,
            WordingReaders.Alcohol => AlcoholPolicy,
            _ => ReadPolicy,
        };

        private readonly ApplicationDbContext _db;
        private readonly IVerifiedWordingService _wordings;
        private readonly ILaneFrameService _frame;
        private readonly IWordingReaders _readers;
        private readonly IWordingTierLaneService _tiers;
        private readonly ICurrentUserService _currentUser;
        private readonly IMemoryCache _cache;
        private readonly ILogger<WordingReaderLaneService> _logger;
        private readonly Func<string, WordingReaderRecords.Record?> _records;
        private readonly Func<WordingTierAuditRecord.Record?> _auditRecord;

        public WordingReaderLaneService(ApplicationDbContext db, IVerifiedWordingService wordings, ILaneFrameService frame, IWordingReaders readers,
            IWordingTierLaneService tiers, ICurrentUserService currentUser, IMemoryCache cache, ILogger<WordingReaderLaneService> logger,
            Func<string, WordingReaderRecords.Record?>? records = null, Func<WordingTierAuditRecord.Record?>? auditRecord = null)
        {
            _db = db;
            _wordings = wordings;
            _frame = frame;
            _readers = readers;
            _tiers = tiers;
            _currentUser = currentUser;
            _cache = cache;
            _logger = logger;
            _records = records ?? WordingReaderRecords.Embedded;
            _auditRecord = auditRecord ?? WordingTierAuditRecord.Embedded;
        }

        /// <summary>The hash a row's source carries: the readers' rules and prompts and the two reader models.</summary>
        public string ReadersHash => WordingTiers.Hash(string.Join('|', WordingReaders.Version, OllamaWordingReader.Signature, _readers.ReaderA.ModelName, _readers.ReaderB.ModelName));

        public string Source => WordingReaders.Source(ReadersHash);

        public string Signature(string tier) => WordingTiers.Hash(string.Join('|', WordingReaders.Signature, OllamaWordingReader.Signature, OllamaIngredientLinkVerifier.Signature, tier,
            _readers.ReaderA.ModelName, _readers.ReaderB.ModelName, _readers.AuditorModel, _readers.CheckModel));

        public static string Subject(string key) => WordingTierLaneService.Subject(key);

        private sealed record Candidate(long Id, string Name, int Lines, int Blocks);

        private sealed record LineRow(long IngredientId, long RecipeId, string RawLine, decimal Quantity, long MeasurementId);

        private sealed record RecipeFacts(string? Title, bool Web, AlcoholRecipeContext? Alcohol);

        private sealed class Item
        {
            public required Candidate Candidate { get; init; }
            public required string Wording { get; init; }
            public required List<LineRow> Lines { get; init; }
            public string Stratum => WordingTierLaneService.Stratum(Candidate.Lines);
            public string Evidence => WordingReaders.EvidenceKey(Candidate.Lines);
            public WordingPack? Pack { get; set; }
            public FixedPack? Fixed { get; set; }
            public ReaderAnswer? A { get; set; }
            public ReaderAnswer? B { get; set; }
            public IReadOnlyList<string?>? FixedA { get; set; }
            public IReadOnlyList<string?>? FixedB { get; set; }
            public string Tier { get; set; } = WordingReaders.Read;
            public string? Verdict { get; set; }
            public GateFood? Food { get; set; }
            public string Rule { get; set; } = string.Empty;
            public string? Detail { get; set; }
            public string? Hold { get; set; }
            public string? Means { get; set; }
            public string? Reading { get; set; }
        }

        public async Task<WordingReaderBatchResult> RunBatchAsync(IReadOnlyDictionary<string, string> modes, int max, IReadOnlyDictionary<string, int>? remaining = null, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var batch = new WordingReaderBatchResult { Stamp = now };
            foreach (var tier in WordingReaders.Tiers) batch.Modes[tier] = modes.TryGetValue(tier, out var m) ? m : LaneMode.Shadow;
            var allowance = WordingReaders.Tiers.ToDictionary(t => t, t => remaining != null && remaining.TryGetValue(t, out var left) ? left : int.MaxValue);
            var signatures = WordingReaders.Tiers.Append(AbstainTier).ToDictionary(t => t, Signature);

            var candidates = await CandidatesAsync(signatures, Math.Max(0, max), batch, cancellationToken);
            batch.Read = candidates.Count;
            if (candidates.Count == 0) return batch;

            var parts = await new LinkAutoApplyService(_db, _cache).CatalogPartsAsync(cancellationToken);
            var foods = parts.Foods.Where(f => !f.Deleted).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            var lexiconRows = await _db.WordingLexicon.AsNoTracking().Select(l => new { l.Term, l.Means }).ToListAsync(cancellationToken);
            var lexicon = WordingReaders.GateLexicon(lexiconRows.Select(l => (l.Term, l.Means)));
            var trusted = parts.TrustedAliases
                .GroupBy(a => IngredientNameNormalizer.WordingKey(a.Name), StringComparer.Ordinal)
                .Where(g => g.Select(a => a.Owner).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Owner, StringComparer.Ordinal);
            var gate = GateCatalog.Build(parts.Foods, parts.Matcher, trusted, lexicon: lexicon, staples: parts.Staples);
            var units = await _db.Measurements.AsNoTracking().ToDictionaryAsync(m => m.Id, m => (MeasurementEntity)m, cancellationToken);

            var ids = candidates.Select(c => c.Id).ToList();
            var lines = (await _db.RecipeIngredients.AsNoTracking()
                    .Where(ri => ids.Contains(ri.IngredientId) && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient) && !ri.Recipe.IsDeleted)
                    .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder)
                    .Select(ri => new LineRow(ri.IngredientId, ri.RecipeId, ri.RawLine, ri.Quantity, ri.MeasurementId))
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.IngredientId)
                .ToDictionary(g => g.Key, g => g.Take(MaxLinesPerWording).ToList());
            var recipes = await RecipesAsync(candidates, lines, cancellationToken);

            var items = new List<Item>();
            foreach (var candidate in candidates)
            {
                var item = new Item { Candidate = candidate, Wording = IngredientNameNormalizer.WordingKey(candidate.Name), Lines = lines.GetValueOrDefault(candidate.Id) ?? new List<LineRow>() };
                if (LinkAutoApplyGuard.IsCompound(item.Wording))
                {
                    await RecordAsync(item, AbstainTier, LaneMode.Shadow, false, signatures[AbstainTier], WordingTiers.CompoundAbstain, null, cancellationToken);
                    batch.Compound++;
                    continue;
                }

                var readings = item.Lines.Select(l => AlcoholLineContext.Read(l.RawLine, recipes.GetValueOrDefault(l.RecipeId)?.Alcohol)).ToList();
                var shared = readings.Count == 0 ? AlcoholLineContext.Read(item.Wording) : AlcoholLineContext.Shared(readings);
                if (AlcoholFixedReading.ModeOf(shared) is { } mode)
                {
                    item.Tier = WordingReaders.Alcohol;
                    item.Fixed = FixedPackOf(item, mode, recipes);
                }
                else
                {
                    var sense = shared;
                    var foodIds = CandidateFoods(item.Wording, parts.Matcher, parts.Staples, gate, sense, foods);
                    item.Pack = WordingReaders.Pack(item.Wording,
                        item.Lines.Select(l => (l.RecipeId, new ReaderLine(l.RawLine, recipes.GetValueOrDefault(l.RecipeId)?.Title,
                            recipes.GetValueOrDefault(l.RecipeId)?.Web == true ? "web" : "book", UnitClass(units.GetValueOrDefault(l.MeasurementId), l.Quantity)))).ToList(),
                        foodIds.Select(id => foods[id]).Select(f => new ReaderFood(f.Id, f.Name, f.Text, f.CategoryId, f.NutrientRows)).ToList());
                }
                items.Add(item);
            }

            await AddMacrosAsync(items, cancellationToken);
            await ReadAsync(items, batch, cancellationToken);

            var passLexicon = new Dictionary<string, string>(StringComparer.Ordinal);
            await LexiconAsync(items, gate, signatures, allowance, batch, passLexicon, now, cancellationToken);
            var restsOnPass = batch.Modes[WordingReaders.Lexicon] == LaneMode.Live || batch.Modes[WordingReaders.Read] != LaneMode.Live;
            var gateWithPass = restsOnPass ? WordingReaders.WithLexicon(gate, passLexicon) : gate;
            if (restsOnPass) await ReadAgainWithMeaningAsync(items, parts.Matcher, foods, batch, cancellationToken);

            var conversions = await ConversionsAsync(items, foods, cancellationToken);
            foreach (var item in items) Decide(item, foods, gateWithPass, units, conversions, recipes);

            await SizeGateAsync(items, cancellationToken);
            await HoldsAsync(items, gateWithPass, foods, cancellationToken);

            foreach (var tierGroup in items.GroupBy(i => i.Tier))
                await WriteTierAsync(tierGroup.Key, tierGroup.ToList(), batch, allowance, signatures, units, conversions, now, cancellationToken);

            batch.Tiers.RemoveWhere(t => !WordingReaders.Tiers.Contains(t));
            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = BatchEntityType, EntityId = 0, ChangeType = BatchChangeType, PropertyName = Lane,
                NewValue = Clip(LaneJournal.Write(batch), 4000), Timestamp = DateTime.UtcNow, ChangedByPersonId = _currentUser.PersonIdOrSystem,
            });
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Wording readers: {Read} wordings, {Calls} calls, {Held} held, {Writes} would write ({Written} written), {Lexicon} lexicon entries, {Lines} lines moved, {Retracted} retracted by the sentinel",
                batch.Read, batch.Calls, batch.Held, batch.WritesByTier.Values.Sum(), batch.WrittenByTier.Values.Sum(), batch.LexiconWritten, batch.LinesMoved, batch.Retracted);
            return batch;
        }

        private FixedPack FixedPackOf(Item item, string mode, IReadOnlyDictionary<long, RecipeFacts> recipes)
        {
            var take = item.Lines.Count < WordingReaders.SmallBelow ? AlcoholFixedReading.SmallLines : AlcoholFixedReading.MaxLines;
            var chosen = item.Lines.GroupBy(l => l.RecipeId).Select(g => g.First()).Take(take).ToList();
            return new FixedPack(item.Wording, mode, chosen.Select(l =>
            {
                var facts = recipes.GetValueOrDefault(l.RecipeId);
                var others = facts?.Alcohol?.OtherLines?.Where(o => !string.Equals(o, l.RawLine, StringComparison.Ordinal)).Take(AlcoholFixedReading.OtherLines).ToList() ?? new List<string>();
                return new FixedLine(l.RawLine, facts?.Title, others, facts?.Alcohol?.Steps, facts?.Web == true);
            }).ToList());
        }

        /// <summary>
        /// The candidate foods of one wording, at most <see cref="WordingReaders.MaxCandidates"/>: the
        /// food a staple or name rule gives it, the owner of a trusted alias of the wording, the name
        /// matcher's shortlist (kept to the foods its alcohol sense allows), the shortlists of its
        /// hyphen and space spellings, and the shortlist of what a lexicon word of it means. Prepared
        /// and branded foods are never candidates.
        /// </summary>
        internal static List<long> CandidateFoods(string wording, FoodNameMatcher matcher, IReadOnlyDictionary<string, FoodCandidate> staples, GateCatalog gate, AlcoholReading? sense, IReadOnlyDictionary<long, GateFood> foods)
        {
            var ids = new List<long>();
            void Add(long id)
            {
                if (ids.Contains(id) || !foods.TryGetValue(id, out var f) || PreparedFoods.IsPrepared(f.CategoryId) || f.DataType == CatalogAdmissionRule.BrandedFood) return;
                ids.Add(id);
            }

            if (IngredientLinkService.DeterministicTarget(wording, matcher, staples) is { } rule) Add(rule.Food.IngredientId);
            if (gate.TrustedAliases.TryGetValue(wording, out var owner)) Add(owner);
            foreach (var c in matcher.Shortlist(wording, WordingReaders.MaxCandidates, sense)) Add(c.IngredientId);
            foreach (var spelling in new[] { wording.Replace('-', ' '), wording.Replace("-", string.Empty).Replace(" ", string.Empty) }.Where(s => s != wording && s.Length > 0).Distinct())
                foreach (var c in matcher.Shortlist(spelling, 4, sense)) Add(c.IngredientId);
            foreach (var word in LinkGate.Words(wording).Select(LinkGate.Fold).Distinct())
                if (gate.Lexicon.TryGetValue(word, out var means))
                    foreach (var c in matcher.Shortlist(means, 4)) Add(c.IngredientId);
            return ids.Take(WordingReaders.MaxCandidates).ToList();
        }

        /// <summary>Energy, protein, fat and carbohydrate per 100 g of every candidate food, as stored, for the pack and the nutrition-equivalent rule.</summary>
        private async Task AddMacrosAsync(List<Item> items, CancellationToken cancellationToken)
        {
            var ids = items.Where(i => i.Pack != null).SelectMany(i => i.Pack!.Candidates.Select(c => c.Id)).Distinct().ToList();
            if (ids.Count == 0) return;
            var nutrients = await _db.Nutrients.AsNoTracking().Where(n => n.ParentNutrientId == null).Select(n => new { n.Id, n.Name }).ToListAsync(cancellationToken);
            long? Find(Func<string, bool> test) => nutrients.Where(n => test(n.Name.ToLowerInvariant())).OrderBy(n => n.Id).Select(n => (long?)n.Id).FirstOrDefault();
            var energy = Find(n => n.Contains("calorie") || n.Contains("energy"));
            var protein = Find(n => n.Contains("protein"));
            var carbohydrate = Find(n => n.Contains("carbohydrate"));
            var fat = Find(n => n.Contains("total lipid") || n == "fat" || n == "total fat");
            var wanted = new[] { energy, protein, carbohydrate, fat }.Where(n => n != null).Select(n => n!.Value).ToList();
            var amounts = new Dictionary<(long, long), decimal>();
            foreach (var chunk in ids.Chunk(2000))
            {
                foreach (var row in await _db.IngredientNutrients.AsNoTracking()
                    .Where(n => chunk.Contains(n.IngredientId) && wanted.Contains(n.NutrientId))
                    .Select(n => new { n.IngredientId, n.NutrientId, n.Amount }).ToListAsync(cancellationToken))
                    amounts.TryAdd((row.IngredientId, row.NutrientId), row.Amount);
            }
            decimal? Of(long food, long? nutrient) => nutrient is { } n && amounts.TryGetValue((food, n), out var a) ? a : null;
            foreach (var item in items.Where(i => i.Pack != null))
                item.Pack = item.Pack! with
                {
                    Candidates = item.Pack.Candidates.Select(c => c with { Kcal = Of(c.Id, energy), Protein = Of(c.Id, protein), Fat = Of(c.Id, fat), Carbohydrate = Of(c.Id, carbohydrate) }).ToList(),
                };
        }

        /// <summary>
        /// The readings, grouped by model as the host keeps one loaded: every pack by reader A (small
        /// packs six to a call), then every fixed pack by reader A, then the same by reader B, each
        /// with the candidates in its own order. A call whose reply cannot be read leaves its packs
        /// unanswered; a host that cannot be reached stops the pass.
        /// </summary>
        private async Task ReadAsync(List<Item> items, WordingReaderBatchResult batch, CancellationToken cancellationToken)
        {
            foreach (var (reader, role) in new[] { (_readers.ReaderA, "A"), (_readers.ReaderB, "B") })
            {
                var packs = items.Where(i => i.Pack != null).ToDictionary(i => i.Wording, i => WordingReaders.Shuffled(i.Pack!, role), StringComparer.Ordinal);
                foreach (var call in WordingReaders.Calls(packs.Values))
                {
                    batch.Calls++;
                    IReadOnlyList<ReaderAnswer?> answers;
                    try
                    {
                        answers = await reader.ReadAsync(call, cancellationToken);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
                    {
                        _logger.LogWarning(ex, "Wording readers: reader {Role} ({Model}) gave no readable reply for {Count} wordings", role, reader.ModelName, call.Count);
                        answers = new ReaderAnswer?[call.Count];
                    }
                    for (var i = 0; i < call.Count; i++)
                    {
                        var item = items.First(x => x.Wording == call[i].Wording && x.Pack != null);
                        if (role == "A") item.A = i < answers.Count ? answers[i] : null;
                        else item.B = i < answers.Count ? answers[i] : null;
                    }
                }

                foreach (var item in items.Where(i => i.Fixed != null))
                {
                    batch.Calls++;
                    IReadOnlyList<string?> readings;
                    try
                    {
                        readings = await reader.ReadFixedAsync(item.Fixed!, cancellationToken);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
                    {
                        _logger.LogWarning(ex, "Wording readers: reader {Role} ({Model}) gave no readable fixed reading for {Wording}", role, reader.ModelName, item.Wording);
                        readings = new string?[item.Fixed!.Lines.Count];
                    }
                    if (role == "A") item.FixedA = readings;
                    else item.FixedB = readings;
                }
            }

            foreach (var item in items.Where(i => i.Pack != null && i.A != null && i.B != null))
            {
                batch.BothRead++;
                if (item.A!.Verdict == item.B!.Verdict && item.A.FoodId == item.B.FoodId) batch.Agreed++;
            }
        }

        /// <summary>
        /// A wording whose meaning the lexicon took in this pass but that the readers did not both give
        /// a food is read again by both, with the foods its meaning names added to its candidates
        /// (the lexicon hits of wordings design 2.2), since on the first reading no candidate may
        /// have shared a word with it.
        /// </summary>
        private async Task ReadAgainWithMeaningAsync(List<Item> items, FoodNameMatcher matcher, IReadOnlyDictionary<long, GateFood> foods, WordingReaderBatchResult batch, CancellationToken cancellationToken)
        {
            var again = items.Where(i => i.Pack != null && i.Means != null
                && !(i.A?.Verdict == IngredientWordingVerdict.Food && i.B?.Verdict == IngredientWordingVerdict.Food)).ToList();
            if (again.Count == 0) return;
            foreach (var item in again)
            {
                var have = item.Pack!.Candidates.Select(c => c.Id).ToHashSet();
                var more = matcher.Shortlist(item.Means!, 4).Select(c => c.IngredientId)
                    .Where(id => !have.Contains(id) && foods.TryGetValue(id, out var f) && !PreparedFoods.IsPrepared(f.CategoryId) && f.DataType != CatalogAdmissionRule.BrandedFood)
                    .Select(id => foods[id]).Select(f => new ReaderFood(f.Id, f.Name, f.Text, f.CategoryId, f.NutrientRows));
                item.Pack = item.Pack with { Candidates = item.Pack.Candidates.Concat(more).Take(item.Pack.Small ? WordingReaders.SmallCandidates : WordingReaders.MaxCandidates).ToList() };
                item.A = null;
                item.B = null;
            }
            await AddMacrosAsync(again, cancellationToken);
            await ReadAsync(again, batch, cancellationToken);
        }

        /// <summary>
        /// Synonyms (wordings design 2.5): where both readers proposed the same meaning for a wording
        /// of one word that the lexicon lacks, reader A's model as the refuter looks for a reason it is
        /// another food; with none the entry is recorded under tier <c>lexicon</c>, written while that
        /// tier is live, and read by this pass's gate either way, so a shadow pass decides as a live one
        /// would.
        /// </summary>
        private async Task LexiconAsync(List<Item> items, GateCatalog gate, IReadOnlyDictionary<string, string> signatures, Dictionary<string, int> allowance,
            WordingReaderBatchResult batch, Dictionary<string, string> passLexicon, DateTime now, CancellationToken cancellationToken)
        {
            var mode = batch.Modes[WordingReaders.Lexicon];
            var live = mode == LaneMode.Live;
            var name = WordingReaders.BatchName(WordingReaders.Lexicon, now);
            var rejected = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == LexiconEntityType && a.ChangeType == LexiconUndoChangeType)
                    .Select(a => a.OldValue).ToListAsync(cancellationToken))
                .Where(v => v != null).Select(v => v!).ToHashSet(StringComparer.Ordinal);
            foreach (var item in items.Where(i => i.Pack != null))
            {
                if (WordingReaders.LexiconTerm(item.Wording) is not { } term) continue;
                var folded = LinkGate.Fold(term);
                if (gate.Lexicon.ContainsKey(folded) || passLexicon.ContainsKey(folded)) continue;
                if (WordingReaders.AgreedMeaning(item.A, item.B, term) is not { } means) continue;
                batch.LexiconProposed++;
                batch.Tiers.Add(WordingReaders.Lexicon);
                var entryKey = term + "=" + means;
                var samples = item.Lines.Select(l => l.RawLine).Distinct(StringComparer.Ordinal).Take(WordingReaders.SmallLines).ToList();
                string? refutation;
                try
                {
                    refutation = await _readers.ReaderA.RefuteAsync(term, means, samples, cancellationToken);
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
                {
                    refutation = "the refuter gave no readable answer";
                }

                var signals = new WordingReaderSignals(term, item.Candidate.Id, item.Candidate.Lines, WordingReaders.Lexicon, null, null, null, means, null, name,
                    item.A?.Means, item.B?.Means, "both proposed", refutation, samples, Means: means);
                if (refutation != null || rejected.Contains(entryKey))
                {
                    batch.LexiconRefuted++;
                    await _frame.RecordAsync(new LaneDecisionInput(Lane, WordingReaders.Lexicon, "term:" + term, Clip("refuted: " + (refutation ?? "the entry was undone before"), 255),
                        mode, false, signatures[WordingReaders.Lexicon], item.Stratum, signals), cancellationToken);
                    continue;
                }
                if (allowance[WordingReaders.Lexicon] <= 0)
                {
                    batch.Capped++;
                    await _frame.RecordAsync(new LaneDecisionInput(Lane, WordingReaders.Lexicon, "term:" + term, $"{CappedDecision}: the tier's writes for 24 hours are spent",
                        mode, false, signatures[WordingReaders.Lexicon], item.Stratum, signals), cancellationToken);
                    continue;
                }

                allowance[WordingReaders.Lexicon]--;
                passLexicon[folded] = means;
                item.Means = means;
                await _frame.RecordAsync(new LaneDecisionInput(Lane, WordingReaders.Lexicon, "term:" + term, Clip($"{term} means {means}", 255),
                    mode, true, signatures[WordingReaders.Lexicon], item.Stratum, signals), cancellationToken);
                batch.WritesByTier[WordingReaders.Lexicon] = batch.WritesByTier.GetValueOrDefault(WordingReaders.Lexicon) + 1;
                if (!live) continue;

                var entry = new WordingLexiconEntity { Term = term, Means = means, Source = Source, Batch = name, CreatedDate = DateTime.UtcNow, CreatedByPersonId = _currentUser.PersonIdOrSystem, Note = Clip($"A: {item.A?.Reason}; B: {item.B?.Reason}; refuter: none", 1023) };
                _db.WordingLexicon.Add(entry);
                await _db.SaveChangesAsync(cancellationToken);
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = LexiconEntityType, EntityId = entry.Id, ChangeType = LexiconAddChangeType, PropertyName = name,
                    NewValue = entryKey, Timestamp = DateTime.UtcNow, ChangedByPersonId = _currentUser.PersonIdOrSystem,
                });
                await _db.SaveChangesAsync(cancellationToken);
                batch.LexiconWritten++;
                batch.WrittenByTier[WordingReaders.Lexicon] = batch.WrittenByTier.GetValueOrDefault(WordingReaders.Lexicon) + 1;
            }
            if (batch.LexiconWritten > 0) batch.Batches.Add(name);
        }

        /// <summary>
        /// The outcome of one wording: the outcome table and the gate for a read wording (two "none"
        /// answers only when no candidate passes the gate on its own; two "unclear" answers only with
        /// a rule that agrees), the fixed readings and the gate for an unclear alcohol word.
        /// </summary>
        private static void Decide(Item item, IReadOnlyDictionary<long, GateFood> foods, GateCatalog gate, IReadOnlyDictionary<long, MeasurementEntity> units,
            IReadOnlyDictionary<long, IngredientEntity> conversions, IReadOnlyDictionary<long, RecipeFacts> recipes)
        {
            if (item.Fixed != null)
            {
                var (reading, reason) = AlcoholFixedReading.Combine(item.Fixed, item.FixedA ?? Array.Empty<string?>(), item.FixedB ?? Array.Empty<string?>());
                item.Rule = reason;
                if (reading == null) return;
                item.Reading = reading.Code;
                if (reading.FdcId == null)
                {
                    item.Rule = $"read {reading.Code}: no food";
                    return;
                }
                var food = foods.Values.Where(f => f.FdcId == reading.FdcId).OrderBy(f => f.Id).FirstOrDefault();
                item.Verdict = IngredientWordingVerdict.Food;
                if (food == null)
                {
                    item.Hold = $"{NoFoodHold}: no live food has FDC id {reading.FdcId}";
                    return;
                }
                item.Food = food;
                var extra = reading.Means == null ? new Dictionary<string, string>() : new Dictionary<string, string> { [LinkGate.Fold(item.Fixed.Mode)] = reading.Means };
                var lines = GateLines(item.Lines, conversions.GetValueOrDefault(food.Id), units, recipes, reading.Sense);
                var holds = LinkGate.Check(new GatePair(item.Wording, item.Candidate.Id, food, ReadersAgreed: true, Lines: lines), WordingReaders.WithLexicon(gate, extra));
                if (holds.Count > 0) item.Hold = string.Join("; ", holds.Select(h => h.Code + ": " + h));
                return;
            }

            var pack = item.Pack!;
            var outcome = WordingReaders.Combine(item.A, item.B, pack);
            item.Rule = outcome.Rule;
            item.Detail = outcome.Detail;
            switch (outcome.Verdict)
            {
                case IngredientWordingVerdict.Food:
                {
                    var food = foods[outcome.FoodId!.Value];
                    item.Verdict = IngredientWordingVerdict.Food;
                    item.Food = food;
                    var holds = LinkGate.Check(new GatePair(item.Wording, item.Candidate.Id, food, ReadersAgreed: true,
                        Lines: GateLines(item.Lines, conversions.GetValueOrDefault(food.Id), units, recipes, null)), gate);
                    if (holds.Count > 0) item.Hold = string.Join("; ", holds.Select(h => h.Code + ": " + h));
                    return;
                }
                case IngredientWordingVerdict.None:
                {
                    var passing = pack.Candidates.FirstOrDefault(c => foods.TryGetValue(c.Id, out var f)
                        && LinkGate.Check(new GatePair(item.Wording, item.Candidate.Id, f, Lines: GateLines(item.Lines, conversions.GetValueOrDefault(f.Id), units, recipes, null)), gate).Count == 0);
                    if (passing != null)
                    {
                        item.Rule = WordingReaders.CandidatePasses;
                        item.Detail = $"#{passing.Id} {passing.Name}";
                        return;
                    }
                    item.Verdict = IngredientWordingVerdict.None;
                    return;
                }
                case IngredientWordingVerdict.Unclear:
                {
                    var rule = WordingReaders.UnclearRule(item.Wording, item.Lines.Select(l => l.RawLine).ToList(), pack.UnitProfile);
                    if (rule == null)
                    {
                        item.Rule = WordingReaders.NoRuleAgrees;
                        return;
                    }
                    item.Verdict = IngredientWordingVerdict.Unclear;
                    item.Detail = rule;
                    return;
                }
            }
        }

        /// <summary>
        /// The size gate (wordings design 2.4 check 9): a wording with more than 500 lines is written
        /// only when the sentinel, reading 20 of its lines that name it against the food, finds none
        /// of them another food, variety or form.
        /// </summary>
        private async Task SizeGateAsync(List<Item> items, CancellationToken cancellationToken)
        {
            foreach (var item in items.Where(i => i.Food != null && i.Hold == null && i.Candidate.Lines > WordingReaders.SizeGateLines))
            {
                if (!_readers.Sentinel.IsConfigured)
                {
                    item.Hold = $"{SizeGateHold}: no sentinel model is set for a wording of {item.Candidate.Lines} lines";
                    continue;
                }
                var sample = item.Lines.Where(l => LineRelinkService.NamesWording(l.RawLine, item.Wording)).Select(l => l.RawLine)
                    .Distinct(StringComparer.Ordinal).Take(WordingReaders.SizeGateSample).ToList();
                var wrong = await WrongLinesAsync(sample, item.Food!.Text, cancellationToken);
                if (wrong != 0) item.Hold = $"{SizeGateHold}: the sentinel found {(wrong < 0 ? "no readable answer on" : wrong.ToString(CultureInfo.InvariantCulture) + " of")} {sample.Count} lines another food";
            }
        }

        /// <summary>The lines the sentinel reads as another food, variety or form than the food; -1 when it answered none of them readably.</summary>
        private async Task<int> WrongLinesAsync(IReadOnlyList<string> lines, string foodText, CancellationToken cancellationToken)
        {
            int wrong = 0, read = 0;
            foreach (var chunk in lines.Chunk(SentinelBatch))
            {
                IReadOnlyList<LinkVerdict> verdicts;
                try
                {
                    verdicts = await _readers.Sentinel.JudgeAsync(chunk.Select((l, i) => new LinkPair(i, l, foodText)).ToList(), cancellationToken);
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException)
                {
                    continue;
                }
                read += verdicts.Count;
                wrong += verdicts.Count(v => v.Judgement is LinkJudgement.DifferentFood or LinkJudgement.MoreThanOneFood or LinkJudgement.VarietyOrForm);
            }
            return read == 0 && lines.Count > 0 ? -1 : wrong;
        }

        /// <summary>The rejection memory and the family check: a pair a retract took back is held while its evidence holds, and an answer whose family settles on another food is held.</summary>
        private async Task HoldsAsync(List<Item> items, GateCatalog gate, IReadOnlyDictionary<long, GateFood> foods, CancellationToken cancellationToken)
        {
            var keys = items.Where(i => i.Verdict != null).Select(i => i.Wording).ToList();
            var rejections = (await _db.IngredientWordingRejections.AsNoTracking()
                    .Where(r => keys.Contains(r.Wording))
                    .Select(r => new { r.Wording, r.Verdict, r.IngredientId, r.EvidenceKey })
                    .ToListAsync(cancellationToken))
                .ToLookup(r => r.Wording, StringComparer.Ordinal);
            foreach (var item in items.Where(i => i.Verdict != null && i.Hold == null))
            {
                var retract = rejections[item.Wording].FirstOrDefault(r => r.Verdict == item.Verdict && r.IngredientId == item.Food?.Id && WordingReaders.StillRetracted(r.EvidenceKey, item.Candidate.Lines));
                if (retract != null) item.Hold = $"{RetractedHold}: the pair was retracted on {retract.EvidenceKey ?? "no evidence key"} and the wording has {item.Candidate.Lines} lines";
            }

            var writes = items.Where(i => i.Verdict == IngredientWordingVerdict.Food && i.Hold == null && i.Food != null).ToList();
            if (writes.Count == 0) return;
            var familyKeys = writes.Select(w => LinkGate.FamilyKey(w.Wording)).ToHashSet(StringComparer.Ordinal);
            var table = (await _db.IngredientWordings.AsNoTracking()
                    .Where(w => w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null)
                    .Select(w => new { w.Wording, Food = w.IngredientId!.Value })
                    .ToListAsync(cancellationToken))
                .Where(w => familyKeys.Contains(LinkGate.FamilyKey(w.Wording)) && foods.ContainsKey(w.Food))
                .Select(w => (w.Wording, foods[w.Food]));
            var answers = writes.Select(w => (w.Wording, w.Food!)).ToList();
            var verdicts = LinkGate.Family(answers.Concat(table), gate);
            foreach (var (item, verdict) in writes.Zip(verdicts.Take(answers.Count)).Where(x => !x.Second.Keeps))
                item.Hold = verdict.Winner is { } winner ? $"family: wordings of \"{verdict.Key}\" settle on #{winner}" : $"family: wordings of \"{verdict.Key}\" tie between two foods";
        }

        /// <summary>
        /// Records every decision of one tier and, while the tier is live, loads its rows through the
        /// verified wording loader (source <c>ai:readers:&lt;hash&gt;</c>, the evidence key in the
        /// decision), relinks the batch and has the sentinel read its moved lines. A shadow or paused
        /// tier runs the loader as a dry run. A none or unclear row moves no line and is not audited.
        /// </summary>
        private async Task WriteTierAsync(string tier, List<Item> items, WordingReaderBatchResult batch, Dictionary<string, int> allowance, IReadOnlyDictionary<string, string> signatures,
            IReadOnlyDictionary<long, MeasurementEntity> units, IReadOnlyDictionary<long, IngredientEntity> conversions, DateTime now, CancellationToken cancellationToken)
        {
            var mode = batch.Modes[tier];
            var live = mode == LaneMode.Live;
            var signature = signatures[tier];
            var name = WordingReaders.BatchName(tier, now);
            var rows = new List<(Item Item, VerifiedWordingRow Row)>();
            foreach (var item in items)
            {
                batch.Tiers.Add(tier);
                var outcomeKey = item.Hold != null ? "held" : item.Verdict ?? item.Rule;
                batch.OutcomeByRule[outcomeKey] = batch.OutcomeByRule.GetValueOrDefault(outcomeKey) + 1;
                if (item.Verdict == null)
                {
                    var unreadable = item.Rule == WordingReaders.Unreadable;
                    await RecordAsync(item, tier, mode, false, signature, unreadable ? $"{UnreadableDecision}: {item.Detail}" : $"no row: {item.Rule}{(item.Detail == null ? string.Empty : " (" + item.Detail + ")")}", null, cancellationToken);
                    continue;
                }
                if (item.Hold != null)
                {
                    await RecordAsync(item, tier, mode, false, signature, "held: " + item.Hold, null, cancellationToken);
                    batch.Held++;
                    var rule = item.Hold.Split(':', 2)[0];
                    batch.HeldByRule[rule] = batch.HeldByRule.GetValueOrDefault(rule) + 1;
                    continue;
                }
                var food = item.Verdict == IngredientWordingVerdict.Food;
                if (food && allowance[tier] <= 0)
                {
                    await RecordAsync(item, tier, mode, false, signature, $"{CappedDecision}: the tier's writes for 24 hours are spent", null, cancellationToken);
                    batch.Capped++;
                    continue;
                }
                if (food) allowance[tier]--;
                rows.Add((item, new VerifiedWordingRow
                {
                    Row = item.Wording,
                    Wording = item.Wording,
                    Verdict = item.Verdict,
                    FoodId = item.Food?.Id,
                    FoodName = item.Food?.Name,
                    Note = Clip($"{tier}: {item.Rule}{(item.Detail == null ? string.Empty : " (" + item.Detail + ")")}; A: {item.A?.Reason ?? item.FixedA?.FirstOrDefault()}; B: {item.B?.Reason ?? item.FixedB?.FirstOrDefault()}", 1023),
                    EvidenceKey = item.Evidence,
                }));
            }
            if (rows.Count == 0) return;

            var loaded = await _wordings.LoadAsync(rows.Select(r => r.Row).ToList(), name, apply: live, source: Source);
            var refused = loaded.Problems.GroupBy(p => p.Wording, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Reason, StringComparer.Ordinal);
            var written = new List<Item>();
            foreach (var (item, row) in rows)
            {
                if (refused.TryGetValue(item.Wording, out var reason))
                {
                    await RecordAsync(item, tier, mode, false, signature, "refused by the loader: " + reason, name, cancellationToken);
                    batch.RefusedByReason[reason] = batch.RefusedByReason.GetValueOrDefault(reason) + 1;
                    continue;
                }
                var isFood = row.Verdict == IngredientWordingVerdict.Food;
                await RecordAsync(item, tier, mode, isFood, signature, isFood ? $"{row.FoodId} {row.FoodName}" : row.Verdict + (item.Detail == null ? string.Empty : ": " + item.Detail), name, cancellationToken);
                if (!isFood)
                {
                    if (row.Verdict == IngredientWordingVerdict.None) batch.RowsNone++;
                    else batch.RowsUnclear++;
                    continue;
                }
                batch.WritesByTier[tier] = batch.WritesByTier.GetValueOrDefault(tier) + 1;
                if (!live) continue;
                batch.WrittenByTier[tier] = batch.WrittenByTier.GetValueOrDefault(tier) + 1;
                written.Add(item);
            }

            if (!live) return;
            batch.Batches.Add(name);
            if (written.Count == 0) return;
            var relinked = await new LineRelinkService(_db, _currentUser).LinkVerifiedWordingsAsync(apply: true, batch: name);
            batch.LinesMoved += relinked.Moved;
            await SentinelAsync(tier, name, written, batch, units, conversions, cancellationToken);
        }

        /// <summary>
        /// The sentinel and post-checks after a live batch (wordings design 2.6): the auditor's
        /// same-food judge reads up to 100 of the moved lines, at least one of every wording, and a
        /// wording with two or more read as another food, variety or form is retracted with its
        /// evidence key; six or more wrong per 100 read, or more than three moved lines that weigh
        /// nothing on their new food, are faults that pause the tier.
        /// </summary>
        private async Task SentinelAsync(string tier, string batchName, List<Item> written, WordingReaderBatchResult batch,
            IReadOnlyDictionary<long, MeasurementEntity> units, IReadOnlyDictionary<long, IngredientEntity> conversions, CancellationToken cancellationToken)
        {
            var weightless = written.Sum(i => i.Lines.Count(l => LineRelinkService.NamesWording(l.RawLine, i.Wording) && l.Quantity > 0
                && RecipeNutritionService.GramsFor(l.Quantity, units.GetValueOrDefault(l.MeasurementId), conversions.GetValueOrDefault(i.Food!.Id)) is null or <= 0));
            if (weightless > PauseAtWeightless) Fault(batch, tier, $"{weightless} moved lines weigh nothing on their new food");

            if (!_readers.Sentinel.IsConfigured)
            {
                Fault(batch, tier, "no sentinel model is set, so a live batch's moved lines cannot be read");
                return;
            }
            var queues = written.OrderByDescending(i => i.Candidate.Lines)
                .Select(i => (Item: i, Lines: new Queue<string>(i.Lines.Where(l => LineRelinkService.NamesWording(l.RawLine, i.Wording)).Select(l => l.RawLine).Distinct(StringComparer.Ordinal))))
                .Where(q => q.Lines.Count > 0)
                .ToList();
            var sample = new List<(Item Item, string Line)>();
            while (sample.Count < SentinelLines && queues.Any(q => q.Lines.Count > 0))
                foreach (var q in queues.Where(q => q.Lines.Count > 0))
                {
                    if (sample.Count >= SentinelLines) break;
                    sample.Add((q.Item, q.Lines.Dequeue()));
                }

            var wrongBy = new Dictionary<string, int>(StringComparer.Ordinal);
            int read = 0, wrong = 0;
            foreach (var group in sample.GroupBy(s => s.Item.Wording))
            {
                var item = group.First().Item;
                foreach (var chunk in group.Chunk(SentinelBatch))
                {
                    IReadOnlyList<LinkVerdict> verdicts;
                    try
                    {
                        verdicts = await _readers.Sentinel.JudgeAsync(chunk.Select((s, i) => new LinkPair(i, s.Line, item.Food!.Text)).ToList(), cancellationToken);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or JsonException)
                    {
                        continue;
                    }
                    read += verdicts.Count;
                    var bad = verdicts.Count(v => v.Judgement is LinkJudgement.DifferentFood or LinkJudgement.MoreThanOneFood or LinkJudgement.VarietyOrForm);
                    wrong += bad;
                    wrongBy[item.Wording] = wrongBy.GetValueOrDefault(item.Wording) + bad;
                }
            }
            batch.SentinelJudged += read;
            batch.SentinelDifferent += wrong;
            if (read > 0 && wrong >= Math.Max(RetractAtWrongLines, (int)Math.Ceiling(PauseAtWrongPer100 * read / (double)SentinelLines)))
                Fault(batch, tier, string.Create(CultureInfo.InvariantCulture, $"the sentinel read {wrong} of {read} moved lines as another food"));

            var retract = wrongBy.Where(kv => kv.Value >= RetractAtWrongLines).Select(kv => kv.Key).ToList();
            if (retract.Count == 0) return;
            var rows = await _db.IngredientWordings.AsNoTracking().Where(w => w.Batch == batchName && retract.Contains(w.Wording)).Select(w => new { w.Id, w.Wording }).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var item = written.First(i => i.Wording == row.Wording);
                await _wordings.RetractAsync(row.Id, $"lane {Lane}/{tier}: the sentinel read {wrongBy[row.Wording]} moved lines as another food", apply: true, evidenceKey: item.Evidence);
                batch.Retracted++;
            }
            _db.ChangeTracker.Clear();
        }

        private static void Fault(WordingReaderBatchResult batch, string tier, string fault)
        {
            if (!batch.FaultsByTier.TryGetValue(tier, out var list)) batch.FaultsByTier[tier] = list = new List<string>();
            list.Add(fault);
        }

        private Task RecordAsync(Item item, string tier, string mode, bool writes, string signature, string? decision, string? batchName, CancellationToken cancellationToken) =>
            _frame.RecordAsync(new LaneDecisionInput(
                Lane, tier, Subject(item.Wording), Clip(decision, 255), mode, writes, signature, item.Stratum,
                new WordingReaderSignals(item.Wording, item.Candidate.Id, item.Candidate.Lines, tier, item.Verdict, item.Food?.Id, item.Food?.Name, item.Food?.Text,
                    item.Evidence, batchName, item.A?.ToString() ?? Joined(item.FixedA), item.B?.ToString() ?? Joined(item.FixedB), item.Rule, item.Hold,
                    item.Lines.Select(l => l.RawLine).Distinct(StringComparer.Ordinal).Take(SampleLines).ToList(), item.Means, item.Fixed?.Mode, item.Reading)),
                cancellationToken);

        private static string? Joined(IReadOnlyList<string?>? readings) => readings == null ? null : string.Join(",", readings.Select(r => r ?? "-"));

        /// <summary>
        /// The wordings the tiers left, as the selection reads them: in-use uncurated wordings with no
        /// live row, in key form, that the tier lane decided under its current signatures at their
        /// present line band without any tier answer that writes (no tier answered, or every answer
        /// was held), and that this lane has not decided under its current signature at that band
        /// (an unreadable or capped decision waits out <see cref="RetryAfter"/>). Most sole-blocked
        /// recipes first, then most lines.
        /// </summary>
        private async Task<List<Candidate>> CandidatesAsync(IReadOnlyDictionary<string, string> signatures, int max, WordingReaderBatchResult batch, CancellationToken cancellationToken)
        {
            var waiting = await WordingTierLaneService.WaitingQuery(_db).ToListAsync(cancellationToken);
            if (waiting.Count == 0 || max == 0) return new List<Candidate>();

            var tierSignatures = WordingTiers.All.ToDictionary(t => t, _tiers.Signature);
            tierSignatures[WordingTierLaneService.AbstainTier] = WordingTiers.Signature;
            var left = new Dictionary<(string, string?), bool>();
            foreach (var d in await TierDecisionQuery(_db).ToListAsync(cancellationToken))
            {
                if (d.SignatureHash == null || !tierSignatures.TryGetValue(d.Tier, out var current) || current != d.SignatureHash) continue;
                var key = (d.Subject, d.Stratum);
                var leaves = !d.Writes && !d.Capped;
                left[key] = left.TryGetValue(key, out var was) ? was && leaves : leaves;
            }

            var since = DateTime.UtcNow - RetryAfter;
            var decided = (await DecidedQuery(_db).ToListAsync(cancellationToken))
                .Where(d => d.SignatureHash != null && signatures.TryGetValue(d.Tier, out var current) && current == d.SignatureHash && (!d.Retry || d.CreatedDate >= since))
                .Select(d => (d.Subject, d.Stratum))
                .ToHashSet();

            var pool = new List<WordingTierLaneService.WaitingRow>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in waiting)
            {
                var key = IngredientNameNormalizer.WordingKey(w.Name);
                if (!string.Equals(key, w.Name.ToLowerInvariant(), StringComparison.Ordinal) || !keys.Add(key)) continue;
                var subject = (Subject(key), (string?)WordingTierLaneService.Stratum(w.Lines));
                if (!left.TryGetValue(subject, out var leaves) || !leaves)
                {
                    batch.NotLeftByTiers++;
                    continue;
                }
                if (decided.Contains(subject)) continue;
                pool.Add(w);
            }
            if (pool.Count == 0) return new List<Candidate>();

            var blockers = await BlockersAsync(pool.Select(p => p.Id).ToList(), cancellationToken);
            return pool.Select(p => new Candidate(p.Id, p.Name, p.Lines, blockers.GetValueOrDefault(p.Id)))
                .OrderByDescending(c => c.Blocks).ThenByDescending(c => c.Lines).ThenBy(c => c.Id)
                .Take(max)
                .ToList();
        }

        /// <summary>For each wording, the recipes it alone keeps from being fully linked (no other ingredient line of the recipe is on an uncurated food); kept for six hours.</summary>
        private async Task<Dictionary<long, int>> BlockersAsync(List<long> ids, CancellationToken cancellationToken)
        {
            var cached = _cache.TryGetValue<Dictionary<long, int>>(BlockersKey, out var known) && known != null ? known : new Dictionary<long, int>();
            var missing = ids.Where(id => !cached.ContainsKey(id)).ToList();
            if (missing.Count == 0) return cached;
            var found = new Dictionary<long, int>(cached);
            foreach (var chunk in missing.Chunk(2000))
            {
                var counts = await BlockersQuery(_db, chunk.ToList()).ToDictionaryAsync(b => b.IngredientId, b => b.Recipes, cancellationToken);
                foreach (var id in chunk) found[id] = counts.GetValueOrDefault(id);
            }
            _cache.Set(BlockersKey, found, BlockersLifetime);
            return found;
        }

        /// <summary>A decision of the tier lane, as the selection reads it.</summary>
        internal sealed record TierDecisionRow(string Subject, string Tier, string? SignatureHash, string? Stratum, bool Writes, bool Capped);

        /// <summary>A decision of this lane, as the selection reads it: whether it waits to be asked again, and when it was made.</summary>
        internal sealed record DecidedRow(string Subject, string Tier, string? SignatureHash, string? Stratum, bool Retry, DateTime CreatedDate);

        /// <summary>A wording and the recipes it alone blocks.</summary>
        internal sealed record BlockerRow(long IngredientId, int Recipes);

        internal static IQueryable<TierDecisionRow> TierDecisionQuery(ApplicationDbContext db) =>
            db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == WordingTierLaneService.Lane)
                .Select(d => new { d.Subject, d.Tier, d.SignatureHash, d.Stratum, d.Writes, Capped = d.Decision != null && d.Decision.StartsWith(WordingTierLaneService.CappedDecision) })
                .Distinct()
                .Select(d => new TierDecisionRow(d.Subject, d.Tier, d.SignatureHash, d.Stratum, d.Writes, d.Capped));

        internal static IQueryable<DecidedRow> DecidedQuery(ApplicationDbContext db) =>
            db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == Lane && d.Subject.StartsWith("wording:"))
                .Select(d => new DecidedRow(d.Subject, d.Tier, d.SignatureHash, d.Stratum,
                    d.Decision != null && (d.Decision.StartsWith(UnreadableDecision) || d.Decision.StartsWith(CappedDecision)), d.CreatedDate));

        internal static IQueryable<BlockerRow> BlockersQuery(ApplicationDbContext db, List<long> ids) =>
            db.RecipeIngredients.AsNoTracking()
                .Where(ri => ids.Contains(ri.IngredientId) && !ri.Recipe.IsDeleted && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient)
                    && !db.RecipeIngredients.Any(o => o.RecipeId == ri.RecipeId && o.IngredientId != ri.IngredientId
                        && (o.LineKind == null || o.LineKind == RecipeIngredientLineKind.Ingredient)
                        && o.Ingredient.CurationStatusId != (long)CurationStatusEnum.Curated))
                .GroupBy(ri => ri.IngredientId)
                .Select(g => new BlockerRow(g.Key, g.Select(x => x.RecipeId).Distinct().Count()));

        /// <summary>
        /// What the packs read about each recipe of the wordings: its title and whether it came from a
        /// web source (not a public-domain book), and, for a wording whose lines hold an alcohol word,
        /// its other lines and steps for the alcohol rules and the fixed readings.
        /// </summary>
        private async Task<Dictionary<long, RecipeFacts>> RecipesAsync(List<Candidate> candidates, Dictionary<long, List<LineRow>> lines, CancellationToken cancellationToken)
        {
            var all = candidates.SelectMany(c => lines.GetValueOrDefault(c.Id) ?? new List<LineRow>()).Select(l => l.RecipeId).Distinct().ToList();
            if (all.Count == 0) return new Dictionary<long, RecipeFacts>();
            var alcohol = candidates
                .Where(c => AlcoholLineContext.Read(c.Name).Kind != AlcoholSenseKind.None
                    || (lines.TryGetValue(c.Id, out var own) && own.Any(l => AlcoholLineContext.Read(l.RawLine).Kind != AlcoholSenseKind.None)))
                .SelectMany(c => lines.GetValueOrDefault(c.Id) ?? new List<LineRow>())
                .Select(l => l.RecipeId)
                .ToHashSet();

            var recipes = new List<(long Id, string Name, bool Web)>();
            foreach (var chunk in all.Chunk(2000))
                recipes.AddRange((await _db.Recipes.AsNoTracking().Where(r => chunk.Contains(r.Id))
                    .Select(r => new { r.Id, r.Name, Web = r.SourceSite != null && r.LicenseStatus != RecipeLicenseStatus.PublicDomain })
                    .ToListAsync(cancellationToken)).Select(r => (r.Id, r.Name, r.Web)));
            var ids = alcohol.ToList();
            var others = ids.Count == 0 ? Enumerable.Empty<string>().ToLookup(_ => 0L) : (await _db.RecipeIngredients.AsNoTracking().Where(ri => ids.Contains(ri.RecipeId))
                    .Select(ri => new { ri.RecipeId, ri.RawLine }).ToListAsync(cancellationToken))
                .ToLookup(ri => ri.RecipeId, ri => ri.RawLine);
            var steps = ids.Count == 0 ? Enumerable.Empty<string>().ToLookup(_ => 0L) : (await _db.RecipeSteps.AsNoTracking().Where(s => ids.Contains(s.RecipeId)).OrderBy(s => s.StepNumber)
                    .Select(s => new { s.RecipeId, s.Description }).ToListAsync(cancellationToken))
                .ToLookup(s => s.RecipeId, s => s.Description ?? string.Empty);
            return recipes.ToDictionary(r => r.Id, r => new RecipeFacts(r.Name, r.Web,
                alcohol.Contains(r.Id) ? new AlcoholRecipeContext(r.Name, others[r.Id].ToList(), string.Join(" ", steps[r.Id]), r.Web) : null));
        }

        private async Task<Dictionary<long, IngredientEntity>> ConversionsAsync(List<Item> items, IReadOnlyDictionary<long, GateFood> foods, CancellationToken cancellationToken)
        {
            var fixedFdcIds = items.Where(i => i.Fixed != null).SelectMany(i => AlcoholFixedReading.Of(i.Fixed!.Mode)).Where(r => r.FdcId != null).Select(r => r.FdcId!).ToHashSet(StringComparer.Ordinal);
            var ids = items.SelectMany(i => i.Pack?.Candidates.Select(c => c.Id) ?? Enumerable.Empty<long>())
                .Concat(foods.Values.Where(f => f.FdcId != null && fixedFdcIds.Contains(f.FdcId)).Select(f => f.Id))
                .Distinct().ToList();
            var conversions = new Dictionary<long, IngredientEntity>();
            foreach (var chunk in ids.Chunk(2000))
                foreach (var c in await _db.Ingredients.AsNoTracking().Where(i => chunk.Contains(i.Id))
                    .Select(i => new { i.Id, i.GramsPerMilliliter, i.GramsPerItem, i.ReferenceServingGrams }).ToListAsync(cancellationToken))
                    conversions[c.Id] = new IngredientEntity { GramsPerMilliliter = c.GramsPerMilliliter, GramsPerItem = c.GramsPerItem, ReferenceServingGrams = c.ReferenceServingGrams };
            return conversions;
        }

        private static string UnitClass(MeasurementEntity? unit, decimal quantity) =>
            WordingHeadDefaults.UnitClass(unit?.Name, unit?.MeasurementCategoryId == RecipeNutritionService.CategoryMass,
                unit?.MeasurementCategoryId == RecipeNutritionService.CategoryVolume, unit?.IsContainer == true, quantity);

        private static List<GateLine> GateLines(List<LineRow> lines, IngredientEntity? food, IReadOnlyDictionary<long, MeasurementEntity> units, IReadOnlyDictionary<long, RecipeFacts> recipes, string? sense) =>
            lines.Select(l =>
            {
                var unit = units.GetValueOrDefault(l.MeasurementId);
                var grams = l.Quantity <= 0 ? 0m : RecipeNutritionService.GramsFor(l.Quantity, unit, food);
                return new GateLine(l.RawLine, unit?.Name, grams, recipes.GetValueOrDefault(l.RecipeId)?.Alcohol, sense);
            }).ToList();

        public async Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string tier, IReadOnlyList<string> faults, CancellationToken cancellationToken = default)
        {
            var reasons = faults.ToList();
            var lane = WordingReaderRecords.LaneOfTier.TryGetValue(tier, out var l) ? l : WordingReaderRecords.ReadLane;
            var golden = WordingReaderRecords.Evaluate(lane, _records(lane), WordingReaderRecords.EmbeddedFloor(lane), _readers.ReaderA.ModelName, _readers.ReaderB.ModelName);
            var (score, auditProblem) = WordingTierAuditRecord.Evaluate(_auditRecord(), _readers.AuditorModel, _readers.CheckModel);
            golden ??= auditProblem;
            if (!_readers.IsConfigured)
            {
                reasons.Add("no model host or model is set for the readers, the auditor and reader B (Ai:BatchOllamaUrl or Ai:OllamaUrl, Ai:Roster)");
            }
            else
            {
                try
                {
                    var audit = await _frame.AuditAsync(Lane, tier, DateTime.UtcNow.AddDays(-30), Sample, _readers.Auditor, _readers.Check, null, cancellationToken);
                    if (audit.Sampled >= 10 && audit.Unreadable * 5 > audit.Sampled)
                        reasons.Add($"the auditor {_readers.AuditorModel} or reader B {_readers.CheckModel} gave no readable answer on {audit.Unreadable} of {audit.Sampled} sampled decisions");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Wording readers {Tier}: the audit could not run", tier);
                    reasons.Add($"the auditor {_readers.AuditorModel} could not be reached: {ex.GetType().Name}: {ex.Message}");
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
    /// The wording reader lane's undo for the lane frame. For the read and alcohol tiers: every row
    /// the lane loaded live in a batch of that tier since a time (food, none and unclear rows alike)
    /// is retracted through the verified wording loader's per-wording retract, with the evidence key
    /// its decision recorded, so its lines go back and the rejection memory keeps the pair out on the
    /// same evidence. For the lexicon tier: every entry the lane wrote since then is taken out,
    /// journaled, and remembered so the same synonym is not written again.
    /// </summary>
    public sealed class WordingReaderUndo : ILaneUndo
    {
        private readonly ApplicationDbContext _db;
        private readonly IVerifiedWordingService _wordings;
        private readonly ICurrentUserService _currentUser;

        public WordingReaderUndo(ApplicationDbContext db, IVerifiedWordingService wordings, ICurrentUserService currentUser)
        {
            _db = db;
            _wordings = wordings;
            _currentUser = currentUser;
        }

        public string Lane => WordingReaderLaneService.Lane;

        public async Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken)
        {
            var since = sinceUtc.ToString("u", CultureInfo.InvariantCulture);
            var prefix = WordingReaders.BatchPrefix + tier + "-";
            if (tier == WordingReaders.Lexicon)
            {
                var entries = await _db.WordingLexicon.Where(e => e.Batch.StartsWith(prefix) && e.CreatedDate >= sinceUtc).ToListAsync(cancellationToken);
                if (apply)
                {
                    var now = DateTime.UtcNow;
                    foreach (var entry in entries)
                    {
                        entry.IsDeleted = true;
                        entry.DeletedAt = now;
                        entry.DeletedByPersonId = _currentUser.PersonIdOrSystem;
                        _db.AuditLogEntries.Add(new AuditLogEntryEntity
                        {
                            EntityType = WordingReaderLaneService.LexiconEntityType, EntityId = entry.Id, ChangeType = WordingReaderLaneService.LexiconUndoChangeType,
                            PropertyName = entry.Batch, OldValue = entry.Term + "=" + entry.Means, Timestamp = now, ChangedByPersonId = _currentUser.PersonIdOrSystem,
                        });
                    }
                    await _db.SaveChangesAsync(cancellationToken);
                }
                return new LaneUndoResult(entries.Count, 0, 0, $"wording readers lexicon: entries written since {since}");
            }

            var rows = await _db.IngredientWordings.AsNoTracking()
                .Where(w => w.Source.StartsWith(WordingReaders.SourcePrefix) && w.Batch.StartsWith(prefix) && (w.LastModifiedDate ?? w.CreatedDate) >= sinceUtc)
                .OrderByDescending(w => w.Id)
                .Select(w => new { w.Id, w.Wording })
                .ToListAsync(cancellationToken);
            var keys = rows.Select(r => WordingReaderLaneService.Subject(r.Wording)).ToList();
            var evidence = (await _db.LaneDecisions.AsNoTracking()
                    .Where(d => d.Lane == Lane && d.Tier == tier && keys.Contains(d.Subject) && d.Mode == LaneMode.Live)
                    .OrderByDescending(d => d.Id)
                    .Select(d => d.SignalsJson)
                    .ToListAsync(cancellationToken))
                .Select(LaneJournal.Read<WordingReaderSignals>)
                .Where(s => s?.EvidenceKey != null)
                .GroupBy(s => s!.Wording, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First()!.EvidenceKey, StringComparer.Ordinal);

            int undone = 0, skipped = 0;
            foreach (var row in rows)
            {
                if (apply)
                {
                    var reason = $"lane {Lane}/{tier} undone its writes since {since}";
                    if (await _wordings.RetractAsync(row.Id, reason, apply: true, evidenceKey: evidence.GetValueOrDefault(row.Wording)) == null)
                    {
                        skipped++;
                        continue;
                    }
                }
                undone++;
            }
            return new LaneUndoResult(undone, skipped, 0, $"wording readers {tier}: rows loaded since {since}, retracted with their evidence");
        }
    }
}
