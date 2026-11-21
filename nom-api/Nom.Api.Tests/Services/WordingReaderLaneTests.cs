using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The wording reader lane on the lane frame, with fake readers: it reads only what the tiers
    /// left, a shadow tier records and writes nothing, a live tier writes through the verified
    /// wording loader with source ai:readers:&lt;hash&gt; and an evidence key and moves the lines by a
    /// batch-scoped relink, disagreement writes nothing, none and unclear rows need their checks, a
    /// synonym both readers propose and the refuter lets stand becomes a lexicon entry the gate reads,
    /// an unclear alcohol word is written only on one fixed reading, the sentinel retracts a wording
    /// it reads as another food, the undo takes rows and entries back, and no tier leaves shadow
    /// without its golden records.
    /// </summary>
    public class WordingReaderLaneTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private sealed class FakeTiers : IWordingTierLaneService
        {
            public string Signature(string tier) => "tier-signature-" + tier;
            public Task<WordingTierBatchResult> RunBatchAsync(IReadOnlyDictionary<string, string> modes, int max, IReadOnlyDictionary<string, int>? remaining = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string tier, IReadOnlyList<string> faults, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<int> WritesTodayAsync(string tier, CancellationToken cancellationToken = default) => Task.FromResult(0);
        }

        private sealed class FakeReader : IWordingReader
        {
            public Func<WordingPack, ReaderAnswer?> Answer { get; set; } = _ => null;
            public Func<FixedLine, string?> Fixed { get; set; } = _ => null;
            public Func<string, string, string?> Refute { get; set; } = (_, _) => null;
            public List<WordingPack> Seen { get; } = new();
            public List<FixedLine> SeenFixed { get; } = new();

            public FakeReader(string model) => ModelName = model;

            public bool IsConfigured => true;
            public string ModelName { get; }

            public Task<IReadOnlyList<ReaderAnswer?>> ReadAsync(IReadOnlyList<WordingPack> packs, CancellationToken cancellationToken = default)
            {
                Seen.AddRange(packs);
                return Task.FromResult<IReadOnlyList<ReaderAnswer?>>(packs.Select(Answer).ToList());
            }

            public Task<IReadOnlyList<string?>> ReadFixedAsync(FixedPack pack, CancellationToken cancellationToken = default)
            {
                SeenFixed.AddRange(pack.Lines);
                return Task.FromResult<IReadOnlyList<string?>>(pack.Lines.Select(Fixed).ToList());
            }

            public Task<string?> RefuteAsync(string term, string means, IReadOnlyList<string> lines, CancellationToken cancellationToken = default) =>
                Task.FromResult(Refute(term, means));
        }

        private sealed class FakeSentinel : IIngredientLinkVerifier
        {
            public LinkJudgement Judgement { get; set; } = LinkJudgement.Same;
            public bool IsConfigured => true;
            public string ModelName => "phi4";
            public int PromptVersion => OllamaIngredientLinkVerifier.CurrentPromptVersion;

            public Task<IReadOnlyList<LinkVerdict>> JudgeAsync(IReadOnlyList<LinkPair> pairs, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<LinkVerdict>>(pairs.Select(p => new LinkVerdict(p.IngredientId, Judgement)).ToList());
        }

        private sealed class FakeAuditor : ILaneAuditor
        {
            public string Model => "phi4";
            public bool? Wrong { get; set; } = false;
            public List<LaneDecisionEntity> Seen { get; } = new();

            public Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                Seen.Add(decision);
                return Task.FromResult(new LaneAuditAnswer(Wrong, Wrong == true ? "different" : "same"));
            }
        }

        private sealed class FakeReaders : IWordingReaders
        {
            public FakeReader A { get; } = new("qwen2.5:14b-instruct");
            public FakeReader B { get; } = new("mistral-nemo");
            public FakeSentinel Judge { get; } = new();
            public FakeAuditor Pairs { get; } = new();
            public FakeAuditor Readings { get; } = new();
            public bool IsConfigured => true;
            public IWordingReader ReaderA => A;
            public IWordingReader ReaderB => B;
            public IIngredientLinkVerifier Sentinel => Judge;
            public string AuditorModel => "phi4";
            public ILaneAuditor Auditor => Pairs;
            public ILaneAuditor FixedAuditor => Readings;

            public void Both(Func<WordingPack, ReaderAnswer?> answer)
            {
                A.Answer = answer;
                B.Answer = answer;
            }
        }

        private const long Gram = 1;
        private const long Teaspoon = 2;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static WordingReaderLaneService Lane(ApplicationDbContext db, FakeReaders readers,
            Func<string, WordingReaderRecords.Record?>? records = null, Func<WordingTierAuditRecord.Record?>? auditRecord = null) =>
            new(db, new VerifiedWordingService(db, new NoUser()), new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance),
                readers, new FakeTiers(), new NoUser(), new MemoryCache(new MemoryCacheOptions()), NullLogger<WordingReaderLaneService>.Instance,
                records ?? (_ => null), auditRecord ?? (() => null));

        private static WordingReaderUndo Undo(ApplicationDbContext db) => new(db, new VerifiedWordingService(db, new NoUser()), new NoUser());

        private static IngredientEntity Food(string name, string description, string fdcId, int category, decimal density = 1m)
        {
            var food = new IngredientEntity
            {
                Name = name,
                FdcId = fdcId,
                FdcDataType = CatalogAdmissionRule.SrLegacyFood,
                FdcCategoryId = category,
                CurationStatusId = (long)CurationStatusEnum.Curated,
                GramsPerMilliliter = density,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            food.Aliases.Add(new IngredientAliasEntity { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext });
            return food;
        }

        private static IngredientEntity Wording(string name) => new() { Name = name, CurationStatusId = (long)CurationStatusEnum.NonCurated };

        private static RecipeEntity Recipe(string name, params (IngredientEntity Food, string Line, decimal Quantity, long Unit)[] lines) => new()
        {
            Name = name,
            ScrapedAtUtc = DateTime.UtcNow,
            RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
            {
                IngredientId = l.Food.Id, SortOrder = i, Quantity = l.Quantity, MeasurementId = l.Unit, RawLine = l.Line,
            }).ToList(),
        };

        private sealed record Kitchen(IngredientEntity BakingSoda, IngredientEntity HardCider, IngredientEntity Saleratus, IngredientEntity Soda, IngredientEntity Stock, IngredientEntity Cider, IngredientEntity PlainSoda);

        private static async Task<Kitchen> KitchenAsync(ApplicationDbContext db)
        {
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "gram", Symbol = "g", MeasurementCategoryId = RecipeNutritionService.CategoryMass, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Teaspoon, Name = "teaspoon", Symbol = "tsp", MeasurementCategoryId = RecipeNutritionService.CategoryVolume, BaseUnitConversionFactor = 4.93m });
            var bakingSoda = Food("Baking Soda", "Leavening agents, baking soda", "175040", 18, 0.9m);
            var hardCider = Food("Beverages, AMBER, hard cider", "Beverages, AMBER, hard cider", "174146", 14);
            var saleratus = Wording("saleratus");
            var soda = Wording("soda");
            var stock = Wording("stock");
            var cider = Wording("cider");
            var plain = Wording("baking soda");
            db.Ingredients.AddRange(bakingSoda, hardCider, saleratus, soda, stock, cider, plain);
            await db.SaveChangesAsync();
            db.Recipes.AddRange(
                Recipe("Soda Biscuits", (saleratus, "1 tsp saleratus", 1m, Teaspoon), (soda, "1 tsp soda", 1m, Teaspoon), (stock, "200 g stock", 200m, Gram)),
                Recipe("Ginger Cakes", (saleratus, "2 tsp saleratus", 2m, Teaspoon), (stock, "100 g stock", 100m, Gram), (plain, "1 tsp baking soda", 1m, Teaspoon)),
                Recipe("Boiled Ham", (cider, "500 g cider", 500m, Gram)),
                Recipe("Pork with Apples", (cider, "300 g cider", 300m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Kitchen(bakingSoda, hardCider, saleratus, soda, stock, cider, plain);
        }

        /// <summary>A decision of the tier lane under the fake tiers' current signature: an abstention, a held answer, or a write.</summary>
        private static async Task TiersDecidedAsync(ApplicationDbContext db, string wording, int lines, bool writes = false, string tier = WordingTierLaneService.AbstainTier)
        {
            db.LaneDecisions.Add(new LaneDecisionEntity
            {
                Lane = AiLanes.WordingTiers, Tier = tier, Subject = "wording:" + wording, Mode = LaneMode.Shadow, Writes = writes,
                Decision = writes ? "1 Food" : "no tier answers", Stratum = WordingTierLaneService.Stratum(lines),
                SignatureHash = tier == WordingTierLaneService.AbstainTier ? WordingTiers.Signature : "tier-signature-" + tier,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        private static ReaderAnswer? Pick(WordingPack pack, string name, string? means = null) =>
            pack.Candidates.FirstOrDefault(c => c.Name == name) is { } food
                ? new ReaderAnswer(IngredientWordingVerdict.Food, food.Id, "names it", means)
                : new ReaderAnswer(IngredientWordingVerdict.None, null, "no candidate", means);

        private static Dictionary<string, string> Live(params string[] tiers) => tiers.ToDictionary(t => t, _ => LaneMode.Live);

        private static WordingTierAuditRecord.Record AuditRecord() => new()
        {
            Model = "phi4", CheckModel = "mistral-nemo", Signature = WordingReaderRecords.ScorerSignature(WordingTierAuditRecord.Lane), Score = 0.9, ScoredAtUtc = DateTime.UtcNow,
            AuditKnownBad = 400, AuditFlaggedBad = 360, AuditKnownGood = 400, AuditFlaggedGood = 4,
            AuditorKnownBad = 400, AuditorFlaggedBad = 380, AuditorKnownGood = 400, AuditorFlaggedGood = 12,
        };

        private static WordingReaderRecords.Record ReaderRecord(string lane) => new()
        {
            Model = "qwen2.5:14b-instruct", CheckModel = "mistral-nemo", Signature = WordingReaderRecords.ScorerSignature(lane),
            Score = 0.99, FalseAcceptRate = 0, Precision = 0.99, Cases = 100, ScoredAtUtc = DateTime.UtcNow,
            AuditModel = "phi4", AuditorKnownBad = 27, AuditorFlaggedBad = 26, AuditorKnownGood = 27, AuditorFlaggedGood = 1,
        };

        private static async Task MoreCiderAsync(ApplicationDbContext db, Kitchen k, int recipes, string? last = null)
        {
            for (var i = 0; i < recipes; i++) db.Recipes.Add(Recipe($"Boiled Ham {i}", (k.Cider, "200 g cider", 200m, Gram)));
            if (last != null) db.Recipes.Add(Recipe(last, (k.Cider, "250 g cider", 250m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        [Fact]
        public async Task A_shadow_pass_records_what_it_would_write_and_writes_nothing()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));

            var batch = await Lane(db, readers).RunBatchAsync(new Dictionary<string, string>(), 10);

            batch.Read.Should().Be(1);
            batch.LexiconProposed.Should().Be(1);
            batch.WritesByTier.Should().Contain(WordingReaders.Read, 1).And.Contain(WordingReaders.Lexicon, 1);
            batch.WrittenByTier.Should().BeEmpty();
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.WordingLexicon.CountAsync()).Should().Be(0);
            var decision = await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders && d.Tier == WordingReaders.Read);
            decision.Should().Match<LaneDecisionEntity>(d => d.Writes && !d.Written && d.Mode == LaneMode.Shadow && d.Subject == "wording:saleratus");
            LaneJournal.Read<WordingReaderSignals>(decision.SignalsJson)!.Should().Match<WordingReaderSignals>(s =>
                s.EvidenceKey == "wording-readers:lines-2" && s.FoodText == "Leavening agents, baking soda" && s.Means == "baking soda");
            readers.A.Seen.Should().HaveCount(2, "the wording is read again with the food its meaning names");
            readers.A.Seen[0].Candidates.Should().NotContain(c => c.Name == "Baking Soda", "no candidate shares a word with saleratus until the meaning names one");
        }

        [Fact]
        public async Task A_live_pass_writes_the_row_through_the_loader_with_the_readers_source_and_the_lexicon_entry_and_moves_the_lines()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));
            var lane = Lane(db, readers);

            var batch = await lane.RunBatchAsync(Live(WordingReaders.Read, WordingReaders.Lexicon), 10);

            batch.WrittenByTier.Should().Contain(WordingReaders.Read, 1).And.Contain(WordingReaders.Lexicon, 1);
            var row = await db.IngredientWordings.SingleAsync();
            row.Should().Match<IngredientWordingEntity>(w => w.Wording == "saleratus" && w.IngredientId == k.BakingSoda.Id && w.Verdict == IngredientWordingVerdict.Food
                && w.Source == lane.Source && w.Source.StartsWith("ai:readers:") && w.Batch.StartsWith("wr-read-"));
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.BakingSoda.Id)).Should().Be(2);
            (await db.WordingLexicon.SingleAsync()).Should().Match<WordingLexiconEntity>(e => e.Term == "saleratus" && e.Means == "baking soda" && e.Source == lane.Source && e.Batch.StartsWith("wr-lexicon-"));
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == WordingReaderLaneService.LexiconAddChangeType)).Should().Be(1);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders && d.Tier == WordingReaders.Read)).Written.Should().BeTrue();
            batch.SentinelJudged.Should().Be(2);
        }

        [Fact]
        public async Task A_live_read_never_rests_on_a_synonym_the_lexicon_tier_has_not_qualified()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read), 10);

            batch.WrittenByTier.Should().BeEmpty();
            (await db.WordingLexicon.CountAsync()).Should().Be(0);
            (await db.IngredientWordings.SingleAsync()).Verdict.Should().Be(IngredientWordingVerdict.None, "with no candidate both read none, and no candidate passes the gate on its own");
        }

        [Fact]
        public async Task Readers_that_disagree_write_nothing()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "soda", 1);
            var readers = new FakeReaders();
            readers.A.Answer = p => Pick(p, "Baking Soda");
            readers.B.Answer = _ => new ReaderAnswer(IngredientWordingVerdict.Unclear, null, "soda water or baking soda");

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read), 10);

            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            batch.OutcomeByRule.Should().ContainKey(WordingReaders.Disagree);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).Decision.Should().StartWith("no row: readers disagree");
        }

        [Fact]
        public async Task The_readers_take_only_what_the_tiers_left()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "baking soda", 1, writes: true, tier: WordingTiers.Staple);
            await TiersDecidedAsync(db, "soda", 1);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda"));

            var batch = await Lane(db, readers).RunBatchAsync(new Dictionary<string, string>(), 10);

            batch.Read.Should().Be(1);
            readers.A.Seen.Select(p => p.Wording).Should().Equal("soda");
            batch.NotLeftByTiers.Should().Be(4, "baking soda has a tier write, and saleratus, stock and cider have no tier decision yet");
        }

        [Fact]
        public async Task A_wording_decided_under_the_current_signature_is_not_read_again()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "soda", 1);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda"));
            await Lane(db, readers).RunBatchAsync(new Dictionary<string, string>(), 10);

            var again = await Lane(db, readers).RunBatchAsync(new Dictionary<string, string>(), 10);

            again.Read.Should().Be(0);
        }

        [Fact]
        public async Task Two_unclear_answers_on_a_bare_class_word_write_an_unclear_row_that_moves_nothing()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await TiersDecidedAsync(db, "stock", 2);
            var readers = new FakeReaders();
            readers.Both(_ => new ReaderAnswer(IngredientWordingVerdict.Unclear, null, "beef, veal or chicken"));

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read), 10);

            batch.RowsUnclear.Should().Be(1);
            (await db.IngredientWordings.SingleAsync()).Should().Match<IngredientWordingEntity>(w => w.Wording == "stock" && w.Verdict == IngredientWordingVerdict.Unclear && w.IngredientId == null);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Stock.Id)).Should().Be(2);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).Writes.Should().BeFalse("a row that moves no line is not audited");
        }

        [Fact]
        public async Task Two_none_answers_write_nothing_when_a_candidate_passes_the_gate_on_its_own()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "baking soda", 1, tier: WordingTiers.Staple);
            var readers = new FakeReaders();
            readers.Both(_ => new ReaderAnswer(IngredientWordingVerdict.None, null, "no such food"));

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read), 10);

            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            batch.OutcomeByRule.Should().ContainKey(WordingReaders.CandidatePasses);
        }

        [Fact]
        public async Task An_unclear_alcohol_word_is_written_only_on_one_fixed_reading_and_the_gate_reads_its_sense()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await TiersDecidedAsync(db, "cider", 2);
            var readers = new FakeReaders();
            readers.A.Fixed = _ => "hard";
            readers.B.Fixed = _ => "hard";

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Alcohol), 10);

            batch.WrittenByTier.Should().Contain(WordingReaders.Alcohol, 1);
            (await db.IngredientWordings.SingleAsync()).Should().Match<IngredientWordingEntity>(w => w.Wording == "cider" && w.IngredientId == k.HardCider.Id && w.Batch.StartsWith("wr-alcohol-"));
            readers.A.Seen.Should().BeEmpty("a fixed reading chooses no food");
            var signals = LaneJournal.Read<WordingReaderSignals>((await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).SignalsJson)!;
            signals.Should().Match<WordingReaderSignals>(s => s.Mode == AlcoholFixedReading.Cider && s.Reading == "hard");
        }

        [Fact]
        public async Task Fixed_readings_that_differ_write_nothing()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "cider", 2);
            var readers = new FakeReaders();
            readers.A.Fixed = l => l.Title == "Boiled Ham" ? "sweet" : "hard";
            readers.B.Fixed = _ => "hard";

            await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Alcohol), 10);

            (await db.IngredientWordings.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task A_synonym_the_refuter_finds_a_reason_against_is_not_written()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));
            readers.A.Refute = (_, _) => "saleratus may be potassium bicarbonate";

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read, WordingReaders.Lexicon), 10);

            batch.LexiconRefuted.Should().Be(1);
            (await db.WordingLexicon.CountAsync()).Should().Be(0);
            (await db.IngredientWordings.Where(w => w.Verdict == IngredientWordingVerdict.Food).CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task The_sentinel_retracts_a_wording_whose_moved_lines_it_reads_as_another_food()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));
            readers.Judge.Judgement = LinkJudgement.DifferentFood;

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read, WordingReaders.Lexicon), 10);

            batch.Retracted.Should().Be(1);
            batch.FaultsByTier.Should().ContainKey(WordingReaders.Read);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Saleratus.Id)).Should().Be(2, "the lines went back to the wording");
            (await db.IngredientWordingRejections.SingleAsync()).Should().Match<IngredientWordingRejectionEntity>(r =>
                r.Wording == "saleratus" && r.EvidenceKey == "wording-readers:lines-2" && r.Source.StartsWith("ai:readers:"));
        }

        [Fact]
        public async Task The_undo_retracts_the_rows_with_their_evidence_and_takes_the_lexicon_entries_out()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read, WordingReaders.Lexicon), 10);
            db.ChangeTracker.Clear();

            (await Undo(db).UndoSinceAsync(WordingReaders.Read, since, apply: false, CancellationToken.None)).Undone.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(1);

            var read = await Undo(db).UndoSinceAsync(WordingReaders.Read, since, apply: true, CancellationToken.None);
            var lexicon = await Undo(db).UndoSinceAsync(WordingReaders.Lexicon, since, apply: true, CancellationToken.None);
            db.ChangeTracker.Clear();

            read.Undone.Should().Be(1);
            lexicon.Undone.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.WordingLexicon.CountAsync()).Should().Be(0);
            (await db.IngredientWordingRejections.SingleAsync()).EvidenceKey.Should().Be("wording-readers:lines-2");
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Saleratus.Id)).Should().Be(2);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == WordingReaderLaneService.LexiconUndoChangeType)).Should().Be(1);
        }

        [Fact]
        public async Task A_synonym_the_undo_took_out_is_not_written_again()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Lane(db, readers).RunBatchAsync(new Dictionary<string, string> { [WordingReaders.Lexicon] = LaneMode.Live }, 10);
            await Undo(db).UndoSinceAsync(WordingReaders.Lexicon, since, apply: true, CancellationToken.None);
            db.LaneDecisions.RemoveRange(db.LaneDecisions.Where(d => d.Lane == AiLanes.WordingReaders));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var again = await Lane(db, readers).RunBatchAsync(new Dictionary<string, string> { [WordingReaders.Lexicon] = LaneMode.Live }, 10);

            again.LexiconRefuted.Should().Be(1);
            (await db.WordingLexicon.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task A_spent_daily_allowance_records_the_wording_as_capped_and_writes_nothing()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "cider", 2);
            var readers = new FakeReaders();
            readers.A.Fixed = _ => "hard";
            readers.B.Fixed = _ => "hard";

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Alcohol), 10, new Dictionary<string, int> { [WordingReaders.Alcohol] = 0 });

            batch.Capped.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).Decision.Should().StartWith(WordingReaderLaneService.CappedDecision);
        }

        [Fact]
        public async Task A_fixed_reading_is_asked_on_every_line_so_a_line_past_the_first_twelve_recipes_read_another_way_writes_nothing()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await MoreCiderAsync(db, k, 12, last: "Boiled Ham Wassail");
            await TiersDecidedAsync(db, "cider", 15);
            var readers = new FakeReaders();
            readers.A.Fixed = l => l.Title == "Boiled Ham Wassail" ? "sweet" : "hard";
            readers.B.Fixed = l => l.Title == "Boiled Ham Wassail" ? "sweet" : "hard";

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Alcohol), 10);

            readers.A.SeenFixed.Should().HaveCount(15, "the relink would move every line, so each is read");
            readers.B.SeenFixed.Should().HaveCount(15);
            batch.Calls.Should().Be(4, "twelve lines to a call, two calls per reader");
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.HardCider.Id)).Should().Be(0);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).Decision.Should().Contain(WordingReaders.Disagree);
        }

        [Fact]
        public async Task A_fixed_reading_of_a_wording_with_more_lines_than_it_reads_writes_nothing_and_asks_no_model()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await MoreCiderAsync(db, k, WordingReaderLaneService.MaxLinesPerWording);
            await TiersDecidedAsync(db, "cider", WordingReaderLaneService.MaxLinesPerWording + 2);
            var readers = new FakeReaders();
            readers.A.Fixed = _ => "hard";
            readers.B.Fixed = _ => "hard";

            await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Alcohol), 10);

            readers.A.SeenFixed.Should().BeEmpty();
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).Decision.Should().Contain(WordingReaderLaneService.FixedNotAllRead);
        }

        [Fact]
        public async Task A_fixed_reading_is_refused_when_another_ingredient_of_the_wording_holds_lines_the_relink_would_move_unread()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var twin = Wording("Cider");
            db.Ingredients.Add(twin);
            await db.SaveChangesAsync();
            db.Recipes.Add(Recipe("Mulled Cider", (twin, "1 kg Cider", 1000m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await TiersDecidedAsync(db, "cider", 2);
            var readers = new FakeReaders();
            readers.A.Fixed = _ => "hard";
            readers.B.Fixed = _ => "hard";

            await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Alcohol), 10);

            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).Decision.Should().Contain("another ingredient of the same wording");
        }

        [Fact]
        public async Task The_auditor_flag_on_a_reader_decision_stands_alone_and_is_corrected_with_the_auditor_own_score()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));
            await Lane(db, readers).RunBatchAsync(new Dictionary<string, string>(), 10);
            readers.Pairs.Wrong = true;

            var verdict = await Lane(db, readers, lane => ReaderRecord(lane), AuditRecord).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>());

            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders && d.Tier == WordingReaders.Read)).AuditOutcome
                .Should().Be(LaneAuditOutcome.Error, "reader B made the pick, so it may not clear the auditor's flag");
            readers.Readings.Seen.Should().BeEmpty();
            verdict.Figures.AuditorSource.Should().Contain("alone");
            verdict.Figures.AuditorSensitivity.Should().BeApproximately(0.95, 1e-9);
            verdict.Figures.AuditorFalseFlagRate.Should().BeApproximately(0.03, 1e-9);
            verdict.Reason.Should().NotContain("golden score record");

            var combinedOnly = AuditRecord();
            combinedOnly.AuditorKnownBad = null;
            (await Lane(db, readers, lane => ReaderRecord(lane), () => combinedOnly).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>())).Reason
                .Should().Contain("no counts for the auditor alone");
        }

        [Fact]
        public async Task A_link_audit_record_of_another_prompt_or_below_its_floor_keeps_the_reader_tiers_in_shadow()
        {
            using var db = NewContext();
            var readers = new FakeReaders();
            var stale = AuditRecord();
            stale.Signature = "0000000000000000";
            var low = AuditRecord();
            low.Score = 0.60;

            (await Lane(db, readers, lane => ReaderRecord(lane), () => stale).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>())).Reason
                .Should().Contain("changed since it was scored");
            (await Lane(db, readers, lane => ReaderRecord(lane), () => low).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>())).Reason
                .Should().Contain("below its floor 75.0");
        }

        [Fact]
        public async Task An_alcohol_decision_keeps_its_lines_in_context_and_is_audited_by_the_auditor_reading_them_in_the_fixed_reading_mode()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "cider", 2);
            var readers = new FakeReaders();
            readers.A.Fixed = _ => "hard";
            readers.B.Fixed = _ => "hard";
            await Lane(db, readers).RunBatchAsync(new Dictionary<string, string>(), 10);

            var decision = await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders);
            LaneJournal.Read<WordingReaderSignals>(decision.SignalsJson)!.FixedSample!.Select(l => l.Title).Should().BeEquivalentTo("Boiled Ham", "Pork with Apples");
            db.ChangeTracker.Clear();

            var verdict = await Lane(db, readers, lane => ReaderRecord(lane), AuditRecord).AuditAndEvaluateAsync(WordingReaders.Alcohol, Array.Empty<string>());

            readers.Readings.Seen.Should().ContainSingle();
            readers.Pairs.Seen.Should().BeEmpty("the same-food judge cannot tell a reading right without the recipe");
            verdict.Figures.AuditorSource.Should().StartWith(WordingReaderRecords.AlcoholLane + " phi4");
            verdict.Reason.Should().NotContain("fixed-reading mode");

            var noAuditor = ReaderRecord(WordingReaderRecords.AlcoholLane);
            noAuditor.AuditorKnownBad = null;
            (await Lane(db, readers, _ => noAuditor, AuditRecord).AuditAndEvaluateAsync(WordingReaders.Alcohol, Array.Empty<string>())).Reason
                .Should().Contain("no counts for its auditor in the fixed-reading mode");
        }

        [Theory]
        [InlineData("hard", false)]
        [InlineData("sweet", true)]
        [InlineData(AlcoholFixedReading.Unsure, null)]
        public async Task The_fixed_reading_auditor_finds_a_decision_wrong_when_it_reads_a_sampled_line_another_way(string reading, bool? wrong)
        {
            var reader = new FakeReader("phi4") { Fixed = _ => reading };
            var signals = new WordingReaderSignals("cider", 1, 2, WordingReaders.Alcohol, IngredientWordingVerdict.Food, 2, "Hard cider", "Beverages, hard cider", "wording-readers:lines-2",
                "wr-alcohol-1", null, null, "both read hard on every line", null, new List<string>(), Mode: AlcoholFixedReading.Cider, Reading: "hard",
                FixedSample: new List<FixedLine> { new("1 pint cider", "Cider Cup", new List<string> { "1 lemon" }, "Let it work in the cask.", false) });

            var answer = await new OllamaWordingReaders.FixedReader(reader).AuditAsync(new LaneDecisionEntity { SignalsJson = LaneJournal.Write(signals) }, CancellationToken.None);

            answer.Wrong.Should().Be(wrong);
            reader.SeenFixed.Single().Steps.Should().Be("Let it work in the cask.");
        }

        [Fact]
        public async Task A_row_loaded_but_never_recorded_is_recorded_as_written_and_its_lines_moved_on_the_next_pass()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var readers = new FakeReaders();
            var lane = Lane(db, readers);
            await new VerifiedWordingService(db, new NoUser()).LoadAsync(new[]
            {
                new VerifiedWordingRow { Row = "saleratus", Wording = "saleratus", Verdict = IngredientWordingVerdict.Food, FoodId = k.BakingSoda.Id, FoodName = "Baking Soda", EvidenceKey = "wording-readers:lines-2" },
            }, "wr-read-20261120000000", apply: true, source: lane.Source);
            db.ChangeTracker.Clear();

            var batch = await lane.RunBatchAsync(Live(WordingReaders.Read), 10);

            batch.Recovered.Should().Be(1);
            var decision = await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders);
            decision.Should().Match<LaneDecisionEntity>(d => d.Written && d.Tier == WordingReaders.Read && d.Decision!.StartsWith(WordingReaderLaneService.RecoveredDecision));
            LaneJournal.Read<WordingReaderSignals>(decision.SignalsJson)!.Should().Match<WordingReaderSignals>(s =>
                s.EvidenceKey == "wording-readers:lines-2" && s.FoodText == "Leavening agents, baking soda" && s.Batch == "wr-read-20261120000000");
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.BakingSoda.Id)).Should().Be(2);

            var again = await lane.RunBatchAsync(Live(WordingReaders.Read), 10);
            again.Recovered.Should().Be(0);
        }

        [Fact]
        public async Task A_row_loaded_but_never_recorded_by_a_paused_tier_is_retracted_with_its_evidence()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var readers = new FakeReaders();
            var lane = Lane(db, readers);
            await new VerifiedWordingService(db, new NoUser()).LoadAsync(new[]
            {
                new VerifiedWordingRow { Row = "saleratus", Wording = "saleratus", Verdict = IngredientWordingVerdict.Food, FoodId = k.BakingSoda.Id, FoodName = "Baking Soda", EvidenceKey = "wording-readers:lines-2" },
            }, "wr-read-20261120000000", apply: true, source: lane.Source);
            db.ChangeTracker.Clear();

            var batch = await lane.RunBatchAsync(new Dictionary<string, string> { [WordingReaders.Read] = LaneMode.Paused }, 10);

            batch.RecoveredRetracted.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.IngredientWordingRejections.SingleAsync()).EvidenceKey.Should().Be("wording-readers:lines-2");
        }

        [Fact]
        public async Task Undoing_a_synonym_also_retracts_the_food_rows_the_gate_let_through_on_it()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await TiersDecidedAsync(db, "saleratus", 2);
            var readers = new FakeReaders();
            readers.Both(p => Pick(p, "Baking Soda", "baking soda"));
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Read, WordingReaders.Lexicon), 10);
            db.ChangeTracker.Clear();
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.BakingSoda.Id)).Should().Be(2);

            (await Undo(db).UndoSinceAsync(WordingReaders.Lexicon, since, apply: false, CancellationToken.None)).Undone.Should().Be(2);
            (await db.IngredientWordings.CountAsync()).Should().Be(1);

            var undone = await Undo(db).UndoSinceAsync(WordingReaders.Lexicon, since, apply: true, CancellationToken.None);
            db.ChangeTracker.Clear();

            undone.Undone.Should().Be(2);
            (await db.WordingLexicon.CountAsync()).Should().Be(0);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Saleratus.Id)).Should().Be(2, "the lines the synonym moved go back");
            (await db.IngredientWordingRejections.SingleAsync()).EvidenceKey.Should().Be("wording-readers:lines-2");
        }

        [Fact]
        public async Task A_wording_whose_lines_exceed_what_the_lane_may_still_move_today_is_capped()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "cider", 2);
            var readers = new FakeReaders();
            readers.A.Fixed = _ => "hard";
            readers.B.Fixed = _ => "hard";

            var batch = await Lane(db, readers).RunBatchAsync(Live(WordingReaders.Alcohol), 10, linesRemaining: 1);

            batch.Capped.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.LaneDecisions.SingleAsync(d => d.Lane == AiLanes.WordingReaders)).Decision.Should().StartWith(WordingReaderLaneService.CappedDecision).And.Contain("may still move");
        }

        [Fact]
        public async Task The_lines_written_today_are_counted_over_every_tier()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await TiersDecidedAsync(db, "cider", 2);
            var readers = new FakeReaders();
            readers.A.Fixed = _ => "hard";
            readers.B.Fixed = _ => "hard";
            var lane = Lane(db, readers);
            await lane.RunBatchAsync(Live(WordingReaders.Alcohol), 10);

            (await lane.LinesWrittenTodayAsync()).Should().Be(2);
        }

        [Fact]
        public async Task Sentinel_retractions_above_four_percent_of_a_week_of_written_wordings_pause_the_tier()
        {
            using var db = NewContext();
            var readers = new FakeReaders();
            for (var i = 0; i < WordingReaderLaneService.RetractionMinimum; i++)
                db.LaneDecisions.Add(new LaneDecisionEntity { Lane = AiLanes.WordingReaders, Tier = WordingReaders.Read, Subject = $"wording:w{i}", Mode = LaneMode.Live, Writes = true, Written = true, CreatedDate = DateTime.UtcNow });
            for (var i = 0; i < 3; i++)
                db.IngredientWordingRejections.Add(new IngredientWordingRejectionEntity
                {
                    Wording = $"w{i}", Verdict = IngredientWordingVerdict.Food, IngredientId = 1, Source = "ai:readers:abc",
                    Reason = $"lane {AiLanes.WordingReaders}/{WordingReaders.Read}: the sentinel read 2 moved lines as another food", CreatedDate = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();

            var verdict = await Lane(db, readers, lane => ReaderRecord(lane), AuditRecord).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>());

            verdict.Figures.PauseReasons.Should().ContainSingle(r => r.Contains("retracted 3 of the 50"));
            db.IngredientWordingRejections.Remove(await db.IngredientWordingRejections.FirstAsync());
            await db.SaveChangesAsync();
            (await Lane(db, readers, lane => ReaderRecord(lane), AuditRecord).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>())).Figures.PauseReasons
                .Should().BeEmpty("2 of 50 is 4 %, not above it");
        }

        [Fact]
        public async Task No_tier_leaves_shadow_without_its_golden_record_and_the_link_audit_record()
        {
            using var db = NewContext();
            var readers = new FakeReaders();

            var verdict = await Lane(db, readers).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>());

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Reason.Should().Contain("wording-read lane has no golden score record");
        }

        [Fact]
        public async Task With_current_records_for_the_roster_the_tier_waits_only_for_its_audited_sample()
        {
            using var db = NewContext();
            var readers = new FakeReaders();
            var record = new WordingReaderRecords.Record
            {
                Model = "qwen2.5:14b-instruct", CheckModel = "mistral-nemo", Signature = WordingReaderRecords.ScorerSignature(WordingReaderRecords.ReadLane),
                Score = 0.9, FalseAcceptRate = 0, Precision = 0.99, Cases = 100, ScoredAtUtc = DateTime.UtcNow,
            };
            var audit = AuditRecord();

            var verdict = await Lane(db, readers, lane => lane == WordingReaderRecords.ReadLane ? record : null, () => audit).AuditAndEvaluateAsync(WordingReaders.Read, Array.Empty<string>());

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Reason.Should().NotContain("golden score record").And.NotContain("was scored with");
            WordingReaderRecords.Evaluate(WordingReaderRecords.ReadLane, record, null, "qwen2.5:14b-instruct", "qwen3:8b").Should().Contain("reader B");
            WordingReaderRecords.Evaluate(WordingReaderRecords.ReadLane, record, new WordingReaderRecords.Floor { MinPrecision = 0.995 }, "qwen2.5:14b-instruct", "mistral-nemo").Should().Contain("below its floor");
        }
    }
}
