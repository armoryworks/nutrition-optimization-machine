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
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Writes and undoes the publication years of book sources (recipe.RecipeSourceEra). A year
    /// sets the era: historic before 1960, modern from 1960; a source with no year is unknown.
    /// The era is what <see cref="ContainerWeighing"/> sizes an unsized can by, so a write makes
    /// that book's recipes due for the nutrition sweep. Every write is one SourceEraSet entry in
    /// audit.AuditLogEntry under the run's stamp (old and new value), and the undo by that stamp
    /// restores the old value, skipping a row changed since.
    /// </summary>
    public class RecipeSourceEraService : IRecipeSourceEraService
    {
        public const string EntityType = "RecipeSourceEra";
        public const string SetChange = "SourceEraSet";
        public const string UndoChange = "SourceEraUndo";
        public const int EarliestYear = 1600;

        private readonly ApplicationDbContext _db;
        private readonly ILogger<RecipeSourceEraService> _logger;

        public RecipeSourceEraService(ApplicationDbContext db, ILogger<RecipeSourceEraService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>A source's era as a journal entry keeps it; null for a source with no row.</summary>
        public sealed record EraValue(int? Year, string? YearSource, string Era);

        private static EraValue? ValueOf(RecipeSourceEraEntity? row) => row == null || row.IsDeleted ? null : new EraValue(row.PublishedYear, row.YearSource, row.Era);

        private IQueryable<string> BookSources() => _db.Recipes.AsNoTracking()
            .Where(r => !r.IsDeleted && (r.SourceSite == null || r.SourceSite == "") && r.SourceAttribution != null && r.SourceAttribution != "")
            .Select(r => r.SourceAttribution!);

        public async Task<RecipeSourceEraListModel> ListAsync(CancellationToken cancellationToken = default)
        {
            var counts = await BookSources().GroupBy(a => a).Select(g => new { Source = g.Key, Recipes = g.Count() }).ToListAsync(cancellationToken);
            var rows = await _db.RecipeSourceEras.AsNoTracking().ToDictionaryAsync(e => e.SourceAttribution, cancellationToken);
            var list = new RecipeSourceEraListModel { Sources = counts.Count };
            foreach (var c in counts.OrderBy(c => c.Source, StringComparer.Ordinal))
            {
                var row = rows.GetValueOrDefault(c.Source);
                var era = row?.Era ?? ContainerEras.Unknown;
                list.ByEra[era] = list.ByEra.GetValueOrDefault(era) + 1;
                list.Items.Add(new RecipeSourceEraModel
                {
                    SourceAttribution = c.Source, Recipes = c.Recipes, PublishedYear = row?.PublishedYear, YearSource = row?.YearSource, Era = era,
                });
            }
            return list;
        }

        public async Task<RecipeSourceEraWriteReport> SetAsync(IReadOnlyList<RecipeSourceEraInput> rows, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            var stamp = Stamp(DateTime.UtcNow);
            var report = new RecipeSourceEraWriteReport { Applied = apply, Stamp = stamp, Received = rows.Count };
            var known = (await BookSources().Distinct().ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var stored = await _db.RecipeSourceEras.IgnoreQueryFilters().ToDictionaryAsync(e => e.SourceAttribution, cancellationToken);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var latest = DateTime.UtcNow.Year;
            var written = new List<(RecipeSourceEraEntity Row, EraValue? Old, EraValue New)>();

            foreach (var input in rows)
            {
                var source = (input.SourceAttribution ?? string.Empty).Trim();
                if (!known.Contains(source))
                {
                    report.Refused.Add($"{source}: no live book recipe carries this attribution");
                    continue;
                }
                if (!seen.Add(source))
                {
                    report.Refused.Add($"{source}: given twice");
                    continue;
                }
                if (input.PublishedYear is { } y && (y < EarliestYear || y > latest))
                {
                    report.Refused.Add(string.Create(CultureInfo.InvariantCulture, $"{source}: year {y} is outside {EarliestYear} to {latest}"));
                    continue;
                }

                var value = new EraValue(input.PublishedYear, Clip(input.YearSource, 255), ContainerEras.FromYear(input.PublishedYear));
                var row = stored.GetValueOrDefault(source);
                var old = ValueOf(row);
                if (old == value)
                {
                    report.Unchanged++;
                    continue;
                }
                if (old == null) report.Added++;
                else report.Changed++;
                report.ByEra[value.Era] = report.ByEra.GetValueOrDefault(value.Era) + 1;
                report.Lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{(old == null ? "add" : "change")} {source}: {Show(old)} -> {Show(value)}"));
                if (!apply) continue;

                if (row == null)
                {
                    row = new RecipeSourceEraEntity { SourceAttribution = source, CreatedDate = stamp, CreatedByPersonId = personId };
                    _db.RecipeSourceEras.Add(row);
                    stored[source] = row;
                }
                else
                {
                    row.IsDeleted = false;
                    row.DeletedAt = null;
                    row.DeletedByPersonId = null;
                    row.LastModifiedDate = stamp;
                    row.LastModifiedByPersonId = personId;
                }
                row.PublishedYear = value.Year;
                row.YearSource = value.YearSource;
                row.Era = value.Era;
                written.Add((row, old, value));
            }

            if (!apply || written.Count == 0) return report;
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var (row, old, value) in written)
                _db.AuditLogEntries.Add(Entry(row, SetChange, old == null ? null : LaneJournal.Write(old), LaneJournal.Write(value), stamp, personId));
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Book source eras written: {Added} added, {Changed} changed, {Unchanged} unchanged, {Refused} refused (stamp {Stamp:O})",
                report.Added, report.Changed, report.Unchanged, report.Refused.Count, stamp);
            return report;
        }

        public async Task<RecipeSourceEraUndoReport> UndoAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            stamp = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
            var report = new RecipeSourceEraUndoReport { Applied = apply, Stamp = stamp };
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == EntityType && a.ChangeType == SetChange && a.Timestamp == stamp)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            var ids = entries.Select(e => e.EntityId).Distinct().ToList();
            var rows = await _db.RecipeSourceEras.IgnoreQueryFilters().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var entry in entries)
            {
                var row = rows.GetValueOrDefault(entry.EntityId);
                var expected = LaneJournal.Read<EraValue>(entry.NewValue);
                if (row == null || expected == null || ValueOf(row) != expected)
                {
                    report.Skipped++;
                    report.Lines.Add($"skipped {entry.PropertyName}: changed since");
                    continue;
                }
                var old = LaneJournal.Read<EraValue>(entry.OldValue);
                report.Undone++;
                report.Lines.Add($"undo {entry.PropertyName}: {Show(expected)} -> {Show(old)}");
                if (!apply) continue;
                if (old == null)
                {
                    row.IsDeleted = true;
                    row.DeletedAt = now;
                    row.DeletedByPersonId = personId;
                }
                else
                {
                    row.PublishedYear = old.Year;
                    row.YearSource = old.YearSource;
                    row.Era = old.Era;
                }
                row.LastModifiedDate = now;
                row.LastModifiedByPersonId = personId;
                _db.AuditLogEntries.Add(Entry(row, UndoChange, entry.NewValue, entry.OldValue, now, personId));
            }
            if (apply) await _db.SaveChangesAsync(cancellationToken);
            return report;
        }

        private static AuditLogEntryEntity Entry(RecipeSourceEraEntity row, string changeType, string? oldValue, string? newValue, DateTime at, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = row.Id,
            ChangeType = changeType,
            PropertyName = Clip(row.SourceAttribution, 256),
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        private static string Show(EraValue? value) => value == null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{value.Year?.ToString(CultureInfo.InvariantCulture) ?? "no year"} ({value.Era})");

        private static DateTime Stamp(DateTime at) => new(at.Ticks - at.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

        private static string? Clip(string? value, int length) => value == null || value.Length <= length ? value : value[..length];
    }
}
