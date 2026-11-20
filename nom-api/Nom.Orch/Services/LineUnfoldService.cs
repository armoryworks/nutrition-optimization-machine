using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// Gives each source line of a joined recipe row ("1 3/4 cup sugar + 1/3 cup sugar") a row of
    /// its own. The row keeps its place, its first line and its stored amount; each further line
    /// is added at the end of the recipe, on the same food, with the amount the line parser reads
    /// from it. Where the lines fall is decided by <see cref="FoldedLine"/>.
    /// </summary>
    public class LineUnfoldService : ILineUnfoldService
    {
        public const string UnfoldChangeType = "LineUnfold";

        private const string UnreadMeasurement = "Gram";
        private const int JournalLength = 4000;
        private const int RecipesPerTransaction = 200;

        private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public LineUnfoldService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Row(long RecipeId, long IngredientId, int SortOrder, string RawLine, decimal Quantity, long MeasurementId, string? LineKind, string IngredientName);
        private sealed record Fold(Row Row, IReadOnlyList<string> Segments);
        private sealed record Line(string RawLine, decimal Quantity, long MeasurementId, string? LineKind);
        private sealed record Lines(List<Line> Kept, int Dropped, bool Reassigned);
        private sealed record Units(Dictionary<long, string> Names, Dictionary<string, long> Ids);

        private sealed record Before(
            [property: JsonPropertyName("at")] string At,
            [property: JsonPropertyName("raw")] string Raw,
            [property: JsonPropertyName("qty")] decimal Quantity,
            [property: JsonPropertyName("m")] long MeasurementId);

        private sealed record After(
            [property: JsonPropertyName("added")] List<int> Added,
            [property: JsonPropertyName("qty")] decimal Quantity,
            [property: JsonPropertyName("m")] long MeasurementId);

        public async Task<LineUnfoldResult> UnfoldAsync(bool apply = false, int maxRows = 5000, int sampleSize = 50)
        {
            var result = new LineUnfoldResult { Applied = apply };
            var units = await UnitsAsync();
            var folds = await PlanAsync(result);
            var batch = folds.Take(Math.Clamp(maxRows, 1, 50000)).ToList();
            var samples = Math.Clamp(sampleSize, 0, 5000);
            var done = 0;

            if (!apply)
            {
                foreach (var fold in batch)
                {
                    Count(result, fold.Row, LinesOf(fold.Segments, fold.Row.Quantity, fold.Row.MeasurementId, fold.Row.LineKind, units), units, samples);
                }

                done = batch.Count;
            }
            else
            {
                var personId = _currentUser.PersonIdOrSystem;
                foreach (var chunk in batch.GroupBy(f => f.Row.RecipeId).Chunk(RecipesPerTransaction))
                {
                    var recipeIds = chunk.Select(g => g.Key).ToList();
                    await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                    var stored = (await _db.RecipeIngredients.IgnoreQueryFilters()
                            .Where(ri => recipeIds.Contains(ri.RecipeId))
                            .ToListAsync())
                        .ToLookup(ri => ri.RecipeId);

                    foreach (var recipe in chunk)
                    {
                        var next = stored[recipe.Key].Select(ri => ri.SortOrder).DefaultIfEmpty(-1).Max() + 1;
                        foreach (var fold in recipe)
                        {
                            var row = stored[recipe.Key].FirstOrDefault(ri => ri.IngredientId == fold.Row.IngredientId && ri.SortOrder == fold.Row.SortOrder);
                            if (row == null || row.IsDeleted || row.RawLine != fold.Row.RawLine || !IsIngredient(row.LineKind)) continue;

                            var lines = LinesOf(fold.Segments, row.Quantity, row.MeasurementId, row.LineKind, units);
                            next = Write(row, lines, next, personId);
                            Count(result, fold.Row, lines, units, samples);
                            done++;
                        }
                    }

                    await _db.SaveChangesAsync();
                    if (tx != null) await tx.CommitAsync();
                    _db.ChangeTracker.Clear();
                }
            }

            result.Remaining = folds.Count - done;
            return result;
        }

        private static bool IsIngredient(string? lineKind) => lineKind is null or RecipeIngredientLineKind.Ingredient;

        private async Task<Units> UnitsAsync()
        {
            var measurements = await _db.Measurements.AsNoTracking()
                .Where(m => !m.IsDeleted)
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();
            return new Units(
                measurements.ToDictionary(m => m.Id, m => m.Name),
                measurements.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Min(m => m.Id), StringComparer.OrdinalIgnoreCase));
        }

        private async Task<List<Fold>> PlanAsync(LineUnfoldResult result)
        {
            var rows = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RawLine.Contains(FoldedLine.Separator)
                    && !ri.Recipe.IsDeleted && ri.Recipe.ScrapedAtUtc != null && !ri.Ingredient.IsDeleted)
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new Row(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine, ri.Quantity, ri.MeasurementId, ri.LineKind, ri.Ingredient.Name))
                .ToListAsync();
            result.RowsExamined = rows.Count;
            result.SkippedOtherKind = rows.Count(r => !IsIngredient(r.LineKind));
            rows.RemoveAll(r => !IsIngredient(r.LineKind));

            var sources = new Dictionary<long, HashSet<string>>();
            foreach (var recipeIds in rows.Select(r => r.RecipeId).Distinct().Chunk(500))
            {
                var documents = await _db.ScrapedDocuments.AsNoTracking()
                    .Where(d => !d.IsDeleted && recipeIds.Contains(d.RecipeId))
                    .Select(d => new { d.RecipeId, d.RawJsonLd })
                    .ToListAsync();
                foreach (var document in documents)
                {
                    if (!sources.TryGetValue(document.RecipeId, out var lines)) sources[document.RecipeId] = lines = new HashSet<string>(StringComparer.Ordinal);
                    lines.UnionWith(FoldedLine.SourceLines(document.RawJsonLd));
                }
            }

            var folds = new List<Fold>();
            foreach (var row in rows)
            {
                var segments = FoldedLine.Segments(row.RawLine, sources.GetValueOrDefault(row.RecipeId));
                if (segments == null || Journal(new Before(LineRelinkService.Place(row.IngredientId, row.SortOrder), row.RawLine, row.Quantity, row.MeasurementId)).Length > JournalLength)
                {
                    result.SkippedAmbiguous++;
                }
                else if (segments.Count == 1)
                {
                    result.SkippedSingleSourceLine++;
                }
                else
                {
                    folds.Add(new Fold(row, segments));
                }
            }

            return folds;
        }

        /// <summary>
        /// The lines a joined row becomes, the row itself first. A later part that repeats an
        /// earlier one without an amount is left out. The stored amount stays on the first line
        /// unless that line states none and a later line states exactly it, in which case it goes
        /// with that line.
        /// </summary>
        private static Lines LinesOf(IReadOnlyList<string> segments, decimal quantity, long measurementId, string? lineKind, Units units)
        {
            var parsed = segments.Select(IngredientLineParser.Parse).ToList();
            var keys = segments.Select(FoldedLine.Key).ToList();
            var kept = new List<int>();
            for (var i = 0; i < segments.Count; i++)
            {
                if (parsed[i] == null && kept.Any(k => keys[k] == keys[i])) continue;
                kept.Add(i);
            }

            var owner = -1;
            if (quantity > 0 && parsed[0] == null && units.Names.TryGetValue(measurementId, out var storedUnit))
            {
                owner = kept.Skip(1).FirstOrDefault(
                    k => parsed[k] is { } p && Math.Round(p.Quantity, 3) == Math.Round(quantity, 3)
                        && p.MeasurementName.Equals(storedUnit, StringComparison.OrdinalIgnoreCase),
                    -1);
            }

            var lines = new List<Line>();
            foreach (var i in kept)
            {
                if (i == 0) lines.Add(new Line(segments[i], owner < 0 ? quantity : 0m, measurementId, lineKind));
                else if (i == owner) lines.Add(new Line(segments[i], quantity, measurementId, null));
                else if (parsed[i] is { } amount && units.Ids.TryGetValue(amount.MeasurementName, out var unitId)) lines.Add(new Line(segments[i], amount.Quantity, unitId, null));
                else lines.Add(new Line(segments[i], 0m, units.Ids.GetValueOrDefault(UnreadMeasurement, measurementId), lineKind));
            }

            return new Lines(lines, segments.Count - kept.Count, owner >= 0);
        }

        private int Write(RecipeIngredientEntity row, Lines lines, int next, long personId)
        {
            var now = DateTime.UtcNow;
            var before = new Before(LineRelinkService.Place(row.IngredientId, row.SortOrder), row.RawLine, row.Quantity, row.MeasurementId);
            var added = new List<int>();

            row.RawLine = lines.Kept[0].RawLine;
            row.Quantity = lines.Kept[0].Quantity;
            row.LastModifiedDate = now;
            row.LastModifiedByPersonId = personId;
            foreach (var line in lines.Kept.Skip(1))
            {
                _db.RecipeIngredients.Add(new RecipeIngredientEntity
                {
                    RecipeId = row.RecipeId,
                    IngredientId = row.IngredientId,
                    SortOrder = next,
                    Quantity = line.Quantity,
                    MeasurementId = line.MeasurementId,
                    RawLine = line.RawLine,
                    LineKind = line.LineKind,
                    CreatedDate = now,
                    CreatedByPersonId = personId,
                });
                added.Add(next++);
            }

            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LineRelinkService.RelinkEntityType,
                EntityId = row.RecipeId,
                ChangeType = UnfoldChangeType,
                PropertyName = nameof(RecipeIngredientEntity.RawLine),
                OldValue = Journal(before),
                NewValue = Journal(new After(added, row.Quantity, row.MeasurementId)),
                Timestamp = now,
                ChangedByPersonId = personId,
            });
            return next;
        }

        private static void Count(LineUnfoldResult result, Row row, Lines lines, Units units, int samples)
        {
            result.Folds++;
            result.LinesAdded += lines.Kept.Count - 1;
            result.DuplicatesDropped += lines.Dropped;
            result.UnmeasuredAdded += lines.Kept.Skip(1).Count(l => l.Quantity == 0);
            if (lines.Kept.Count == 1) result.Collapsed++;
            if (lines.Reassigned) result.QuantitiesReassigned++;
            if (result.Samples.Count >= samples) return;

            result.Samples.Add(new LineUnfoldSample
            {
                RecipeId = row.RecipeId,
                IngredientId = row.IngredientId,
                IngredientName = row.IngredientName,
                RawLine = row.RawLine,
                Lines = lines.Kept
                    .Select(l => new LineUnfoldLine { RawLine = l.RawLine, Quantity = l.Quantity, Measurement = units.Names.GetValueOrDefault(l.MeasurementId, string.Empty) })
                    .ToList(),
            });
        }

        private static string Journal<T>(T value) => JsonSerializer.Serialize(value, Json);

        private static T? Read<T>(string? journal) where T : class
        {
            if (string.IsNullOrEmpty(journal)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(journal, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Puts one row that was taken apart back together from its journal entry: the lines it
        /// added are deleted and the row gets its joined text back, and its amount too when that
        /// is still what the unfold left. False, with nothing changed, when the row or one of the
        /// added lines is no longer where the unfold left it or no longer reads as part of the
        /// joined text.
        /// </summary>
        internal static async Task<bool> RefoldAsync(ApplicationDbContext db, AuditLogEntryEntity entry, long personId)
        {
            if (Read<Before>(entry.OldValue) is not { Raw: not null } before
                || Read<After>(entry.NewValue) is not { Added: not null } after
                || LineRelinkService.ReadPlace(before.At) is not { } at
                || after.Added.Contains(at.SortOrder)
                || after.Added.Distinct().Count() != after.Added.Count) return false;

            var lines = await db.RecipeIngredients
                .Where(ri => ri.RecipeId == entry.EntityId && ri.IngredientId == at.IngredientId
                    && (ri.SortOrder == at.SortOrder || after.Added.Contains(ri.SortOrder)))
                .ToListAsync();
            var row = lines.SingleOrDefault(ri => ri.SortOrder == at.SortOrder);
            var added = lines.Where(ri => ri.SortOrder != at.SortOrder).ToList();
            if (row == null || row.RawLine.Length == 0 || row.RawLine == before.Raw
                || !before.Raw.TrimStart().StartsWith(row.RawLine, StringComparison.Ordinal)
                || added.Count != after.Added.Count
                || added.Any(line => line.RawLine.Length == 0 || !before.Raw.Contains(line.RawLine, StringComparison.Ordinal))) return false;

            foreach (var line in added)
            {
                if (db.Database.IsRelational())
                {
                    await db.Database.ExecuteSqlAsync(
                        $"""DELETE FROM recipe."RecipeIngredient" WHERE "RecipeId" = {line.RecipeId} AND "IngredientId" = {line.IngredientId} AND "SortOrder" = {line.SortOrder}""");
                    db.Entry(line).State = EntityState.Detached;
                }
                else
                {
                    db.RecipeIngredients.Remove(line);
                }
            }

            if (row.Quantity == after.Quantity && row.MeasurementId == after.MeasurementId)
            {
                row.Quantity = before.Quantity;
                row.MeasurementId = before.MeasurementId;
            }

            row.RawLine = before.Raw;
            row.LastModifiedDate = DateTime.UtcNow;
            row.LastModifiedByPersonId = personId;
            return true;
        }
    }
}
