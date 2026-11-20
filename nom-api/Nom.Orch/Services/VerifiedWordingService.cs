using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Loads reviewed wording verdicts into recipe.IngredientWording. Each row added, changed or
    /// taken out is one audit entry (EntityType IngredientWording, ChangeType WordingVerify, old
    /// and new value "verdict:foodId"); each food the load curates is one entry (EntityType
    /// Ingredient, ChangeType WordingCurate) that an unload takes back.
    /// </summary>
    public class VerifiedWordingService : IVerifiedWordingService
    {
        public const string WordingEntityType = "IngredientWording";
        public const string VerifyChangeType = "WordingVerify";
        public const string CurateEntityType = "Ingredient";
        public const string CurateChangeType = "WordingCurate";
        public const string SourcePrefix = "verified:";
        public const string TsvHeader = "wording\tverdict\tfood_id\tfood_fdc_id\tfood_name\tlines_at_review\tbatch_row\treason";
        public const string ExportHeader = "wording\tverdict\tfood_id\tfood_fdc_id\tfood_name\tbatch\treason";

        public const string NotKeyForm = "wording is not in key form";
        public const string UnknownVerdict = "verdict is not food, none or unclear";
        public const string TargetMissing = "food not found";
        public const string TargetBranded = "food is branded";
        public const string TargetNoNutrition = "food has no nutrition";
        public const string TargetNotCurated = "food is not curated";
        public const string NamesCuratedFood = "wording is the name of a curated food";
        public const string ConflictingRows = "rows give the wording different verdicts or foods";
        public const string DuplicateRow = "row repeats an earlier row";
        public const string ExistsDifferent = "table gives the wording another verdict or food";
        public const string MalformedLine = "line does not have the header's columns";

        private const int BusiestFoodCount = 20;
        private static readonly string[] PromotableDataTypes = { "foundation_food", "sr_legacy_food" };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public VerifiedWordingService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Target(long Id, string Name, string? FdcId, string DataType, long Status, bool HasNutrition, string? AdmissionReason);
        private sealed record Accepted(VerifiedWordingRow Row, string Wording, string Verdict, Target? Food);

        /// <summary>
        /// Reads db/verified-wordings.tsv: a header naming its columns (wording and verdict
        /// required; food_id, food_fdc_id, food_name, batch_row and reason read when present), then
        /// one tab-separated row per wording. A line without the header's columns is reported.
        /// </summary>
        public static List<VerifiedWordingRow> ParseTsv(string tsv, ICollection<VerifiedWordingProblem> problems)
        {
            var rows = new List<VerifiedWordingRow>();
            var lines = (tsv ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim().Length == 0) return rows;

            var header = lines[0].TrimStart('﻿').Split('\t').Select(h => h.Trim().ToLowerInvariant()).ToList();
            int Column(string name) => header.IndexOf(name);
            var wording = Column("wording");
            var verdict = Column("verdict");
            if (wording < 0 || verdict < 0)
            {
                problems.Add(new VerifiedWordingProblem { Row = "1", Reason = MalformedLine });
                return rows;
            }

            var foodId = Column("food_id");
            var fdcId = Column("food_fdc_id");
            var foodName = Column("food_name");
            var batchRow = Column("batch_row");
            var reason = Column("reason");
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                var fields = lines[i].Split('\t');
                string? Field(int index) => index >= 0 && index < fields.Length && fields[index].Trim().Length > 0 ? fields[index].Trim() : null;
                if (fields.Length != header.Count)
                {
                    problems.Add(new VerifiedWordingProblem { Row = Field(batchRow) ?? (i + 1).ToString(CultureInfo.InvariantCulture), Wording = Field(wording) ?? string.Empty, Reason = MalformedLine });
                    continue;
                }

                rows.Add(new VerifiedWordingRow
                {
                    Row = Field(batchRow) ?? (i + 1).ToString(CultureInfo.InvariantCulture),
                    Wording = Field(wording) ?? string.Empty,
                    Verdict = Field(verdict) ?? string.Empty,
                    FoodId = long.TryParse(Field(foodId), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null,
                    FoodFdcId = Field(fdcId),
                    FoodName = Field(foodName),
                    Note = Field(reason),
                });
            }

            return rows;
        }

        public async Task<VerifiedWordingLoadResult> LoadAsync(IReadOnlyCollection<VerifiedWordingRow> rows, string batch, bool apply = false, bool replace = false)
        {
            batch = (batch ?? string.Empty).Trim();
            if (batch.Length == 0 || batch.Length > 128) throw new ArgumentException("A batch name of 1 to 128 characters is required.", nameof(batch));

            var result = new VerifiedWordingLoadResult { Applied = apply, Rows = rows.Count };
            var targets = await TargetsAsync(rows);
            var curatedNames = (await _db.Ingredients.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .Select(i => new { i.Id, i.Name })
                    .ToListAsync())
                .ToLookup(i => i.Name.Trim().ToLowerInvariant(), i => i.Id);

            var candidates = new List<Accepted>();
            void Refuse(VerifiedWordingRow row, string reason)
            {
                result.Rejected++;
                result.Problems.Add(new VerifiedWordingProblem { Row = row.Row, Wording = row.Wording ?? string.Empty, Reason = reason });
            }

            foreach (var row in rows)
            {
                var wording = (row.Wording ?? string.Empty).Trim();
                var verdict = (row.Verdict ?? string.Empty).Trim().ToLowerInvariant();
                if (wording.Length == 0 || wording.Length > 511 || IngredientNameNormalizer.WordingKey(wording) != wording)
                {
                    Refuse(row, NotKeyForm);
                    continue;
                }

                if (verdict is not (IngredientWordingVerdict.Food or IngredientWordingVerdict.None or IngredientWordingVerdict.Unclear))
                {
                    Refuse(row, UnknownVerdict);
                    continue;
                }

                Target? food = null;
                if (verdict == IngredientWordingVerdict.Food)
                {
                    food = Resolve(row, targets);
                    var refusal = food == null ? TargetMissing
                        : food.DataType == "branded_food" ? TargetBranded
                        : !food.HasNutrition ? TargetNoNutrition
                        : food.Status != (long)CurationStatusEnum.Curated && !IsPromotable(food) ? TargetNotCurated
                        : null;
                    if (refusal != null)
                    {
                        Refuse(row, refusal);
                        continue;
                    }
                }

                if (curatedNames[wording].Any(id => id != food?.Id))
                {
                    Refuse(row, NamesCuratedFood);
                    continue;
                }

                candidates.Add(new Accepted(row, wording, verdict, food));
            }

            var accepted = new List<Accepted>();
            foreach (var group in candidates.GroupBy(c => c.Wording, StringComparer.Ordinal))
            {
                if (group.Select(c => (c.Verdict, c.Food?.Id)).Distinct().Count() > 1)
                {
                    foreach (var c in group) Refuse(c.Row, ConflictingRows);
                    continue;
                }

                accepted.Add(group.First());
                foreach (var c in group.Skip(1)) Refuse(c.Row, DuplicateRow);
            }

            var keys = accepted.Select(a => a.Wording).ToList();
            var existing = await _db.IngredientWordings.IgnoreQueryFilters()
                .Where(w => keys.Contains(w.Wording))
                .ToDictionaryAsync(w => w.Wording, StringComparer.Ordinal);

            var writes = new List<(Accepted Entry, IngredientWordingEntity? Row, Replaced? Old)>();
            foreach (var entry in accepted)
            {
                existing.TryGetValue(entry.Wording, out var row);
                if (row is { IsDeleted: false })
                {
                    if (row.Verdict == entry.Verdict && row.IngredientId == entry.Food?.Id)
                    {
                        result.Unchanged++;
                        continue;
                    }

                    if (!replace)
                    {
                        Refuse(entry.Row, ExistsDifferent);
                        continue;
                    }

                    result.Changed++;
                    writes.Add((entry, row, new Replaced(Value(row.Verdict, row.IngredientId), row.Batch, row.Note)));
                    continue;
                }

                result.Added++;
                writes.Add((entry, row, null));
            }

            var promoted = writes.Where(w => w.Entry.Food != null && w.Entry.Food.Status != (long)CurationStatusEnum.Curated)
                .Select(w => w.Entry.Food!.Id).Distinct().ToList();
            result.Promoted = promoted.Count;
            result.BusiestFoods = writes.Where(w => w.Entry.Food != null)
                .GroupBy(w => w.Entry.Food!.Id)
                .Select(g => new VerifiedWordingFood { IngredientId = g.Key, Name = g.First().Entry.Food!.Name, Wordings = g.Count() })
                .OrderByDescending(f => f.Wordings).ThenBy(f => f.IngredientId)
                .Take(BusiestFoodCount)
                .ToList();

            if (!apply || writes.Count == 0) return result;

            var personId = _currentUser.PersonIdOrSystem;
            var now = DateTime.UtcNow;
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            foreach (var food in await _db.Ingredients.Where(i => promoted.Contains(i.Id)).ToListAsync())
            {
                _db.AuditLogEntries.Add(Journal(CurateEntityType, food.Id, CurateChangeType, nameof(IngredientEntity.CurationStatusId),
                    Status(food.CurationStatusId), Status((long)CurationStatusEnum.Curated), now, personId));
                food.CurationStatusId = (long)CurationStatusEnum.Curated;
                food.LastModifiedDate = now;
                food.LastModifiedByPersonId = personId;
            }

            var journaled = new List<(IngredientWordingEntity Row, Replaced? Old)>();
            foreach (var (entry, existingRow, old) in writes)
            {
                var row = existingRow;
                if (row == null)
                {
                    row = new IngredientWordingEntity { Wording = entry.Wording, CreatedDate = now, CreatedByPersonId = personId };
                    _db.IngredientWordings.Add(row);
                }
                else
                {
                    row.IsDeleted = false;
                    row.DeletedAt = null;
                    row.DeletedByPersonId = null;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                }

                row.Verdict = entry.Verdict;
                row.IngredientId = entry.Food?.Id;
                row.Batch = batch;
                row.Source = Truncate(SourcePrefix + batch, 255);
                row.Note = entry.Row.Note == null ? null : Truncate(entry.Row.Note.Trim(), 1023);
                journaled.Add((row, old));
            }

            await _db.SaveChangesAsync();
            foreach (var (row, old) in journaled)
            {
                _db.AuditLogEntries.Add(Journal(WordingEntityType, row.Id, VerifyChangeType, nameof(IngredientWordingEntity.IngredientId),
                    old?.Value, Value(row.Verdict, row.IngredientId), now, personId));
                if (old == null) continue;
                _db.AuditLogEntries.Add(Journal(WordingEntityType, row.Id, VerifyChangeType, nameof(IngredientWordingEntity.Batch), old.Batch, row.Batch, now, personId));
                _db.AuditLogEntries.Add(Journal(WordingEntityType, row.Id, VerifyChangeType, nameof(IngredientWordingEntity.Note), old.Note, row.Note, now, personId));
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
            return result;
        }

        public async Task<VerifiedWordingUnloadResult> UnloadAsync(string batch, bool apply = false)
        {
            batch = (batch ?? string.Empty).Trim();
            var rows = await _db.IngredientWordings.Where(w => w.Batch == batch).ToListAsync();
            var foodIds = rows.Where(r => r.IngredientId != null).Select(r => r.IngredientId!.Value).Distinct().ToList();
            var rowIds = rows.Select(r => r.Id).ToList();
            var earlier = await EarlierAsync(rows, batch);
            var stillPointed = (await _db.IngredientWordings
                    .Where(w => w.IngredientId != null && foodIds.Contains(w.IngredientId.Value) && !rowIds.Contains(w.Id))
                    .Select(w => w.IngredientId!.Value)
                    .Distinct()
                    .ToListAsync())
                .Concat(earlier.Values.Select(e => e.IngredientId).OfType<long>())
                .ToList();
            var curatedByLoad = await _db.AuditLogEntries
                .Where(a => a.EntityType == CurateEntityType && a.ChangeType == CurateChangeType
                    && foodIds.Contains(a.EntityId) && !stillPointed.Contains(a.EntityId))
                .ToListAsync();
            var promotedIds = curatedByLoad.Select(a => a.EntityId).Distinct().ToList();
            var demote = await _db.Ingredients
                .Where(i => promotedIds.Contains(i.Id) && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .ToListAsync();

            var result = new VerifiedWordingUnloadResult { Applied = apply, Removed = rows.Count - earlier.Count, Restored = earlier.Count, Demoted = demote.Count };
            if (!apply || rows.Count == 0) return result;

            var personId = _currentUser.PersonIdOrSystem;
            var now = DateTime.UtcNow;
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
            foreach (var row in rows)
            {
                if (earlier.TryGetValue(row.Id, out var back))
                {
                    _db.AuditLogEntries.Add(Journal(WordingEntityType, row.Id, VerifyChangeType, nameof(IngredientWordingEntity.IngredientId),
                        Value(row.Verdict, row.IngredientId), Value(back.Verdict, back.IngredientId), now, personId));
                    row.Verdict = back.Verdict;
                    row.IngredientId = back.IngredientId;
                    row.Batch = back.Batch;
                    row.Source = back.Batch == null ? row.Source : Truncate(SourcePrefix + back.Batch, 255);
                    row.Note = back.Note;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                    continue;
                }

                _db.AuditLogEntries.Add(Journal(WordingEntityType, row.Id, VerifyChangeType, nameof(IngredientWordingEntity.IngredientId),
                    Value(row.Verdict, row.IngredientId), null, now, personId));
                row.IsDeleted = true;
                row.DeletedAt = now;
                row.DeletedByPersonId = personId;
            }

            foreach (var food in demote)
            {
                var promotion = curatedByLoad.Where(a => a.EntityId == food.Id).OrderByDescending(a => a.Id).First();
                food.CurationStatusId = long.TryParse(promotion.OldValue, NumberStyles.None, CultureInfo.InvariantCulture, out var was)
                    ? was : (long)CurationStatusEnum.PendingCuration;
                food.LastModifiedDate = now;
                food.LastModifiedByPersonId = personId;
            }

            _db.AuditLogEntries.RemoveRange(curatedByLoad.Where(a => demote.Any(f => f.Id == a.EntityId)));
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
            return result;
        }

        public async Task<string> ExportTsvAsync()
        {
            var rows = await _db.IngredientWordings.AsNoTracking()
                .OrderBy(w => w.Wording)
                .Select(w => new
                {
                    w.Wording, w.Verdict, w.IngredientId,
                    FdcId = w.Ingredient == null ? null : w.Ingredient.FdcId,
                    Name = w.Ingredient == null ? null : w.Ingredient.Name,
                    w.Batch, w.Note,
                })
                .ToListAsync();

            var tsv = new StringBuilder().Append(ExportHeader).Append('\n');
            foreach (var row in rows.OrderBy(r => r.Wording, StringComparer.Ordinal))
            {
                tsv.AppendJoin('\t', Cell(row.Wording), row.Verdict,
                        row.IngredientId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        Cell(row.FdcId), Cell(row.Name), Cell(row.Batch), Cell(row.Note))
                    .Append('\n');
            }

            return tsv.ToString();
        }

        private async Task<List<Target>> TargetsAsync(IReadOnlyCollection<VerifiedWordingRow> rows)
        {
            var foodRows = rows.Where(r => string.Equals((r.Verdict ?? string.Empty).Trim(), IngredientWordingVerdict.Food, StringComparison.OrdinalIgnoreCase)).ToList();
            var fdcIds = foodRows.Select(r => r.FoodFdcId?.Trim()).Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();
            var ids = foodRows.Where(r => string.IsNullOrWhiteSpace(r.FoodFdcId) && r.FoodId != null).Select(r => r.FoodId!.Value).Distinct().ToList();
            return await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && ((i.FdcId != null && fdcIds.Contains(i.FdcId)) || ids.Contains(i.Id)))
                .Select(i => new Target(i.Id, i.Name, i.FdcId, i.FdcDataType ?? string.Empty, i.CurationStatusId, i.IngredientNutrients.Any(n => !n.IsDeleted), i.AdmissionReason))
                .ToListAsync();
        }

        /// <summary>
        /// The food a row names: by its USDA id when it gives one (a curated entry before one
        /// awaiting curation, then the oldest), otherwise by id with the name it gives.
        /// </summary>
        private static Target? Resolve(VerifiedWordingRow row, List<Target> targets)
        {
            var fdcId = row.FoodFdcId?.Trim();
            if (!string.IsNullOrEmpty(fdcId))
            {
                return targets.Where(t => t.FdcId == fdcId)
                    .OrderByDescending(t => t.Status == (long)CurationStatusEnum.Curated)
                    .ThenBy(t => t.Id)
                    .FirstOrDefault();
            }

            var name = row.FoodName?.Trim();
            return targets.FirstOrDefault(t => t.Id == row.FoodId && !string.IsNullOrEmpty(name)
                && string.Equals(t.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPromotable(Target food) =>
            food.Status == (long)CurationStatusEnum.PendingCuration && food.FdcId != null && PromotableDataTypes.Contains(food.DataType)
            && food.AdmissionReason == null;

        private sealed record Replaced(string Value, string? Batch, string? Note);

        private sealed record Earlier(string Verdict, long? IngredientId, string? Batch, string? Note);

        /// <summary>
        /// For each row of the batch that the batch's load took over from an earlier verdict with
        /// replace, that earlier verdict, food, batch and note, read from the load's journal.
        /// </summary>
        private async Task<Dictionary<long, Earlier>> EarlierAsync(List<IngredientWordingEntity> rows, string batch)
        {
            var ids = rows.Select(r => r.Id).ToList();
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == WordingEntityType && a.ChangeType == VerifyChangeType && ids.Contains(a.EntityId))
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var earlier = new Dictionary<long, Earlier>();
            foreach (var row in rows)
            {
                var own = entries.Where(a => a.EntityId == row.Id).ToList();
                var load = own.FirstOrDefault(a => a.PropertyName == nameof(IngredientWordingEntity.IngredientId) && a.NewValue != null);
                if (load == null || load.OldValue == null || load.NewValue != Value(row.Verdict, row.IngredientId)) continue;
                var moved = own.FirstOrDefault(a => a.PropertyName == nameof(IngredientWordingEntity.Batch) && a.Timestamp == load.Timestamp && a.NewValue == batch);
                if (moved == null) continue;
                var cut = load.OldValue.LastIndexOf(':');
                if (cut <= 0) continue;
                long? food = long.TryParse(load.OldValue[(cut + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
                var note = own.FirstOrDefault(a => a.PropertyName == nameof(IngredientWordingEntity.Note) && a.Timestamp == load.Timestamp);
                earlier[row.Id] = new Earlier(load.OldValue[..cut], food, moved.OldValue, note?.OldValue);
            }
            return earlier;
        }

        internal static string Value(string verdict, long? ingredientId) =>
            string.Create(CultureInfo.InvariantCulture, $"{verdict}:{ingredientId}");

        private static string Status(long status) => status.ToString(CultureInfo.InvariantCulture);

        private static AuditLogEntryEntity Journal(string entityType, long entityId, string changeType, string property, string? oldValue, string? newValue, DateTime at, long personId) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            PropertyName = property,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        private static string Cell(string? text) =>
            (text ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
    }
}
