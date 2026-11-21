using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The reprocess lane: the detector stores what a rebuild would change and why; the lane takes
    /// candidates by priority, previews in shadow and writes only live, records every decision on
    /// the lane frame, checks every changed line by rule, has the line judge audit a sample and
    /// undoes only its own batches when it pauses.
    /// </summary>
    public class ReprocessLaneTests
    {
        private const long Piece = 1, Cup = 2, Teaspoon = 3, Tablespoon = 4, Gram = 5, Ounce = 6, Pinch = 7;
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Operator : ICurrentUserService
        {
            public long? PersonId => 2;
            public long PersonIdOrSystem => 2;
            public long RequiredPersonId => 2;
            public string? UserId => "operator";
            public string RequiredUserId => "operator";
        }

        private sealed class FakeJudge : IReprocessLineJudge
        {
            public Func<LineJudgeCase, LineJudgement?> Answer { get; set; } = _ => LineJudgement.Better;
            public bool IsConfigured { get; set; } = true;
            public string ModelName { get; set; } = "mistral-nemo";
            public int Calls { get; private set; }

            public Task<IReadOnlyList<LineJudgeVerdict>> JudgeAsync(IReadOnlyList<LineJudgeCase> cases, CancellationToken cancellationToken = default)
            {
                Calls++;
                IReadOnlyList<LineJudgeVerdict> verdicts = cases.Select(c => (c, v: Answer(c))).Where(x => x.v != null).Select(x => new LineJudgeVerdict(x.c.Id, x.v!.Value)).ToList();
                return Task.FromResult(verdicts);
            }
        }

        private static async Task<ApplicationDbContext> SeededAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3 },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = Teaspoon, Name = "Teaspoon", Symbol = "tsp", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = Tablespoon, Name = "Tablespoon", Symbol = "tbsp", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = Ounce, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = Pinch, Name = "Pinch", Symbol = "pinch", MeasurementCategoryId = 2 });
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name, decimal? gramsPerItem = null)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = (long)CurationStatusEnum.Curated, GramsPerItem = gramsPerItem };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private sealed record Row(IngredientEntity Food, string RawLine, decimal Quantity, long MeasurementId, int SortOrder = 0, string? LineKind = null);

        private static async Task<RecipeEntity> RecipeAsync(ApplicationDbContext db, string name, string[] source, Row[] rows,
            string[]? steps = null, int version = 1, string[]? sourceSteps = null, CurationStatusEnum status = CurationStatusEnum.NonCurated)
        {
            steps ??= new[] { "Mix everything well.", "Bake until done." };
            var recipe = new RecipeEntity
            {
                Name = name, CurationStatusId = (long)status, ScrapedAtUtc = Imported, LicenseStatus = RecipeLicenseStatus.PublicDomain, SourceAttribution = "A book",
                RecipeIngredients = rows.Select(r => new RecipeIngredientEntity
                {
                    IngredientId = r.Food.Id, SortOrder = r.SortOrder, Quantity = r.Quantity, MeasurementId = r.MeasurementId, RawLine = r.RawLine, LineKind = r.LineKind,
                    CreatedDate = Imported,
                }).ToList(),
                RecipeSteps = steps.Select((s, i) => new RecipeStepEntity { StepNumber = i + 1, Summary = s, Description = s }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            for (var v = 1; v <= version; v++)
            {
                var record = new ScraperRecipe
                {
                    Name = name,
                    Ingredients = source.Select(l => new ScraperIngredient { RawLine = l }).ToList(),
                    Steps = (v == version && sourceSteps != null ? sourceSteps : steps).Select((s, i) => new ScraperStep { Order = i + 1, Instruction = s }).ToList(),
                };
                var content = SourceRecord.Content(record);
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity
                {
                    RecipeId = recipe.Id, RawJsonLd = content, Format = ScrapedDocumentFormat.StagedCookbook, ContentSha256 = SourceRecord.Sha256(content), Version = v, FetchedAtUtc = Imported,
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipe;
        }

        private static RecipeReprocessService Reprocess(ApplicationDbContext db) =>
            new(db, new Operator(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(), new Mock<IRecipeNutritionService>().Object,
                NullLogger<RecipeReprocessService>.Instance);

        private static ReprocessDetectorService Detector(ApplicationDbContext db) => new(db, Reprocess(db), NullLogger<ReprocessDetectorService>.Instance);

        private static LaneFrameService Frame(ApplicationDbContext db, IEnumerable<ILaneUndo>? undos = null) =>
            new(db, undos ?? Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance);

        private static ReprocessLaneService Lane(ApplicationDbContext db, FakeJudge? judge = null, IRecipeReprocessService? reprocess = null) =>
            new(db, reprocess ?? Reprocess(db), Frame(db), judge ?? new FakeJudge(), NullLogger<ReprocessLaneService>.Instance);

        private static Task<List<RecipeIngredientEntity>> LinesAsync(ApplicationDbContext db, long recipeId) =>
            db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId).OrderBy(ri => ri.SortOrder).ToListAsync();

        [Fact]
        public void The_rule_audit_reads_numbers_in_digits_fractions_and_words()
        {
            ReprocessRuleAudit.Numbers("1 1/2 cups and ½ pint, two or three eggs, a couple of ounces").Should().Contain(new[] { 1.5m, 0.5m, 2m, 3m, 1m });
            ReprocessRuleAudit.GroundedIn(0.5m, new[] { "one jill of cold water" }).Should().BeTrue("a jill is half a cup");
            ReprocessRuleAudit.GroundedIn(8m, new[] { "two quarts of water" }).Should().BeTrue("a quart is four cups");
            ReprocessRuleAudit.GroundedIn(4m, new[] { "a pinch of salt" }).Should().BeFalse();
        }

        [Fact]
        public void The_rule_audit_fails_an_ungrounded_amount_a_lost_amount_a_food_not_named_and_pieces_of_a_measure_or_a_liquid()
        {
            ReprocessRuleAudit.Failures(new("2 tablespns. water", "water", 2, "Tablespoon", null)).Should().BeEmpty();
            ReprocessRuleAudit.Failures(new("salt", "salt", 4, "Ounce", null, FilledFromMethod: true), new[] { "Add a pinch of salt." })
                .Should().Equal(ReprocessRuleAudit.Grounded);
            ReprocessRuleAudit.Failures(new("2 eggs", "eggs", 0, null, null)).Should().Equal(ReprocessRuleAudit.Grounded);
            ReprocessRuleAudit.Failures(new("two cups of milk", "milk", 0, null, null)).Should().Equal(ReprocessRuleAudit.Grounded);
            ReprocessRuleAudit.Failures(new("salt to taste", "salt", 0, null, null)).Should().BeEmpty();
            ReprocessRuleAudit.Failures(new("2 cups flour", "Sugar", 2, "Cup", null, FoodChanged: true)).Should().Equal(ReprocessRuleAudit.Named);
            ReprocessRuleAudit.Failures(new("2 tablespns. water", "water", 2, "Piece", null)).Should().Equal(ReprocessRuleAudit.UnitSane);
            ReprocessRuleAudit.Failures(new("2 eggs", "eggs", 2, "Piece", null, GramsPerItem: 50)).Should().BeEmpty();
            ReprocessRuleAudit.Failures(new("2 cups chicken", "chicken", 2, "Cup", null)).Should().Equal(ReprocessRuleAudit.UnitSane);
            ReprocessRuleAudit.Failures(new("2 cups chicken broth", "chicken broth", 2, "Cup", null)).Should().BeEmpty();
            ReprocessRuleAudit.Failures(new("For the sauce:", "for the sauce", 0, null, "note")).Should().BeEmpty();
        }

        [Theory]
        [InlineData("1 gallon of salt", "Salt", 16, "Cup", false)]
        [InlineData("2 quarts black pepper", "Black Pepper", 8, "Cup", false)]
        [InlineData("1/2 gallon baking soda", "Baking Soda", 8, "Cup", false)]
        [InlineData("1 gallon water, 1 quart salt", "Salt", 4, "Cup", true)]
        [InlineData("1 cup salt", "Salt, table", 1, "Cup", true)]
        [InlineData("3 tablespoons baking powder", "Baking Powder", 3, "Tablespoon", true)]
        [InlineData("2 gallons flour", "Flour", 32, "Cup", false)]
        [InlineData("1 gallon flour", "Flour", 16, "Cup", true)]
        [InlineData("1 gallon salt pork", "Salt pork", 16, "Cup", true)]
        [InlineData("1 gallon green peppers", "Green Peppers", 16, "Cup", true)]
        [InlineData("1 pound salt", "Salt", 1, "Pound", true)]
        [InlineData("1 gallon of baking soda", "Leavening agents, baking soda", 16, "Cup", false)]
        [InlineData("2 gallons sugar", "Sugars, granulated", 32, "Cup", false)]
        [InlineData("2 quarts black pepper", "Spices, pepper, black", 8, "Cup", false)]
        [InlineData("2 quarts cinnamon", "Spices, cinnamon, ground", 8, "Cup", false)]
        [InlineData("3 gallons oats", "Cereals, oats, regular and quick, not fortified, dry", 48, "Cup", false)]
        [InlineData("2 cups sugar", "Sugars, granulated", 2, "Cup", true)]
        [InlineData("1 tablespoon cinnamon", "Spices, cinnamon, ground", 1, "Tablespoon", true)]
        [InlineData("1 gallon soda", "Soda", 16, "Cup", true)]
        [InlineData("1 gallon club soda", "Beverages, carbonated, club soda", 16, "Cup", true)]
        [InlineData("1 gallon tomatoes", "Tomatoes, red, ripe, canned, no salt added", 16, "Cup", true)]
        [InlineData("2 gallons rice cakes", "Snacks, rice cakes, brown rice, plain", 32, "Cup", true)]
        public void The_rule_audit_fails_a_volume_of_a_seasoning_over_a_quart_and_of_a_dry_staple_over_a_gallon(string raw, string food, decimal quantity, string unit, bool sane)
        {
            ReprocessRuleAudit.Failures(new(raw, food, quantity, unit, null)).Contains(ReprocessRuleAudit.UnitSane).Should().Be(!sane);
        }

        [Fact]
        public void The_line_judge_reads_each_verdict_by_its_number_and_its_signature_ignores_the_model()
        {
            var cases = new[]
            {
                new LineJudgeCase(7, "Cake", "2 tablespns. water", string.Empty, new LineJudgeRow("water", 2, "Piece"), new LineJudgeRow("water", 2, "Tablespoon")),
                new LineJudgeCase(9, "Cake", "salt", string.Empty, null, new LineJudgeRow("salt", 0, null)),
            };
            var verdicts = OllamaLineJudge.Read("{\"results\":[{\"n\":1,\"verdict\":\"better\"},{\"n\":2,\"verdict\":\"maybe\"}]}", cases);

            verdicts.Should().Equal(new LineJudgeVerdict(7, LineJudgement.Better));
            OllamaLineJudge.Body(cases).Should().Contain("BEFORE: water; 2 Piece").And.Contain("AFTER: water; 2 Tablespoon").And.Contain("BEFORE: (no row)");
            OllamaLineJudge.Signature.Should().HaveLength(16);
            var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Roster:ReaderB"] = "mistral-small" }).Build();
            OllamaLineJudge.ModelFor(settings).Should().Be("mistral-small");
            OllamaLineJudge.ModelFor(new ConfigurationBuilder().Build()).Should().Be("mistral-nemo");
        }

        [Fact]
        public async Task The_line_judge_asks_the_batch_host_with_its_schema()
        {
            var handler = new Capture("{\"message\":{\"content\":\"{\\\"results\\\":[{\\\"n\\\":1,\\\"verdict\\\":\\\"worse\\\"}]}\"}}");
            var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:BatchOllamaUrl"] = "http://model-host:11434", ["Reprocess:JudgeModel"] = "mistral-nemo:12b",
            }).Build();
            var judge = new OllamaLineJudge(new HttpClient(handler), settings);

            var verdicts = await judge.JudgeAsync(new[] { new LineJudgeCase(0, "Cake", "1 cup sugar", string.Empty, new LineJudgeRow("sugar", 1, "Cup"), new LineJudgeRow("sugar", 1, "Piece")) });

            verdicts.Single().Judgement.Should().Be(LineJudgement.Worse);
            handler.Uri!.ToString().Should().Be("http://model-host:11434/api/chat");
            handler.Body.Should().Contain("\"model\":\"mistral-nemo:12b\"").And.Contain("\"enum\":[\"better\",\"same\",\"worse\",\"unclear\"]");
        }

        private sealed class Capture : HttpMessageHandler
        {
            private readonly string _reply;
            public Capture(string reply) => _reply = reply;
            public Uri? Uri { get; private set; }
            public string? Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Uri = request.RequestUri;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_reply, Encoding.UTF8, "application/json") };
            }
        }

        [Fact]
        public void The_judge_record_lets_the_lane_qualify_only_when_it_scored_this_prompt_and_model_above_its_floor()
        {
            var floor = new ReprocessJudgeRecord.Floor { MinScore = 0.95, MaxFalseAcceptRate = 0.02 };
            ReprocessJudgeRecord.Record Good() => new()
            {
                Model = "mistral-nemo", Signature = OllamaLineJudge.Signature, ScoredAtUtc = Imported, Cases = 200, Score = 0.97, FalseAcceptRate = 0.01,
                AuditKnownBad = 100, AuditFlaggedBad = 80, AuditKnownGood = 100, AuditFlaggedGood = 2,
            };

            ReprocessJudgeRecord.Evaluate(null, floor, "mistral-nemo").Problem.Should().Contain("no golden score record");
            var (score, problem) = ReprocessJudgeRecord.Evaluate(Good(), floor, "mistral-nemo");
            problem.Should().BeNull();
            score!.Sensitivity.Should().Be(0.8);
            score.FalseFlagRate.Should().Be(0.02);
            ReprocessJudgeRecord.Evaluate(Good(), floor, "mistral-small").Problem.Should().Contain("scored with mistral-nemo");
            var stale = Good();
            stale.Signature = "0000000000000000";
            ReprocessJudgeRecord.Evaluate(stale, floor, "mistral-nemo").Problem.Should().Contain("prompt changed");
            var low = Good();
            low.Score = 0.9;
            ReprocessJudgeRecord.Evaluate(low, floor, "mistral-nemo").Problem.Should().Contain("below its floor");
            var lax = Good();
            lax.FalseAcceptRate = 0.05;
            ReprocessJudgeRecord.Evaluate(lax, floor, "mistral-nemo").Problem.Should().Contain("above its ceiling");
            ReprocessJudgeRecord.EmbeddedFloor().Should().NotBeNull("model-lanes.json is built into the API").And.Match<ReprocessJudgeRecord.Floor>(f => f.MinScore == 0.95);
        }

        [Fact]
        public void The_detector_names_each_reason_from_the_dry_run()
        {
            RecipeReprocessLineDiff Line(string raw, decimal? oldQuantity, bool filled, params string[] changes) =>
                new() { RawLine = raw, OldQuantity = oldQuantity, FilledFromMethod = filled, Changes = changes.ToList() };
            var live = new List<ReprocessDetectorService.LiveRow> { new(1, 0, "2 cups flour", 2), new(2, 0, "salt", 0) };
            var result = new RecipeReprocessRecipeResult
            {
                Outcome = RecipeReprocessOutcome.Rebuilt,
                Lines =
                {
                    Line("2 tablespns. water", 2, false, "amount"),
                    Line("butter", 0, true, "amount"),
                    Line("onions", 2, false, "amount"),
                    Line("For the sauce:", 0, false, "kind"),
                    Line("1 cup sugar", 1, false, "food"),
                },
            };

            var reasons = ReprocessDetectorService.Reasons(result, live, sourceVersion: 1);

            reasons.Should().Be(ReprocessReason.OrderLost | ReprocessReason.Amount | ReprocessReason.MethodFill | ReprocessReason.Food | ReprocessReason.Kind);
            ReprocessDetectorService.Reasons(new RecipeReprocessRecipeResult { Lines = { Line("100 grams of butter", null, false, "added") } },
                new List<ReprocessDetectorService.LiveRow> { new(1, 0, "Burro - 100g", 30) }, sourceVersion: 2).Should().Be(ReprocessReason.Source);
            ReprocessDetectorService.Reasons(new RecipeReprocessRecipeResult(), new List<ReprocessDetectorService.LiveRow> { new(5, 0, "apple, peach, or any other marmalade", 0) },
                1, raw => 9).Should().Be(ReprocessReason.Compound);
            ReprocessDetectorService.Priority(ReprocessReason.OrderLost, approved: true).Should().Be(0);
            ReprocessDetectorService.Priority(ReprocessReason.Amount | ReprocessReason.OrderLost, approved: true).Should().Be(350);
            ReprocessDetectorService.Priority(ReprocessReason.Source, approved: false).Should().Be(200);
        }

        [Fact]
        public async Task The_detector_stores_what_a_rebuild_would_change_and_reads_a_recipe_again_only_when_its_source_or_the_rules_change()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            var eggs = await FoodAsync(db, "eggs", 50);
            var spoon = await RecipeAsync(db, "Spoon", new[] { "2 tablespns. water", "1 cup flour" },
                new[] { new Row(water, "2 tablespns. water", 2, Piece), new Row(flour, "1 cup flour", 1, Cup, 1) });
            var order = await RecipeAsync(db, "Order", new[] { "1 cup sugar", "3 eggs" },
                new[] { new Row(eggs, "3 eggs", 3, Piece), new Row(sugar, "1 cup sugar", 1, Cup) });
            var clean = await RecipeAsync(db, "Clean", new[] { "1 cup sugar", "1 cup flour" },
                new[] { new Row(sugar, "1 cup sugar", 1, Cup), new Row(flour, "1 cup flour", 1, Cup, 1) });

            var first = await Detector(db).DetectBatchAsync(0, 100);

            first.Read.Should().Be(3);
            first.Candidates.Should().Be(2);
            var rows = await db.ReprocessCandidates.AsNoTracking().ToDictionaryAsync(c => c.RecipeId);
            rows[spoon.Id].Reasons.Should().Be(ReprocessReason.Amount);
            rows[spoon.Id].Tier.Should().Be(ReprocessTier.Rebuild);
            rows[spoon.Id].Priority.Should().Be(300);
            rows[order.Id].Reasons.Should().Be(ReprocessReason.OrderLost);
            rows[order.Id].Tier.Should().Be(ReprocessTier.Order);
            rows[clean.Id].Reasons.Should().Be(0);
            rows.Values.Should().OnlyContain(c => c.RuleVersion == RecipeLineBuilder.Version && c.Outcome == null);

            (await Detector(db).DetectBatchAsync(0, 100)).Read.Should().Be(0, "nothing changed since the rules read them");
            var record = new ScraperRecipe { Name = "Clean", Ingredients = new List<ScraperIngredient> { new() { RawLine = "1 cup sugar" }, new() { RawLine = "1 cup flour" } } };
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity
            {
                RecipeId = clean.Id, RawJsonLd = SourceRecord.Content(record), Format = ScrapedDocumentFormat.StagedCookbook, Version = 2, FetchedAtUtc = Imported,
            });
            await db.SaveChangesAsync();
            (await Detector(db).DetectBatchAsync(0, 100)).Read.Should().Be(1, "a newer source is read again");
        }

        [Fact]
        public async Task A_shadow_batch_previews_and_records_every_decision_and_writes_nothing()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            var recipe = await RecipeAsync(db, "Spoon", new[] { "2 tablespns. water", "1 cup flour" },
                new[] { new Row(water, "2 tablespns. water", 2, Piece), new Row(flour, "1 cup flour", 1, Cup, 1) });
            await Detector(db).DetectBatchAsync(0, 100);

            var batch = await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Shadow, 50);

            batch.Taken.Should().Be(1);
            batch.Rebuilt.Should().Be(1);
            batch.LinesChecked.Should().Be(1);
            batch.LinesFailing.Should().Be(0);
            (await LinesAsync(db, recipe.Id)).Single(l => l.IngredientId == water.Id).MeasurementId.Should().Be(Piece);
            (await db.RecipeReprocessJournals.CountAsync()).Should().Be(0);
            var decision = await db.LaneDecisions.SingleAsync();
            (decision.Lane, decision.Tier, decision.Mode, decision.Writes, decision.Written, decision.Stratum).Should().Be((AiLanes.Reprocess, ReprocessTier.Rebuild, LaneMode.Shadow, true, false, "D2"));
            var signals = LaneJournal.Read<ReprocessSignals>(decision.SignalsJson)!;
            signals.Lines.Single().Should().Match<ReprocessSignalLine>(l => l.Raw == "2 tablespns. water" && l.OldUnit == "Piece" && l.NewUnit == "Tablespoon");
            (await db.ReprocessCandidates.SingleAsync()).Outcome.Should().Be(ReprocessOutcome.Shadow);
            (await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Shadow, 50)).Taken.Should().Be(0, "a recipe decided in shadow is not decided again in shadow");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == ReprocessLaneService.BatchChangeType)).Should().Be(1, "a batch that took nothing is not journaled");
        }

        [Fact]
        public async Task A_live_batch_writes_under_the_lanes_own_batch_and_takes_the_recipes_decided_in_shadow()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            var recipe = await RecipeAsync(db, "Spoon", new[] { "2 tablespns. water", "1 cup flour" },
                new[] { new Row(water, "2 tablespns. water", 2, Piece), new Row(flour, "1 cup flour", 1, Cup, 1) });
            await Detector(db).DetectBatchAsync(0, 100);
            await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Shadow, 50);

            var batch = await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Live, 50);

            batch.Rebuilt.Should().Be(1);
            batch.Faults.Should().BeEmpty("a rebuilt recipe rebuilt again changes nothing");
            (await LinesAsync(db, recipe.Id)).Single(l => l.IngredientId == water.Id).MeasurementId.Should().Be(Tablespoon);
            var journal = await db.RecipeReprocessJournals.SingleAsync();
            journal.BatchId.Should().StartWith(RecipeReprocessService.LaneBatchPrefix);
            var candidate = await db.ReprocessCandidates.SingleAsync();
            (candidate.Outcome, candidate.JournalId).Should().Be((ReprocessOutcome.Rebuilt, journal.Id));
            (await db.LaneDecisions.CountAsync(d => d.Written)).Should().Be(1);
        }

        [Fact]
        public async Task The_order_tier_restores_only_the_order()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var eggs = await FoodAsync(db, "eggs", 50);
            var recipe = await RecipeAsync(db, "Order", new[] { "1 cup sugar", "3 eggs" },
                new[] { new Row(eggs, "3 eggs", 3, Piece), new Row(sugar, "1 cup sugar", 1, Cup) });
            await Detector(db).DetectBatchAsync(0, 100);

            (await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Live, 50)).Taken.Should().Be(0);
            var batch = await Lane(db).RunBatchAsync(ReprocessTier.Order, LaneMode.Live, 50);

            batch.Rebuilt.Should().Be(1);
            (await LinesAsync(db, recipe.Id)).Select(l => l.IngredientId).Should().Equal(sugar.Id, eggs.Id);
            (await db.RecipeReprocessJournals.SingleAsync()).Kind.Should().Be(RecipeReprocessKind.Order);
            LaneJournal.Read<ReprocessSignals>((await db.LaneDecisions.SingleAsync()).SignalsJson)!.Lines.Should().BeEmpty();
        }

        private sealed class Scripted : IRecipeReprocessService
        {
            public Func<RecipeReprocessRequestModel, bool, RecipeReprocessResult> Answer { get; set; } = (_, _) => new RecipeReprocessResult();

            public Task<RecipeReprocessResult> RebuildAsync(RecipeReprocessRequestModel request, bool apply = false, bool orderOnly = false, string? batchId = null) =>
                Task.FromResult(Answer(request, apply));

            public Task<RecipeDuplicateResult> RetireDuplicatesAsync(RecipeDuplicateRequestModel request, bool apply = false) => Task.FromResult(new RecipeDuplicateResult());

            public Task<RecipeReprocessUndoResult> UndoAsync(DateTime? sinceUtc, string? batchId, string? kind = null, bool apply = false, bool laneOnly = false) =>
                Task.FromResult(new RecipeReprocessUndoResult());
        }

        private static async Task<List<long>> CandidatesAsync(ApplicationDbContext db, int count, bool approved = false)
        {
            var ids = new List<long>();
            for (var i = 0; i < count; i++)
            {
                var recipe = new RecipeEntity { Name = "R" + i, CurationStatusId = (long)(approved ? CurationStatusEnum.Curated : CurationStatusEnum.NonCurated) };
                db.Recipes.Add(recipe);
                await db.SaveChangesAsync();
                db.ReprocessCandidates.Add(new ReprocessCandidateEntity
                {
                    RecipeId = recipe.Id, Reasons = ReprocessReason.Amount, RuleVersion = RecipeLineBuilder.Version, Tier = ReprocessTier.Rebuild, Priority = 300, DetectedAt = Imported,
                });
                ids.Add(recipe.Id);
            }
            await db.SaveChangesAsync();
            return ids;
        }

        [Fact]
        public async Task A_batch_whose_changed_lines_fail_the_rule_checks_or_that_holds_too_many_approved_recipes_reports_a_fault()
        {
            using var db = await SeededAsync();
            await CandidatesAsync(db, 30, approved: true);
            var scripted = new Scripted
            {
                Answer = (request, _) => new RecipeReprocessResult
                {
                    Results = request.RecipeIds.Select((id, i) => i < 2
                        ? new RecipeReprocessRecipeResult { RecipeId = id, Outcome = RecipeReprocessOutcome.Held, Reason = RecipeReprocessHold.ApprovedFewerAmounts }
                        : new RecipeReprocessRecipeResult
                        {
                            RecipeId = id, Outcome = RecipeReprocessOutcome.Rebuilt,
                            Lines =
                            {
                                new RecipeReprocessLineDiff { RawLine = "2 tablespns. water", OldFood = "water", OldQuantity = 2, OldMeasurementId = Piece, NewFood = "water", NewQuantity = 2, NewMeasurementId = Tablespoon, Changes = { "amount" } },
                                new RecipeReprocessLineDiff { RawLine = "2 cups flour", OldFood = "flour", OldQuantity = 2, OldMeasurementId = Cup, NewFood = "Sugar", NewQuantity = 2, NewMeasurementId = Cup, Changes = { "food" } },
                            },
                        }).ToList(),
                },
            };

            var batch = await Lane(db, reprocess: scripted).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Shadow, 100);

            batch.Taken.Should().Be(30);
            batch.LinesChecked.Should().BeGreaterThanOrEqualTo(ReprocessLaneService.MinimumLinesForShare);
            batch.FailuresByCheck.Should().ContainKey(ReprocessRuleAudit.Named).And.ContainKey(ReprocessRuleAudit.NoWorse);
            batch.Faults.Should().Contain(f => f.Contains("rule checks passed")).And.Contain(f => f.Contains("approved recipes were held"));
        }

        [Fact]
        public async Task The_lane_records_every_outcome_although_a_failed_rebuild_cleared_the_shared_change_tracker()
        {
            using var db = await SeededAsync();
            var ids = await CandidatesAsync(db, 3);
            db.ChangeTracker.Clear();
            var scripted = new Scripted
            {
                Answer = (request, _) =>
                {
                    db.ChangeTracker.Clear();
                    return new RecipeReprocessResult
                    {
                        Results = request.RecipeIds.Select(id => new RecipeReprocessRecipeResult { RecipeId = id, Outcome = RecipeReprocessOutcome.Failed, Reason = "numeric field overflow" }).ToList(),
                    };
                },
            };

            var batch = await Lane(db, reprocess: scripted).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Live, 10);

            batch.Failed.Should().Be(3);
            db.ChangeTracker.Clear();
            (await db.ReprocessCandidates.AsNoTracking().Where(c => ids.Contains(c.RecipeId)).Select(c => c.Outcome).ToListAsync())
                .Should().OnlyContain(o => o == ReprocessOutcome.Failed);
        }

        [Fact]
        public async Task The_judge_audits_the_changed_lines_and_the_rule_audit_confirms_a_flag_only_when_a_line_fails_a_check()
        {
            var judge = new FakeJudge { Answer = c => c.SourceLine.Contains("flour") ? LineJudgement.Worse : LineJudgement.Same };
            var auditor = new ReprocessLaneService.LineJudgeAuditor(judge);
            LaneDecisionEntity Decision(params ReprocessSignalLine[] lines) => new()
            {
                SignalsJson = LaneJournal.Write(new ReprocessSignals(1, "Cake", "rebuilt", null, new[] { "D2" }, "rl-1", null, false, lines)),
            };
            ReprocessSignalLine Line(string raw, params string[] failures) =>
                new(raw, "x", 1, "Cup", null, "x", 2, "Cup", null, false, false, null, string.Empty, new[] { "amount" }, failures);

            (await auditor.AuditAsync(Decision(Line("1 cup sugar"), Line("2 cups flour")), CancellationToken.None)).Wrong.Should().BeTrue();
            (await auditor.AuditAsync(Decision(Line("1 cup sugar")), CancellationToken.None)).Wrong.Should().BeFalse();
            (await auditor.AuditAsync(Decision(), CancellationToken.None)).Wrong.Should().BeFalse("only the order changed");
            judge.Answer = _ => LineJudgement.Unclear;
            (await auditor.AuditAsync(Decision(Line("1 cup sugar")), CancellationToken.None)).Wrong.Should().BeNull();

            var check = new ReprocessLaneService.RuleAuditCheck();
            (await check.ConfirmsWrongAsync(Decision(Line("2 cups flour", ReprocessRuleAudit.Grounded)), CancellationToken.None)).Should().BeTrue();
            (await check.ConfirmsWrongAsync(Decision(Line("2 cups flour")), CancellationToken.None)).Should().BeFalse();
        }

        [Fact]
        public async Task The_rebuild_tier_cannot_go_live_without_a_current_judge_record_and_reports_a_judge_it_cannot_reach()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            await RecipeAsync(db, "Spoon", new[] { "2 tablespns. water", "1 cup flour" },
                new[] { new Row(water, "2 tablespns. water", 2, Piece), new Row(flour, "1 cup flour", 1, Cup, 1) });
            await Detector(db).DetectBatchAsync(0, 100);
            var judge = new FakeJudge { IsConfigured = false };
            var lane = Lane(db, judge);
            var batch = await lane.RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Shadow, 50);

            var verdict = await lane.AuditAndEvaluateAsync(ReprocessTier.Rebuild, batch.Faults);

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Figures.GoldenProblem.Should().Contain("line judge");
            verdict.Figures.PauseReasons.Should().Contain(r => r.Contains("no model host"));
            var state = await db.LaneStates.SingleAsync(s => s.Lane == AiLanes.Reprocess && s.Tier == ReprocessTier.Rebuild);
            state.SignatureHash.Should().Be(lane.Signature(ReprocessTier.Rebuild));
            lane.Signature(ReprocessTier.Order).Should().NotBe(lane.Signature(ReprocessTier.Rebuild));
        }

        [Fact]
        public async Task The_order_tier_is_audited_by_an_exact_rule_and_needs_no_judge()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var eggs = await FoodAsync(db, "eggs", 50);
            await RecipeAsync(db, "Order", new[] { "1 cup sugar", "3 eggs" }, new[] { new Row(eggs, "3 eggs", 3, Piece), new Row(sugar, "1 cup sugar", 1, Cup) });
            await Detector(db).DetectBatchAsync(0, 100);
            var judge = new FakeJudge { IsConfigured = false };
            var lane = Lane(db, judge);
            await lane.RunBatchAsync(ReprocessTier.Order, LaneMode.Shadow, 50);

            var verdict = await lane.AuditAndEvaluateAsync(ReprocessTier.Order, Array.Empty<string>());

            verdict.Figures.AuditorExact.Should().BeTrue();
            verdict.Figures.GoldenProblem.Should().BeNull();
            verdict.Figures.PauseReasons.Should().BeEmpty();
            verdict.Figures.Audited.Should().Be(1);
            judge.Calls.Should().Be(0);
        }

        [Fact]
        public void The_rule_audit_grounds_an_amount_only_in_a_number_a_range_or_an_article_before_a_measure_or_the_food()
        {
            var method = new[] { "Add the onions and cook 10 minutes, then 2 more onions." };
            ReprocessRuleAudit.Failures(new("onions", "onions", 5, "Piece", null, FilledFromMethod: true, GramsPerItem: 110), method)
                .Should().Equal(ReprocessRuleAudit.Grounded);
            ReprocessRuleAudit.GroundedIn(5m, method).Should().BeFalse("5 lies between 2 and 10 but no range is stated");
            ReprocessRuleAudit.GroundedIn(2.5m, new[] { "2 to 3 cups of milk" }).Should().BeTrue();
            ReprocessRuleAudit.GroundedIn(2.5m, new[] { "2-3 cups of milk" }).Should().BeTrue();
            ReprocessRuleAudit.GroundedIn(1m, new[] { "Put the beef in a pan with the onions." }, new[] { "onions" }).Should().BeFalse("a pan is not an onion");
            ReprocessRuleAudit.GroundedIn(1m, new[] { "Slice an onion thinly." }, new[] { "onions" }).Should().BeTrue();
            ReprocessRuleAudit.GroundedIn(0.5m, new[] { "Stir the oatmeal into a teacupful of water." }, new[] { "water" }).Should().BeTrue();
            ReprocessRuleAudit.GroundedIn(30m, new[] { "2 cans of 15 oz tomatoes" }).Should().BeTrue();
            ReprocessRuleAudit.GroundedIn(8m, new[] { "two quarts of water", "a cup of sugar" }).Should().BeTrue();
            ReprocessRuleAudit.GroundedIn(4m, new[] { "a cup of sugar", "a quart of water" }).Should().BeTrue();
            ReprocessRuleAudit.GroundedIn(4m, new[] { "a cup of sugar and 1 egg" }).Should().BeFalse("a quart named in another text gives no factor here");
        }

        [Fact]
        public void A6_fails_a_rebuild_whose_kcal_per_serving_leaves_the_band_and_passes_one_that_comes_closer()
        {
            ReprocessRuleAudit.KcalPerServing(new[] { new ReprocessRuleAudit.Portion(200, 387, null), new ReprocessRuleAudit.Portion(100, null, null), new ReprocessRuleAudit.Portion(50, 900, "note") }, 4)
                .Should().Be(193.5m);
            ReprocessRuleAudit.KcalPerServing(new[] { new ReprocessRuleAudit.Portion(200, 387, null) }, null).Should().BeNull("a label with no servings is a whole-recipe total");
            ReprocessRuleAudit.KcalPlausible(194, 3890).Should().BeFalse();
            ReprocessRuleAudit.KcalPlausible(194, 20).Should().BeFalse();
            ReprocessRuleAudit.KcalPlausible(5000, 3000).Should().BeTrue("it comes closer to the band");
            ReprocessRuleAudit.KcalPlausible(194, null).Should().BeTrue();
            ReprocessRuleAudit.KcalPlausible(null, 400).Should().BeTrue();
        }

        [Fact]
        public async Task A_rebuild_whose_kcal_per_serving_leaves_the_band_fails_A6_and_the_rule_audit_confirms_its_flag()
        {
            using var db = await SeededAsync();
            (await db.Measurements.FindAsync(Cup))!.BaseUnitConversionFactor = 236.588m;
            var sugar = await FoodAsync(db, "sugar");
            sugar.GramsPerMilliliter = 0.85m;
            db.Nutrients.Add(new NutrientEntity { Id = 1, Name = "Calories" });
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = sugar.Id, NutrientId = 1, Amount = 387, MeasurementId = Gram });
            var recipe = new RecipeEntity
            {
                Name = "Sugar cake", CurationStatusId = (long)CurationStatusEnum.NonCurated, Servings = 4,
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = sugar.Id, Quantity = 1, MeasurementId = Cup, RawLine = "1 cup sugar", CreatedDate = Imported } },
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.ReprocessCandidates.Add(new ReprocessCandidateEntity
            {
                RecipeId = recipe.Id, Reasons = ReprocessReason.Amount, RuleVersion = RecipeLineBuilder.Version, Tier = ReprocessTier.Rebuild, Priority = 300, DetectedAt = Imported,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var scripted = new Scripted
            {
                Answer = (request, _) => new RecipeReprocessResult
                {
                    Results = request.RecipeIds.Select(id => new RecipeReprocessRecipeResult
                    {
                        RecipeId = id, Outcome = RecipeReprocessOutcome.Rebuilt,
                        Lines =
                        {
                            new RecipeReprocessLineDiff
                            {
                                RawLine = "1 gallon sugar", OldIngredientId = sugar.Id, OldFood = "sugar", OldQuantity = 1, OldMeasurementId = Cup,
                                NewIngredientId = sugar.Id, NewFood = "sugar", NewQuantity = 16, NewMeasurementId = Cup, Changes = { "amount" },
                            },
                        },
                    }).ToList(),
                },
            };

            var batch = await Lane(db, reprocess: scripted).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Shadow, 10);

            batch.FailuresByCheck.Should().ContainKey(ReprocessRuleAudit.Plausible);
            var decision = await db.LaneDecisions.SingleAsync();
            LaneJournal.Read<ReprocessSignals>(decision.SignalsJson)!.Implausible.Should().BeTrue();
            (await new ReprocessLaneService.RuleAuditCheck().ConfirmsWrongAsync(decision, CancellationToken.None)).Should().BeTrue();
        }

        [Fact]
        public async Task The_order_auditor_flags_an_order_restore_whose_rows_do_not_follow_the_source()
        {
            var auditor = new ReprocessLaneService.OrderRuleAuditor();
            var check = new ReprocessLaneService.RuleAuditCheck();
            LaneDecisionEntity Decision(params string[] newOrder) => new()
            {
                SignalsJson = LaneJournal.Write(new ReprocessSignals(1, "Cake", "rebuilt", null, new[] { "D1" }, "rl-1", null, false, Array.Empty<ReprocessSignalLine>(),
                    SourceOrder: new[] { "1 cup sugar", "1 cup sugar", "3 eggs", "1 cup flour" }, NewOrder: newOrder)),
            };

            (await auditor.AuditAsync(Decision("1 cup sugar", "1 cup sugar", "3 eggs", "1 cup flour"), CancellationToken.None)).Wrong.Should().BeFalse();
            (await auditor.AuditAsync(Decision("1 cup sugar", "3 eggs", "1 cup sugar"), CancellationToken.None)).Wrong.Should().BeTrue();
            (await check.ConfirmsWrongAsync(Decision("3 eggs", "1 cup sugar"), CancellationToken.None)).Should().BeTrue();
            ReprocessRuleAudit.FollowsSource(new[] { "1 cup flour", "1 tsp salt", "2 eggs" }, new[] { "1 cup flour + 1 tsp salt", "2 eggs" }).Should().BeTrue();
        }

        [Fact]
        public async Task An_order_batch_checks_every_restore_against_the_source_order()
        {
            using var db = await SeededAsync();
            var sugar = await FoodAsync(db, "sugar");
            var eggs = await FoodAsync(db, "eggs", 50);
            await RecipeAsync(db, "Order", new[] { "1 cup sugar", "3 eggs" }, new[] { new Row(eggs, "3 eggs", 3, Piece), new Row(sugar, "1 cup sugar", 1, Cup) });
            await Detector(db).DetectBatchAsync(0, 100);

            var batch = await Lane(db).RunBatchAsync(ReprocessTier.Order, LaneMode.Shadow, 50);

            (batch.LinesChecked, batch.LinesFailing).Should().Be((1, 0));
            var signals = LaneJournal.Read<ReprocessSignals>((await db.LaneDecisions.SingleAsync()).SignalsJson)!;
            signals.SourceOrder.Should().Equal("1 cup sugar", "3 eggs");
            signals.NewOrder.Should().Equal("1 cup sugar", "3 eggs");
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("mistral-nemo:12b", null)]
        [InlineData("mistral-small", null)]
        [InlineData("qwen2.5:14b-instruct", "reader B's family")]
        [InlineData("phi4", "reader B's family")]
        [InlineData("llama3.1:8b", "reader B's family")]
        public void The_line_judge_must_be_of_reader_bs_family(string? judge, string? problem)
        {
            var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Reprocess:JudgeModel"] = judge }).Build();

            var found = OllamaLineJudge.FamilyProblem(settings);

            if (problem == null) found.Should().BeNull();
            else found.Should().Contain(problem);
        }

        [Fact]
        public async Task The_lanes_undo_takes_back_only_its_own_batches_and_marks_those_candidates_undone()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            var byLane = await RecipeAsync(db, "Lane", new[] { "2 tablespns. water", "1 cup flour" },
                new[] { new Row(water, "2 tablespns. water", 2, Piece), new Row(flour, "1 cup flour", 1, Cup, 1) });
            var byOperator = await RecipeAsync(db, "Operator", new[] { "1 teaspn. sugar", "1 cup flour" },
                new[] { new Row(sugar, "1 teaspn. sugar", 1, Piece), new Row(flour, "1 cup flour", 1, Cup, 1) });
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Detector(db).DetectBatchAsync(0, 100);
            var candidate = await db.ReprocessCandidates.SingleAsync(c => c.RecipeId == byOperator.Id);
            candidate.Outcome = ReprocessOutcome.Held;
            await db.SaveChangesAsync();
            await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Live, 50);
            await Reprocess(db).RebuildAsync(new RecipeReprocessRequestModel { RecipeIds = new List<long> { byOperator.Id } }, apply: true);
            db.ChangeTracker.Clear();
            var undo = new ReprocessLaneUndo(Reprocess(db), db);

            var dry = await undo.UndoSinceAsync(ReprocessTier.Rebuild, since, apply: false, CancellationToken.None);
            var applied = await undo.UndoSinceAsync(ReprocessTier.Rebuild, since, apply: true, CancellationToken.None);

            (dry.Undone, applied.Undone).Should().Be((1, 1));
            (await LinesAsync(db, byLane.Id)).Single(l => l.IngredientId == water.Id).MeasurementId.Should().Be(Piece);
            (await LinesAsync(db, byOperator.Id)).Single(l => l.IngredientId == sugar.Id).MeasurementId.Should().Be(Teaspoon, "an operator's rebuild is not the lane's to undo");
            (await db.ReprocessCandidates.SingleAsync(c => c.RecipeId == byLane.Id)).Outcome.Should().Be(ReprocessOutcome.Undone);
            (await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Live, 50)).Taken.Should().Be(0, "an undone recipe waits for the next rules");
        }

        [Fact]
        public async Task The_detector_marks_second_copies_as_duplicates()
        {
            using var db = await SeededAsync();
            var flour = await FoodAsync(db, "flour");
            var sugar = await FoodAsync(db, "sugar");
            var first = await RecipeAsync(db, "Plain Cake", new[] { "1 cup flour", "1 cup sugar" }, new[] { new Row(flour, "1 cup flour", 1, Cup), new Row(sugar, "1 cup sugar", 1, Cup, 1) });
            var second = await RecipeAsync(db, "Plain Cake", new[] { "1 cup flour", "1 cup sugar" }, new[] { new Row(flour, "1 cup flour", 1, Cup), new Row(sugar, "1 cup sugar", 1, Cup, 1) });

            (await Detector(db).MarkDuplicatesAsync()).Should().Be(1);
            await Detector(db).DetectBatchAsync(0, 100);

            var rows = await db.ReprocessCandidates.AsNoTracking().ToDictionaryAsync(c => c.RecipeId);
            rows[second.Id].Reasons.Should().Be(ReprocessReason.Duplicate, "the mark stays when the recipe is read");
            rows[first.Id].Reasons.Should().Be(0);
            (await Lane(db).RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Live, 50)).Taken.Should().Be(0, "a duplicate is for the duplicate retire, not a rebuild");
        }

        [Fact]
        public async Task The_status_counts_candidates_by_reason_and_outcome_and_shows_the_last_batch()
        {
            using var db = await SeededAsync();
            var water = await FoodAsync(db, "water");
            var flour = await FoodAsync(db, "flour");
            await RecipeAsync(db, "Spoon", new[] { "2 tablespns. water", "1 cup flour" },
                new[] { new Row(water, "2 tablespns. water", 2, Piece), new Row(flour, "1 cup flour", 1, Cup, 1) });
            await Detector(db).DetectBatchAsync(0, 100);
            var lane = Lane(db);

            var before = await lane.StatusAsync(enabled: true);
            await lane.RunBatchAsync(ReprocessTier.Rebuild, LaneMode.Shadow, 50);
            var after = await lane.StatusAsync(enabled: true);

            (before.Read, before.Waiting, before.WaitingByTier[ReprocessTier.Rebuild]).Should().Be((1, 1, 1));
            before.ByReason.Single(r => r.Code == "D2").Should().Be(new ReprocessReasonCount("D2", "amount read differently", 1, 1));
            before.JudgeProblem.Should().Contain("no golden score record");
            after.ByOutcome[ReprocessOutcome.Shadow].Should().Be(1);
            after.Waiting.Should().Be(1, "a recipe decided in shadow still waits for the live lane");
            after.LastBatches.Single().Should().Match<ReprocessLaneBatchResult>(b => b.Tier == ReprocessTier.Rebuild && b.Rebuilt == 1);
        }
    }
}
