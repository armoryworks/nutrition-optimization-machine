using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The branded lift: a pending branded food is curated once two or more independent sources
    /// agree with its stored values (its FDC record counting as one), the counts of agreeing and
    /// disagreeing sources are stored, Open Food Facts and a later FDC release never lift a food, no
    /// value is ever changed, a curated food is never demoted, and every run is journaled under its
    /// own stamp and undone by it. A household's confirmed label lifts the food it was taken for.
    /// </summary>
    public class BrandedLiftTests
    {
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000;
        private const long Person = 10;
        private const long Household = 100, OtherHousehold = 200, ThirdHousehold = 300;
        private const string Branded = CatalogAdmissionRule.BrandedFood;

        private static ApplicationDbContext NewContext()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = 16, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 4 });
            db.Set<NutrientEntity>().AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = 16 },
                new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Carbs, Name = "Total Carbohydrates", DefaultMeasurementId = 1 },
                new NutrientEntity { Id = Fat, Name = "Fat", DefaultMeasurementId = 1 });
            db.SaveChanges();
            return db;
        }

        private static IngredientEntity Granola(ApplicationDbContext db, CurationStatusEnum status = CurationStatusEnum.PendingCuration, string? reason = CatalogAdmissionRule.AwaitingSecondSource,
            string? energyCheck = EnergyCheckOutcome.General, string name = "Oat granola bar (Acme)", string fdcId = "2000002")
        {
            var food = new IngredientEntity
            {
                Name = name, FdcId = fdcId, FdcDataType = Branded, FdcCategory = "Snack, Energy & Granola Bars", CurationStatusId = (long)status,
                AdmissionReason = reason, EnergyCheck = energyCheck, ReferenceServingGrams = 40m, CreatedDate = DateTime.UtcNow,
            };
            db.Ingredients.Add(food);
            db.SaveChanges();
            db.Set<IngredientNutrientEntity>().AddRange(
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Calories, Amount = 400m, MeasurementId = 16 },
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Protein, Amount = 10m, MeasurementId = 1 },
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Carbs, Amount = 60m, MeasurementId = 1 },
                new IngredientNutrientEntity { IngredientId = food.Id, NutrientId = Fat, Amount = 13m, MeasurementId = 1 });
            db.SaveChanges();
            return food;
        }

        private static string Label(decimal kcal, decimal protein, decimal carb, decimal fat, decimal? servingGrams = 40m, string basis = NutritionBasis.Serving) =>
            VerificationNutrients.ToJson(new NutritionFactsParseResult
            {
                Basis = basis,
                ServingGrams = servingGrams,
                Calories = kcal,
                Nutrients =
                {
                    new NutrientReading { Key = "protein", Amount = protein, Unit = "g" },
                    new NutrientReading { Key = "carbohydrate", Amount = carb, Unit = "g" },
                    new NutrientReading { Key = "fat", Amount = fat, Unit = "g" },
                },
            });

        private static IngredientVerificationEntity HouseholdLabel(long foodId, long household, string json, decimal? servingGrams = 40m, bool confirmed = true) => new()
        {
            IngredientId = foodId, HouseholdId = household, Source = IngredientVerificationService.UserLabelSource(Person), Kind = IngredientVerificationKind.Label,
            Status = IngredientVerificationStatus.Pending, NutrientsJson = json, ServingGrams = servingGrams, Confirmed = confirmed,
            ConfirmedAt = confirmed ? DateTime.UtcNow : null, LastAskedDate = DateTime.UtcNow, CreatedDate = DateTime.UtcNow,
        };

        private static IngredientVerificationEntity LaneRow(long foodId, string source, string json) => new()
        {
            IngredientId = foodId, HouseholdId = null, Source = source, Kind = IngredientVerificationKind.Label,
            Status = IngredientVerificationStatus.Pending, NutrientsJson = json, LastAskedDate = DateTime.UtcNow, CreatedDate = DateTime.UtcNow,
        };

        private static BrandedLiftService Service(ApplicationDbContext db) => new(db, NullLogger<BrandedLiftService>.Instance);

        private static IngredientEntity Get(ApplicationDbContext db, long id) => db.Ingredients.AsNoTracking().Single(i => i.Id == id);

        [Fact]
        public async Task A_household_label_that_agrees_is_the_second_source_and_lifts_the_food_with_both_counts_stored_and_journaled()
        {
            using var db = NewContext();
            var food = Granola(db);
            db.IngredientVerifications.Add(HouseholdLabel(food.Id, Household, Label(160m, 4m, 24m, 5.2m)));
            await db.SaveChangesAsync();

            var dry = await Service(db).RunAsync(apply: false, Person);

            dry.Lifted.Should().Be(1);
            dry.Applied.Should().BeFalse();
            dry.RunStamp.Should().BeNull();
            Get(db, food.Id).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0, "a dry run writes nothing");

            var run = await Service(db).RunAsync(apply: true, Person);

            run.Lifted.Should().Be(1);
            run.ByAgreeingSources.Should().ContainKey("2").WhoseValue.Should().Be(1);
            var lifted = Get(db, food.Id);
            lifted.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            lifted.AgreeingSources.Should().Be(2);
            lifted.DisagreeingSources.Should().Be(0);
            lifted.AdmissionReason.Should().BeNull();
            (await db.IngredientNutrients.Where(n => n.IngredientId == food.Id).OrderBy(n => n.NutrientId).Select(n => n.Amount).ToListAsync())
                .Should().Equal(new[] { 13m, 60m, 10m, 400m }, "the lift never changes a value");

            var journal = await db.AuditLogEntries.ToListAsync();
            journal.Should().OnlyContain(a => a.Timestamp == run.RunStamp && a.ChangedByPersonId == Person);
            journal.Single(a => a.ChangeType == BrandedLiftService.RunChangeType).NewValue.Should().Contain(CatalogAdmissionRule.Version).And.Contain(BrandedSourceAgreement.Version);
            journal.Single(a => a.ChangeType == BrandedLiftService.LiftChangeType).Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.OldValue == "9001" && a.NewValue == "9003");
            journal.Single(a => a.PropertyName == nameof(IngredientEntity.AgreeingSources)).NewValue.Should().Be("2");
            journal.Single(a => a.PropertyName == nameof(IngredientEntity.AdmissionReason)).OldValue.Should().Be(CatalogAdmissionRule.AwaitingSecondSource);
            journal.Single(a => a.ChangeType == BrandedLiftService.EvidenceChangeType).NewValue.Should().Be("fdc-record agrees; household:100 agrees", "both sources are named");
        }

        [Theory]
        [InlineData("no-gram-weight")]
        [InlineData("unconfirmed")]
        [InlineData("open-food-facts")]
        [InlineData("fdc-release")]
        [InlineData("other-source")]
        public async Task A_label_without_a_gram_weight_an_unconfirmed_label_open_food_facts_or_a_later_fdc_release_never_lifts_a_food(string evidence)
        {
            using var db = NewContext();
            var food = Granola(db);
            var agreeing = Label(160m, 4m, 24m, 5.2m);
            db.IngredientVerifications.Add(evidence switch
            {
                "no-gram-weight" => HouseholdLabel(food.Id, Household, Label(160m, 4m, 24m, 5.2m, servingGrams: null), servingGrams: null),
                "unconfirmed" => HouseholdLabel(food.Id, Household, agreeing, confirmed: false),
                "open-food-facts" => LaneRow(food.Id, "off:0001112223334", Label(400m, 10m, 60m, 13m, null, NutritionBasis.Per100g)),
                "fdc-release" => LaneRow(food.Id, "fdc:2000002", Label(400m, 10m, 60m, 13m, null, NutritionBasis.Per100g)),
                _ => LaneRow(food.Id, "blog:example", Label(400m, 10m, 60m, 13m, null, NutritionBasis.Per100g)),
            });
            await db.SaveChangesAsync();

            var run = await Service(db).RunAsync(apply: true, Person);

            run.Lifted.Should().Be(0);
            var held = Get(db, food.Id);
            held.CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
            held.AdmissionReason.Should().Be(CatalogAdmissionRule.AwaitingSecondSource);
            held.AgreeingSources.Should().Be(1, "the FDC record alone is one source");
            held.DisagreeingSources.Should().Be(0);
        }

        [Fact]
        public async Task An_open_food_facts_disagreement_holds_the_food_until_one_more_agreeing_source_outweighs_it()
        {
            using var db = NewContext();
            var food = Granola(db);
            db.IngredientVerifications.AddRange(
                HouseholdLabel(food.Id, Household, Label(160m, 4m, 24m, 5.2m)),
                LaneRow(food.Id, "off:0001112223334", Label(300m, 10m, 60m, 13m, null, NutritionBasis.Per100g)));
            await db.SaveChangesAsync();

            var first = await Service(db).RunAsync(apply: true, Person);

            first.Held.Should().Be(1);
            first.HeldByReason.Should().ContainKey(CatalogAdmissionRule.SourcesDisagree);
            Get(db, food.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.PendingCuration
                && f.AgreeingSources == 2 && f.DisagreeingSources == 1 && f.AdmissionReason == CatalogAdmissionRule.SourcesDisagree);

            db.IngredientVerifications.Add(LaneRow(food.Id, "manufacturer:acme.example", Label(400m, 10m, 60m, 13m, null, NutritionBasis.Per100g)));
            await db.SaveChangesAsync();
            var second = await Service(db).RunAsync(apply: true, Person);

            second.Lifted.Should().Be(1);
            Get(db, food.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.Curated
                && f.AgreeingSources == 3 && f.DisagreeingSources == 1 && f.AdmissionReason == null);
        }

        [Fact]
        public async Task More_agreeing_sources_raise_the_stored_count_and_a_later_disagreement_never_demotes_a_curated_food()
        {
            using var db = NewContext();
            var food = Granola(db);
            db.IngredientVerifications.AddRange(
                HouseholdLabel(food.Id, Household, Label(160m, 4m, 24m, 5.2m)),
                HouseholdLabel(food.Id, Household, Label(160m, 4m, 24m, 5.2m)));
            await db.SaveChangesAsync();
            await Service(db).RunAsync(apply: true, Person);
            Get(db, food.Id).AgreeingSources.Should().Be(2, "two labels of one household are one source");

            db.IngredientVerifications.AddRange(
                HouseholdLabel(food.Id, OtherHousehold, Label(165m, 4m, 25m, 5m)),
                LaneRow(food.Id, "manufacturer:acme.example", Label(400m, 10m, 60m, 13m, null, NutritionBasis.Per100g)));
            await db.SaveChangesAsync();
            var recount = await Service(db).RunAsync(apply: true, Person);

            recount.Recounted.Should().Be(1);
            Get(db, food.Id).AgreeingSources.Should().Be(4);

            db.IngredientVerifications.AddRange(
                HouseholdLabel(food.Id, ThirdHousehold, Label(240m, 4m, 24m, 5.2m)),
                LaneRow(food.Id, "off:1", Label(250m, 10m, 60m, 13m, null, NutritionBasis.Per100g)),
                HouseholdLabel(food.Id, 400, Label(240m, 4m, 24m, 5.2m)));
            await db.SaveChangesAsync();
            var later = await Service(db).RunAsync(apply: true, Person);

            later.KeptCurated.Should().Be(1);
            Get(db, food.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.Curated
                && f.AgreeingSources == 4 && f.DisagreeingSources == 3 && f.AdmissionReason == CatalogAdmissionRule.SourcesDisagree);
        }

        [Fact]
        public async Task A_food_the_admission_rule_has_not_judged_or_holds_for_another_reason_is_never_lifted()
        {
            using var db = NewContext();
            var unjudged = Granola(db, reason: null, energyCheck: null, name: "Unjudged bar (Acme)", fdcId: "2000010");
            var duplicate = Granola(db, reason: CatalogAdmissionRule.AwaitingSecondSource + "+" + CatalogAdmissionRule.DuplicatePrefix + "77", name: "Duplicate bar (Acme)", fdcId: "2000011");
            db.IngredientVerifications.AddRange(
                HouseholdLabel(unjudged.Id, Household, Label(160m, 4m, 24m, 5.2m)),
                HouseholdLabel(duplicate.Id, Household, Label(160m, 4m, 24m, 5.2m)));
            await db.SaveChangesAsync();

            var run = await Service(db).RunAsync(apply: true, Person);

            run.NotJudged.Should().Be(1);
            run.Lifted.Should().Be(0);
            Get(db, unjudged.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.PendingCuration && f.AgreeingSources == null);
            Get(db, duplicate.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.PendingCuration
                && f.AgreeingSources == 2 && f.AdmissionReason == CatalogAdmissionRule.DuplicatePrefix + "77");
        }

        [Fact]
        public async Task The_undo_puts_back_what_the_run_wrote_skips_a_value_changed_since_and_is_itself_undone()
        {
            using var db = NewContext();
            var food = Granola(db);
            var other = Granola(db, name: "Nut granola bar (Acme)", fdcId: "2000003");
            db.IngredientVerifications.AddRange(
                HouseholdLabel(food.Id, Household, Label(160m, 4m, 24m, 5.2m)),
                HouseholdLabel(other.Id, Household, Label(160m, 4m, 24m, 5.2m)));
            await db.SaveChangesAsync();
            var run = await Service(db).RunAsync(apply: true, Person);
            var changed = await db.Ingredients.SingleAsync(i => i.Id == other.Id);
            changed.AdmissionReason = "admin:note";
            await db.SaveChangesAsync();

            var dry = await Service(db).UndoAsync(run.RunStamp!.Value, apply: false, Person);
            dry.Demoted.Should().Be(2);
            Get(db, food.Id).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated, "an undo dry run writes nothing");

            var undo = await Service(db).UndoAsync(run.RunStamp!.Value, apply: true, Person);

            undo.Demoted.Should().Be(2);
            undo.Skipped.Should().Be(1, "the other food's reason was changed since the run");
            Get(db, food.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.PendingCuration
                && f.AgreeingSources == null && f.DisagreeingSources == null && f.AdmissionReason == CatalogAdmissionRule.AwaitingSecondSource);
            Get(db, other.Id).AdmissionReason.Should().Be("admin:note");
            (await db.AuditLogEntries.CountAsync(a => a.Timestamp == undo.UndoStamp && a.ChangeType == BrandedLiftService.UndoChangeType)).Should().BeGreaterThan(0);

            var redo = await Service(db).UndoAsync(undo.UndoStamp!.Value, apply: true, Person);

            redo.Relifted.Should().Be(2);
            Get(db, food.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.Curated && f.AgreeingSources == 2);
        }

        [Fact]
        public async Task Confirming_a_household_label_lifts_the_branded_food_it_was_taken_for()
        {
            using var db = NewContext();
            var food = Granola(db);
            var row = HouseholdLabel(food.Id, Household, Label(150m, 4m, 24m, 5.2m), confirmed: false);
            db.IngredientVerifications.Add(row);
            await db.SaveChangesAsync();
            var verification = new IngredientVerificationService(db, new Mock<ITesseractOcrService>().Object, new Mock<IBarcodeDecodeService>().Object,
                new Mock<IMediaStorageService>().Object, new Mock<IVerificationLookupLane>().Object, new ScanRateLimiter(), NullLogger<IngredientVerificationService>.Instance,
                Service(db));

            await verification.ConfirmAsync(row.Id, Person, new IngredientVerificationConfirmModel
            {
                Basis = NutritionBasis.Serving, ServingGrams = 40m, Calories = 160m,
                Nutrients = { new() { Key = "protein", Amount = 4m, Unit = "g" }, new() { Key = "carbohydrate", Amount = 24m, Unit = "g" }, new() { Key = "fat", Amount = 5.2m, Unit = "g" } },
            });

            Get(db, food.Id).Should().Match<IngredientEntity>(f => f.CurationStatusId == (long)CurationStatusEnum.Curated && f.AgreeingSources == 2);
        }

        [Fact]
        public async Task A_household_is_asked_about_a_pending_branded_food_but_not_about_a_curated_one()
        {
            using var db = NewContext();
            var pending = Granola(db);
            var curated = Granola(db, CurationStatusEnum.Curated, reason: null, name: "Nut granola bar (Acme)", fdcId: "2000003");
            var verification = new IngredientVerificationService(db, new Mock<ITesseractOcrService>().Object, new Mock<IBarcodeDecodeService>().Object,
                new Mock<IMediaStorageService>().Object, new Mock<IVerificationLookupLane>().Object, new ScanRateLimiter(), NullLogger<IngredientVerificationService>.Instance);

            var rows = await verification.NeedsVerificationAsync(Household, new[] { pending.Id, curated.Id });

            rows.Single(r => r.IngredientId == pending.Id).ShouldAsk.Should().BeTrue("a branded record alone is one source; a household label can be the second");
            rows.Single(r => r.IngredientId == curated.Id).ShouldAsk.Should().BeFalse();
        }
    }
}
