using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The admin pipeline page counts each recipe line once by the label rule, buckets waiting
    /// recipes and unresolved wordings, reports the link lane's proposals and caps, and reads each
    /// lane's settings and queue by the lane's own rule.
    /// </summary>
    public class PipelineStatusTests
    {
        private const long Gram = 1, Cup = 2, Piece = 3, Can = 4;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static async Task<ApplicationDbContext> SeededAsync()
        {
            var db = NewContext();
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 240m },
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Can, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true });
            await db.SaveChangesAsync();
            return db;
        }

        private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
            new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();

        private static PipelineStatusService Service(ApplicationDbContext db, IConfiguration? configuration = null, bool matcherConfigured = true, IMemoryCache? cache = null)
        {
            var matcher = new Mock<IIngredientLinkMatcher>();
            matcher.SetupGet(m => m.IsConfigured).Returns(matcherConfigured);
            matcher.SetupGet(m => m.ModelName).Returns("qwen2.5:14b-instruct");
            var repair = new Mock<IRecipeRepairModel>();
            repair.SetupGet(m => m.IsConfigured).Returns(true);
            repair.SetupGet(m => m.ModelName).Returns("qwen2.5:14b-instruct");
            var classify = new Mock<IRecipeClassificationModel>();
            classify.SetupGet(m => m.IsConfigured).Returns(true);
            classify.SetupGet(m => m.ModelName).Returns("qwen2.5:14b-instruct");
            var tagger = new Mock<IRecipeToolTagger>();
            tagger.SetupGet(m => m.IsConfigured).Returns(false);
            tagger.SetupGet(m => m.ModelName).Returns("qwen2.5:3b");
            var memory = cache ?? new MemoryCache(new MemoryCacheOptions());
            var links = new IngredientLinkService(db, matcher.Object, new Mock<IIngredientLinkVerifier>().Object, memory, NullLogger<IngredientLinkService>.Instance);
            return new PipelineStatusService(db, memory, configuration ?? Config(), links, new LinkAutoApplyService(db, memory),
                matcher.Object, repair.Object, classify.Object, tagger.Object);
        }

        private static IngredientEntity Food(string name, long status = 9003, bool nutrients = true, decimal? gramsPerItem = null, decimal? density = null, decimal? serving = null) => new()
        {
            Name = name,
            CurationStatusId = status,
            GramsPerItem = gramsPerItem,
            GramsPerMilliliter = density,
            ReferenceServingGrams = serving,
            IngredientNutrients = nutrients ? new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } } : new List<IngredientNutrientEntity>(),
        };

        private static RecipeEntity Recipe(string name, long status, bool deleted = false) => new() { Name = name, CurationStatusId = status, IsDeleted = deleted };

        private static int _sort;

        private static RecipeIngredientEntity Line(RecipeEntity recipe, IngredientEntity food, decimal quantity, long measurement, string? kind = null, string? raw = null, bool deleted = false) => new()
        {
            Recipe = recipe,
            Ingredient = food,
            Quantity = quantity,
            MeasurementId = measurement,
            LineKind = kind,
            RawLine = raw ?? food.Name,
            SortOrder = System.Threading.Interlocked.Increment(ref _sort),
            IsDeleted = deleted,
        };

        [Fact]
        public async Task The_line_funnel_puts_each_line_in_one_place()
        {
            await using var db = await SeededAsync();
            var flour = Food("Flour");
            var milk = Food("Milk");
            var egg = Food("Egg", serving: 50m);
            var lemon = Food("Lemon");
            var mystery = Food("mystery spice", status: 9000, nutrients: false);
            var approved = Recipe("Approved", 9003);
            var waiting = Recipe("Waiting", 9000);
            var gone = Recipe("Deleted", 9003, deleted: true);
            db.RecipeIngredients.AddRange(
                Line(approved, flour, 1, Gram, kind: RecipeIngredientLineKind.Equipment),
                Line(approved, flour, 0, Gram),
                Line(approved, mystery, 2, Gram),
                Line(approved, flour, 100, Gram),
                Line(approved, milk, 1, Cup),
                Line(approved, egg, 2, Piece),
                Line(approved, flour, 1, Gram, raw: "1 cup + 2 tbsp flour"),
                Line(approved, flour, 5, Gram, deleted: true),
                Line(waiting, lemon, 1, Piece),
                Line(waiting, egg, 1, Can),
                Line(gone, flour, 100, Gram));
            await db.SaveChangesAsync();

            var status = await Service(db).GetAsync();

            status.Lines.All.Should().BeEquivalentTo(new PipelineLineFunnel
            {
                Total = 9, NotAnIngredient = 1, Ingredient = 8, OnCuratedFood = 7, Unresolved = 1, NotMeasured = 1, NoData = 1,
                NoConversion = 3, Counted = 3, CountedByServingFallback = 1, CountWithoutItemWeight = 3, VolumeWithoutDensity = 1, CountableStrict = 2,
            });
            status.Lines.Approved.Should().BeEquivalentTo(new PipelineLineFunnel
            {
                Total = 7, NotAnIngredient = 1, Ingredient = 6, OnCuratedFood = 5, Unresolved = 1, NotMeasured = 1, NoData = 1,
                NoConversion = 1, Counted = 3, CountedByServingFallback = 1, CountWithoutItemWeight = 1, VolumeWithoutDensity = 1, CountableStrict = 2,
            });
            status.Lines.Folded.Should().Be(1);
            status.Lines.FoldedOnApproved.Should().Be(1);
            status.Recipes.ApprovedWithUncountedLine.Should().Be(1);

            var live = await db.RecipeIngredients.Include(ri => ri.Measurement).Include(ri => ri.Ingredient).ThenInclude(i => i.IngredientNutrients)
                .Where(ri => !ri.Recipe.IsDeleted).ToListAsync();
            var byRule = live.GroupBy(ri => RecipeNutritionService.StatusFor(ri, ri.Ingredient.IngredientNutrients.Any())).ToDictionary(g => g.Key, g => g.Count());
            byRule.GetValueOrDefault(NutritionLineStatus.Counted).Should().Be(status.Lines.All.Counted);
            byRule.GetValueOrDefault(NutritionLineStatus.NoConversion).Should().Be(status.Lines.All.NoConversion);
            byRule.GetValueOrDefault(NutritionLineStatus.NoData).Should().Be(status.Lines.All.NoData);
            byRule.GetValueOrDefault(NutritionLineStatus.NotMeasured).Should().Be(status.Lines.All.NotMeasured);
            byRule.GetValueOrDefault(NutritionLineStatus.NotAnIngredient).Should().Be(status.Lines.All.NotAnIngredient);
        }

        [Fact]
        public async Task Waiting_recipes_are_bucketed_by_distinct_uncurated_foods()
        {
            await using var db = await SeededAsync();
            var curated = Food("Salt");
            var uncurated = Enumerable.Range(1, 4).Select(i => Food($"wording {i}", status: 9000, nutrients: false)).ToList();
            var none = Recipe("none", 9000);
            var noteOnly = Recipe("note only", 9000);
            var one = Recipe("one", 9000);
            var two = Recipe("two", 9000);
            var four = Recipe("four", 9000);
            db.RecipeIngredients.AddRange(
                Line(none, curated, 1, Gram),
                Line(noteOnly, uncurated[0], 0, Gram, kind: RecipeIngredientLineKind.Note),
                Line(one, uncurated[0], 1, Gram),
                Line(one, uncurated[0], 2, Gram),
                Line(two, uncurated[0], 1, Gram),
                Line(two, uncurated[1], 1, Gram));
            db.RecipeIngredients.AddRange(uncurated.Select(u => Line(four, u, 1, Gram)));
            await db.SaveChangesAsync();

            var status = await Service(db).GetAsync();

            status.Recipes.Waiting.Should().Be(5);
            status.Recipes.WaitingBlockedBy.Should().BeEquivalentTo(new PipelineBlockedBy { None = 2, One = 1, Two = 1, ThreeOrMore = 1 });
            status.Lanes.Single(l => l.Key == "auto-approve").Queue.Should().Be(2);
        }

        [Fact]
        public async Task Wordings_in_use_counts_any_line_and_unresolved_wordings_only_ingredient_lines()
        {
            await using var db = await SeededAsync();
            var recipe = Recipe("big", 9000);
            var uses = new[] { 1, 4, 5, 19, 20, 99, 100, 1, 1, 1, 1, 1 };
            for (var i = 0; i < uses.Length; i++)
            {
                var food = Food($"wording {i:00}", status: 9000, nutrients: false);
                for (var n = 0; n < uses[i]; n++) db.RecipeIngredients.Add(Line(recipe, food, 1, Gram));
            }
            db.RecipeIngredients.Add(Line(recipe, Food("serves four", status: 9000, nutrients: false), 0, Gram, kind: RecipeIngredientLineKind.Note));
            await db.SaveChangesAsync();

            var status = await Service(db).GetAsync();

            status.Foods.WordingsInUse.Should().Be(13);
            status.Foods.UnresolvedWordings.Should().Be(12);
            status.Foods.WordingsByUse.Should().BeEquivalentTo(new[]
            {
                new PipelineWordingBucket("1", 6, 6),
                new PipelineWordingBucket("2-4", 1, 4),
                new PipelineWordingBucket("5-19", 2, 24),
                new PipelineWordingBucket("20-99", 2, 119),
                new PipelineWordingBucket("100+", 1, 100),
            }, o => o.WithStrictOrdering());
            status.Stuck.UnresolvedWordings.Should().HaveCount(PipelineStatusService.TopCount);
            status.Stuck.UnresolvedWordings.Select(s => s.Lines).Should().BeInDescendingOrder();
            status.Stuck.UnresolvedWordings.Take(3).Select(s => s.Name).Should().Equal("wording 06", "wording 05", "wording 04");
            status.Stuck.UnresolvedWordings.Where(s => s.Lines == 1).Select(s => s.Name).Should().BeInAscendingOrder();
        }

        [Fact]
        public async Task Unlinked_foods_split_by_proposal_state()
        {
            await using var db = await SeededAsync();
            var recipe = Recipe("r", 9000);
            var pending = Food("pending", 9000, false);
            var noCandidate = Food("no candidate", 9000, false);
            var rejected = Food("rejected", 9000, false);
            var never = Food("never", 9000, false);
            foreach (var (food, lines) in new[] { (pending, 2), (noCandidate, 3), (rejected, 1), (never, 4) })
                for (var i = 0; i < lines; i++) db.RecipeIngredients.Add(Line(recipe, food, 1, Gram));
            await db.SaveChangesAsync();
            db.FoodCatalogProposals.AddRange(
                Proposal(pending.Id, "ai:qwen", FoodProposalStatus.Pending),
                Proposal(noCandidate.Id, IngredientLinkService.NoCandidateSource, FoodProposalStatus.Rejected),
                Proposal(rejected.Id, "ai:qwen", FoodProposalStatus.Rejected));
            await db.SaveChangesAsync();

            var status = await Service(db).GetAsync();

            status.Foods.UnresolvedByLinkState.Should().BeEquivalentTo(new[]
            {
                new PipelineLinkStateCount("pendingPick", 1, 2),
                new PipelineLinkStateCount("noCandidate", 1, 3),
                new PipelineLinkStateCount("rejectedPick", 1, 1),
                new PipelineLinkStateCount("neverProposed", 1, 4),
            }, o => o.WithStrictOrdering());
        }

        private static FoodCatalogProposalEntity Proposal(long? ingredientId, string source, FoodProposalStatus status, string? reason = null, string? fdcId = null) => new()
        {
            Action = FoodProposalAction.Update,
            Batch = IngredientLinkService.Batch,
            IngredientId = ingredientId,
            Field = FoodCatalogReviewService.FdcLinkField,
            Source = source,
            Status = status,
            Reason = reason,
            FdcId = fdcId,
            CreatedDate = DateTime.UtcNow,
        };

        [Fact]
        public async Task Proposals_report_pending_by_source_and_the_verified_allowance()
        {
            await using var db = await SeededAsync();
            db.FoodCatalogProposals.AddRange(
                Proposal(null, "ai:qwen", FoodProposalStatus.Pending, "→ x" + IngredientLinkService.VerifiedMark),
                Proposal(null, "ai:qwen", FoodProposalStatus.Pending, "→ y"),
                Proposal(null, IngredientLinkService.ExactSource, FoodProposalStatus.Pending),
                Proposal(null, "ai:qwen", FoodProposalStatus.Applied, fdcId: "111") .With(p => { p.ReviewedByPersonId = SystemConstants.SystemPersonId; p.ReviewedAt = DateTime.UtcNow.AddHours(-2); }),
                Proposal(null, "ai:qwen", FoodProposalStatus.Applied, fdcId: "222") .With(p => { p.ReviewedByPersonId = SystemConstants.SystemPersonId; p.ReviewedAt = DateTime.UtcNow.AddHours(-30); }));
            await db.SaveChangesAsync();
            var config = Config(new()
            {
                ["IngredientLinking:MaxVerifiedAppliesPerDay"] = "5",
                ["IngredientLinking:MaxVerifiedAppliesPerFoodPerDay"] = "1",
            });

            var status = await Service(db, config).GetAsync();

            status.Proposals.Should().BeEquivalentTo(new
            {
                Pending = 3, PendingModelPicks = 2, PendingModelPicksVerified = 1, PendingExactMatches = 1, PendingOther = 0,
                VerifiedAppliedLast24h = 1, VerifiedDailyCap = 5, VerifiedPerFoodCap = 1, FoodsAtCap = 1,
                NameMatchDailyCap = 200, NameMatchPerFoodCap = 25,
            });
            var link = status.Lanes.Single(l => l.Key == "ingredient-link");
            link.Caps.Should().ContainEquivalentOf(new PipelineLaneCap("Verified model picks per day", 5, 1));
            link.Caps.Should().ContainEquivalentOf(new PipelineLaneCap("Verified model picks per food per day", 1, null));
            link.Caps.Should().ContainEquivalentOf(new PipelineLaneCap("Name matches per food per day", 25, null));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("1")]
        public async Task Applied_counts_are_the_real_applies_when_the_cap_is_lowered_below_them(string cap)
        {
            await using var db = await SeededAsync();
            foreach (var (source, fdc) in new[] { ("ai:qwen", "1"), ("ai:qwen", "2"), ("ai:qwen", "3"), (IngredientLinkService.ExactSource, "4"), (IngredientLinkService.ExactSource, "5") })
                db.FoodCatalogProposals.Add(Proposal(null, source, FoodProposalStatus.Applied, fdcId: fdc)
                    .With(p => { p.ReviewedByPersonId = SystemConstants.SystemPersonId; p.ReviewedAt = DateTime.UtcNow.AddHours(-1); }));
            await db.SaveChangesAsync();
            var config = Config(new()
            {
                ["IngredientLinking:MaxVerifiedAppliesPerDay"] = cap,
                ["IngredientLinking:MaxNameMatchAppliesPerDay"] = cap,
            });

            var status = await Service(db, config).GetAsync();

            status.Proposals.VerifiedAppliedLast24h.Should().Be(3);
            status.Proposals.NameMatchAppliedLast24h.Should().Be(2);
            var link = status.Lanes.Single(l => l.Key == "ingredient-link");
            link.Caps.Should().ContainEquivalentOf(new PipelineLaneCap("Verified model picks per day", int.Parse(cap), 3));
            link.Caps.Should().ContainEquivalentOf(new PipelineLaneCap("Name matches per day", int.Parse(cap), 2));
        }

        [Theory]
        [InlineData(null, null, false)]
        [InlineData("100", null, true)]
        [InlineData(null, "true", true)]
        [InlineData("100", "false", false)]
        public async Task Prose_drain_is_on_by_default_only_under_a_daily_cap(string? cap, string? drain, bool expected)
        {
            await using var db = await SeededAsync();
            var settings = new Dictionary<string, string?>
            {
                ["Ai:Lanes:prose-rewrite:MaxItemsPerDay"] = cap,
                ["ProseRewrite:DrainWhileFull"] = drain,
            };
            var config = Config(settings.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value));

            var status = await Service(db, config).GetAsync();

            status.Lanes.Single(l => l.Key == "prose-rewrite").Switches.Single(s => s.Key == "DrainWhileFull").On.Should().Be(expected);
            Nom.Orch.Services.Support.AiLanes.ProseDrainWhileFull(config).Should().Be(expected);
        }

        [Fact]
        public async Task Lanes_show_switches_caps_and_missing_models()
        {
            await using var db = await SeededAsync();
            var config = Config(new()
            {
                ["IngredientLinking:AutoApplyVerifiedAi"] = "false",
                ["Curation:AutoApprove:RequireOwnImage"] = "false",
                ["Curation:AutoApprove:MaxUnquantifiedLines"] = "2",
                ["Curation:AiRepair:Enabled"] = "false",
            });

            var status = await Service(db, config, matcherConfigured: false).GetAsync();

            status.Lanes.Select(l => l.Key).Should().Equal("intake", "scraped-repair", "ai-repair", "line-classify", "ingredient-link",
                "prose-rewrite", "nutrition-recalc", "auto-approve", "dish-grouping", "tool-tagging", "image-finder");
            var link = status.Lanes.Single(l => l.Key == "ingredient-link");
            link.ModelConfigured.Should().BeFalse();
            link.Model.Should().BeNull();
            link.GateLane.Should().Be("link");
            link.Switches.Single(s => s.Key == "AutoApplyVerifiedAi").On.Should().BeFalse();
            link.Switches.Single(s => s.Key == "AutoApplyCertain").On.Should().BeTrue();
            link.Switches.Single(s => s.Key == "AutoApplyNameMatches").On.Should().BeFalse();
            link.Caps.Select(c => c.Limit).Should().Equal(500, 25, 200, 25);
            var approve = status.Lanes.Single(l => l.Key == "auto-approve");
            approve.Switches.Single(s => s.Key == "RequireOwnImage").On.Should().BeFalse();
            approve.Caps.Single().Should().Be(new PipelineLaneCap("Unquantified lines allowed", 2, null));
            status.Lanes.Single(l => l.Key == "ai-repair").Enabled.Should().BeFalse();
            status.Lanes.Single(l => l.Key == "line-classify").Should().Match<PipelineLane>(l => l.Enabled && l.ModelConfigured == true && l.Model == "qwen2.5:14b-instruct");
            status.Lanes.Single(l => l.Key == "tool-tagging").ModelConfigured.Should().BeFalse();
            status.Lanes.Single(l => l.Key == "prose-rewrite").Enabled.Should().BeFalse();
            status.Ai.QuietHours.Should().Be("01:00-05:00");
            status.Ai.QuietTimeZone.Should().Be("America/Denver");
        }

        [Fact]
        public async Task Lane_queues_follow_the_lanes_own_rules()
        {
            await using var db = await SeededAsync();
            var food = Food("Salt").With(f => f.FdcId = "173468");
            var current =Recipe("current", 9002).With(r => { r.ScrapedAtUtc = DateTime.UtcNow; r.VettingIssues = "2 ingredient line(s) have no parseable quantity"; });
            var older = Recipe("older", 9002).With(r => { r.ScrapedAtUtc = DateTime.UtcNow; r.VettingIssues = "2 ingredient line(s) have no parseable quantity"; });
            var classified = Recipe("classified", 9000);
            var unclassified = Recipe("unclassified", 9000);
            db.RecipeIngredients.AddRange(
                Line(current, food, 1, Gram), Line(older, food, 1, Gram),
                Line(classified, food, 0, Gram), Line(unclassified, food, 0, Gram));
            var staleRejection = Food("stale", 9000, false);
            var freshRejection = Food("fresh", 9000, false);
            db.RecipeIngredients.AddRange(Line(classified, staleRejection, 1, Gram), Line(classified, freshRejection, 1, Gram));
            await db.SaveChangesAsync();
            db.AuditLogEntries.AddRange(
                Audit(current.Id, RecipeAiRepairService.AttemptChangeType, RecipeAiRepairService.AttemptVersionPrefix + "[m] (clean)", DateTime.UtcNow),
                Audit(older.Id, RecipeAiRepairService.AttemptChangeType, "v1 [m] (clean)", DateTime.UtcNow),
                Audit(classified.Id, RecipeLineClassificationService.AttemptChangeType, RecipeLineClassificationService.AttemptVersionPrefix + "[m] x", DateTime.UtcNow),
                Audit(unclassified.Id, RecipeLineClassificationService.AttemptChangeType, "v0 [m] x", DateTime.UtcNow));
            foreach (var (food2, age) in new[] { (staleRejection, 8), (freshRejection, 1) })
            {
                var rejection = Proposal(food2.Id, IngredientLinkService.NoCandidateSource, FoodProposalStatus.Rejected);
                rejection.ReviewedByPersonId = SystemConstants.SystemPersonId;
                rejection.CreatedDate = DateTime.UtcNow.AddDays(-age);
                db.FoodCatalogProposals.Add(rejection);
            }
            await db.SaveChangesAsync();

            var status = await Service(db).GetAsync();

            status.Lanes.Single(l => l.Key == "ai-repair").Queue.Should().Be(1);
            status.Lanes.Single(l => l.Key == "line-classify").Queue.Should().Be(1);
            status.Proposals.LinkQueue.Should().Be(1);
            status.Lanes.Single(l => l.Key == "ingredient-link").Queue.Should().Be(1);
        }

        [Fact]
        public async Task The_prose_lane_queue_and_its_give_ups_follow_the_clean_room_lane_version()
        {
            await using var db = await SeededAsync();
            var version = Nom.Orch.Services.Support.ProseLaneModels.From(Config()).Version;
            var waiting = Recipe("withheld", 9003).With(r => r.ProseWithheld = true);
            var quarantined = Recipe("quarantined", 9000).With(r => r.ContainsSourceProse = true);
            var gaveUp = Recipe("given up", 9003).With(r => { r.ProseWithheld = true; r.ProseLaneVersion = version; r.ProseAttempts = Nom.Orch.Services.Support.ProseCleanRoom.AttemptCount; });
            var oldVersion = Recipe("given up before", 9003).With(r => { r.ProseWithheld = true; r.ProseLaneVersion = "v2-old"; r.ProseAttempts = Nom.Orch.Services.Support.ProseCleanRoom.AttemptCount; });
            db.Recipes.AddRange(waiting, quarantined, gaveUp, oldVersion);
            db.LaneDecisions.AddRange(
                new LaneDecisionEntity { Lane = ProseLaneService.Lane, Tier = ProseLaneService.Tier, Subject = "recipe:1", Decision = "exhausted:copied-text", Mode = "shadow", SignatureHash = version },
                new LaneDecisionEntity { Lane = ProseLaneService.Lane, Tier = ProseLaneService.Tier, Subject = "recipe:2", Decision = "exhausted:copied-text", Mode = "shadow", SignatureHash = version },
                new LaneDecisionEntity { Lane = ProseLaneService.Lane, Tier = ProseLaneService.Tier, Subject = "recipe:3", Decision = "exhausted:judge:missing", Mode = "shadow", SignatureHash = "v2-old" },
                new LaneDecisionEntity { Lane = ProseLaneService.Lane, Tier = ProseLaneService.Tier, Subject = "recipe:4", Decision = "rewrite", Mode = "shadow", SignatureHash = version });
            await db.SaveChangesAsync();

            var status = await Service(db).GetAsync();

            status.Lanes.Single(l => l.Key == "prose-rewrite").Queue.Should().Be(3);
            status.Lanes.Single(l => l.Key == "prose-rewrite").Switches.Should().NotContain(s => s.Key == "CheckFidelity");
            status.Recipes.ProseGaveUp.Should().Be(1);
            status.Recipes.ProseGaveUpByClass.Should().BeEquivalentTo(new Dictionary<string, int> { ["copied-text"] = 2 });
        }

        private static AuditLogEntryEntity Audit(long recipeId, string changeType, string newValue, DateTime at) => new()
        {
            EntityType = "Recipe",
            EntityId = recipeId,
            ChangeType = changeType,
            NewValue = newValue,
            Timestamp = at,
            ChangedByPersonId = SystemConstants.SystemPersonId,
        };

        [Fact]
        public async Task The_journal_is_summed_by_change_type_with_day_and_week_windows()
        {
            await using var db = await SeededAsync();
            var now = DateTime.UtcNow;
            db.AuditLogEntries.AddRange(
                new AuditLogEntryEntity { EntityType = "RecipeIngredient", ChangeType = "LineRelink", EntityId = 1, Timestamp = now.AddHours(-1), ChangedByPersonId = 1 },
                new AuditLogEntryEntity { EntityType = "RecipeIngredient", ChangeType = "LineRelink", EntityId = 2, Timestamp = now.AddDays(-3), ChangedByPersonId = 1 },
                new AuditLogEntryEntity { EntityType = "RecipeIngredient", ChangeType = "LineRelink", EntityId = 3, Timestamp = now.AddDays(-10), ChangedByPersonId = 1 },
                new AuditLogEntryEntity { EntityType = "Ingredient", ChangeType = "FoodRetire", EntityId = 4, Timestamp = now.AddDays(-20), ChangedByPersonId = 1 });
            await db.SaveChangesAsync();

            var status = await Service(db).GetAsync();

            status.Journal.Should().HaveCount(2);
            status.Journal[0].Should().BeEquivalentTo(new { EntityType = "RecipeIngredient", ChangeType = "LineRelink", Last24h = 1, Last7d = 2, Total = 3 });
            status.Journal[0].LastAt.Should().BeCloseTo(now.AddHours(-1), TimeSpan.FromSeconds(1));
            status.Journal[1].Should().BeEquivalentTo(new { EntityType = "Ingredient", ChangeType = "FoodRetire", Last24h = 0, Last7d = 0, Total = 1 });
        }

        [Fact]
        public async Task A_second_call_is_served_from_the_cache_and_refresh_recomputes()
        {
            await using var db = await SeededAsync();
            db.Recipes.Add(Recipe("first", 9003));
            await db.SaveChangesAsync();
            var service = Service(db);

            var first = await service.GetAsync();
            db.Recipes.Add(Recipe("second", 9003));
            await db.SaveChangesAsync();
            var cached = await service.GetAsync();
            var refreshed = await service.GetAsync(refresh: true);

            first.FromCache.Should().BeFalse();
            cached.FromCache.Should().BeTrue();
            cached.GeneratedAt.Should().Be(first.GeneratedAt);
            cached.Recipes.Approved.Should().Be(1);
            refreshed.FromCache.Should().BeFalse();
            refreshed.Recipes.Approved.Should().Be(2);
        }
    }

    internal static class PipelineTestExtensions
    {
        public static T With<T>(this T value, Action<T> change)
        {
            change(value);
            return value;
        }
    }
}
