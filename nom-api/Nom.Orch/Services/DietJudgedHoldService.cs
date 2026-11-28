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
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Loads, lists and undoes the judged holds of the stored diet gate (plan item ck23, U5). A load adds a row per recipe
    /// and restriction type not yet held and leaves one already held as it is, so loading the same file twice changes
    /// nothing; the table's triggers hold each written recipe through the gate's write log until the next cycle flags it.
    /// A food judged for one type is held for every type that keeps out what the judged type keeps out (<see cref="Implied"/>):
    /// a wheat food holds gluten, so a Wheat Allergy hold also holds the gluten types, and a gluten hold holds every gluten
    /// type; nothing implies Wheat Allergy, since gluten also comes from barley and rye. Every added row is one
    /// DietJudgedHoldLoad entry in audit.AuditLogEntry under the load's stamp, written in the same save as the rows, and the
    /// undo by that stamp deletes the rows it added that are unchanged since.
    /// </summary>
    public class DietJudgedHoldService : IDietJudgedHoldService
    {
        public const string EntityType = "RecipeDietJudgedHold";
        public const string LoadChange = "DietJudgedHoldLoad";
        public const string UndoChange = "DietJudgedHoldUndo";
        public const int MaxFood = 60;

        private static readonly string[] GlutenTypes = { "Celiac Disease", "Gluten Sensitivity", "Gluten-Free" };

        /// <summary>The restriction types a judged food of a type is held for as well, by name.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Implied = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wheat Allergy"] = GlutenTypes,
            ["Celiac Disease"] = GlutenTypes,
            ["Gluten Sensitivity"] = GlutenTypes,
            ["Gluten-Free"] = GlutenTypes,
        };

        private readonly ApplicationDbContext _db;
        private readonly ILogger<DietJudgedHoldService> _logger;

        public DietJudgedHoldService(ApplicationDbContext db, ILogger<DietJudgedHoldService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>A hold as a journal entry keeps it.</summary>
        public sealed record HoldValue(long RecipeId, long RestrictionTypeId, string Food, string JudgedBy, DateTime JudgedAt, string Evidence);

        private static HoldValue ValueOf(RecipeDietJudgedHoldEntity row) => new(row.RecipeId, row.RestrictionTypeId, row.Food, row.JudgedBy, row.JudgedAt, row.Evidence);

        private async Task<Dictionary<long, string>> TypesAsync(CancellationToken cancellationToken)
        {
            var ids = await _db.Set<RestrictionCriterionEntity>().AsNoTracking().Select(c => c.RestrictionTypeId).Distinct().ToListAsync(cancellationToken);
            return await _db.Set<ReferenceEntity>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        }

        public async Task<DietJudgedHoldListModel> ListAsync(CancellationToken cancellationToken = default)
        {
            var types = await TypesAsync(cancellationToken);
            var rows = await _db.RecipeDietJudgedHolds.AsNoTracking().OrderByDescending(h => h.LoadedAt).ThenBy(h => h.RecipeId).ThenBy(h => h.RestrictionTypeId).ToListAsync(cancellationToken);
            var list = new DietJudgedHoldListModel { Holds = rows.Count };
            foreach (var row in rows)
            {
                var type = types.GetValueOrDefault(row.RestrictionTypeId) ?? "#" + row.RestrictionTypeId.ToString(CultureInfo.InvariantCulture);
                list.ByType[type] = list.ByType.GetValueOrDefault(type) + 1;
                list.Items.Add(new DietJudgedHoldModel
                {
                    Id = row.Id, RecipeId = row.RecipeId, RestrictionTypeId = row.RestrictionTypeId, Type = type, Food = row.Food, JudgedBy = row.JudgedBy,
                    JudgedAt = row.JudgedAt, Evidence = row.Evidence, LoadedAt = row.LoadedAt,
                });
            }
            return list;
        }

        public async Task<DietJudgedHoldLoadReport> LoadAsync(IReadOnlyList<DietJudgedHoldInput> rows, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            var stamp = Stamp(DateTime.UtcNow);
            var report = new DietJudgedHoldLoadReport { Applied = apply, Stamp = stamp, Received = rows.Count };
            var types = await TypesAsync(cancellationToken);
            var byName = types.GroupBy(t => t.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Min(t => t.Key), StringComparer.OrdinalIgnoreCase);
            var recipeIds = rows.Select(r => r.RecipeId).Distinct().ToList();
            var live = new HashSet<long>();
            foreach (var batch in recipeIds.Chunk(5000))
                live.UnionWith(await _db.Recipes.IgnoreQueryFilters().AsNoTracking().Where(r => !r.IsDeleted && batch.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken));
            var held = (await _db.RecipeDietJudgedHolds.AsNoTracking().Select(h => new { h.RecipeId, h.RestrictionTypeId }).ToListAsync(cancellationToken))
                .Select(h => (h.RecipeId, h.RestrictionTypeId)).ToHashSet();
            var seen = new HashSet<(long, long)>();
            var added = new List<RecipeDietJudgedHoldEntity>();
            var accepted = new List<(DietJudgedHoldInput Input, string Type, string Food, string JudgedBy, string Evidence)>();

            foreach (var input in rows)
            {
                var id = input.RecipeId.ToString(CultureInfo.InvariantCulture);
                var typeName = (input.Type ?? string.Empty).Trim();
                var food = (input.Food ?? string.Empty).Trim();
                var judgedBy = (input.JudgedBy ?? string.Empty).Trim();
                var evidence = (input.Evidence ?? string.Empty).Trim();
                if (!byName.TryGetValue(typeName, out var typeId))
                {
                    report.Refused.Add($"{id} {typeName}: no restriction type of this name");
                    continue;
                }
                if (!live.Contains(input.RecipeId))
                {
                    report.Refused.Add($"{id} {typeName}: no live recipe has this id");
                    continue;
                }
                if (food.Length == 0 || food.Length > MaxFood || food.Contains('\n'))
                {
                    report.Refused.Add(string.Create(CultureInfo.InvariantCulture, $"{id} {typeName}: the food must be a short word of 1 to {MaxFood} characters"));
                    continue;
                }
                if (judgedBy.Length == 0 || judgedBy.Length > 100 || evidence.Length == 0 || evidence.Length > 255 || input.JudgedAt == default)
                {
                    report.Refused.Add($"{id} {typeName}: who judged it, when, and the evidence file are all needed");
                    continue;
                }
                if (!seen.Add((input.RecipeId, typeId)))
                {
                    report.Refused.Add($"{id} {typeName}: given twice");
                    continue;
                }
                accepted.Add((input, types[typeId], food, judgedBy, evidence));
                if (held.Contains((input.RecipeId, typeId)))
                {
                    report.Unchanged++;
                    continue;
                }
                report.Added++;
                Add(input, typeId, food, judgedBy, evidence, $"({judgedBy}, {evidence})");
            }

            foreach (var (input, typeName, food, judgedBy, evidence) in accepted)
                foreach (var impliedName in Implied.GetValueOrDefault(typeName) ?? Array.Empty<string>())
                {
                    if (!byName.TryGetValue(impliedName, out var impliedId) || !seen.Add((input.RecipeId, impliedId)) || held.Contains((input.RecipeId, impliedId))) continue;
                    report.Added++;
                    report.Implied++;
                    var because = $"; implied by {typeName}";
                    Add(input, impliedId, food, judgedBy, evidence.Length + because.Length <= 255 ? evidence + because : evidence, $"(implied by {typeName}; {judgedBy}, {evidence})");
                }

            if (!apply || added.Count == 0) return report;
            _db.RecipeDietJudgedHolds.AddRange(added);
            foreach (var row in added) _db.AuditLogEntries.Add(Entry(row, LoadChange, null, LaneJournal.Write(ValueOf(row)), stamp, personId));
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Judged diet holds loaded: {Added} added, {Implied} implied, {Unchanged} already held, {Refused} refused (stamp {Stamp:O})", report.Added, report.Implied, report.Unchanged, report.Refused.Count, stamp);
            return report;

            void Add(DietJudgedHoldInput input, long typeId, string food, string judgedBy, string evidence, string note)
            {
                var name = types[typeId];
                report.ByType[name] = report.ByType.GetValueOrDefault(name) + 1;
                report.Lines.Add(string.Create(CultureInfo.InvariantCulture, $"hold {input.RecipeId} for {name}: {food} {note}"));
                added.Add(new RecipeDietJudgedHoldEntity
                {
                    RecipeId = input.RecipeId, RestrictionTypeId = typeId, Food = food, JudgedBy = judgedBy,
                    JudgedAt = DateTime.SpecifyKind(input.JudgedAt, DateTimeKind.Utc), Evidence = evidence, LoadedAt = stamp, LoadedByPersonId = personId,
                });
            }
        }

        public async Task<DietJudgedHoldUndoReport> UndoAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            stamp = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
            var report = new DietJudgedHoldUndoReport { Applied = apply, Stamp = stamp };
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == EntityType && a.ChangeType == LoadChange && a.Timestamp == stamp)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            var recipeIds = entries.Select(e => e.EntityId).Distinct().ToList();
            var rows = (await _db.RecipeDietJudgedHolds.Where(h => recipeIds.Contains(h.RecipeId)).ToListAsync(cancellationToken)).ToDictionary(h => (h.RecipeId, h.RestrictionTypeId));
            var now = DateTime.UtcNow;
            foreach (var entry in entries)
            {
                var expected = LaneJournal.Read<HoldValue>(entry.NewValue);
                var row = expected == null ? null : rows.GetValueOrDefault((expected.RecipeId, expected.RestrictionTypeId));
                if (row == null || expected == null || ValueOf(row) != expected)
                {
                    report.Skipped++;
                    report.Lines.Add($"skipped {entry.PropertyName}: removed or changed since");
                    continue;
                }
                report.Undone++;
                report.Lines.Add($"release {entry.PropertyName}: {expected.Food}");
                if (!apply) continue;
                _db.RecipeDietJudgedHolds.Remove(row);
                _db.AuditLogEntries.Add(Entry(row, UndoChange, entry.NewValue, null, now, personId));
            }
            if (apply) await _db.SaveChangesAsync(cancellationToken);
            return report;
        }

        private static AuditLogEntryEntity Entry(RecipeDietJudgedHoldEntity row, string changeType, string? oldValue, string? newValue, DateTime at, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = row.RecipeId,
            ChangeType = changeType,
            PropertyName = string.Create(CultureInfo.InvariantCulture, $"recipe:{row.RecipeId}:type:{row.RestrictionTypeId}"),
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        private static DateTime Stamp(DateTime at) => new(at.Ticks - at.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
