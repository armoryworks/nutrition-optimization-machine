using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Re-reads the scraped recipe lines stored as pieces, cans or packages. A line changes when
    /// <see cref="IngredientLineParser.ReadsPackage"/> holds for its wording and the parser gives
    /// it another quantity or measurement than it has: the size its can or package states, or
    /// Package for a jar, bottle, box or bag that was counted as a piece. A line on a food weighed
    /// drained (<see cref="WeighsDrained"/>) is never given its can's stated size, which is the net
    /// weight with the liquid: it is read as that many cans or packages instead.
    /// </summary>
    public class PackageLineService : IPackageLineService
    {
        public const string RereadChangeType = "LineReread";
        public const string RereadEntityType = "RecipeIngredient";

        private const long CategoryMass = 1L;
        private const long CategoryVolume = 2L;
        private const decimal SameQuantity = 0.0005m;
        private const string DrainedWord = "drained";

        private static readonly string[] CandidateMeasurements = { IngredientLineParser.Piece, IngredientLineParser.Can, IngredientLineParser.Package };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public PackageLineService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Unit(long Id, string Name, long CategoryId, bool IsContainer);
        private sealed record Line(
            long RecipeId, long IngredientId, int SortOrder, decimal Quantity, long MeasurementId, string RawLine, DateTime ModifiedUtc,
            bool HasNutrition, bool HasItemWeight, bool HasDensity, bool Drained);
        private sealed record Change(Line Line, string First, Unit From, decimal Quantity, Unit To);
        private sealed record Plan(int Examined, List<Change> Changes, List<Line> Touches, int StopCounting, int StartCounting, List<string> Missing, int Drained);

        /// <summary>
        /// Whether a food's values are for its drained solids ("Chickpeas ..., canned, drained,
        /// rinsed in tap water", "Beans, pinto, canned, drained solids"), so the net weight a can
        /// states is not the weight of the food.
        /// </summary>
        public static bool WeighsDrained(string? foodName) => foodName != null && foodName.Contains(DrainedWord, StringComparison.OrdinalIgnoreCase);

        private static readonly Regex SaysDrained = new(@"(?<!\bnot\s)\bdrained\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PackedInLiquid = new(@"\b(?:cans?|tins?|jars?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether a line's stated size is a can's or jar's net weight with its liquid while the
        /// food is used drained, so the line counts cans or jars instead: the food's values are for
        /// its drained solids (<see cref="WeighsDrained"/>) or the line itself says "drained" (not
        /// "not drained"), and the line names a can, tin or jar. A frozen or dry package is weighed
        /// as it is sold.
        /// </summary>
        public static bool CountsDrained(string? foodName, string? rawLine) =>
            rawLine != null && (WeighsDrained(foodName) || SaysDrained.IsMatch(rawLine)) && PackedInLiquid.IsMatch(rawLine);

        private async Task<Plan> PlanAsync()
        {
            var units = (await _db.Measurements.AsNoTracking()
                    .Where(m => !m.IsDeleted)
                    .Select(m => new Unit(m.Id, m.Name, m.MeasurementCategoryId, m.IsContainer))
                    .ToListAsync())
                .GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(u => u.Id).First(), StringComparer.OrdinalIgnoreCase);
            var stored = CandidateMeasurements.Where(units.ContainsKey).Select(name => units[name]).ToDictionary(u => u.Id);
            var storedIds = stored.Keys.ToList();

            var lines = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RawLine != "" && ri.Quantity > 0
                    && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient)
                    && storedIds.Contains(ri.MeasurementId)
                    && !ri.Recipe.IsDeleted && ri.Recipe.ScrapedAtUtc != null)
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new Line(
                    ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine, ri.LastModifiedDate ?? ri.CreatedDate,
                    ri.Ingredient.IngredientNutrients.Any(),
                    ri.Ingredient.GramsPerItem > 0 || ri.Ingredient.ReferenceServingGrams > 0,
                    ri.Ingredient.GramsPerMilliliter > 0,
                    ri.Ingredient.Name.ToLower().Contains(DrainedWord)))
                .ToListAsync();

            var firstReread = await _db.AuditLogEntries
                .Where(a => a.EntityType == RereadEntityType && a.ChangeType == RereadChangeType)
                .MinAsync(a => (DateTime?)a.Timestamp);

            var read = new Dictionary<string, ParsedQuantity?>(StringComparer.Ordinal);
            var changes = new List<Change>();
            var touches = new List<Line>();
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            int stop = 0, start = 0, drained = 0;
            foreach (var line in lines)
            {
                var from = stored[line.MeasurementId];
                var first = line.RawLine.Split(" + ", 2)[0];
                if (!read.TryGetValue(first, out var parsed))
                {
                    parsed = IngredientLineParser.Parse(first);
                    if (parsed != null && (parsed.MeasurementName == IngredientLineParser.Piece || !IngredientLineParser.ReadsPackage(first))) parsed = null;
                    read[first] = parsed;
                }
                if ((line.Drained || SaysDrained.IsMatch(first)) && PackedInLiquid.IsMatch(first) && parsed != null && IngredientLineParser.StatedSize(first) != null)
                {
                    parsed = IngredientLineParser.SizedContainers(first);
                    drained++;
                }

                var weighedAsItem = line.HasNutrition && line.HasItemWeight;
                Unit? to = null;
                if (parsed != null && !units.TryGetValue(parsed.MeasurementName, out to)) missing.Add(parsed.MeasurementName);
                if (to == null || (to.Id == from.Id && Math.Abs(parsed!.Quantity - line.Quantity) < SameQuantity))
                {
                    if (from.IsContainer && weighedAsItem && (firstReread == null || line.ModifiedUtc < firstReread))
                    {
                        touches.Add(line);
                        stop++;
                    }
                    continue;
                }

                changes.Add(new Change(line, first, from, parsed!.Quantity, to));
                var weighedAfter = line.HasNutrition && (to.CategoryId == CategoryMass
                    || (to.CategoryId == CategoryVolume ? line.HasDensity : !to.IsContainer && line.HasItemWeight));
                if (weighedAsItem && !weighedAfter) stop++;
                if (!weighedAsItem && weighedAfter) start++;
            }

            return new Plan(lines.Count, changes, touches, stop, start, missing.ToList(), drained);
        }

        public async Task<PackageLinePreview> PreviewAsync(int sampleSize = 50)
        {
            var plan = await PlanAsync();
            var step = Math.Max(1, plan.Changes.Count / Math.Clamp(sampleSize, 1, 5000));
            return new PackageLinePreview
            {
                LinesExamined = plan.Examined,
                ChangeCount = plan.Changes.Count,
                TouchCount = plan.Touches.Count,
                StopCounting = plan.StopCounting,
                StartCounting = plan.StartCounting,
                MissingMeasurements = plan.Missing,
                DrainedAsContainers = plan.Drained,
                Transitions = plan.Changes
                    .GroupBy(c => (From: c.From.Name, To: c.To.Name))
                    .Select(g => new PackageLineTransition { FromMeasurement = g.Key.From, ToMeasurement = g.Key.To, Lines = g.Count() })
                    .OrderByDescending(t => t.Lines).ThenBy(t => t.FromMeasurement, StringComparer.Ordinal).ThenBy(t => t.ToMeasurement, StringComparer.Ordinal)
                    .ToList(),
                Samples = plan.Changes
                    .Where((_, index) => index % step == 0)
                    .Take(Math.Clamp(sampleSize, 1, 5000))
                    .Select(c => new PackageLineChange
                    {
                        RecipeId = c.Line.RecipeId,
                        IngredientId = c.Line.IngredientId,
                        SortOrder = c.Line.SortOrder,
                        Line = c.First,
                        FromQuantity = c.Line.Quantity,
                        FromMeasurement = c.From.Name,
                        ToQuantity = c.Quantity,
                        ToMeasurement = c.To.Name,
                    })
                    .ToList(),
            };
        }

        public async Task<PackageLineResult> ApplyAsync(int maxLines = 5000) => await ApplyAsync(maxLines, null);

        /// <summary>
        /// Applies the re-read to the given lines only (recipe, ingredient, position), or to every
        /// line when none are given. The label review uses it for the lines it returns to
        /// ingredient, which the re-read skipped while they were labelled.
        /// </summary>
        internal async Task<PackageLineResult> ApplyAsync(int maxLines, IReadOnlySet<(long RecipeId, long IngredientId, int SortOrder)>? only)
        {
            var plan = await PlanAsync();
            if (only != null)
            {
                bool Chosen(Line line) => only.Contains((line.RecipeId, line.IngredientId, line.SortOrder));
                plan = plan with { Changes = plan.Changes.Where(c => Chosen(c.Line)).ToList(), Touches = plan.Touches.Where(Chosen).ToList() };
            }
            var result = new PackageLineResult { Remaining = plan.Changes.Count };
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in plan.Changes.Take(Math.Clamp(maxLines, 1, 50000)).Chunk(500))
            {
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var rows = await RowsAsync(chunk.Select(c => c.Line));
                var now = DateTime.UtcNow;
                foreach (var change in chunk)
                {
                    var line = change.Line;
                    if (!rows.TryGetValue((line.RecipeId, line.IngredientId, line.SortOrder), out var row)
                        || row.Quantity != line.Quantity || row.MeasurementId != line.MeasurementId)
                    {
                        result.Skipped++;
                        continue;
                    }

                    row.Quantity = change.Quantity;
                    row.MeasurementId = change.To.Id;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RereadEntityType,
                        EntityId = line.RecipeId,
                        ChangeType = RereadChangeType,
                        PropertyName = nameof(RecipeIngredientEntity.Quantity),
                        OldValue = Amount(line.IngredientId, line.SortOrder, line.Quantity, line.MeasurementId),
                        NewValue = Amount(line.IngredientId, line.SortOrder, row.Quantity, row.MeasurementId),
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    result.Changed++;
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                _db.ChangeTracker.Clear();
                result.Remaining -= chunk.Length;
            }

            foreach (var chunk in plan.Touches.Chunk(500))
            {
                var rows = await RowsAsync(chunk);
                var now = DateTime.UtcNow;
                foreach (var line in chunk)
                {
                    if (!rows.TryGetValue((line.RecipeId, line.IngredientId, line.SortOrder), out var row) || row.MeasurementId != line.MeasurementId) continue;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                    result.Touched++;
                }

                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }

            return result;
        }

        public async Task<PackageLineUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == RereadEntityType && a.ChangeType == RereadChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var result = new PackageLineUndoResult { Applied = apply };
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(500))
            {
                await using var tx = apply ? await _db.Database.BeginTransactionAsync() : null;
                var recipeIds = chunk.Select(e => e.EntityId).Distinct().ToList();
                var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_db, recipeIds);
                var rows = (await _db.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId)).ToListAsync())
                    .ToDictionary(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder));
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (rebuilt.TryGetValue(entry.EntityId, out var rebuiltAt) && rebuiltAt > entry.Timestamp)
                    {
                        result.Skipped++;
                        result.SkippedRebuilt++;
                        continue;
                    }

                    if (ReadAmount(entry.OldValue) is not { } was || ReadAmount(entry.NewValue) is not { } became
                        || !rows.TryGetValue((entry.EntityId, became.IngredientId, became.SortOrder), out var row)
                        || row.Quantity != became.Quantity || row.MeasurementId != became.MeasurementId)
                    {
                        result.Skipped++;
                        continue;
                    }

                    row.Quantity = was.Quantity;
                    row.MeasurementId = was.MeasurementId;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                    if (apply) _db.AuditLogEntries.Remove(entry);
                    result.Restored++;
                }

                if (tx == null) continue;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            if (!apply) _db.ChangeTracker.Clear();
            return result;
        }

        private async Task<Dictionary<(long, long, int), RecipeIngredientEntity>> RowsAsync(IEnumerable<Line> lines)
        {
            var recipeIds = lines.Select(l => l.RecipeId).Distinct().ToList();
            return (await _db.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId)).ToListAsync())
                .ToDictionary(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder));
        }

        internal static string Amount(long ingredientId, int sortOrder, decimal quantity, long measurementId) =>
            string.Create(CultureInfo.InvariantCulture, $"{LineRelinkService.Place(ingredientId, sortOrder)}|{quantity:0.####}|{measurementId}");

        internal static (long IngredientId, int SortOrder, decimal Quantity, long MeasurementId)? ReadAmount(string? value)
        {
            var parts = (value ?? string.Empty).Split('|');
            return parts.Length == 3
                && LineRelinkService.ReadPlace(parts[0]) is { } place
                && decimal.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var measurementId)
                ? (place.IngredientId, place.SortOrder, quantity, measurementId)
                : null;
        }
    }
}
