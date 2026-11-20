using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Puts back the piece counts the AI repair lane filled from a quote that states no count. The
    /// fills are read from the lane's own journal (<see cref="RecipeAiRepairService.RepairChangeType"/>
    /// entries named Quantity[ingredient] or Quantity[ingredient:position]); each revert is journaled
    /// in the format of <see cref="PackageLineService"/>, so <see cref="UndoAsync"/> can replay it.
    /// </summary>
    public class AiRepairRevertService : IAiRepairRevertService
    {
        public const string RevertChangeType = "AiRepairRevert";
        public const string RevertEntityType = "RecipeIngredient";

        private const int RecipesPerTransaction = 200;

        private static readonly Regex FillProperty = new(@"^Quantity\[(?<i>\d+)(?::(?<s>-?\d+))?\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FillOld = new(@"^(?<q>\d+(?:\.\d+)?) \(measurement (?<m>\d+)\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FillNew = new(@"^(?<q>\d+(?:\.\d+)?) Piece from ""(?<quote>.*)""$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly int _maxUnquantifiedLines;

        public AiRepairRevertService(ApplicationDbContext db, ICurrentUserService currentUser, IConfiguration? configuration = null)
        {
            _db = db;
            _currentUser = currentUser;
            _maxUnquantifiedLines = Math.Max(0, configuration?.GetValue("Curation:AutoApprove:MaxUnquantifiedLines", 0) ?? 0);
        }

        private sealed record Fill(long EntryId, long RecipeId, long IngredientId, int? SortOrder, decimal OldQuantity, long OldMeasurementId, decimal Quantity, string Quote);
        private sealed record Line(long RecipeId, long IngredientId, int SortOrder, decimal Quantity, long MeasurementId, string RawLine, decimal? GramsPerItem, long RecipeStatusId);
        private sealed record Revert(Fill Fill, Line Line, string Reason, decimal Quantity, long MeasurementId, string? Unit);

        public async Task<QuantityRevertResult> RevertAsync(bool apply = false, int maxLines = 5000, int sampleSize = 50, IReadOnlyCollection<string>? reasons = null)
        {
            var chosen = (reasons ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            var result = new QuantityRevertResult
            {
                Applied = apply,
                Reasons = chosen.OrderBy(r => r, StringComparer.Ordinal).ToList(),
                ReasonMeanings = RecipeRepairGrounding.RefusalReasons.Concat(RecipeRepairGrounding.AcceptanceReasons).ToDictionary(p => p.Key, p => p.Value),
            };
            var pieceId = await _db.Measurements.AsNoTracking()
                .Where(m => !m.IsDeleted && m.Name == IngredientLineParser.Piece)
                .Select(m => (long?)m.Id)
                .MinAsync();
            if (pieceId == null) return result;

            var units = await _db.Measurements.AsNoTracking()
                .Where(m => !m.IsDeleted)
                .GroupBy(m => m.Name)
                .Select(g => new { Name = g.Key, Id = g.Min(m => m.Id) })
                .ToDictionaryAsync(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);
            var fills = await FillsAsync();
            result.FillsExamined = fills.Count;
            var lines = await LinesAsync(fills.Select(f => f.RecipeId));

            var reverts = new List<Revert>();
            var seen = new HashSet<(long, long, int)>();
            var filledFoods = fills.Select(f => (f.RecipeId, f.IngredientId)).ToHashSet();
            foreach (var fill in fills)
            {
                var recipeLines = lines.GetValueOrDefault(fill.RecipeId) ?? new List<Line>();
                var candidates = recipeLines.Where(l => l.IngredientId == fill.IngredientId && (fill.SortOrder == null || l.SortOrder == fill.SortOrder)).ToList();
                var holding = candidates.Where(l => l.Quantity == fill.Quantity && l.MeasurementId == pieceId).ToList();
                if (holding.Count == 0 && recipeLines.All(l => l.IngredientId != fill.IngredientId))
                {
                    var moved = recipeLines.Where(l => l.MeasurementId == pieceId && l.Quantity == fill.Quantity
                            && !filledFoods.Contains((l.RecipeId, l.IngredientId)) && !seen.Contains((l.RecipeId, l.IngredientId, l.SortOrder))
                            && IngredientLineParser.Parse(l.RawLine) is not { MeasurementName: IngredientLineParser.Piece })
                        .ToList();
                    if (moved.Count > 1)
                    {
                        result.Ambiguous++;
                        continue;
                    }
                    if (moved.Count == 1) result.FoundMoved++;
                    holding = moved;
                }
                if (fill.SortOrder == null && holding.Count > 1)
                {
                    result.Ambiguous++;
                    continue;
                }

                if (holding.Count != 1 || !seen.Add((fill.RecipeId, holding[0].IngredientId, holding[0].SortOrder)))
                {
                    result.LineChanged++;
                    continue;
                }

                var line = holding[0];
                if (RecipeRepairGrounding.PieceRefusal(fill.Quote, line.GramsPerItem) is not { } reason)
                {
                    result.Grounded++;
                    var kept = RecipeRepairGrounding.PieceAcceptance(fill.Quote, line.GramsPerItem) ?? string.Empty;
                    result.GroundedByReason[kept] = result.GroundedByReason.GetValueOrDefault(kept) + 1;
                    continue;
                }

                result.Ungrounded++;
                result.ByReason[reason] = result.ByReason.GetValueOrDefault(reason) + 1;
                if (chosen.Count > 0 && !chosen.Contains(reason))
                {
                    result.NotChosen++;
                    continue;
                }
                reverts.Add(IngredientLineParser.Parse(RecipeRepairGrounding.Normalize(fill.Quote).TrimEnd('.', ',', ';', ':')) is { MeasurementName: not IngredientLineParser.Piece, Quantity: > 0 } unit
                    && units.TryGetValue(unit.MeasurementName, out var unitId)
                        ? new Revert(fill, line, reason, unit.Quantity, unitId, unit.MeasurementName)
                        : new Revert(fill, line, reason, fill.OldQuantity, fill.OldMeasurementId, null));
            }

            var recipes = reverts.GroupBy(r => r.Line.RecipeId).ToList();
            result.Recipes = recipes.Count;
            result.RecipesByStatus = recipes
                .GroupBy(g => RecipeLineApproval.StatusName(g.First().Line.RecipeStatusId))
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count());
            var approved = await RecipeLineApproval.ApprovedLinesAsync(_db, recipes.Select(g => g.Key));
            result.ApprovedRecipes = approved.Count;
            result.Reread = reverts.Count(r => r.Unit != null);
            var restored = reverts.ToDictionary(r => (r.Line.RecipeId, r.Line.IngredientId, r.Line.SortOrder), r => r.Quantity);
            result.ApprovedStopPassing = RecipeLineApproval.StopPassing(approved,
                (recipeId, line) => restored.TryGetValue((recipeId, line.IngredientId, line.SortOrder), out var quantity) ? line with { Quantity = quantity } : line,
                _maxUnquantifiedLines);

            var samples = Math.Clamp(sampleSize, 0, 5000);
            if (samples > 0 && reverts.Count > 0)
            {
                var step = Math.Max(1, reverts.Count / samples);
                result.Samples = reverts.Where((_, i) => i % step == 0).Take(samples).Select(r => new QuantityRevertSample
                {
                    RecipeId = r.Line.RecipeId,
                    IngredientId = r.Line.IngredientId,
                    SortOrder = r.Line.SortOrder,
                    Line = r.Line.RawLine,
                    Quote = r.Fill.Quote,
                    FilledQuantity = r.Fill.Quantity,
                    RestoredQuantity = r.Quantity,
                    RestoredUnit = r.Unit,
                    Reason = r.Reason,
                    RecipeStatus = RecipeLineApproval.StatusName(r.Line.RecipeStatusId),
                }).ToList();
            }

            result.Remaining = reverts.Count;
            if (!apply) return result;

            var personId = _currentUser.PersonIdOrSystem;
            foreach (var chunk in reverts.Take(Math.Clamp(maxLines, 1, 50000)).GroupBy(r => r.Line.RecipeId).Chunk(RecipesPerTransaction))
            {
                var recipeIds = chunk.Select(g => g.Key).ToList();
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var rows = (await _db.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId)).ToListAsync())
                    .ToDictionary(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder));
                var now = DateTime.UtcNow;
                foreach (var revert in chunk.SelectMany(g => g))
                {
                    var line = revert.Line;
                    result.Remaining--;
                    if (!rows.TryGetValue((line.RecipeId, line.IngredientId, line.SortOrder), out var row)
                        || row.Quantity != line.Quantity || row.MeasurementId != line.MeasurementId)
                    {
                        result.Skipped++;
                        continue;
                    }

                    row.Quantity = revert.Quantity;
                    row.MeasurementId = revert.MeasurementId;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RevertEntityType,
                        EntityId = line.RecipeId,
                        ChangeType = RevertChangeType,
                        PropertyName = nameof(RecipeIngredientEntity.Quantity),
                        OldValue = PackageLineService.Amount(line.IngredientId, line.SortOrder, line.Quantity, line.MeasurementId),
                        NewValue = PackageLineService.Amount(line.IngredientId, line.SortOrder, row.Quantity, row.MeasurementId),
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    result.Changed++;
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                _db.ChangeTracker.Clear();
            }

            return result;
        }

        /// <summary>The newest journaled piece fill of each line, oldest line first.</summary>
        private async Task<List<Fill>> FillsAsync()
        {
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RevertEntityType && a.ChangeType == RecipeAiRepairService.RepairChangeType
                    && a.PropertyName != null && a.PropertyName.StartsWith("Quantity[") && a.NewValue != null && a.NewValue.Contains(" Piece from "))
                .Select(a => new { a.Id, a.EntityId, a.PropertyName, a.OldValue, a.NewValue })
                .ToListAsync();

            var fills = new List<Fill>();
            foreach (var entry in entries)
            {
                var place = FillProperty.Match(entry.PropertyName ?? string.Empty);
                var old = FillOld.Match(entry.OldValue ?? string.Empty);
                var filled = FillNew.Match(entry.NewValue ?? string.Empty);
                if (!place.Success || !old.Success || !filled.Success) continue;

                fills.Add(new Fill(
                    entry.Id,
                    entry.EntityId,
                    long.Parse(place.Groups["i"].Value, CultureInfo.InvariantCulture),
                    place.Groups["s"].Success ? int.Parse(place.Groups["s"].Value, CultureInfo.InvariantCulture) : null,
                    decimal.Parse(old.Groups["q"].Value, CultureInfo.InvariantCulture),
                    long.Parse(old.Groups["m"].Value, CultureInfo.InvariantCulture),
                    decimal.Parse(filled.Groups["q"].Value, CultureInfo.InvariantCulture),
                    filled.Groups["quote"].Value));
            }

            return fills
                .GroupBy(f => (f.RecipeId, f.IngredientId, f.SortOrder))
                .Select(g => g.OrderByDescending(f => f.EntryId).First())
                .OrderByDescending(f => f.EntryId)
                .ToList();
        }

        private async Task<Dictionary<long, List<Line>>> LinesAsync(IEnumerable<long> recipeIds)
        {
            var result = new Dictionary<long, List<Line>>();
            foreach (var chunk in recipeIds.Distinct().Chunk(500))
            {
                var rows = await _db.RecipeIngredients.AsNoTracking()
                    .Where(ri => chunk.Contains(ri.RecipeId) && !ri.IsDeleted && !ri.Recipe.IsDeleted)
                    .Select(ri => new Line(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine, ri.Ingredient.GramsPerItem, ri.Recipe.CurationStatusId))
                    .ToListAsync();
                foreach (var group in rows.GroupBy(r => r.RecipeId)) result[group.Key] = group.ToList();
            }

            return result;
        }

        public async Task<LineCorrectionUndoResult> UndoAsync(DateTime sinceUtc)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == RevertEntityType && a.ChangeType == RevertChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var result = new LineCorrectionUndoResult();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(500))
            {
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var recipeIds = chunk.Select(e => e.EntityId).Distinct().ToList();
                var rows = (await _db.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId)).ToListAsync())
                    .ToDictionary(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder));
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (PackageLineService.ReadAmount(entry.OldValue) is not { } was || PackageLineService.ReadAmount(entry.NewValue) is not { } became
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
                    _db.AuditLogEntries.Remove(entry);
                    result.Restored++;
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }

            return result;
        }
    }
}
