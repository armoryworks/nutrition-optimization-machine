using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The branded lift (imports.md 2d, owner answer 2026-11-20 "two or more, more is better"). For
    /// each branded food with evidence it reads the stored per-100 g values (the FDC branded record)
    /// and every source in <c>recipe.IngredientVerification</c>, counts with
    /// <see cref="BrandedSourceAgreement"/> how many agree and disagree, stores both counts, and lets
    /// <see cref="CatalogAdmissionRule.Decide"/> say whether the food is admitted. A pending food the
    /// rule now passes becomes Curated; a curated food is never demoted and keeps the rule's reason.
    /// No nutrient value is read from a source into the catalog, and no model is involved.
    /// </summary>
    public class BrandedLiftService : IBrandedLiftService
    {
        public const string LiftChangeType = "BrandedLift";
        public const string FactsChangeType = "BrandedLiftFacts";
        public const string EvidenceChangeType = "BrandedLiftEvidence";
        public const string RunChangeType = "BrandedLiftRun";
        public const string UndoChangeType = "BrandedLiftUndo";
        public const string FoodEntityType = "Ingredient";
        public const string RunEntityType = "BrandedLift";
        public const string HouseholdLabelPrefix = "label:user:";
        public const string ManufacturerPrefix = "manufacturer:";
        public const string OpenFoodFactsPrefix = "off:";
        public const string FdcPrefix = "fdc:";
        public const int MaxChangedFoods = 10_000;

        private const long Pending = (long)CurationStatusEnum.PendingCuration;
        private const long Curated = (long)CurationStatusEnum.Curated;
        private static readonly string[] CalorieNames = { "energy", "calories", "kcal" };
        private static readonly string[] ProteinNames = { "protein" };
        private static readonly string[] CarbNames = { "carbohydrate", "carbs" };
        private static readonly string[] FatNames = { "total lipid", "fat" };
        private static readonly string[] FiberNames = { "fiber", "fibre" };
        private static readonly string[] AlcoholNames = { "alcohol" };
        private static readonly string[] UndoneChangeTypes = { LiftChangeType, FactsChangeType, UndoChangeType };

        private readonly ApplicationDbContext _db;
        private readonly ILogger<BrandedLiftService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public BrandedLiftService(ApplicationDbContext db, ILogger<BrandedLiftService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<BrandedLiftReport> RunAsync(bool apply, long personId, IReadOnlyCollection<long>? foodIds = null, CancellationToken cancellationToken = default)
        {
            var report = new BrandedLiftReport { RuleVersion = $"{CatalogAdmissionRule.Version} {BrandedSourceAgreement.Version}" };
            var query = _db.Ingredients
                .Include(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                .Where(i => i.FdcDataType == CatalogAdmissionRule.BrandedFood && (i.CurationStatusId == Pending || i.CurationStatusId == Curated));
            if (foodIds != null)
            {
                var ids = foodIds.Distinct().ToList();
                query = query.Where(i => ids.Contains(i.Id));
            }
            else
            {
                query = query.Where(i => i.AgreeingSources != null
                    || _db.IngredientVerifications.Any(v => v.IngredientId == i.Id && v.Kind == IngredientVerificationKind.Label && v.Status != IngredientVerificationStatus.Skipped));
            }
            var foods = await query.OrderBy(i => i.Id).ToListAsync(cancellationToken);
            var foodIdList = foods.Select(f => f.Id).ToList();
            var evidence = (await _db.IngredientVerifications.AsNoTracking()
                    .Where(v => foodIdList.Contains(v.IngredientId) && v.Kind == IngredientVerificationKind.Label && v.Status != IngredientVerificationStatus.Skipped)
                    .ToListAsync(cancellationToken))
                .OrderBy(v => v.ConfirmedAt ?? v.LastModifiedDate ?? v.CreatedDate).ThenBy(v => v.Id)
                .ToLookup(v => v.IngredientId);

            var changes = new List<(IngredientEntity Food, string ChangeType, string Property, string? Old, string? New)>();
            var notes = new Dictionary<long, string>();
            foreach (var food in foods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (food.EnergyCheck == null)
                {
                    report.NotJudged++;
                    if (report.Lines.Count < 400) report.Lines.Add($"not judged #{food.Id} {food.Name}: the admission rule has not judged it (run --admit with --branded)");
                    continue;
                }
                report.Judged++;

                var stored = new BrandedStoredValues(Amount(food, CalorieNames), Amount(food, ProteinNames), Amount(food, CarbNames), Amount(food, FatNames), food.ReferenceServingGrams);
                var agreement = BrandedSourceAgreement.Judge(stored, evidence[food.Id].Select(Reading).Where(r => r != null).Select(r => r!));
                var nutrition = new FoodQualityInput(food.Name, stored.Kcal, stored.Protein, stored.Carb, stored.Fat,
                    AlcoholGramsPer100g: AnyAmount(food, AlcoholNames), FiberGramsPer100g: AnyAmount(food, FiberNames));
                var decision = CatalogAdmissionRule.Decide(new AdmissionFacts(CatalogAdmissionRule.BrandedFood, null, food.FdcCategory, food.Name, nutrition,
                    AgreeingSources: agreement.Agreeing, DisagreeingSources: agreement.Disagreeing), _validator);
                var kept = (food.AdmissionReason ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t != CatalogAdmissionRule.AwaitingSecondSource && t != CatalogAdmissionRule.SourcesDisagree && !decision.Reasons.Contains(t));
                var reasons = decision.Reasons.Concat(kept).ToList();
                var reason = reasons.Count == 0 ? null : Truncate(string.Join("+", reasons), 255);
                var status = food.CurationStatusId == Pending && reasons.Count == 0 ? Curated : food.CurationStatusId;

                var bucket = agreement.Agreeing >= 4 ? "4+" : agreement.Agreeing.ToString(CultureInfo.InvariantCulture);
                report.ByAgreeingSources[bucket] = report.ByAgreeingSources.GetValueOrDefault(bucket) + 1;
                var summary = Summary(agreement);
                if (status != food.CurationStatusId)
                {
                    report.Lifted++;
                    if (report.Lines.Count < 400) report.Lines.Add($"lift     #{food.Id} {food.Name}: {agreement.Agreeing} sources agree ({summary})");
                }
                else if (food.CurationStatusId == Pending)
                {
                    report.Held++;
                    foreach (var r in reasons) report.HeldByReason[CatalogAdmissionRule.ReasonBucket(r)] = report.HeldByReason.GetValueOrDefault(CatalogAdmissionRule.ReasonBucket(r)) + 1;
                    if (report.Lines.Count < 400) report.Lines.Add($"held     #{food.Id} {food.Name}: {reason} ({summary})");
                }
                else if (reasons.Count > 0)
                {
                    report.KeptCurated++;
                    if (report.Lines.Count < 400) report.Lines.Add($"kept     #{food.Id} {food.Name}: curated, the rule says {reason} ({summary})");
                }
                else report.Recounted++;

                var before = changes.Count;
                void Set(string changeType, string property, string? old, string? value)
                {
                    if (old != value) changes.Add((food, changeType, property, old, value));
                }
                Set(LiftChangeType, nameof(IngredientEntity.CurationStatusId), Long(food.CurationStatusId), Long(status));
                Set(FactsChangeType, nameof(IngredientEntity.AgreeingSources), Int(food.AgreeingSources), Int(agreement.Agreeing));
                Set(FactsChangeType, nameof(IngredientEntity.DisagreeingSources), Int(food.DisagreeingSources), Int(agreement.Disagreeing));
                Set(FactsChangeType, nameof(IngredientEntity.AdmissionReason), food.AdmissionReason, reason);
                if (changes.Count > before) notes[food.Id] = Truncate(summary, 4000);
            }

            report.ChangedFoods = notes.Count;
            if (report.ChangedFoods > MaxChangedFoods)
                report.Stopped.Add($"size: {report.ChangedFoods} foods would change, above {MaxChangedFoods}");
            if (report.Stopped.Count > 0 || !apply || changes.Count == 0)
            {
                foreach (var reason in report.Stopped) _logger.LogWarning("Branded lift stopped itself, nothing written: {Reason}", reason);
                return report;
            }

            var stamp = Stamp(null);
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
            _db.AuditLogEntries.Add(Journal(RunEntityType, 0, RunChangeType, "RuleVersion", null, report.RuleVersion, stamp, personId));
            foreach (var (food, changeType, property, old, value) in changes)
            {
                WriteValue(food, property, value);
                food.LastModifiedDate = stamp;
                food.LastModifiedByPersonId = personId;
                _db.AuditLogEntries.Add(Journal(FoodEntityType, food.Id, changeType, property, old, value, stamp, personId));
            }
            foreach (var (foodId, summary) in notes)
                _db.AuditLogEntries.Add(Journal(FoodEntityType, foodId, EvidenceChangeType, "Sources", null, summary, stamp, personId));
            await _db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            report.Applied = true;
            report.RunStamp = stamp;
            _logger.LogInformation("Branded lift {Stamp:O}: {Judged} judged, {Lifted} lifted, {Held} held, {Changed} foods changed.", stamp, report.Judged, report.Lifted, report.Held, report.ChangedFoods);
            return report;
        }

        public async Task<BrandedLiftUndoReport> UndoAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            var report = new BrandedLiftUndoReport { RunStamp = stamp };
            var entries = await _db.AuditLogEntries
                .Where(a => a.Timestamp == stamp && a.EntityType == FoodEntityType && UndoneChangeTypes.Contains(a.ChangeType))
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            var foodIds = entries.Select(e => e.EntityId).Distinct().ToList();
            var foods = await _db.Ingredients.IgnoreQueryFilters().Where(i => foodIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
            var now = Stamp(stamp);
            var undone = new List<AuditLogEntryEntity>();
            foreach (var entry in entries)
            {
                if (!foods.TryGetValue(entry.EntityId, out var food) || ReadValue(food, entry.PropertyName) != entry.NewValue)
                {
                    report.Skipped++;
                    report.Lines.Add($"left     #{entry.EntityId} {food?.Name}: {entry.PropertyName} changed since the run");
                    continue;
                }
                if (entry.PropertyName == nameof(IngredientEntity.CurationStatusId) && entry.NewValue == Long(Curated)) report.Demoted++;
                else if (entry.PropertyName == nameof(IngredientEntity.CurationStatusId) && entry.OldValue == Long(Curated)) report.Relifted++;
                else report.ValuesRestored++;
                if (apply)
                {
                    WriteValue(food, entry.PropertyName, entry.OldValue);
                    food.LastModifiedDate = now;
                    food.LastModifiedByPersonId = personId;
                }
                undone.Add(entry);
            }
            if (!apply) return report;

            _db.AuditLogEntries.Add(Journal(RunEntityType, 0, UndoChangeType, "UndoOf", null, stamp.ToString("O", CultureInfo.InvariantCulture), now, personId));
            foreach (var entry in undone)
                _db.AuditLogEntries.Add(Journal(entry.EntityType, entry.EntityId, UndoChangeType, entry.PropertyName, entry.NewValue, entry.OldValue, now, personId));
            await _db.SaveChangesAsync(cancellationToken);
            report.Applied = true;
            report.UndoStamp = now;
            _logger.LogInformation("Branded lift undo {Stamp:O} as {UndoStamp:O}: {Demoted} demoted, {Relifted} lifted again, {Skipped} skipped.", stamp, now, report.Demoted, report.Relifted, report.Skipped);
            return report;
        }

        /// <summary>
        /// One evidence row as a source's reading per 100 g, or null when it carries no reading: a
        /// household's confirmed label (one source per household), a manufacturer page, Open Food
        /// Facts or an FDC release written by the lookup lane. A per-serving label is scaled by its
        /// explicit gram weight only; without one its values stay unknown.
        /// </summary>
        public static BrandedSourceReading? Reading(IngredientVerificationEntity row)
        {
            string key, kind;
            if (row.HouseholdId is { } household)
            {
                if (!row.Confirmed || !row.Source.StartsWith(HouseholdLabelPrefix, StringComparison.Ordinal)) return null;
                key = "household:" + household.ToString(CultureInfo.InvariantCulture);
                kind = BrandedSourceAgreement.HouseholdLabel;
            }
            else if (row.Source.StartsWith(ManufacturerPrefix, StringComparison.Ordinal))
            {
                key = row.Source.ToLowerInvariant();
                kind = BrandedSourceAgreement.Manufacturer;
            }
            else if (row.Source.StartsWith(OpenFoodFactsPrefix, StringComparison.Ordinal))
            {
                key = "off";
                kind = BrandedSourceAgreement.OpenFoodFacts;
            }
            else if (row.Source.StartsWith(FdcPrefix, StringComparison.Ordinal))
            {
                key = row.Source;
                kind = BrandedSourceAgreement.FdcRelease;
            }
            else
            {
                key = row.Source;
                kind = "other";
            }

            var label = VerificationNutrients.FromJson(row.NutrientsJson, row.ServingGrams, row.ServingText);
            decimal? factor = null;
            decimal? servingGrams = null;
            if (label?.Basis == NutritionBasis.Per100g) factor = 1m;
            else if (label?.Basis == NutritionBasis.Serving && label.ServingGrams is { } grams && grams > 0m)
            {
                factor = 100m / grams;
                servingGrams = grams;
            }
            if (label == null || factor is not { } f)
                return new BrandedSourceReading(key, kind, null, null, null, null, null);

            SourceValue? Value(string nutrientKey, decimal insignificantBelow)
            {
                var reading = label.Nutrients.FirstOrDefault(n => n.Key == nutrientKey);
                var toGrams = reading?.Unit switch { "mg" => 0.001m, "mcg" => 0.000001m, _ => 1m };
                if (reading?.Amount is { } amount) return new SourceValue(amount * toGrams * f);
                if (reading?.LessThan is { } below) return new SourceValue(null, below * toGrams * f);
                return label.NotSignificantSource.Contains(nutrientKey) ? new SourceValue(null, insignificantBelow * f) : null;
            }

            var kcal = label.Calories is { } c ? new SourceValue(c * f)
                : label.NotSignificantSource.Contains("calories") ? new SourceValue(null, 5m * f) : null;
            return new BrandedSourceReading(key, kind, servingGrams, kcal, Value("protein", 1m), Value("carbohydrate", 1m), Value("fat", 0.5m));
        }

        private static string Summary(BrandedAgreement agreement) =>
            string.Join("; ", new[] { agreement.Agreeing > 0 ? $"{BrandedSourceAgreement.StoredRecordKey} {BrandedSourceAgreement.Agrees}" : $"{BrandedSourceAgreement.StoredRecordKey} {BrandedSourceAgreement.Incomplete}" }
                .Concat(agreement.Verdicts.Select(v => v.Detail == null ? $"{v.Key} {v.Verdict}" : $"{v.Key} {v.Verdict} ({v.Detail})")));

        private static decimal? Amount(IngredientEntity food, string[] patterns) =>
            food.IngredientNutrients
                .FirstOrDefault(n => !n.IsDeleted && n.Nutrient != null && n.Nutrient.ParentNutrientId == null
                    && patterns.Any(p => n.Nutrient.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                ?.Amount;

        private static decimal? AnyAmount(IngredientEntity food, string[] patterns) =>
            food.IngredientNutrients
                .FirstOrDefault(n => !n.IsDeleted && n.Nutrient != null && patterns.Any(p => n.Nutrient.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                ?.Amount;

        private static DateTime Stamp(DateTime? after)
        {
            var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            return after is { } a && now <= a ? a.AddTicks(10) : now;
        }

        private static AuditLogEntryEntity Journal(string entityType, long entityId, string changeType, string? property, string? old, string? value, DateTime at, long personId) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            PropertyName = property,
            OldValue = old,
            NewValue = value,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        private static string? ReadValue(IngredientEntity food, string? property) => property switch
        {
            nameof(IngredientEntity.CurationStatusId) => Long(food.CurationStatusId),
            nameof(IngredientEntity.AgreeingSources) => Int(food.AgreeingSources),
            nameof(IngredientEntity.DisagreeingSources) => Int(food.DisagreeingSources),
            nameof(IngredientEntity.AdmissionReason) => food.AdmissionReason,
            _ => null,
        };

        private static void WriteValue(IngredientEntity food, string? property, string? value)
        {
            switch (property)
            {
                case nameof(IngredientEntity.CurationStatusId):
                    food.CurationStatusId = long.Parse(value!, CultureInfo.InvariantCulture);
                    break;
                case nameof(IngredientEntity.AgreeingSources):
                    food.AgreeingSources = value == null ? null : int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case nameof(IngredientEntity.DisagreeingSources):
                    food.DisagreeingSources = value == null ? null : int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case nameof(IngredientEntity.AdmissionReason):
                    food.AdmissionReason = value;
                    break;
            }
        }

        private static string? Long(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static string? Int(int? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
