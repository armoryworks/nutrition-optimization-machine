using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Writes recipe lines by their key (recipe, food, position) for the repairs that move, add or
    /// remove lines and journal each change: a key update and a real delete on the database, and
    /// the same through the change tracker on a provider without SQL.
    /// </summary>
    internal static class RecipeLineRowWriter
    {
        /// <summary>
        /// Re-points one line and returns the position it ended at, or null when the line is no
        /// longer where the caller found it or the diet guard holds the move (counted in <see cref="LineMoveDietGuard.Held"/>),
        /// read with the line's text or, for a move that rewrites the line, with <paramref name="asLine"/>. A recipe
        /// that already has the target food at the wanted position (most scraped recipes keep every line at position
        /// 0) gets the line at the next free position, so both lines keep their own quantity.
        /// </summary>
        public static async Task<int?> MoveAsync(ApplicationDbContext db, LineMoveDietGuard guard, long recipeId, long fromId, int fromSortOrder, long toId, int wantedSortOrder, long personId, string? asLine = null)
        {
            if (await guard.HoldsAsync(db, recipeId, fromId, fromSortOrder, toId, asLine)) return null;
            if (db.Database.IsRelational())
            {
                var positions = await db.Database.SqlQuery<int>(
                    $"""
                     UPDATE recipe."RecipeIngredient" line
                     SET "IngredientId" = {toId},
                         "IngredientEntityId" = CASE WHEN line."IngredientEntityId" = {fromId} THEN {toId} ELSE line."IngredientEntityId" END,
                         "SortOrder" = CASE WHEN EXISTS (SELECT 1 FROM recipe."RecipeIngredient" other
                                                         WHERE other."RecipeId" = {recipeId} AND other."IngredientId" = {toId} AND other."SortOrder" = {wantedSortOrder})
                                            THEN (SELECT max(other."SortOrder") + 1 FROM recipe."RecipeIngredient" other WHERE other."RecipeId" = {recipeId})
                                            ELSE {wantedSortOrder} END,
                         "LastModifiedDate" = now(), "LastModifiedByPersonId" = {personId}
                     WHERE line."RecipeId" = {recipeId} AND line."IngredientId" = {fromId} AND line."SortOrder" = {fromSortOrder}
                       AND EXISTS (SELECT 1 FROM recipe."Ingredient" food WHERE food."Id" = {toId} AND food."IsDeleted" = false)
                     RETURNING line."SortOrder" AS "Value"
                     """).ToListAsync();
                return positions.Count == 0 ? null : positions[0];
            }

            var row = await db.RecipeIngredients.IgnoreQueryFilters()
                .FirstOrDefaultAsync(ri => ri.RecipeId == recipeId && ri.IngredientId == fromId && ri.SortOrder == fromSortOrder);
            if (row == null || !await db.Ingredients.IgnoreQueryFilters().AnyAsync(i => i.Id == toId && !i.IsDeleted)) return null;

            var keys = await db.RecipeIngredients.IgnoreQueryFilters().Where(ri => ri.RecipeId == recipeId)
                .Select(ri => new { ri.IngredientId, ri.SortOrder }).ToListAsync();
            var position = keys.Any(k => k.IngredientId == toId && k.SortOrder == wantedSortOrder) ? keys.Max(k => k.SortOrder) + 1 : wantedSortOrder;
            db.RecipeIngredients.Remove(row);
            await db.SaveChangesAsync();
            db.Entry(row).State = EntityState.Detached;
            db.RecipeIngredients.Add(new RecipeIngredientEntity
            {
                RecipeId = recipeId,
                IngredientId = toId,
                SortOrder = position,
                Quantity = row.Quantity,
                MeasurementId = row.MeasurementId,
                RawLine = row.RawLine,
                LineKind = row.LineKind,
                CreatedDate = row.CreatedDate,
                CreatedByPersonId = row.CreatedByPersonId,
                LastModifiedDate = DateTime.UtcNow,
                LastModifiedByPersonId = personId,
            });
            await db.SaveChangesAsync();
            return position;
        }

        /// <summary>Sets the text and amount of the line at a key; false when no live line is there.</summary>
        public static async Task<bool> RewriteAsync(ApplicationDbContext db, long recipeId, long ingredientId, int sortOrder, string rawLine, decimal quantity, long measurementId, long personId)
        {
            if (db.Database.IsRelational())
            {
                return await db.Database.ExecuteSqlAsync(
                    $"""
                     UPDATE recipe."RecipeIngredient"
                     SET "RawLine" = {rawLine}, "Quantity" = {quantity}, "MeasurementId" = {measurementId},
                         "LastModifiedDate" = now(), "LastModifiedByPersonId" = {personId}
                     WHERE "RecipeId" = {recipeId} AND "IngredientId" = {ingredientId} AND "SortOrder" = {sortOrder} AND "IsDeleted" = false
                     """) == 1;
            }

            var row = await db.RecipeIngredients
                .FirstOrDefaultAsync(ri => ri.RecipeId == recipeId && ri.IngredientId == ingredientId && ri.SortOrder == sortOrder);
            if (row == null) return false;
            row.RawLine = rawLine;
            row.Quantity = quantity;
            row.MeasurementId = measurementId;
            row.LastModifiedDate = DateTime.UtcNow;
            row.LastModifiedByPersonId = personId;
            await db.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Deletes the line at a key for good, so the key is free again (an entity removed under a
        /// request would only be marked deleted and keep the key taken).
        /// </summary>
        public static async Task DeleteAsync(ApplicationDbContext db, long recipeId, long ingredientId, int sortOrder)
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlAsync(
                    $"""DELETE FROM recipe."RecipeIngredient" WHERE "RecipeId" = {recipeId} AND "IngredientId" = {ingredientId} AND "SortOrder" = {sortOrder}""");
                return;
            }

            var row = await db.RecipeIngredients.IgnoreQueryFilters()
                .FirstOrDefaultAsync(ri => ri.RecipeId == recipeId && ri.IngredientId == ingredientId && ri.SortOrder == sortOrder);
            if (row == null) return;
            db.RecipeIngredients.Remove(row);
            await db.SaveChangesAsync();
        }
    }
}
