using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Import.Golden
{
    public static partial class ModelLanes
    {
        /// <summary>The lanes of the wording readers and their auditor, scored with reader A (or the auditor) as the model and reader B as the check model.</summary>
        public static readonly IReadOnlyList<string> Readers = new[] { WordingReadLane, WordingLexiconLane, AlcoholReadingLane, LinkAuditLane };

        /// <summary>Known-bad and known-good link pairs link-audit reads, each taken by a stable hash of its id so the set does not move with the file's order.</summary>
        public const int AuditPairsPerSide = 400;

        /// <summary>The check model a run records, only for a lane that asks one (the reader lanes, link-audit and prose-rewrite): the one named, else reader B of the roster; else none, whatever was named.</summary>
        public static string? CheckModelOf(string lane, string? named) =>
            !Readers.Contains(lane) && lane != ProseRewriteLane ? null
            : named is { Length: > 0 } ? named : new Nom.Orch.Settings.LaneModelRoster().ReaderB;

        /// <summary>The auditor a run scores alone and records: for link-audit the model scored; for alcohol-reading the one named, else the roster's auditor; else none.</summary>
        public static string? AuditModelOf(string lane, string model, string? named) => lane switch
        {
            LinkAuditLane => model,
            AlcoholReadingLane => named is { Length: > 0 } ? named : new Nom.Orch.Settings.LaneModelRoster().Auditor,
            _ => null,
        };

        /// <summary>
        /// One wording the readers are scored on: a row of wording-pairs.tsv (= or !), or a judged pair
        /// of link-pairs.tsv whose wording the deterministic tiers leave (no tier writes it), as the
        /// lane selects it.
        /// </summary>
        public sealed record ReadCase(string Id, string Wording, long FoodId, bool Right, string Description, string JudgedBy)
        {
            public LaneCaseResult Outcome(long? written, string actual)
            {
                var accepted = written == FoodId;
                return new LaneCaseResult(Id, Right ? accepted : !accepted, !Right && accepted, !Right,
                    Right ? $"#{FoodId} ({Wording} -> {Description})" : $"not #{FoodId} ({Wording} -> {Description})", actual, JudgedBy, accepted);
            }
        }

        public static IEnumerable<ReadCase> ReadCases(string goldenDir)
        {
            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "wording-pairs.tsv")).Rows)
            {
                if (r["expect"] is not ("=" or "!")) continue;
                yield return new ReadCase("read:" + r.Id, IngredientNameNormalizer.WordingKey(r["wording"]), long.Parse(r["foodId"]), r["expect"] == "=", r["name"], r["judgedBy"]);
            }

            var catalog = GoldenTierCatalog(goldenDir);
            var decisions = new Dictionary<string, WordingTierDecision>(StringComparer.Ordinal);
            foreach (var c in TierCases(goldenDir))
            {
                if (!decisions.TryGetValue(c.Wording, out var decision))
                    decisions[c.Wording] = decision = WordingTiers.Decide(c.Wording, 0, Array.Empty<TierLine>(), catalog);
                if (decision.Writes || LinkAutoApplyGuard.IsCompound(c.Wording)) continue;
                yield return new ReadCase("read:" + c.Id, c.Wording, c.FoodId, c.Right, c.Description, c.JudgedBy);
            }
        }

        /// <summary>One row of wording-lexicon.tsv: a term, what it means (or, for a ! row, what it does not mean), and who judged it.</summary>
        public sealed record LexiconCase(string Id, string Term, string Means, bool Right, string JudgedBy)
        {
            public LaneCaseResult Outcome(string? entry)
            {
                var matches = entry != null && Matches(entry, Means);
                return new LaneCaseResult(Id, Right ? matches : !matches, !Right && matches, !Right,
                    Right ? $"{Term} means {Means}" : $"{Term} does not mean {Means}", entry == null ? "no entry" : $"{Term} means {entry}", JudgedBy, entry != null);
            }

            /// <summary>Whether an entry's meaning and the judged one name the same food: the folded words of one hold every word of the other.</summary>
            public static bool Matches(string entry, string means)
            {
                var a = LinkGate.Words(entry).Select(LinkGate.Fold).ToHashSet(StringComparer.Ordinal);
                var b = LinkGate.Words(means).Select(LinkGate.Fold).ToHashSet(StringComparer.Ordinal);
                return a.Count > 0 && b.Count > 0 && (a.IsSubsetOf(b) || b.IsSubsetOf(a));
            }
        }

        public static IEnumerable<LexiconCase> LexiconCases(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, "wording-lexicon.tsv")).Rows
                .Select(r => new LexiconCase("lexicon:" + r.Id, r["term"], r["means"], r["expect"] == "=", r["judgedBy"]));

        /// <summary>
        /// One row of alcohol-readings.tsv: a line whose alcohol word the rules leave unclear, with what
        /// its recipe says, and the reading it must get: a reading code, "unsure" (nothing may be
        /// written) or "!code" (anything but that reading, which a data rule refuses).
        /// </summary>
        public sealed record AlcoholReadingCase(string Id, string Mode, FixedLine Line, string Expect, string JudgedBy)
        {
            public FixedPack Pack => new(Line.Raw, Mode, new[] { Line });

            public LaneCaseResult Outcome(string? combined, string? audited = null)
            {
                var refused = Expect.StartsWith('!') ? Expect[1..] : null;
                var pass = refused != null ? combined != refused : Expect == AlcoholFixedReading.Unsure ? combined == null : combined == Expect;
                var falseAccept = combined != null && (refused != null ? combined == refused : combined != Expect);
                var (good, bad) = AuditorFlags(audited);
                return new LaneCaseResult(Id, pass, falseAccept, true, Expect, combined ?? "nothing written", JudgedBy, combined != null, AuditorFlagsGood: good, AuditorFlagsBad: bad);
            }

            /// <summary>
            /// How the auditor's own reading of the line flags the judged reading (known good) and the
            /// other reading of its mode (known bad): a flag when it reads the line another way. Only a
            /// row judged to one reading counts, and only when the auditor gave a reading.
            /// </summary>
            public (bool? Good, bool? Bad) AuditorFlags(string? audited)
            {
                if (audited is null or AlcoholFixedReading.Unsure || AlcoholFixedReading.Find(Mode, Expect) is null) return (null, null);
                var other = AlcoholFixedReading.Of(Mode).Select(r => r.Code).FirstOrDefault(c => c != Expect);
                return (audited != Expect, other == null ? null : audited != other);
            }
        }

        public static IEnumerable<AlcoholReadingCase> AlcoholReadingCases(string goldenDir)
        {
            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "alcohol-readings.tsv")).Rows)
            {
                var others = r.Value("context")?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                yield return new AlcoholReadingCase("alcohol-reading:" + r.Id, r["mode"],
                    new FixedLine(r["raw"], r.Value("title"), others, r.Value("steps"), r["site"] == "yes"), r["expect"], r["source"]);
            }
        }

        /// <summary>The recipe context of an alcohol-readings.tsv row as the alcohol rules read it.</summary>
        public static AlcoholRecipeContext? Context(FixedLine line) =>
            line.Title == null && line.OtherLines.Count == 0 && line.Steps == null && !line.HasSourceSite
                ? null
                : new AlcoholRecipeContext(line.Title, line.OtherLines.Count == 0 ? null : line.OtherLines, line.Steps, line.HasSourceSite);

        /// <summary>One link pair the auditor and its confirming reader read: wrong, near or compound (known bad) or right (known good).</summary>
        public sealed record AuditCase(string Id, string Wording, string Description, bool Bad, string JudgedBy)
        {
            public LaneCaseResult Outcome(LinkJudgement? auditor, LinkJudgement? check)
            {
                var alone = auditor is { } a && a != LinkJudgement.Same && a != LinkJudgement.Unsure;
                var flagged = alone && check is { } c && c != LinkJudgement.Same && c != LinkJudgement.Unsure;
                return new LaneCaseResult(Id, flagged == Bad, Bad && !flagged, Bad, Bad ? "flagged" : "not flagged",
                    auditor == null ? "no readable answer" : $"{auditor}{(check == null ? string.Empty : " / " + check)}", JudgedBy, Flagged: flagged,
                    AuditorFlagsGood: Bad ? null : alone, AuditorFlagsBad: Bad ? alone : null);
            }
        }

        /// <summary>The known-bad and known-good pairs of link-pairs.tsv link-audit reads: <see cref="AuditPairsPerSide"/> of each, by the stable hash of the pair's id.</summary>
        public static IEnumerable<AuditCase> AuditCases(string goldenDir)
        {
            var rows = GoldenTsv.Read(Path.Combine(goldenDir, "link-pairs.tsv")).Rows.Where(r => r["verdict"] == "ok" || Bad.Contains(r["verdict"])).ToList();
            string Order(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
            foreach (var bad in new[] { true, false })
                foreach (var r in rows.Where(r => Bad.Contains(r["verdict"]) == bad).OrderBy(r => Order(r.Id), StringComparer.Ordinal).Take(AuditPairsPerSide))
                    yield return new AuditCase("audit:" + r.Id, r["wording"], r["description"], bad, r["judgedBy"]);
        }

        /// <summary>
        /// The reader lanes and link-audit. wording-read: readers A (the model named) and B (the check
        /// model) read every case's wording on its pack (no lines in the fixtures; candidates from the
        /// frozen catalog), the refuter clears agreed synonyms, the outcome table and the gate decide,
        /// and a case passes when the food written is the judged one (= row) or not (! row).
        /// wording-lexicon: both readers read the term with what it is said to mean among the
        /// candidates, and an entry is written when they propose the same meaning and the refuter finds
        /// none. alcohol-reading: both readers give the fixed reading of each line and the combination
        /// (unsure, disagreement and the data rules) decides. link-audit: the auditor (the model named)
        /// reads each pair with the same-food judge and the check model re-reads the pairs it flags; a
        /// pair counts as flagged only when both find it wrong, and its known-bad and known-good flags
        /// are the audit check's score the lane frame corrects with.
        /// </summary>
        private static async Task<IReadOnlyList<LaneCaseResult>> ReaderResultsAsync(string lane, string goldenDir, HttpClient http, IConfiguration config, int? limit, Action<string> progress, CancellationToken cancellationToken)
        {
            var host = config["Ai:BatchOllamaUrl"] ?? string.Empty;
            var model = config["Ai:LinkModel"] ?? string.Empty;
            var checkModel = CheckModelOf(lane, config[CheckModelKey])!;
            var results = new List<LaneCaseResult>();
            switch (lane)
            {
                case LinkAuditLane:
                {
                    var cases = AuditCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var auditor = new OllamaIngredientLinkVerifier(http, host, model);
                    var check = new OllamaIngredientLinkVerifier(http, host, checkModel);
                    foreach (var batch in cases.Chunk(VerifyBatch))
                    {
                        var pairs = batch.Select((c, i) => new LinkPair(i, c.Wording, c.Description)).ToList();
                        var first = (await AskAsync(() => auditor.JudgeAsync(pairs, cancellationToken), progress, cancellationToken)).ToDictionary(v => v.IngredientId, v => v.Judgement);
                        var flagged = pairs.Where(p => first.TryGetValue(p.IngredientId, out var j) && j is not (LinkJudgement.Same or LinkJudgement.Unsure)).ToList();
                        var second = flagged.Count == 0 ? new Dictionary<long, LinkJudgement>() : (await AskAsync(() => check.JudgeAsync(flagged, cancellationToken), progress, cancellationToken)).ToDictionary(v => v.IngredientId, v => v.Judgement);
                        results.AddRange(batch.Select((c, i) => c.Outcome(first.TryGetValue(i, out var a) ? a : null, second.TryGetValue(i, out var b) ? b : null)));
                        progress($"{lane}: {results.Count}/{cases.Count}");
                    }
                    return results;
                }
                case AlcoholReadingLane:
                {
                    var cases = AlcoholReadingCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var a = new OllamaWordingReader(http, host, model);
                    var b = new OllamaWordingReader(http, host, checkModel);
                    var auditor = new OllamaWordingReader(http, host, AuditModelOf(lane, model, config[AuditModelKey])!);
                    var first = new List<IReadOnlyList<string?>>();
                    foreach (var c in cases) first.Add(await ReadFixedOrNothing(a, c.Pack, cancellationToken));
                    var second = new List<IReadOnlyList<string?>>();
                    foreach (var c in cases) second.Add(await ReadFixedOrNothing(b, c.Pack, cancellationToken));
                    for (var i = 0; i < cases.Count; i++)
                    {
                        var (reading, _) = AlcoholFixedReading.Combine(cases[i].Pack, first[i], second[i]);
                        var audited = (await ReadFixedOrNothing(auditor, cases[i].Pack, cancellationToken)).FirstOrDefault();
                        results.Add(cases[i].Outcome(reading?.Code, audited));
                        progress($"{lane}: {results.Count}/{cases.Count}");
                    }
                    return results;
                }
                default:
                {
                    var (matcher, staples) = UsdaMatcher(goldenDir);
                    var foods = GateFoods(goldenDir).Where(f => !f.Deleted).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
                    var gate = GateCatalog.Build(foods.Values, matcher, staples: staples);
                    var a = new OllamaWordingReader(http, host, model);
                    var b = new OllamaWordingReader(http, host, checkModel);
                    WordingPack PackOf(string wording, params string[] extra)
                    {
                        var ids = WordingReaderLaneService.CandidateFoods(wording, matcher, staples, gate, AlcoholLineContext.Read(wording), foods);
                        foreach (var more in extra)
                            foreach (var c in matcher.Shortlist(more, 4)) if (!ids.Contains(c.IngredientId) && foods.ContainsKey(c.IngredientId)) ids.Add(c.IngredientId);
                        return WordingReaders.Pack(wording, Array.Empty<(long, ReaderLine)>(), ids.Select(id => foods[id]).Select(f => new ReaderFood(f.Id, f.Name, f.Text, f.CategoryId, f.NutrientRows)).ToList());
                    }

                    if (lane == WordingLexiconLane)
                    {
                        var cases = LexiconCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                        var packs = cases.Select(c => PackOf(c.Term, c.Means)).ToList();
                        var answersA = await ReadAllAsync(a, packs, "A", cancellationToken);
                        var answersB = await ReadAllAsync(b, packs, "B", cancellationToken);
                        for (var i = 0; i < cases.Count; i++)
                        {
                            var term = WordingReaders.LexiconTerm(cases[i].Term) ?? cases[i].Term;
                            var means = WordingReaders.AgreedMeaning(answersA[i], answersB[i], term);
                            string? entry = null;
                            if (means != null && await RefuteOrRefusedAsync(a, term, means, cancellationToken) == null) entry = means;
                            results.Add(cases[i].Outcome(entry));
                            progress($"{lane}: {results.Count}/{cases.Count}");
                        }
                        return results;
                    }

                    var readCases = ReadCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var wordings = readCases.Select(c => c.Wording).Distinct(StringComparer.Ordinal).ToList();
                    var wordingPacks = wordings.Select(w => PackOf(w)).ToList();
                    var readA = await ReadAllAsync(a, wordingPacks, "A", cancellationToken);
                    var readB = await ReadAllAsync(b, wordingPacks, "B", cancellationToken);
                    var lexicon = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (var i = 0; i < wordings.Count; i++)
                    {
                        if (WordingReaders.LexiconTerm(wordings[i]) is not { } term || WordingReaders.AgreedMeaning(readA[i], readB[i], term) is not { } means) continue;
                        if (await RefuteOrRefusedAsync(a, term, means, cancellationToken) == null) lexicon.TryAdd(LinkGate.Fold(term), means);
                    }
                    var withLexicon = WordingReaders.WithLexicon(gate, lexicon);
                    var written = new Dictionary<string, (long? Food, string Actual)>(StringComparer.Ordinal);
                    for (var i = 0; i < wordings.Count; i++)
                    {
                        var outcome = WordingReaders.Combine(readA[i], readB[i], wordingPacks[i]);
                        if (outcome.Verdict != IngredientWordingVerdict.Food)
                        {
                            written[wordings[i]] = (null, outcome.Verdict ?? outcome.Rule);
                            continue;
                        }
                        var holds = LinkGate.Check(new GatePair(wordings[i], 0, foods[outcome.FoodId!.Value], ReadersAgreed: true), withLexicon);
                        written[wordings[i]] = holds.Count == 0
                            ? (outcome.FoodId, $"#{outcome.FoodId} ({outcome.Rule})")
                            : (null, $"#{outcome.FoodId} held: {string.Join("; ", holds.Select(h => h.Code))}");
                    }
                    foreach (var c in readCases)
                    {
                        var (food, actual) = written[c.Wording];
                        results.Add(c.Outcome(food, actual));
                    }
                    progress($"{lane}: {results.Count}/{readCases.Count}");
                    return results;
                }
            }
        }

        private static async Task<List<ReaderAnswer?>> ReadAllAsync(IWordingReader reader, IReadOnlyList<WordingPack> packs, string role, CancellationToken cancellationToken)
        {
            var shuffled = packs.Select(p => WordingReaders.Shuffled(p, role)).ToList();
            var index = shuffled.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i, ReferenceEqualityComparer.Instance);
            var answers = new ReaderAnswer?[packs.Count];
            foreach (var call in WordingReaders.Calls(shuffled))
            {
                IReadOnlyList<ReaderAnswer?> read;
                try
                {
                    read = await AskAsync(() => reader.ReadAsync(call, cancellationToken), null, cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                for (var i = 0; i < call.Count && i < read.Count; i++) answers[index[call[i]]] = read[i];
            }
            return answers.ToList();
        }

        private static async Task<IReadOnlyList<string?>> ReadFixedOrNothing(IWordingReader reader, FixedPack pack, CancellationToken cancellationToken)
        {
            try
            {
                return await AskAsync(() => reader.ReadFixedAsync(pack, cancellationToken), null, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return new string?[pack.Lines.Count];
            }
        }

        private static async Task<string?> RefuteOrRefusedAsync(IWordingReader refuter, string term, string means, CancellationToken cancellationToken)
        {
            try
            {
                return await AskAsync(() => refuter.RefuteAsync(term, means, Array.Empty<string>(), cancellationToken), null, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return "no readable answer";
            }
        }
    }
}
