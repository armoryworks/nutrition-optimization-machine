using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The queue of lines the line guard held (<see cref="LineGuardHoldEntity"/>). A writer records the holds it
    /// found on the recipes it read: an open hold of the same line and writer is updated, a new one is added, and
    /// an open hold of a line the writer no longer holds is resolved. A line is the same line by its food and its
    /// source text wherever it stands: a hold follows its line to a new position (an order pass moved it) unless
    /// another open hold stands there or another held line takes its old place, and only then is a line matched
    /// by its position alone. Nothing is saved here; the writer saves with its own changes.
    /// </summary>
    public static class LineGuardHolds
    {
        public const string Released = "released";
        public const string Rebuilt = "rebuilt";
        public const string Undone = "undone";

        /// <summary>One held line: the stored row and the rebuilt row, the source line and the guard's reason.</summary>
        public sealed record Held(
            long RecipeId, long IngredientId, int SortOrder, string RawLine, string Reason,
            string? StoredFood, decimal StoredQuantity, string? StoredUnit, string? StoredKind,
            long? RebuiltIngredientId, string? RebuiltFood, decimal RebuiltQuantity, string? RebuiltUnit, string? RebuiltKind, bool RebuiltFromMethod);

        /// <summary>
        /// Records <paramref name="holds"/> of <paramref name="origin"/> on <paramref name="recipeIds"/>, the recipes
        /// the writer read; an open hold on those recipes of an origin in <paramref name="resolve"/> (the writer's own
        /// and any it supersedes, each with the resolution it gets) that is not held again is resolved.
        /// </summary>
        public static async Task RecordAsync(
            ApplicationDbContext db, string origin, IReadOnlyCollection<long> recipeIds, IReadOnlyCollection<Held> holds, string ruleVersion, DateTime now,
            IReadOnlyDictionary<string, string> resolve, CancellationToken cancellationToken = default)
        {
            if (recipeIds.Count == 0) return;
            var ids = recipeIds.ToList();
            var origins = resolve.Keys.Append(origin).Distinct().ToList();
            var open = await db.LineGuardHolds
                .Where(h => ids.Contains(h.RecipeId) && h.ResolvedAt == null && origins.Contains(h.Origin))
                .ToListAsync(cancellationToken);
            var own = open.Where(h => h.Origin == origin).ToList();
            var byKey = own.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
            var occupied = own.Select(Key).ToHashSet();
            var mine = holds.Where(h => ids.Contains(h.RecipeId)).ToList();
            var targeted = mine.Select(h => (h.RecipeId, h.IngredientId, h.SortOrder)).ToHashSet();
            var kept = new HashSet<LineGuardHoldEntity>(ReferenceEqualityComparer.Instance);
            var matched = new Dictionary<Held, LineGuardHoldEntity>(ReferenceEqualityComparer.Instance);

            foreach (var hold in mine)
            {
                if (byKey.TryGetValue((hold.RecipeId, hold.IngredientId, hold.SortOrder), out var at) && at.RawLine == hold.RawLine && kept.Add(at)) matched[hold] = at;
            }
            foreach (var hold in mine.Where(h => !matched.ContainsKey(h)))
            {
                if (occupied.Contains((hold.RecipeId, hold.IngredientId, hold.SortOrder))) continue;
                var moved = own.FirstOrDefault(h => !kept.Contains(h) && h.RecipeId == hold.RecipeId && h.IngredientId == hold.IngredientId
                    && h.RawLine == hold.RawLine && !targeted.Contains(Key(h)));
                if (moved == null) continue;
                byKey.Remove(Key(moved));
                moved.SortOrder = hold.SortOrder;
                byKey[Key(moved)] = moved;
                occupied.Add(Key(moved));
                kept.Add(moved);
                matched[hold] = moved;
            }
            foreach (var hold in mine.Where(h => !matched.ContainsKey(h)))
            {
                if (byKey.TryGetValue((hold.RecipeId, hold.IngredientId, hold.SortOrder), out var at) && kept.Add(at)) matched[hold] = at;
            }

            foreach (var hold in mine)
            {
                if (!matched.TryGetValue(hold, out var row))
                {
                    row = new LineGuardHoldEntity
                    {
                        RecipeId = hold.RecipeId, IngredientId = hold.IngredientId, SortOrder = hold.SortOrder, Origin = origin,
                        CreatedDate = now, CreatedByPersonId = SystemConstants.SystemPersonId,
                    };
                    db.LineGuardHolds.Add(row);
                    byKey[Key(row)] = row;
                }
                else
                {
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }

                kept.Add(row);
                row.Reason = hold.Reason;
                row.RuleVersion = ruleVersion;
                row.RawLine = hold.RawLine;
                row.StoredFood = Clamp(hold.StoredFood);
                row.StoredQuantity = hold.StoredQuantity;
                row.StoredUnit = hold.StoredUnit;
                row.StoredKind = hold.StoredKind;
                row.RebuiltIngredientId = hold.RebuiltIngredientId;
                row.RebuiltFood = Clamp(hold.RebuiltFood);
                row.RebuiltQuantity = hold.RebuiltQuantity;
                row.RebuiltUnit = hold.RebuiltUnit;
                row.RebuiltKind = hold.RebuiltKind;
                row.RebuiltFromMethod = hold.RebuiltFromMethod;
                row.HeldAt = now;
            }

            foreach (var row in open.Where(h => !kept.Contains(h)))
            {
                row.ResolvedAt = now;
                row.Resolution = row.Origin == origin ? Released : resolve.GetValueOrDefault(row.Origin, Released);
                row.LastModifiedDate = now;
                row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }
        }

        /// <summary>Resolves the open holds of <paramref name="origin"/> on <paramref name="recipeIds"/> with <paramref name="resolution"/>, as an undo of the writer's change does.</summary>
        public static async Task ResolveAsync(
            ApplicationDbContext db, string origin, IReadOnlyCollection<long> recipeIds, string resolution, DateTime now, CancellationToken cancellationToken = default)
        {
            if (recipeIds.Count == 0) return;
            var ids = recipeIds.ToList();
            foreach (var row in await db.LineGuardHolds.Where(h => ids.Contains(h.RecipeId) && h.ResolvedAt == null && h.Origin == origin).ToListAsync(cancellationToken))
            {
                row.ResolvedAt = now;
                row.Resolution = resolution;
                row.LastModifiedDate = now;
                row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }
        }

        private static (long, long, int) Key(LineGuardHoldEntity h) => (h.RecipeId, h.IngredientId, h.SortOrder);

        private static string? Clamp(string? text) => text is { Length: > 255 } ? text[..255] : text;
    }
}
