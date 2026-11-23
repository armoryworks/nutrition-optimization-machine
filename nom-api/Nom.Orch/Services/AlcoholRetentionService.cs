using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Stores each alcohol line's retention (<see cref="AlcoholRetention"/>) and recalculates its
    /// recipe so the label counts only the alcohol that stays, and its energy only that alcohol's
    /// calories (alcohol design A7). Every changed line gets an AlcoholRetention journal entry with
    /// its old and new factor and basis; <see cref="UndoAsync"/> gives them back by time, skipping a
    /// line read again since and a recipe rebuilt since. A run writes only where the reading
    /// changes, so it can run again after steps or lines change.
    /// </summary>
    public class AlcoholRetentionService : IAlcoholRetentionService
    {
        public const string ChangeType = "AlcoholRetention";
        public const string LineEntityType = ScrapedRecipeRepairService.LineEntityType;
        public const int Chunk = 200;

        private readonly ApplicationDbContext _db;
        private readonly IRecipeNutritionService _nutrition;
        private readonly ILogger<AlcoholRetentionService> _logger;

        public AlcoholRetentionService(ApplicationDbContext db, IRecipeNutritionService nutrition, ILogger<AlcoholRetentionService> logger)
        {
            _db = db;
            _nutrition = nutrition;
            _logger = logger;
        }

        /// <summary>The journal's property name for a line.</summary>
        public static string PropertyName(long ingredientId, int sortOrder) =>
            string.Create(CultureInfo.InvariantCulture, $"AlcoholRetention[{ingredientId}:{sortOrder}]");

        /// <summary>A line's retention as the journal keeps it ("0.3500|timed:simmer:30"), or empty when it has none.</summary>
        public static string JournalValue(decimal? factor, string? basis) =>
            factor == null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{factor.Value:0.0000}|{basis}");

        /// <summary>The factor and basis of a journal value; (null, null) for an empty one.</summary>
        public static (decimal? Factor, string? Basis) ReadJournalValue(string? value)
        {
            if (string.IsNullOrEmpty(value)) return (null, null);
            var bar = value.IndexOf('|');
            var factor = decimal.Parse(bar < 0 ? value : value[..bar], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return (factor, bar < 0 ? null : value[(bar + 1)..]);
        }

        public async Task<AlcoholRetentionRunResult> RunAsync(bool apply, int maxRecipes = 20000, long afterRecipeId = 0, int sampleSize = 50, CancellationToken cancellationToken = default)
        {
            var stamp = DateTime.UtcNow;
            var result = new AlcoholRetentionRunResult { Applied = apply, Version = AlcoholRetention.Version, StampUtc = stamp };
            var foodIds = await _db.IngredientNutrients.AsNoTracking()
                .Where(n => !n.IsDeleted && n.Amount > 0m && n.Nutrient.Name == DietCriteriaMatcher.AlcoholNutrientName)
                .Select(n => n.IngredientId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (foodIds.Count == 0) return result;

            var recipeIds = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RecipeId > afterRecipeId && foodIds.Contains(ri.IngredientId))
                .Select(ri => ri.RecipeId)
                .Distinct()
                .OrderBy(id => id)
                .Take(Math.Max(1, maxRecipes))
                .ToListAsync(cancellationToken);
            result.Recipes = recipeIds.Count;

            foreach (var chunk in recipeIds.Chunk(Chunk))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ids = chunk.ToList();
                var steps = (await _db.RecipeSteps.AsNoTracking()
                        .Where(st => ids.Contains(st.RecipeId) && !st.IsDeleted)
                        .Select(st => new { st.RecipeId, st.StepNumber, st.Description })
                        .ToListAsync(cancellationToken))
                    .GroupBy(st => st.RecipeId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(st => st.StepNumber).Select(st => st.Description).ToList());
                var lines = await _db.RecipeIngredients
                    .Where(ri => !ri.IsDeleted && ids.Contains(ri.RecipeId) && foodIds.Contains(ri.IngredientId))
                    .ToListAsync(cancellationToken);

                var touched = new HashSet<long>();
                foreach (var line in lines.OrderBy(l => l.RecipeId).ThenBy(l => l.SortOrder))
                {
                    result.Lines++;
                    var reading = AlcoholRetention.Read(steps.GetValueOrDefault(line.RecipeId), line.RawLine);
                    result.ByKind[reading.Kind] = result.ByKind.GetValueOrDefault(reading.Kind) + 1;
                    if (reading.Estimated) result.Estimated++;
                    if (reading.Unnamed) result.Unnamed++;
                    if (line.AlcoholRetention == reading.Factor && line.AlcoholRetentionBasis == reading.Basis) continue;

                    result.Changed++;
                    if (result.Samples.Count < sampleSize)
                    {
                        result.Samples.Add(new AlcoholRetentionSample
                        {
                            RecipeId = line.RecipeId,
                            IngredientId = line.IngredientId,
                            SortOrder = line.SortOrder,
                            Line = line.RawLine ?? string.Empty,
                            OldFactor = line.AlcoholRetention,
                            OldBasis = line.AlcoholRetentionBasis,
                            NewFactor = reading.Factor,
                            NewBasis = reading.Basis,
                            UsdaCode = reading.Code,
                            Estimated = reading.Estimated,
                            Unnamed = reading.Unnamed,
                        });
                    }
                    if (!apply) continue;

                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = LineEntityType,
                        EntityId = line.RecipeId,
                        ChangeType = ChangeType,
                        PropertyName = PropertyName(line.IngredientId, line.SortOrder),
                        OldValue = JournalValue(line.AlcoholRetention, line.AlcoholRetentionBasis),
                        NewValue = JournalValue(reading.Factor, reading.Basis),
                        Timestamp = stamp,
                        ChangedByPersonId = SystemConstants.SystemPersonId,
                    });
                    line.AlcoholRetention = reading.Factor;
                    line.AlcoholRetentionBasis = reading.Basis;
                    line.LastModifiedDate = stamp;
                    line.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    touched.Add(line.RecipeId);
                }

                if (apply && touched.Count > 0)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    foreach (var recipeId in touched.OrderBy(id => id))
                    {
                        await _nutrition.RecalculateAsync(recipeId);
                        result.RecipesRecalculated++;
                    }
                }
                _db.ChangeTracker.Clear();
            }

            _logger.LogInformation("Alcohol retention ({Version}): {Lines} lines in {Recipes} recipes read, {Estimated} estimated ({Unnamed} with the drink named in no step), {Changed} changed, {Recalculated} recipes recalculated{DryRun}",
                AlcoholRetention.Version, result.Lines, result.Recipes, result.Estimated, result.Unnamed, result.Changed, result.RecipesRecalculated, apply ? string.Empty : " (dry run)");
            return result;
        }

        public async Task<AlcoholRetentionUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false, CancellationToken cancellationToken = default)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == LineEntityType && a.ChangeType == ChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            var result = new AlcoholRetentionUndoResult { Applied = apply, Entries = entries.Count };
            var restored = new Dictionary<(long RecipeId, long IngredientId, int SortOrder), (decimal? Factor, string? Basis)>();

            foreach (var chunk in entries.Chunk(500))
            {
                var recipeIds = chunk.Select(e => e.EntityId).Distinct().ToList();
                var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_db, recipeIds);
                var trail = await RecipeOrderTrail.LoadAsync(_db, recipeIds);
                var rows = (await _db.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId) && !ri.IsDeleted).ToListAsync(cancellationToken))
                    .GroupBy(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder))
                    .ToDictionary(g => g.Key, g => g.First());
                var touched = new HashSet<long>();
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (rebuilt.TryGetValue(entry.EntityId, out var rebuiltAt) && rebuiltAt > entry.Timestamp)
                    {
                        result.Skipped++;
                        result.SkippedRebuilt++;
                        continue;
                    }
                    var key = Key(entry.PropertyName);
                    var (newFactor, newBasis) = ReadJournalValue(entry.NewValue);
                    if (key == null || trail.Follow(entry.EntityId, key.Value.IngredientId, key.Value.SortOrder, entry.Timestamp) is not { } at
                        || !rows.TryGetValue((entry.EntityId, key.Value.IngredientId, at), out var row))
                    {
                        result.Skipped++;
                        continue;
                    }
                    var lineKey = (entry.EntityId, key.Value.IngredientId, at);
                    var (currentFactor, currentBasis) = restored.TryGetValue(lineKey, out var earlier) ? earlier : (row.AlcoholRetention, row.AlcoholRetentionBasis);
                    if (currentFactor != newFactor || currentBasis != newBasis)
                    {
                        result.Skipped++;
                        continue;
                    }

                    result.Restored++;
                    var (oldFactor, oldBasis) = ReadJournalValue(entry.OldValue);
                    restored[lineKey] = (oldFactor, oldBasis);
                    trail.Carry(entry.EntityId, entry.Timestamp, key.Value.IngredientId, key.Value.SortOrder, r => r with { AlcoholRetention = oldFactor, AlcoholRetentionBasis = oldBasis });
                    if (!apply) continue;
                    row.AlcoholRetention = oldFactor;
                    row.AlcoholRetentionBasis = oldBasis;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    _db.AuditLogEntries.Remove(entry);
                    touched.Add(entry.EntityId);
                }

                if (apply)
                {
                    await trail.StageAsync(_db);
                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    foreach (var recipeId in touched.OrderBy(id => id))
                    {
                        await _nutrition.RecalculateAsync(recipeId);
                        result.RecipesRecalculated++;
                    }
                }
                _db.ChangeTracker.Clear();
            }
            return result;
        }

        private static (long IngredientId, int SortOrder)? Key(string? propertyName)
        {
            var m = Regex.Match(propertyName ?? string.Empty, @"^AlcoholRetention\[(\d+):(-?\d+)\]$");
            return m.Success ? (long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) : null;
        }
    }
}
