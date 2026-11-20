using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The line rules an approved recipe must keep passing: no more real ingredient lines without an
    /// amount than the auto-approval allowance, and every line that feeds the nutrition label on a
    /// curated food. Lets a correction count, before it writes, the approved recipes it would put
    /// outside those rules.
    /// </summary>
    public static class RecipeLineApproval
    {
        public sealed record Line(long IngredientId, int SortOrder, decimal Quantity, string RawLine, string? LineKind, bool Curated);

        public static bool Passes(IEnumerable<Line> lines, int maxUnquantifiedLines)
        {
            var rows = lines.Select(l => (Line: l, Row: new RecipeIngredientEntity { Quantity = l.Quantity, RawLine = l.RawLine, LineKind = l.LineKind })).ToList();
            var unquantified = rows.Count(r => r.Row.Quantity <= 0 && !RecipeVettingService.IsAcceptablyUnquantified(r.Row.RawLine, r.Row.LineKind));
            return unquantified <= maxUnquantifiedLines && rows.All(r => r.Line.Curated || !RecipeApprovalGate.CountsTowardNutrition(r.Row));
        }

        /// <summary>The lines of those of <paramref name="recipeIds"/> that are approved, by recipe.</summary>
        public static async Task<Dictionary<long, List<Line>>> ApprovedLinesAsync(ApplicationDbContext db, IEnumerable<long> recipeIds)
        {
            var result = new Dictionary<long, List<Line>>();
            foreach (var chunk in recipeIds.Distinct().Chunk(500))
            {
                var rows = await db.RecipeIngredients.AsNoTracking()
                    .Where(ri => chunk.Contains(ri.RecipeId) && !ri.IsDeleted
                        && !ri.Recipe.IsDeleted && ri.Recipe.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .Select(ri => new
                    {
                        ri.RecipeId,
                        Line = new Line(ri.IngredientId, ri.SortOrder, ri.Quantity, ri.RawLine, ri.LineKind,
                            !ri.Ingredient.IsDeleted && ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated),
                    })
                    .ToListAsync();
                foreach (var group in rows.GroupBy(r => r.RecipeId)) result[group.Key] = group.Select(r => r.Line).ToList();
            }

            return result;
        }

        /// <summary>
        /// How many of the approved recipes pass the line rules now and would not once
        /// <paramref name="change"/> is applied to each of their lines.
        /// </summary>
        public static int StopPassing(Dictionary<long, List<Line>> approved, Func<long, Line, Line> change, int maxUnquantifiedLines) =>
            approved.Count(recipe => Passes(recipe.Value, maxUnquantifiedLines)
                && !Passes(recipe.Value.Select(line => change(recipe.Key, line)), maxUnquantifiedLines));

        public static string StatusName(long curationStatusId) =>
            Enum.IsDefined(typeof(CurationStatusEnum), curationStatusId)
                ? ((CurationStatusEnum)curationStatusId).ToString()
                : curationStatusId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
