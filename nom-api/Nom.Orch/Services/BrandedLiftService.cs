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
    /// rule now passes becomes Curated; a food the rules curated goes back to pending when its
    /// sources no longer lift it (owner answer: never fewer than two), while a food an admin curated
    /// is never demoted and keeps the rule's reason. Undoing a lift excludes the evidence that lifted
    /// the food, so the same evidence never lifts it again. No nutrient value is read from a source
    /// into the catalog, and no model is involved.
    /// </summary>
    public class BrandedLiftService : IBrandedLiftService
    {
        public const string LiftChangeType = "BrandedLift";
        public const string FactsChangeType = "BrandedLiftFacts";
        public const string EvidenceChangeType = "BrandedLiftEvidence";
        public const string RunChangeType = "BrandedLiftRun";
        public const string UndoChangeType = "BrandedLiftUndo";
        public const string RowsChangeType = "BrandedLiftRows";
        public const string ExcludeChangeType = "BrandedLiftExclude";
        public const string AgreeingRowsProperty = "AgreeingRows";
        public const string ExcludedProperty = "LiftExcluded";
        public const string EvidenceEntityType = "IngredientVerification";
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
        private static readonly string[] RuleStatusChangeTypes = { LiftChangeType, UndoChangeType, CatalogAdmissionRule.AdmitChangeType, CatalogAdmissionRule.AdmitUndoChangeType };
        private const string ExcludedValue = "true";
        private const string IncludedValue = "false";

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
            var (evidence, excludedCount) = await EvidenceAsync(foodIdList, cancellationToken);
            report.ExcludedEvidence = excludedCount;
            var ruleCurated = await RuleCuratedAsync(foods.Where(f => f.CurationStatusId == Curated).Select(f => f.Id).ToList(), cancellationToken);

            var changes = new List<(IngredientEntity Food, string ChangeType, string Property, string? Old, string? New)>();
            var notes = new Dictionary<long, string>();
            var liftedRows = new Dictionary<long, string>();
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

                var (agreement, agreeingRows) = Judge(food, evidence[food.Id]);
                var nutrition = new FoodQualityInput(food.Name, Amount(food, CalorieNames), Amount(food, ProteinNames), Amount(food, CarbNames), Amount(food, FatNames),
                    AlcoholGramsPer100g: AnyAmount(food, AlcoholNames), FiberGramsPer100g: AnyAmount(food, FiberNames));
                var decision = CatalogAdmissionRule.Decide(new AdmissionFacts(CatalogAdmissionRule.BrandedFood, null, food.FdcCategory, food.Name, nutrition,
                    AgreeingSources: agreement.Agreeing, DisagreeingSources: agreement.Disagreeing), _validator);
                var kept = (food.AdmissionReason ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t != CatalogAdmissionRule.AwaitingSecondSource && t != CatalogAdmissionRule.SourcesDisagree && !decision.Reasons.Contains(t));
                var reasons = decision.Reasons.Concat(kept).ToList();
                var reason = reasons.Count == 0 ? null : Truncate(string.Join("+", reasons), 255);
                var status = food.CurationStatusId == Pending && reasons.Count == 0 ? Curated
                    : food.CurationStatusId == Curated && decision.Reasons.Count > 0 && ruleCurated.Contains(food.Id) ? Pending
                    : food.CurationStatusId;

                var bucket = agreement.Agreeing >= 4 ? "4+" : agreement.Agreeing.ToString(CultureInfo.InvariantCulture);
                report.ByAgreeingSources[bucket] = report.ByAgreeingSources.GetValueOrDefault(bucket) + 1;
                var summary = Summary(agreement);
                if (status == Curated && food.CurationStatusId == Pending)
                {
                    report.Lifted++;
                    liftedRows[food.Id] = string.Join(",", agreeingRows.Select(id => id.ToString(CultureInfo.InvariantCulture)));
                    if (report.Lines.Count < 400) report.Lines.Add($"lift     #{food.Id} {food.Name}: {agreement.Agreeing} sources agree ({summary})");
                }
                else if (status == Pending && food.CurationStatusId == Curated)
                {
                    report.Demoted++;
                    if (report.Lines.Count < 400) report.Lines.Add($"demote   #{food.Id} {food.Name}: curated by the rules, which now say {reason} ({summary})");
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
                    if (report.Lines.Count < 400) report.Lines.Add($"kept     #{food.Id} {food.Name}: curated by an admin, the rule says {reason} ({summary})");
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
            foreach (var (foodId, rows) in liftedRows)
                _db.AuditLogEntries.Add(Journal(FoodEntityType, foodId, RowsChangeType, AgreeingRowsProperty, null, Truncate(rows, 4000), stamp, personId));
            await _db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            report.Applied = true;
            report.RunStamp = stamp;
            _logger.LogInformation("Branded lift {Stamp:O}: {Judged} judged, {Lifted} lifted, {Demoted} demoted, {Held} held, {Changed} foods changed.", stamp, report.Judged, report.Lifted, report.Demoted, report.Held, report.ChangedFoods);
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
            var unlifted = new HashSet<long>();
            var countsLeft = new HashSet<long>();
            foreach (var entry in entries)
            {
                if (!foods.TryGetValue(entry.EntityId, out var food) || ReadValue(food, entry.PropertyName) != entry.NewValue)
                {
                    report.Skipped++;
                    report.Lines.Add($"left     #{entry.EntityId} {food?.Name}: {entry.PropertyName} changed since the run");
                    if (entry.PropertyName is nameof(IngredientEntity.AgreeingSources) or nameof(IngredientEntity.DisagreeingSources)) countsLeft.Add(entry.EntityId);
                    continue;
                }
                if (entry.PropertyName == nameof(IngredientEntity.CurationStatusId) && entry.NewValue == Long(Curated))
                {
                    report.Demoted++;
                    unlifted.Add(entry.EntityId);
                }
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

            var rowEntries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.Timestamp == stamp && a.EntityType == FoodEntityType && a.ChangeType == RowsChangeType && unlifted.Contains(a.EntityId))
                .ToListAsync(cancellationToken);
            var toExclude = rowEntries
                .SelectMany(a => (a.NewValue ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(id => long.Parse(id, CultureInfo.InvariantCulture))
                .Distinct().ToList();
            var flips = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.Timestamp == stamp && a.EntityType == EvidenceEntityType && a.PropertyName == ExcludedProperty)
                .ToListAsync(cancellationToken);
            var state = await ExclusionStateAsync(toExclude.Concat(flips.Select(f => f.EntityId)).Distinct().ToList(), cancellationToken);
            var evidenceChanges = new List<(long RowId, string Old, string New)>();
            foreach (var rowId in toExclude.Where(id => !state.GetValueOrDefault(id)))
            {
                evidenceChanges.Add((rowId, IncludedValue, ExcludedValue));
                state[rowId] = true;
            }
            foreach (var flip in flips)
            {
                if ((state.GetValueOrDefault(flip.EntityId) ? ExcludedValue : IncludedValue) != flip.NewValue || flip.OldValue == null) continue;
                evidenceChanges.Add((flip.EntityId, flip.NewValue!, flip.OldValue));
                state[flip.EntityId] = flip.OldValue == ExcludedValue;
            }
            report.EvidenceExcluded = evidenceChanges.Count(c => c.New == ExcludedValue);
            report.EvidenceIncluded = evidenceChanges.Count(c => c.New == IncludedValue);

            var recounts = new List<(IngredientEntity Food, string Property, string? Old, string? New)>();
            var recountIds = unlifted.Where(countsLeft.Contains).ToList();
            if (recountIds.Count > 0)
            {
                var recountFoods = await _db.Ingredients.IgnoreQueryFilters()
                    .Include(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                    .Where(i => recountIds.Contains(i.Id)).ToListAsync(cancellationToken);
                var rows = await _db.IngredientVerifications.AsNoTracking()
                    .Where(v => recountIds.Contains(v.IngredientId) && v.Kind == IngredientVerificationKind.Label && v.Status != IngredientVerificationStatus.Skipped)
                    .ToListAsync(cancellationToken);
                var evidence = OrderEvidence(rows.Where(r => !state.GetValueOrDefault(r.Id)));
                foreach (var food in recountFoods)
                {
                    var (agreement, _) = Judge(food, evidence[food.Id]);
                    if (Int(food.AgreeingSources) != Int(agreement.Agreeing))
                        recounts.Add((food, nameof(IngredientEntity.AgreeingSources), Int(food.AgreeingSources), Int(agreement.Agreeing)));
                    if (Int(food.DisagreeingSources) != Int(agreement.Disagreeing))
                        recounts.Add((food, nameof(IngredientEntity.DisagreeingSources), Int(food.DisagreeingSources), Int(agreement.Disagreeing)));
                    report.Recounted++;
                    if (CatalogAdmissionRule.SourcesLift(agreement.Agreeing, agreement.Disagreeing))
                    {
                        report.StillLifted++;
                        report.Lines.Add($"recount  #{food.Id} {food.Name}: without the excluded evidence {agreement.Agreeing} sources still agree; the next lift curates it again");
                    }
                }
            }
            if (!apply) return report;

            _db.AuditLogEntries.Add(Journal(RunEntityType, 0, UndoChangeType, "UndoOf", null, stamp.ToString("O", CultureInfo.InvariantCulture), now, personId));
            foreach (var entry in undone)
                _db.AuditLogEntries.Add(Journal(entry.EntityType, entry.EntityId, UndoChangeType, entry.PropertyName, entry.NewValue, entry.OldValue, now, personId));
            foreach (var (food, property, old, value) in recounts)
            {
                WriteValue(food, property, value);
                food.LastModifiedDate = now;
                food.LastModifiedByPersonId = personId;
                _db.AuditLogEntries.Add(Journal(FoodEntityType, food.Id, UndoChangeType, property, old, value, now, personId));
            }
            foreach (var (rowId, old, value) in evidenceChanges)
                _db.AuditLogEntries.Add(Journal(EvidenceEntityType, rowId, ExcludeChangeType, ExcludedProperty, old, value, now, personId));
            await _db.SaveChangesAsync(cancellationToken);
            report.Applied = true;
            report.UndoStamp = now;
            _logger.LogInformation("Branded lift undo {Stamp:O} as {UndoStamp:O}: {Demoted} demoted, {Relifted} lifted again, {Excluded} evidence rows excluded, {Skipped} skipped.",
                stamp, now, report.Demoted, report.Relifted, report.EvidenceExcluded, report.Skipped);
            return report;
        }

        private (BrandedAgreement Agreement, IReadOnlyList<long> AgreeingRows) Judge(IngredientEntity food, IEnumerable<IngredientVerificationEntity> rows)
        {
            var read = rows.Select(r => (Row: r, Reading: Reading(r))).Where(r => r.Reading != null).ToList();
            var stored = new BrandedStoredValues(Amount(food, CalorieNames), Amount(food, ProteinNames), Amount(food, CarbNames), Amount(food, FatNames), food.ReferenceServingGrams);
            var agreement = BrandedSourceAgreement.Judge(stored, read.Select(r => r.Reading!));
            var agreeingKeys = agreement.AgreeingKeys.ToHashSet(StringComparer.Ordinal);
            return (agreement, read.Where(r => agreeingKeys.Contains(r.Reading!.Key)).Select(r => r.Row.Id).Distinct().OrderBy(id => id).ToList());
        }

        private async Task<(ILookup<long, IngredientVerificationEntity> Evidence, int Excluded)> EvidenceAsync(List<long> foodIds, CancellationToken cancellationToken)
        {
            var rows = await _db.IngredientVerifications.AsNoTracking()
                .Where(v => foodIds.Contains(v.IngredientId) && v.Kind == IngredientVerificationKind.Label && v.Status != IngredientVerificationStatus.Skipped)
                .ToListAsync(cancellationToken);
            var state = await ExclusionStateAsync(rows.Select(r => r.Id).ToList(), cancellationToken);
            var kept = rows.Where(r => !state.GetValueOrDefault(r.Id)).ToList();
            return (OrderEvidence(kept), rows.Count - kept.Count);
        }

        private static ILookup<long, IngredientVerificationEntity> OrderEvidence(IEnumerable<IngredientVerificationEntity> rows) =>
            rows.OrderBy(v => v.ConfirmedAt ?? v.LastModifiedDate ?? v.CreatedDate).ThenBy(v => v.Id).ToLookup(v => v.IngredientId);

        private async Task<Dictionary<long, bool>> ExclusionStateAsync(List<long> rowIds, CancellationToken cancellationToken)
        {
            if (rowIds.Count == 0) return new Dictionary<long, bool>();
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == EvidenceEntityType && a.PropertyName == ExcludedProperty && rowIds.Contains(a.EntityId))
                .Select(a => new { a.Id, a.EntityId, a.NewValue })
                .ToListAsync(cancellationToken);
            return entries.GroupBy(a => a.EntityId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).Last().NewValue == ExcludedValue);
        }

        private async Task<HashSet<long>> RuleCuratedAsync(List<long> foodIds, CancellationToken cancellationToken)
        {
            if (foodIds.Count == 0) return new HashSet<long>();
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == FoodEntityType && a.PropertyName == nameof(IngredientEntity.CurationStatusId) && foodIds.Contains(a.EntityId))
                .Select(a => new { a.Id, a.EntityId, a.ChangeType, a.NewValue })
                .ToListAsync(cancellationToken);
            return entries.GroupBy(a => a.EntityId)
                .Select(g => g.OrderBy(a => a.Id).Last())
                .Where(a => RuleStatusChangeTypes.Contains(a.ChangeType) && a.NewValue == Long(Curated))
                .Select(a => a.EntityId)
                .ToHashSet();
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
