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
        public const string RetireChangeType = "FoodRetire";
        public const string RetireEntityType = "Ingredient";
        public const string RetiredContextPrefix = "retired:";

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
            return await MoveAsync(moves, Math.Clamp(maxLines, 1, 50000));
        }

        private async Task<LineRelinkResult> MoveAsync(List<Move> moves, int maxLines)
        {
            var result = new LineRelinkResult { Remaining = moves.Count };
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in moves.Take(maxLines).Chunk(500))
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

        public async Task<LineRelinkResult?> MoveFoodAsync(long fromIngredientId, long toIngredientId, string? keepWords = null)
        {
            if (fromIngredientId == toIngredientId) return null;
            var foods = await _db.Ingredients.AsNoTracking()
                .Where(i => (i.Id == fromIngredientId || i.Id == toIngredientId) && !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .Select(i => new { i.Id, HasNutrition = i.IngredientNutrients.Any(n => !n.IsDeleted) })
                .ToListAsync();
            if (foods.Count != 2 || !foods.Single(f => f.Id == toIngredientId).HasNutrition) return null;

            var lines = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.IngredientId == fromIngredientId)
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder)
                .Select(ri => new Line(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine))
                .ToListAsync();
            var keep = Words(keepWords ?? string.Empty);
            return await MoveAsync(
                lines.Where(line => keep.Count == 0 || !Words(line.RawLine).Overlaps(keep))
                    .Select(line => new Move(line, string.Empty, toIngredientId)).ToList(),
                int.MaxValue);
        }

        public async Task<FoodRetireResult?> RetireAsync(long fromIngredientId, long toIngredientId, bool takeName = false)
        {
            if (fromIngredientId == toIngredientId) return null;
            var foods = await _db.Ingredients
                .Where(i => (i.Id == fromIngredientId || i.Id == toIngredientId) && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .ToListAsync();
            var source = foods.SingleOrDefault(f => f.Id == fromIngredientId);
            var target = foods.SingleOrDefault(f => f.Id == toIngredientId);
            if (source == null || target == null
                || !await _db.IngredientNutrients.AnyAsync(n => n.IngredientId == toIngredientId)) return null;

            var result = new FoodRetireResult
            {
                LinesLeft = await _db.RecipeIngredients.CountAsync(ri => ri.IngredientId == fromIngredientId),
                InUseElsewhere = await IsUsedOutsideRecipeLinesAsync(fromIngredientId),
            };
            if (result.LinesLeft > 0 || result.InUseElsewhere) return result;

            var personId = _currentUser.PersonIdOrSystem;
            var now = DateTime.UtcNow;
            var retiredName = source.Name.Trim();
            var keptName = target.Name.Trim();
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            source.Name = Truncate($"{retiredName} (deleted #{source.Id})", 2000);
            source.IsDeleted = true;
            source.DeletedAt = now;
            source.DeletedByPersonId = personId;
            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = RetireEntityType,
                EntityId = source.Id,
                ChangeType = RetireChangeType,
                PropertyName = nameof(IngredientEntity.IsDeleted),
                OldValue = retiredName,
                NewValue = Place(target.Id, 0),
                Timestamp = now,
                ChangedByPersonId = personId,
            });
            await _db.SaveChangesAsync();

            if (takeName)
            {
                target.Name = retiredName;
                target.NameNormalized = IngredientNameNormalizer.Normalize(retiredName);
                target.LastModifiedDate = now;
                target.LastModifiedByPersonId = personId;
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RetireEntityType,
                    EntityId = target.Id,
                    ChangeType = RetireChangeType,
                    PropertyName = nameof(IngredientEntity.Name),
                    OldValue = keptName,
                    NewValue = retiredName,
                    Timestamp = now,
                    ChangedByPersonId = personId,
                });
            }

            result.Alias = takeName ? keptName : retiredName;
            await AddRetiredAliasAsync(target.Id, result.Alias, source.Id, personId);
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            result.Retired = true;
            result.Name = target.Name;
            return result;
        }

        private async Task AddRetiredAliasAsync(long ingredientId, string alias, long retiredId, long personId)
        {
            if (alias.Length > 511) return;
            var lowered = alias.ToLowerInvariant();
            var existing = await _db.IngredientAliases.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.IngredientId == ingredientId && a.AliasName.ToLower() == lowered);
            if (existing is { IsDeleted: false }) return;

            var context = RetiredContextPrefix + retiredId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (existing != null)
            {
                existing.IsDeleted = false;
                existing.DeletedAt = null;
                existing.DeletedByPersonId = null;
                existing.SourceContext = context;
                existing.LastModifiedDate = DateTime.UtcNow;
                existing.LastModifiedByPersonId = personId;
                return;
            }

            _db.IngredientAliases.Add(new IngredientAliasEntity
            {
                IngredientId = ingredientId,
                AliasName = alias,
                SourceContext = context,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = personId,
            });
        }

        /// <summary>
        /// Whether anything a household or another food relies on still points at the food: a
        /// pantry, shopping list, plan, goal, restriction, substitution, variation or component.
        /// </summary>
        private async Task<bool> IsUsedOutsideRecipeLinesAsync(long id)
        {
            if (!_db.Database.IsRelational()) return false;
            var used = await _db.Database
                .SqlQuery<bool>($"""
                    SELECT (
                        EXISTS (SELECT 1 FROM recipe."IngredientComponent" x WHERE x."IngredientId" = {id} OR x."ComponentIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."IngredientSubstitution" x WHERE x."IngredientId" = {id} OR x."SubstituteIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."RecipeAugmentation" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."RecipeSubstitution" x WHERE x."IngredientId" = {id} OR x."SubstituteIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."RecipeVariationItem" x WHERE x."IngredientId" = {id} OR x."SubstituteIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."GoalItem" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."HouseholdIngredient" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."MealPlan" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."Restriction" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."RestrictionCriterion" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM shopping."PantryItem" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM shopping."ShoppingListItem" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM shopping."HouseholdShoppingItem" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM communication."MessageThread" x WHERE x."IngredientId" = {id})
                    ) AS "Value"
                    """)
                .ToListAsync();
            return used.Count > 0 && used[0];
        }

        private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

        public async Task<LineRelinkUndoResult> UndoAsync(DateTime sinceUtc)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => ((a.EntityType == RelinkEntityType && a.ChangeType == RelinkChangeType)
                        || (a.EntityType == RetireEntityType && a.ChangeType == RetireChangeType))
                    && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var result = new LineRelinkUndoResult();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(500))
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                foreach (var entry in chunk)
                {
                    if (entry.ChangeType == RetireChangeType)
                    {
                        if (await RestoreAsync(entry, personId))
                        {
                            _db.AuditLogEntries.Remove(entry);
                            if (entry.PropertyName == nameof(IngredientEntity.IsDeleted)) result.FoodsRestored++;
                        }
                        else
                        {
                            result.Skipped++;
                        }

                        await _db.SaveChangesAsync();
                        continue;
                    }

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

        /// <summary>
        /// Takes back one step of a retirement: the main entry gives up a name it took, or the
        /// retired food comes back under its own name and the alias that pointed at the main
        /// entry goes. False when the name has since been taken by another food.
        /// </summary>
        private async Task<bool> RestoreAsync(AuditLogEntryEntity entry, long personId)
        {
            var food = await _db.Ingredients.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == entry.EntityId);
            if (food == null || string.IsNullOrEmpty(entry.OldValue)
                || await _db.Ingredients.IgnoreQueryFilters().AnyAsync(i => i.Id != food.Id && i.Name == entry.OldValue)) return false;

            if (entry.PropertyName == nameof(IngredientEntity.Name))
            {
                if (food.Name != entry.NewValue) return false;
                food.NameNormalized = IngredientNameNormalizer.Normalize(entry.OldValue);
            }
            else
            {
                if (!food.IsDeleted) return false;
                food.IsDeleted = false;
                food.DeletedAt = null;
                food.DeletedByPersonId = null;
                var context = RetiredContextPrefix + food.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _db.IngredientAliases.RemoveRange(await _db.IngredientAliases.Where(a => a.SourceContext == context).ToListAsync());
            }

            food.Name = entry.OldValue;
            food.LastModifiedDate = DateTime.UtcNow;
            food.LastModifiedByPersonId = personId;
            return true;
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
