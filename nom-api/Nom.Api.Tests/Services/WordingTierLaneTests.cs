using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The wording tier lane on the lane frame: a shadow tier records and writes nothing, a live
    /// tier writes its rows through the verified wording loader with its source and evidence key and
    /// moves their lines by a batch-scoped relink, the lane's undo retracts them with that evidence
    /// (never demoting a food the load curated), the rejection memory keeps a retracted pair out
    /// until the wording's lines double, a pause holds across a restart, and a tier cannot leave
    /// shadow without the link auditor's golden record.
    /// </summary>
    public class WordingTierLaneTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private sealed class FakeJudges : IWordingTierJudges, ILaneAuditor, ILaneAuditCheck
        {
            public bool IsConfigured => true;
            public string AuditorModel => "phi4";
            public string CheckModel => "mistral-nemo";
            public ILaneAuditor Auditor => this;
            public ILaneAuditCheck Check => this;
            public string Model => AuditorModel;
            public string Name => CheckModel;
            public Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) => Task.FromResult(new LaneAuditAnswer(false, "same"));
            public Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) => Task.FromResult<bool?>(false);
        }

        private const long Gram = 1;
        private const long Teaspoon = 2;

        private static ApplicationDbContext NewContext(string? name = null) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static WordingTierLaneService Lane(ApplicationDbContext db, IMemoryCache? cache = null) =>
            new(db, new VerifiedWordingService(db, new NoUser()), new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance),
                new FakeJudges(), new NoUser(), cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<WordingTierLaneService>.Instance);

        private static WordingTierUndo Undo(ApplicationDbContext db) => new(db, new VerifiedWordingService(db, new NoUser()));

        private static IngredientEntity Food(string name, string fdcId, long status = (long)CurationStatusEnum.Curated, decimal? density = 1m) => new()
        {
            Name = name,
            FdcId = fdcId,
            FdcDataType = "sr_legacy_food",
            CurationStatusId = status,
            GramsPerMilliliter = density,
            IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
        };

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

        private sealed record Kitchen(IngredientEntity Cream, IngredientEntity Creams, IngredientEntity Soda, IngredientEntity BakingSoda);

        private static async Task<Kitchen> KitchenAsync(ApplicationDbContext db, long sodaStatus = (long)CurationStatusEnum.Curated)
        {
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "gram", Symbol = "g", MeasurementCategoryId = RecipeNutritionService.CategoryMass, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Teaspoon, Name = "teaspoon", Symbol = "tsp", MeasurementCategoryId = RecipeNutritionService.CategoryVolume, BaseUnitConversionFactor = 4.93m });
            var cream = Food("Light Cream", "170857");
            var bakingSoda = Food("Leavening agents, baking soda", "175040", sodaStatus, 0.9m);
            var creams = Wording("light creams");
            var soda = Wording("baking soda");
            db.Ingredients.AddRange(cream, bakingSoda, creams, soda);
            await db.SaveChangesAsync();
            db.Recipes.AddRange(
                Recipe("Scones", (creams, "200 g light creams", 200m, Gram), (soda, "1 tsp baking soda", 1m, Teaspoon)),
                Recipe("Custard", (creams, "100 g light creams", 100m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Kitchen(cream, creams, soda, bakingSoda);
        }

        private static Dictionary<string, string> Live(params string[] tiers) => tiers.ToDictionary(t => t, _ => LaneMode.Live);

        [Fact]
        public async Task A_shadow_tier_records_what_it_would_write_and_writes_nothing()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);

            var batch = await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);

            batch.WritesByTier.Should().Contain(WordingTiers.NameKey, 1).And.Contain(WordingTiers.Staple, 1);
            batch.WrittenByTier.Should().BeEmpty();
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Cream.Id)).Should().Be(0);
            var decision = await db.LaneDecisions.SingleAsync(d => d.Tier == WordingTiers.NameKey);
            decision.Should().Match<LaneDecisionEntity>(d => d.Lane == AiLanes.WordingTiers && d.Writes && !d.Written && d.Mode == LaneMode.Shadow
                && d.Subject == "wording:light creams" && d.Stratum == "lines-2^1");
            LaneJournal.Read<WordingTierSignals>(decision.SignalsJson)!.Should().Match<WordingTierSignals>(s =>
                s.FoodId == k.Cream.Id && s.EvidenceKey == "wording-tiers:lines-2" && s.Lines == 2);
        }

        [Fact]
        public async Task The_operators_dry_run_gives_each_tiers_coverage_and_samples_records_no_decision_and_writes_nothing()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);
            var decisions = await db.LaneDecisions.CountAsync();

            var preview = await Lane(db).PreviewAsync(0, 100, 5);

            preview.Should().Match<WordingTierPreview>(p => p.Waiting == 2 && p.Read == 2 && p.ReadLines == 3 && p.Abstained == 0,
                "the tiers had no operator entry point and could not be measured on staging with ai-pause-all on (checkpoint 2, M15)");
            preview.ByTier[WordingTiers.NameKey].Should().Match<WordingTierCoverage>(c => c.Wordings == 1 && c.Lines == 2 && c.WouldWrite == 1 && c.WouldWriteLines == 2 && c.LineShare == 0.6667);
            preview.ByTier[WordingTiers.Staple].Should().Match<WordingTierCoverage>(c => c.Wordings == 1 && c.WouldWrite == 1 && c.WouldWriteLines == 1);
            preview.Samples[WordingTiers.NameKey].Single().Should().Match<WordingTierSample>(s => s.Wording == "light creams" && s.FoodId == k.Cream.Id && s.Hold == null && s.SampleLines.Count == 2);
            (await db.LaneDecisions.CountAsync()).Should().Be(decisions, "a preview records no decision, and a wording decided already is previewed again");
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await Lane(db).PreviewAsync(1, 100, 5)).Read.Should().Be(1);
        }

        [Fact]
        public async Task A_live_tier_writes_its_rows_through_the_loader_with_its_source_and_moves_their_lines()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);

            var batch = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);

            batch.WrittenByTier.Should().Contain(WordingTiers.NameKey, 1).And.NotContainKey(WordingTiers.Staple);
            var row = await db.IngredientWordings.SingleAsync();
            row.Should().Match<IngredientWordingEntity>(w => w.Wording == "light creams" && w.IngredientId == k.Cream.Id
                && w.Source == "deterministic:name-key" && w.Batch!.StartsWith("wt-name-key-") && w.Verdict == IngredientWordingVerdict.Food);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Cream.Id)).Should().Be(2, "the batch-scoped relink moved both lines");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == VerifiedWordingService.VerifyChangeType)).Should().Be(1);
            (await db.LaneDecisions.SingleAsync(d => d.Tier == WordingTiers.NameKey)).Written.Should().BeTrue();
            (await db.LaneDecisions.SingleAsync(d => d.Tier == WordingTiers.Staple)).Written.Should().BeFalse("the staple tier is still in shadow");
        }

        [Fact]
        public async Task The_lanes_undo_retracts_its_rows_with_their_evidence_and_never_demotes_a_food_the_load_curated()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db, sodaStatus: (long)CurationStatusEnum.PendingCuration);
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Lane(db).RunBatchAsync(Live(WordingTiers.Staple), 10);
            db.ChangeTracker.Clear();
            (await db.Ingredients.SingleAsync(i => i.Id == k.BakingSoda.Id)).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "the load curates a pending SR Legacy target");

            var dry = await Undo(db).UndoSinceAsync(WordingTiers.Staple, since, apply: false, CancellationToken.None);
            dry.Undone.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(1);

            var undone = await Undo(db).UndoSinceAsync(WordingTiers.Staple, since, apply: true, CancellationToken.None);
            db.ChangeTracker.Clear();

            undone.Undone.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.IngredientWordingRejections.SingleAsync()).Should().Match<IngredientWordingRejectionEntity>(r =>
                r.Wording == "baking soda" && r.IngredientId == k.BakingSoda.Id && r.EvidenceKey == "wording-tiers:lines-1" && r.Source == "deterministic:staple");
            (await db.RecipeIngredients.SingleAsync(ri => ri.RawLine == "1 tsp baking soda")).IngredientId.Should().Be(k.Soda.Id, "the line went back to its wording");
            (await db.Ingredients.SingleAsync(i => i.Id == k.BakingSoda.Id)).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "a retract does not demote a food its load curated");
        }

        [Fact]
        public async Task A_retracted_pair_is_held_on_the_same_evidence_and_written_again_once_the_wordings_lines_double()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);
            await Undo(db).UndoSinceAsync(WordingTiers.NameKey, since, apply: true, CancellationToken.None);
            db.LaneDecisions.RemoveRange(db.LaneDecisions);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var again = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);

            again.HeldByRule.Should().ContainKey(WordingTierLaneService.RetractedHold);
            again.RefusedByTier.Should().BeEmpty("a retracted pair is held before the loader, so it is never a loader refusal");
            (await db.IngredientWordings.CountAsync()).Should().Be(0);

            db.Recipes.Add(Recipe("Trifle", (k.Creams, "50 g light creams", 50m, Gram), (k.Creams, "60 g light creams", 60m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var doubled = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);

            doubled.WrittenByTier.Should().Contain(WordingTiers.NameKey, 1, "four lines are twice the two the pair was retracted on");
            (await db.IngredientWordings.SingleAsync()).IngredientId.Should().Be(k.Cream.Id);
        }

        [Fact]
        public async Task A_retracted_pair_stays_held_until_the_wordings_lines_double_even_across_a_power_of_two()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            db.Recipes.Add(Recipe("Fool", (k.Creams, "30 g light creams", 30m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);
            await Undo(db).UndoSinceAsync(WordingTiers.NameKey, since, apply: true, CancellationToken.None);
            (await db.IngredientWordingRejections.SingleAsync()).EvidenceKey.Should().Be("wording-tiers:lines-3");
            db.LaneDecisions.RemoveRange(db.LaneDecisions);
            db.Recipes.Add(Recipe("Syllabub", (k.Creams, "40 g light creams", 40m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var four = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);

            four.HeldByRule.Should().ContainKey(WordingTierLaneService.RetractedHold, "four lines are not twice the three the pair was retracted on");
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
        }

        [Theory]
        [InlineData(null, 1000, true)]
        [InlineData("wording-tiers:lines-63", 63, true)]
        [InlineData("wording-tiers:lines-63", 64, true)]
        [InlineData("wording-tiers:lines-63", 125, true)]
        [InlineData("wording-tiers:lines-63", 126, false)]
        [InlineData("wording-tiers:lines-1", 1, true)]
        [InlineData("wording-tiers:lines-1", 2, false)]
        [InlineData("ai:readers:abc", 1, false)]
        [InlineData("wording-tiers:lines-x", 1, false)]
        public void A_retract_holds_the_pair_until_the_lines_reach_twice_its_evidence(string? evidence, int lines, bool held)
        {
            WordingTierLaneService.StillRetracted(evidence, lines).Should().Be(held);
        }

        [Fact]
        public async Task A_would_be_write_recorded_in_shadow_is_written_once_its_tier_goes_live()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);

            var live = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);

            live.Read.Should().Be(1, "the shadow sample the tier qualified on is asked again live; the staple wording is still in shadow");
            live.WrittenByTier.Should().Contain(WordingTiers.NameKey, 1);
            (await db.IngredientWordings.SingleAsync()).Should().Match<IngredientWordingEntity>(w => w.Wording == "light creams" && w.IngredientId == k.Cream.Id);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Cream.Id)).Should().Be(2);

            var after = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);
            after.Read.Should().Be(0);
        }

        [Theory]
        [InlineData("deterministic:name-key", WordingTiers.NameKey)]
        [InlineData("deterministic:staple", WordingTiers.NameKey)]
        [InlineData("ai:readers:0123456789abcdef", WordingTiers.VerifiedWording)]
        [InlineData("verified:w01", WordingTiers.VerifiedWording)]
        public async Task The_verified_wording_tier_reads_no_row_a_tier_wrote(string source, string answeredBy)
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            db.IngredientWordings.Add(new IngredientWordingEntity
            {
                Wording = "light creams, divided", Verdict = IngredientWordingVerdict.Food, IngredientId = k.Cream.Id, Source = source, Batch = "earlier",
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);

            (await db.LaneDecisions.SingleAsync(d => d.Subject == "wording:light creams")).Tier.Should().Be(answeredBy);
        }

        [Fact]
        public async Task A_row_loaded_but_never_recorded_is_recorded_relinked_and_reached_by_the_undo()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var since = DateTime.UtcNow.AddMinutes(-1);
            await LoadWithoutRecordAsync(db, k);

            var batch = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);
            db.ChangeTracker.Clear();

            batch.Recovered.Should().Be(1);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Cream.Id)).Should().Be(2, "the recovered row's batch is relinked");
            var decision = await db.LaneDecisions.SingleAsync(d => d.Tier == WordingTiers.NameKey);
            decision.Should().Match<LaneDecisionEntity>(d => d.Written && d.Subject == "wording:light creams" && d.Decision!.StartsWith(WordingTierLaneService.RecoveredDecision));
            LaneJournal.Read<WordingTierSignals>(decision.SignalsJson)!.EvidenceKey.Should().Be("wording-tiers:lines-2");

            var again = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);
            again.Recovered.Should().Be(0);

            var undone = await Undo(db).UndoSinceAsync(WordingTiers.NameKey, since, apply: true, CancellationToken.None);
            db.ChangeTracker.Clear();
            undone.Undone.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Creams.Id)).Should().Be(2, "the lines went back to their wording");
        }

        [Fact]
        public async Task A_row_loaded_but_never_recorded_by_a_tier_now_paused_is_retracted()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await LoadWithoutRecordAsync(db, k);

            var batch = await Lane(db).RunBatchAsync(new Dictionary<string, string> { [WordingTiers.NameKey] = LaneMode.Paused }, 10);
            db.ChangeTracker.Clear();

            batch.RecoveredRetracted.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.IngredientWordingRejections.SingleAsync()).EvidenceKey.Should().Be("wording-tiers:lines-2");
        }

        private static async Task LoadWithoutRecordAsync(ApplicationDbContext db, Kitchen k)
        {
            var loaded = await new VerifiedWordingService(db, new NoUser()).LoadAsync(new[]
            {
                new VerifiedWordingRow
                {
                    Row = "light creams", Wording = "light creams", Verdict = IngredientWordingVerdict.Food, FoodId = k.Cream.Id, FoodName = k.Cream.Name,
                    EvidenceKey = WordingTierLaneService.EvidenceKey(2),
                },
            }, "wt-name-key-20261120000000", apply: true, source: WordingTiers.Source(WordingTiers.NameKey));
            loaded.Added.Should().Be(1);
            db.ChangeTracker.Clear();
        }

        [Fact]
        public async Task A_wording_decided_under_the_current_signature_is_not_asked_again()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);

            var second = await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);

            second.Read.Should().Be(0);
            (await db.LaneDecisions.CountAsync()).Should().Be(2);
        }

        [Fact]
        public async Task An_alias_with_no_context_never_answers_and_a_trusted_one_does()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var topping = Wording("pouring topping");
            var dressing = Wording("table dressing");
            db.Ingredients.AddRange(topping, dressing);
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = k.Cream.Id, AliasName = "pouring topping", SourceContext = null },
                new IngredientAliasEntity { IngredientId = k.Cream.Id, AliasName = "table dressing", SourceContext = IngredientAliasTrust.AdminPrefix + "person:7" });
            await db.SaveChangesAsync();
            db.Recipes.Add(Recipe("Pie", (topping, "100 g pouring topping", 100m, Gram), (dressing, "100 g table dressing", 100m, Gram)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);

            (await db.LaneDecisions.SingleAsync(d => d.Subject == "wording:pouring topping")).Tier.Should().Be(WordingTierLaneService.AbstainTier);
            (await db.LaneDecisions.SingleAsync(d => d.Subject == "wording:table dressing")).Tier.Should().Be(WordingTiers.Alias);
        }

        [Fact]
        public async Task A_bare_head_word_takes_its_default_from_the_curated_lines_of_approved_recipes()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var soda = Wording("soda");
            db.Ingredients.Add(soda);
            await db.SaveChangesAsync();
            for (var i = 0; i < WordingHeadDefaults.MinimumLines; i++)
            {
                var approved = Recipe($"Soda bread {i}", (k.BakingSoda, "1 tsp soda", 1m, Teaspoon));
                approved.CurationStatusId = (long)CurationStatusEnum.Curated;
                db.Recipes.Add(approved);
            }
            db.Recipes.Add(Recipe("Biscuits", (soda, "1 tsp soda", 1m, Teaspoon)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await Lane(db).RunBatchAsync(Live(WordingTiers.HeadDefault), 10);

            var decision = await db.LaneDecisions.SingleAsync(d => d.Subject == "wording:soda");
            decision.Should().Match<LaneDecisionEntity>(d => d.Tier == WordingTiers.HeadDefault && d.Written);
            (await db.IngredientWordings.SingleAsync(w => w.Wording == "soda")).Should().Match<IngredientWordingEntity>(w =>
                w.IngredientId == k.BakingSoda.Id && w.Source == "deterministic:head-default");
            (await db.RecipeIngredients.SingleAsync(ri => ri.Recipe.Name == "Biscuits")).IngredientId.Should().Be(k.BakingSoda.Id);
        }

        [Fact]
        public async Task A_tier_whose_daily_allowance_is_spent_leaves_its_wordings_for_a_later_pass()
        {
            using var db = NewContext();
            await KitchenAsync(db);

            var spent = new Dictionary<string, int> { [WordingTiers.NameKey] = 0 };
            var batch = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10, spent);

            batch.Capped.Should().Be(1);
            (await db.IngredientWordings.CountAsync()).Should().Be(0);
            (await db.LaneDecisions.SingleAsync(d => d.Tier == WordingTiers.NameKey)).Should().Match<LaneDecisionEntity>(d =>
                !d.Writes && !d.Written && d.Decision!.StartsWith(WordingTierLaneService.CappedDecision));

            var next = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10, spent);
            next.Read.Should().Be(0, "a capped wording is set aside for the cap window, so the next pass reads further down the list");

            foreach (var capped in db.LaneDecisions.Where(d => d.Tier == WordingTiers.NameKey)) capped.CreatedDate = DateTime.UtcNow.AddHours(-25);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var later = await Lane(db).RunBatchAsync(Live(WordingTiers.NameKey), 10);
            later.WrittenByTier.Should().Contain(WordingTiers.NameKey, 1, "once the cap window has passed the wording is asked again");
        }

        [Fact]
        public async Task A_paused_tier_stays_paused_across_a_restart_and_writes_nothing()
        {
            var name = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            services.AddScoped<IPlatformFeatureService, PlatformFeatureService>();
            services.AddScoped<ILaneFrameService, LaneFrameService>();
            using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await KitchenAsync(db);
                db.LaneStates.Add(new LaneStateEntity { Lane = AiLanes.WordingTiers, Tier = WordingTiers.NameKey, Mode = LaneMode.Paused, Since = DateTime.UtcNow, WindowStart = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            AiLaneGate Gate()
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:QuietHours"] = "" }).Build();
                return new AiLaneGate(provider.GetRequiredService<IServiceScopeFactory>(), configuration,
                    new AiHostMonitor(new NoClients(), configuration, NullLogger<AiHostMonitor>.Instance), NullLogger<AiLaneGate>.Instance);
            }

            var mode = await Gate().WriteModeAsync(AiLanes.WordingTiers, WordingTiers.NameKey, CancellationToken.None);
            var afterRestart = await Gate().WriteModeAsync(AiLanes.WordingTiers, WordingTiers.NameKey, CancellationToken.None);

            mode.Should().Be(LaneMode.Paused);
            afterRestart.Should().Be(LaneMode.Paused);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var batch = await Lane(db).RunBatchAsync(new Dictionary<string, string> { [WordingTiers.NameKey] = afterRestart }, 10);
                batch.WrittenByTier.Should().BeEmpty();
                (await db.IngredientWordings.CountAsync()).Should().Be(0);
                (await db.LaneDecisions.SingleAsync(d => d.Tier == WordingTiers.NameKey)).Mode.Should().Be(LaneMode.Paused);
            }
        }

        [Fact]
        public async Task A_tier_cannot_leave_shadow_without_the_link_auditors_golden_record()
        {
            using var db = NewContext();
            await KitchenAsync(db);
            await Lane(db).RunBatchAsync(new Dictionary<string, string>(), 10);

            var verdict = await Lane(db).AuditAndEvaluateAsync(WordingTiers.NameKey, Array.Empty<string>());

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Reason.Should().Contain("link auditor");
            (await db.LaneDecisions.SingleAsync(d => d.Tier == WordingTiers.NameKey)).AuditOutcome.Should().Be(LaneAuditOutcome.Pass);
        }

        [Theory]
        [InlineData("phi4", "mistral-nemo", "phi4", "mistral-nemo", null)]
        [InlineData("phi4", "mistral-nemo", "phi4", "Mistral-Nemo", null)]
        [InlineData("phi4", "mistral-nemo", "phi4", "mistral-small", "reader B mistral-nemo")]
        [InlineData("phi4", null, "phi4", "mistral-nemo", "does not name the reader B")]
        [InlineData("phi4", "mistral-nemo", "phi4-mini", "mistral-nemo", "scored with phi4")]
        public void The_link_audit_record_must_have_scored_both_the_auditor_and_reader_b_the_roster_names(string recordAuditor, string? recordCheck, string auditor, string check, string? problem)
        {
            var record = new WordingTierAuditRecord.Record
            {
                Model = recordAuditor, CheckModel = recordCheck, Signature = WordingReaderRecords.ScorerSignature(WordingTierAuditRecord.Lane), Score = 0.9,
                ScoredAtUtc = DateTime.UtcNow, AuditKnownBad = 40, AuditFlaggedBad = 36, AuditKnownGood = 200, AuditFlaggedGood = 2,
            };

            var (score, golden) = WordingTierAuditRecord.Evaluate(record, auditor, check);

            score.Should().NotBeNull();
            if (problem == null) golden.Should().BeNull();
            else golden.Should().Contain(problem);
        }

        [Fact]
        public void The_first_tiers_are_held_to_99_percent_and_the_others_to_98_and_every_policy_is_one_the_frame_accepts()
        {
            WordingTierLaneService.Policy(WordingTiers.Staple).QualifyBar.Should().Be(0.99);
            WordingTierLaneService.Policy(WordingTiers.NameKey).QualifyBar.Should().Be(0.98);
            WordingTiers.All.Select(WordingTierLaneService.Policy).SelectMany(p => p.Problems()).Should().BeEmpty();
        }

        private sealed class NoClients : System.Net.Http.IHttpClientFactory
        {
            public System.Net.Http.HttpClient CreateClient(string name) => new();
        }
    }
}
