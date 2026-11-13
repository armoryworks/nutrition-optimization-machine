using System;
using System.Collections.Generic;
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
    /// Moves a scraped recipe's line to the curated food its wording names. A line moves when its
    /// normalised wording, or that wording without its preparation words, is the name of another
    /// curated food that has nutrition, and the wording does not already name the food it is on.
    /// </summary>
    public class LineRelinkService : ILineRelinkService
    {
        public const string RelinkChangeType = "LineRelink";
        public const string RelinkEntityType = "RecipeIngredient";

        private static readonly Regex Word = new(@"[\p{L}\p{N}%']+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public LineRelinkService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Line(long RecipeId, long IngredientId, int SortOrder, string RawLine);
        private sealed record Move(Line Line, string Wording, long TargetId);

        private async Task<(int Examined, List<Move> Moves, Dictionary<long, string> Names)> PlanAsync()
        {
            var curated = await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .OrderBy(i => i.Id)
                .Select(i => new { i.Id, i.Name, HasNutrition = i.IngredientNutrients.Any(n => !n.IsDeleted) })
                .ToListAsync();
            var names = curated.ToDictionary(i => i.Id, i => i.Name.Trim());
            var targets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var food in curated.Where(f => f.HasNutrition)) targets.TryAdd(food.Name.Trim(), food.Id);

            var lines = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RawLine != ""
                    && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient)
                    && !ri.Recipe.IsDeleted && ri.Recipe.ScrapedAtUtc != null
                    && !ri.Ingredient.IsDeleted && ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated)
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder)
                .Select(ri => new Line(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine))
                .ToListAsync();

            var decided = new Dictionary<(long, string), (string Wording, long? TargetId)>();
            var moves = new List<Move>();
            foreach (var line in lines)
            {
                var first = line.RawLine.Split(" + ", 2)[0];
                if (!decided.TryGetValue((line.IngredientId, first), out var decision))
                {
                    var wording = IngredientNameNormalizer.Normalize(first);
                    decision = (wording, TargetFor(wording, line.IngredientId, names[line.IngredientId], targets));
                    decided[(line.IngredientId, first)] = decision;
                }
                if (decision.TargetId is { } targetId) moves.Add(new Move(line, decision.Wording, targetId));
            }

            return (lines.Count, moves, names);
        }

        /// <summary>
        /// The curated food a wording belongs on when it is not the one it is on, or null to leave
        /// the line alone. The whole wording is tried first; the wording without its preparation
        /// words only moves a line whose wording does not contain the name of its present food.
        /// </summary>
        internal static long? TargetFor(string wording, long currentId, string currentName, IReadOnlyDictionary<string, long> targets)
        {
            if (wording.Length == 0) return null;

            var exact = IngredientNameNormalizer.ExactKeys(wording);
            if (exact.Contains(currentName, StringComparer.OrdinalIgnoreCase)) return null;
            foreach (var key in exact)
                if (targets.TryGetValue(key, out var found)) return found == currentId ? null : found;

            var bare = IngredientNameNormalizer.LookupKeys(wording);
            if (bare.Contains(currentName, StringComparer.OrdinalIgnoreCase)) return null;
            foreach (var key in bare)
                if (targets.TryGetValue(key, out var found))
                    return found == currentId || Words(currentName).IsSubsetOf(Words(wording)) ? null : found;

            return null;
        }

        private static HashSet<string> Words(string text) =>
            Word.Matches(text.ToLowerInvariant()).Select(m => Singular(m.Value)).ToHashSet();

        private static string Singular(string word) =>
            word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal) ? word[..^3] + "y"
            : word.Length > 4 && word.EndsWith("oes", StringComparison.Ordinal) ? word[..^2]
            : word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1]
            : word;

        public async Task<LineRelinkPreview> PreviewAsync(int sampleSize = 50)
        {
            var (examined, moves, names) = await PlanAsync();
            return new LineRelinkPreview
            {
                LinesExamined = examined,
                MoveCount = moves.Count,
                Samples = moves
                    .GroupBy(m => (m.Line.IngredientId, Wording: m.Wording.ToLowerInvariant(), m.TargetId))
                    .Select(g => new LineRelinkMove
                    {
                        FromIngredientId = g.Key.IngredientId,
                        FromName = names[g.Key.IngredientId],
                        Wording = g.Key.Wording,
                        ToIngredientId = g.Key.TargetId,
                        ToName = names[g.Key.TargetId],
                        Lines = g.Count(),
                    })
                    .OrderByDescending(m => m.Lines).ThenBy(m => m.FromIngredientId).ThenBy(m => m.Wording, StringComparer.Ordinal)
                    .Take(Math.Clamp(sampleSize, 1, 5000))
                    .ToList(),
            };
        }

        public async Task<LineRelinkResult> ApplyAsync(int maxLines = 5000)
        {
            var (_, moves, _) = await PlanAsync();
            var result = new LineRelinkResult { Remaining = moves.Count };
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in moves.Take(Math.Clamp(maxLines, 1, 50000)).Chunk(500))
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                foreach (var move in chunk)
                {
                    var line = move.Line;
                    var position = await MoveAsync(line.RecipeId, line.IngredientId, line.SortOrder, move.TargetId, line.SortOrder, personId);
                    if (position == null)
                    {
                        result.Skipped++;
                        continue;
                    }

                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RelinkEntityType,
                        EntityId = line.RecipeId,
                        ChangeType = RelinkChangeType,
                        PropertyName = nameof(RecipeIngredientEntity.IngredientId),
                        OldValue = Place(line.IngredientId, line.SortOrder),
                        NewValue = Place(move.TargetId, position.Value),
                        Timestamp = DateTime.UtcNow,
                        ChangedByPersonId = personId,
                    });
                    result.Moved++;
                    if (position.Value != line.SortOrder) result.Repositioned++;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                result.Remaining -= chunk.Length;
            }

            return result;
        }

        public async Task<LineRelinkUndoResult> UndoAsync(DateTime sinceUtc)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == RelinkEntityType && a.ChangeType == RelinkChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var result = new LineRelinkUndoResult();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(500))
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                foreach (var entry in chunk)
                {
                    if (ReadPlace(entry.OldValue) is not { } was || ReadPlace(entry.NewValue) is not { } now
                        || await MoveAsync(entry.EntityId, now.IngredientId, now.SortOrder, was.IngredientId, was.SortOrder, personId) == null)
                    {
                        result.Skipped++;
                        continue;
                    }

                    _db.AuditLogEntries.Remove(entry);
                    result.Restored++;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            return result;
        }

        internal static string Place(long ingredientId, int sortOrder) =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ingredientId}@{sortOrder}");

        internal static (long IngredientId, int SortOrder)? ReadPlace(string? value)
        {
            var parts = (value ?? string.Empty).Split('@');
            return parts.Length == 2
                && long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ingredientId)
                && int.TryParse(parts[1], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var sortOrder)
                ? (ingredientId, sortOrder)
                : null;
        }

        /// <summary>
        /// Re-points one line and returns the position it ended at, or null when the line is no
        /// longer where the caller found it. A recipe that already has the target food at the
        /// wanted position (most scraped recipes keep every line at position 0) gets the line at
        /// the next free position, so both lines keep their own quantity.
        /// </summary>
        private async Task<int?> MoveAsync(long recipeId, long fromId, int fromSortOrder, long toId, int wantedSortOrder, long personId)
        {
            var positions = await _db.Database.SqlQuery<int>(
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
    }
}
