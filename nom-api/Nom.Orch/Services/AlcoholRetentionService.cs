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
    /// changes, so it can run again after steps or lines change. An apply writes in batches of
    /// <see cref="Chunk"/> recipes, each in its own transaction with its lines, their journal
    /// entries (stamped with the batch's time), its recipes' labels and a batch mark under the
    /// run's stamp (staging checkpoint 8, T15): a failure or a restart leaves whole batches only,
    /// and a run with resume goes on after the last one under the same stamp.
    /// </summary>
    public class AlcoholRetentionService : IAlcoholRetentionService
    {
        public const string ChangeType = "AlcoholRetention";
        public const string LineEntityType = ScrapedRecipeRepairService.LineEntityType;
        public const int Chunk = 200;

        /// <summary>The journal's entity type of a run's marks: one per committed batch (<see cref="BatchChangeType"/>) and one at its end (<see cref="DoneChangeType"/>), each with the run's stamp as OldValue.</summary>
        public const string RunEntityType = "AlcoholRetentionRun";
        public const string BatchChangeType = "AlcoholRetentionBatch";
        public const string DoneChangeType = "AlcoholRetentionDone";

        private readonly ApplicationDbContext _db;
        private readonly IRecipeNutritionService _nutrition;
        private readonly ILogger<AlcoholRetentionService> _logger;

        public AlcoholRetentionService(ApplicationDbContext db, IRecipeNutritionService nutrition, ILogger<AlcoholRetentionService> logger)
        {
            _db = db;
            _nutrition = nutrition;
            _logger = logger;
        }

        /// <summary>A hook a test sets to fail an apply's batch (the batch's index) before it writes anything.</summary>
        internal Func<int, Task>? BeforeBatchWrite { get; set; }

        /// <summary>A hook a test sets to fail an apply's batch (the batch's index) after its writes and before its commit.</summary>
        internal Func<int, Task>? BeforeBatchCommit { get; set; }

        /// <summary>The journal's property name for a line.</summary>
        public static string PropertyName(long ingredientId, int sortOrder) =>
            string.Create(CultureInfo.InvariantCulture, $"AlcoholRetention[{ingredientId}:{sortOrder}]");

        /// <summary>A line's retention as the journal keeps it ("0.3500|timed:simmer:30"), or empty when it has none.</summary>
        public static string JournalValue(decimal? factor, string? basis) =>
            factor == null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{factor.Value:0.0000}|{basis}");

        /// <summary>The factor and basis of a journal value; (null, null) for an empty one.</summary>
        public static (decimal? Factor, string? Basis) ReadJournalValue(string? value) =>
            TryReadJournalValue(value, out var read) ? read : throw new FormatException($"'{value}' is not a retention journal value.");

        /// <summary>
        /// The factor and basis of a journal value, or false when another step rewrote it into something else, such as the
        /// marker a purge leaves in place of a purged recipe's journal values (staging checkpoint 9, U18).
        /// </summary>
        public static bool TryReadJournalValue(string? value, out (decimal? Factor, string? Basis) read)
        {
            read = (null, null);
            if (string.IsNullOrEmpty(value)) return true;
            var bar = value.IndexOf('|');
            if (!decimal.TryParse(bar < 0 ? value : value[..bar], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var factor)) return false;
            read = (factor, bar < 0 ? null : value[(bar + 1)..]);
            return true;
        }

        public async Task<AlcoholRetentionRunResult> RunAsync(bool apply, int maxRecipes = 20000, long afterRecipeId = 0, int sampleSize = 50, bool resume = false, CancellationToken cancellationToken = default)
        {
            var stamp = DateTime.UtcNow;
            var result = new AlcoholRetentionRunResult { Applied = apply, Version = AlcoholRetention.Version, StampUtc = stamp };
            if (resume && await UnfinishedRunAsync(cancellationToken) is { } unfinished)
            {
                stamp = unfinished.Stamp;
                result.StampUtc = stamp;
                result.ResumedRunUtc = stamp;
                afterRecipeId = Math.Max(afterRecipeId, unfinished.LastRecipeId);
            }
            var foodIds = await _db.IngredientNutrients.AsNoTracking()
                .Where(n => !n.IsDeleted && n.Amount > 0m && n.Nutrient.Name == DietCriteriaMatcher.AlcoholNutrientName)
                .Select(n => n.IngredientId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (foodIds.Count == 0)
            {
                result.Completed = true;
                return result;
            }

            var recipeIds = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RecipeId > afterRecipeId && foodIds.Contains(ri.IngredientId))
                .Select(ri => ri.RecipeId)
                .Distinct()
                .OrderBy(id => id)
                .Take(Math.Max(1, maxRecipes))
                .ToListAsync(cancellationToken);
            result.Recipes = recipeIds.Count;

            var index = 0;
            var committed = afterRecipeId;
            foreach (var chunk in recipeIds.Chunk(Chunk))
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Batches++;
                AlcoholRetentionRunResult part = new();
                Exception? failed = null;
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    part = new AlcoholRetentionRunResult();
                    try
                    {
                        await BatchAsync(chunk, foodIds, apply, stamp, index, part, Math.Max(0, sampleSize - result.Samples.Count), cancellationToken);
                        failed = null;
                        break;
                    }
                    catch (Exception ex) when (apply && ex is not OperationCanceledException)
                    {
                        failed = ex;
                        _db.ChangeTracker.Clear();
                        _logger.LogWarning(ex, "Alcohol retention ({Version}): batch {Batch} (recipes {First} to {Last}) failed on attempt {Attempt}; it wrote nothing", AlcoholRetention.Version, index, chunk[0], chunk[^1], attempt);
                    }
                }
                if (failed != null)
                {
                    result.Error = failed.GetType().Name + ": " + failed.Message;
                    result.ResumeAfterRecipeId = committed;
                    _logger.LogError("Alcohol retention ({Version}) stopped at batch {Batch}: {Error}; {Written} batches committed; resume after recipe {Resume} under run {Stamp:o}",
                        AlcoholRetention.Version, index, result.Error, result.BatchesWritten, committed, stamp);
                    return result;
                }
                Add(result, part);
                if (apply && part.Changed > 0) result.BatchesWritten++;
                committed = chunk[^1];
                index++;
            }

            if (apply && recipeIds.Count > 0)
            {
                _db.AuditLogEntries.Add(RunMark(DoneChangeType, recipeIds[^1], stamp, DateTime.UtcNow, null));
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }
            result.Completed = true;
            _logger.LogInformation("Alcohol retention ({Version}): {Lines} lines in {Recipes} recipes read in {Batches} batches, {Estimated} estimated ({Unnamed} with the drink named in no step), {UpperBound} upper bounds in {RecipesUpperBound} recipes, {Changed} changed in {Touched} recipes, {Recalculated} recipes recalculated{DryRun}",
                AlcoholRetention.Version, result.Lines, result.Recipes, result.Batches, result.Estimated, result.Unnamed, result.UpperBound, result.RecipesUpperBound, result.Changed, result.RecipesTouched, result.RecipesRecalculated, apply ? string.Empty : " (dry run)");
            return result;
        }

        /// <summary>
        /// Reads one batch of recipes and, on an apply, writes it in one transaction: each changed line with its journal entry
        /// (stamped with the batch's own time), the recalculated labels of the recipes it touched, and the batch's mark under the
        /// run's stamp, so a stop leaves whole batches only and a resumed run knows where to go on.
        /// </summary>
        private async Task BatchAsync(long[] chunk, List<long> foodIds, bool apply, DateTime runStamp, int index, AlcoholRetentionRunResult result, int samplesLeft, CancellationToken cancellationToken)
        {
            var ids = chunk.ToList();
            var steps = (await _db.RecipeSteps.AsNoTracking()
                    .Where(st => ids.Contains(st.RecipeId) && !st.IsDeleted)
                    .Select(st => new { st.RecipeId, st.StepNumber, st.Description })
                    .ToListAsync(cancellationToken))
                .GroupBy(st => st.RecipeId)
                .ToDictionary(g => g.Key, g => g.OrderBy(st => st.StepNumber).Select(st => st.Description).ToList());
            await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
            var lines = await _db.RecipeIngredients
                .Where(ri => !ri.IsDeleted && ids.Contains(ri.RecipeId) && foodIds.Contains(ri.IngredientId))
                .ToListAsync(cancellationToken);

            var stamp = DateTime.UtcNow;
            if (stamp < runStamp) stamp = runStamp;
            var touched = new HashSet<long>();
            var bounded = new HashSet<long>();
            var ordered = lines.OrderBy(l => l.RecipeId).ThenBy(l => l.SortOrder).ToList();
            var readings = ordered.GroupBy(l => l.RecipeId)
                .SelectMany(g => g.Zip(AlcoholRetention.ReadRecipe(steps.GetValueOrDefault(g.Key), g.Select(l => l.RawLine).ToList())))
                .ToList();
            foreach (var (line, reading) in readings)
            {
                result.Lines++;
                result.ByKind[reading.Kind] = result.ByKind.GetValueOrDefault(reading.Kind) + 1;
                if (reading.Estimated) result.Estimated++;
                if (reading.Unnamed) result.Unnamed++;
                if (reading.UpTo)
                {
                    result.UpperBound++;
                    bounded.Add(line.RecipeId);
                    var reason = reading.Reason ?? "time";
                    result.ByReason[reason] = result.ByReason.GetValueOrDefault(reason) + 1;
                }
                if (line.AlcoholRetention == reading.Factor && line.AlcoholRetentionBasis == reading.Basis) continue;

                result.Changed++;
                touched.Add(line.RecipeId);
                if (result.Samples.Count < samplesLeft)
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
                        Reason = reading.UpTo ? reading.Reason ?? "time" : null,
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
            }

            result.RecipesUpperBound += bounded.Count;
            result.RecipesTouched += touched.Count;
            if (apply && touched.Count > 0)
            {
                if (BeforeBatchWrite != null) await BeforeBatchWrite(index);
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                await _nutrition.RecalculateManyAsync(touched.OrderBy(id => id).ToList());
                result.RecipesRecalculated += touched.Count;
                _db.ChangeTracker.Clear();
                _db.AuditLogEntries.Add(RunMark(BatchChangeType, chunk[^1], runStamp, stamp, string.Create(CultureInfo.InvariantCulture, $"{index}|{chunk[0]}-{chunk[^1]}|{result.Changed}")));
                await _db.SaveChangesAsync(cancellationToken);
                if (BeforeBatchCommit != null) await BeforeBatchCommit(index);
                if (tx != null) await tx.CommitAsync(cancellationToken);
            }
            _db.ChangeTracker.Clear();
        }

        private static void Add(AlcoholRetentionRunResult into, AlcoholRetentionRunResult part)
        {
            into.Lines += part.Lines;
            into.Changed += part.Changed;
            into.Estimated += part.Estimated;
            into.Unnamed += part.Unnamed;
            into.UpperBound += part.UpperBound;
            into.RecipesUpperBound += part.RecipesUpperBound;
            into.RecipesTouched += part.RecipesTouched;
            into.RecipesRecalculated += part.RecipesRecalculated;
            foreach (var (kind, n) in part.ByKind) into.ByKind[kind] = into.ByKind.GetValueOrDefault(kind) + n;
            foreach (var (reason, n) in part.ByReason) into.ByReason[reason] = into.ByReason.GetValueOrDefault(reason) + n;
            into.Samples.AddRange(part.Samples);
        }

        /// <summary>The journal entry that marks a committed batch, or the end of a run, under the run's stamp ("o").</summary>
        private static AuditLogEntryEntity RunMark(string changeType, long lastRecipeId, DateTime runStamp, DateTime at, string? detail) => new()
        {
            EntityType = RunEntityType,
            EntityId = lastRecipeId,
            ChangeType = changeType,
            PropertyName = "Run",
            OldValue = runStamp.ToString("o", CultureInfo.InvariantCulture),
            NewValue = detail,
            Timestamp = at,
            ChangedByPersonId = SystemConstants.SystemPersonId,
        };

        private sealed record Unfinished(DateTime Stamp, long LastRecipeId);

        /// <summary>The latest run whose batches were marked and whose end was not: the run a resume goes on with.</summary>
        private async Task<Unfinished?> UnfinishedRunAsync(CancellationToken cancellationToken)
        {
            var last = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RunEntityType && (a.ChangeType == BatchChangeType || a.ChangeType == DoneChangeType))
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.ChangeType, a.OldValue })
                .FirstOrDefaultAsync(cancellationToken);
            if (last == null || last.ChangeType == DoneChangeType || last.OldValue == null) return null;
            var run = last.OldValue;
            var lastRecipe = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RunEntityType && a.ChangeType == BatchChangeType && a.OldValue == run)
                .MaxAsync(a => a.EntityId, cancellationToken);
            return new Unfinished(DateTime.Parse(run, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), lastRecipe);
        }

        public async Task<AlcoholRetentionUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false, CancellationToken cancellationToken = default)
        {
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == LineEntityType && a.ChangeType == ChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            var result = new AlcoholRetentionUndoResult { Applied = apply, Entries = entries.Count };
            var restored = new Dictionary<(long RecipeId, long IngredientId, int SortOrder), (decimal? Factor, string? Basis)>();
            var purged = new HashSet<long>();
            foreach (var chunk in entries.Select(e => e.EntityId).Distinct().Chunk(5000))
                purged.UnionWith(await _db.RecipeRemovals.AsNoTracking().Where(r => chunk.Contains(r.RecipeId) && r.PurgedAtUtc != null).Select(r => r.RecipeId).ToListAsync(cancellationToken));

            foreach (var chunk in entries.Chunk(500))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                var recipeIds = chunk.Select(e => e.EntityId).Distinct().ToList();
                var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_db, recipeIds);
                var trail = await RecipeOrderTrail.LoadAsync(_db, recipeIds);
                var rows = (await _db.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId) && !ri.IsDeleted).ToListAsync(cancellationToken))
                    .GroupBy(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder))
                    .ToDictionary(g => g.Key, g => g.First());
                var touched = new HashSet<long>();
                var taken = new List<long>();
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (purged.Contains(entry.EntityId) || IsPurgedValue(entry.OldValue) || IsPurgedValue(entry.NewValue))
                    {
                        Skip(result, PurgedSkip, entry.EntityId);
                        continue;
                    }
                    if (rebuilt.TryGetValue(entry.EntityId, out var rebuiltAt) && rebuiltAt > entry.Timestamp)
                    {
                        result.Skipped++;
                        result.SkippedRebuilt++;
                        continue;
                    }
                    var key = Key(entry.PropertyName);
                    if (!TryReadJournalValue(entry.NewValue, out var written) || !TryReadJournalValue(entry.OldValue, out var earlierValue))
                    {
                        Skip(result, UnreadableSkip, entry.EntityId);
                        continue;
                    }
                    var (newFactor, newBasis) = written;
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
                    var (oldFactor, oldBasis) = earlierValue;
                    restored[lineKey] = (oldFactor, oldBasis);
                    trail.Carry(entry.EntityId, entry.Timestamp, key.Value.IngredientId, key.Value.SortOrder, r => r with { AlcoholRetention = oldFactor, AlcoholRetentionBasis = oldBasis });
                    if (!apply) continue;
                    row.AlcoholRetention = oldFactor;
                    row.AlcoholRetentionBasis = oldBasis;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    taken.Add(entry.Id);
                    touched.Add(entry.EntityId);
                }

                if (apply)
                {
                    await trail.StageAsync(_db);
                    _db.AuditLogEntries.RemoveRange(await _db.AuditLogEntries.Where(a => taken.Contains(a.Id)).ToListAsync(cancellationToken));
                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    if (touched.Count > 0) await _nutrition.RecalculateManyAsync(touched.OrderBy(id => id).ToList());
                    result.RecipesRecalculated += touched.Count;
                    if (tx != null) await tx.CommitAsync(cancellationToken);
                }
                _db.ChangeTracker.Clear();
            }

            if (apply)
            {
                var marks = await _db.AuditLogEntries.Where(a => a.EntityType == RunEntityType && a.Timestamp >= sinceUtc).ToListAsync(cancellationToken);
                result.RunMarksRemoved = marks.Count;
                _db.AuditLogEntries.RemoveRange(marks);
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }
            else
            {
                result.RunMarksRemoved = await _db.AuditLogEntries.CountAsync(a => a.EntityType == RunEntityType && a.Timestamp >= sinceUtc, cancellationToken);
            }
            return result;
        }

        /// <summary>Why an entry of a recipe whose source wording a purge deleted for good is skipped, as the retire undo names it.</summary>
        public const string PurgedSkip = "purged: its source wording is deleted for good, so it cannot be put back";

        /// <summary>Why an entry whose old or new value another step rewrote into something that is no retention is skipped.</summary>
        public const string UnreadableSkip = "its journal value was rewritten by another step";

        private static bool IsPurgedValue(string? value) => value != null && value.StartsWith(RecipeRemovalService.PurgedValuePrefix, StringComparison.Ordinal);

        private static void Skip(AlcoholRetentionUndoResult result, string reason, long recipeId)
        {
            result.Skipped++;
            result.SkippedByReason[reason] = result.SkippedByReason.GetValueOrDefault(reason) + 1;
            if (reason == PurgedSkip && !result.PurgedRecipeIds.Contains(recipeId) && result.PurgedRecipeIds.Count < 500) result.PurgedRecipeIds.Add(recipeId);
        }

        private static (long IngredientId, int SortOrder)? Key(string? propertyName)
        {
            var m = Regex.Match(propertyName ?? string.Empty, @"^AlcoholRetention\[(\d+):(-?\d+)\]$");
            return m.Success ? (long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) : null;
        }
    }
}
