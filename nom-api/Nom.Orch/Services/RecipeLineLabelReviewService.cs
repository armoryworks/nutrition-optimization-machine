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
    /// Returns to ingredient the recipe lines whose label says they are not one but whose amount or
    /// wording says they are. Each change is one audit entry holding the line position and the old
    /// label, so <see cref="UndoAsync"/> can put a batch back.
    /// </summary>
    public class RecipeLineLabelReviewService : IRecipeLineLabelReviewService
    {
        public const string ReviewChangeType = "LineKindReview";
        public const string ReviewEntityType = "RecipeIngredient";

        private const int RecipesPerTransaction = 200;

        private static readonly Regex LineProperty = new(@"^LineKind\[(?<i>\d+):(?<s>-?\d+)\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly int _maxUnquantifiedLines;

        public RecipeLineLabelReviewService(ApplicationDbContext db, ICurrentUserService currentUser, IConfiguration? configuration = null)
        {
            _db = db;
            _currentUser = currentUser;
            _maxUnquantifiedLines = Math.Max(0, configuration?.GetValue("Curation:AutoApprove:MaxUnquantifiedLines", 0) ?? 0);
        }

        private sealed record Label(long RecipeId, long IngredientId, int SortOrder, decimal Quantity, string RawLine, string LineKind, bool OnCuratedFood, long RecipeStatusId);
        private sealed record Flip(Label Label, string Reason);

        public async Task<LineLabelReviewResult> ReviewAsync(bool apply = false, bool shortLinesOnCuratedFoods = false, int maxLines = 5000, int sampleSize = 50)
        {
            var result = new LineLabelReviewResult { Applied = apply, ShortLinesOnCuratedFoods = shortLinesOnCuratedFoods };
            var names = await RecipeLineKindGuard.CuratedNamesAsync(_db);
            var exempt = RecipeIngredientLineKind.All.Where(RecipeIngredientLineKind.IsExempt).ToList();
            var labels = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.LineKind != null && exempt.Contains(ri.LineKind) && !ri.Recipe.IsDeleted)
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new Label(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.Quantity, ri.RawLine, ri.LineKind!,
                    !ri.Ingredient.IsDeleted && ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated,
                    ri.Recipe.CurationStatusId))
                .ToListAsync();
            result.LinesExamined = labels.Count;

            var wide = new List<Flip>();
            foreach (var label in labels)
            {
                var reason = RecipeLineKindGuard.Refusal(label.LineKind, label.Quantity, label.RawLine, label.OnCuratedFood, names, shortLinesOnCuratedFoods: true);
                if (reason == null) continue;
                wide.Add(new Flip(label, reason));
                if (reason == RecipeLineKindGuard.QuantityReason) result.WithAmount++;
                else if (reason == RecipeLineKindGuard.CuratedNameReason) result.NamingCuratedFood++;
                else result.ShortOnCuratedFood++;
            }

            var certain = wide.Where(f => f.Reason != RecipeLineKindGuard.ShortLineReason).ToList();
            var approved = await RecipeLineApproval.ApprovedLinesAsync(_db, wide.Select(f => f.Label.RecipeId));
            result.Certain = Count(certain, approved);
            result.WithShortLines = Count(wide, approved);

            var chosen = shortLinesOnCuratedFoods ? wide : certain;
            result.RecipesByStatus = chosen.Select(f => f.Label).GroupBy(l => l.RecipeId)
                .GroupBy(g => RecipeLineApproval.StatusName(g.First().RecipeStatusId))
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count());
            var samples = Math.Clamp(sampleSize, 0, 5000);
            if (samples > 0 && chosen.Count > 0)
            {
                var step = Math.Max(1, chosen.Count / samples);
                result.Samples = chosen.Where((_, i) => i % step == 0).Take(samples).Select(f => new LineLabelReviewSample
                {
                    RecipeId = f.Label.RecipeId,
                    IngredientId = f.Label.IngredientId,
                    SortOrder = f.Label.SortOrder,
                    Line = f.Label.RawLine,
                    Quantity = f.Label.Quantity,
                    LineKind = f.Label.LineKind,
                    Reason = f.Reason,
                    RecipeStatus = RecipeLineApproval.StatusName(f.Label.RecipeStatusId),
                }).ToList();
            }

            result.Remaining = chosen.Count;
            if (!apply) return result;

            var personId = _currentUser.PersonIdOrSystem;
            var returned = new HashSet<(long RecipeId, long IngredientId, int SortOrder)>();
            foreach (var chunk in chosen.Take(Math.Clamp(maxLines, 1, 50000)).GroupBy(f => f.Label.RecipeId).Chunk(RecipesPerTransaction))
            {
                var recipeIds = chunk.Select(g => g.Key).ToList();
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var rows = (await _db.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId)).ToListAsync())
                    .ToDictionary(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder));
                var now = DateTime.UtcNow;
                foreach (var flip in chunk.SelectMany(g => g))
                {
                    var label = flip.Label;
                    result.Remaining--;
                    if (!rows.TryGetValue((label.RecipeId, label.IngredientId, label.SortOrder), out var row)
                        || row.LineKind != label.LineKind || row.RawLine != label.RawLine || row.Quantity != label.Quantity)
                    {
                        result.Skipped++;
                        continue;
                    }

                    row.LineKind = RecipeIngredientLineKind.Ingredient;
                    returned.Add((label.RecipeId, label.IngredientId, label.SortOrder));
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = ReviewEntityType,
                        EntityId = label.RecipeId,
                        ChangeType = ReviewChangeType,
                        PropertyName = RecipeLineClassificationService.LinePropertyName(label.IngredientId, label.SortOrder),
                        OldValue = label.LineKind,
                        NewValue = Clamp($"{RecipeIngredientLineKind.Ingredient} ({flip.Reason}): {label.RawLine}", 4000),
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    result.Changed++;
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                _db.ChangeTracker.Clear();
            }

            if (returned.Count > 0)
            {
                var reread = await new PackageLineService(_db, _currentUser).ApplyAsync(50000, returned);
                result.Reread = reread.Changed;
            }

            return result;
        }

        private LineLabelReviewCount Count(List<Flip> flips, Dictionary<long, List<RecipeLineApproval.Line>> approved)
        {
            var places = flips.Select(f => (f.Label.RecipeId, f.Label.IngredientId, f.Label.SortOrder)).ToHashSet();
            var recipes = flips.Select(f => f.Label.RecipeId).ToHashSet();
            var touched = approved.Where(a => recipes.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value);
            return new LineLabelReviewCount
            {
                Lines = flips.Count,
                Recipes = recipes.Count,
                ApprovedRecipes = touched.Count,
                ApprovedStopPassing = RecipeLineApproval.StopPassing(touched,
                    (recipeId, line) => places.Contains((recipeId, line.IngredientId, line.SortOrder)) ? line with { LineKind = RecipeIngredientLineKind.Ingredient } : line,
                    _maxUnquantifiedLines),
            };
        }

        public async Task<LineCorrectionUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == ReviewEntityType && a.ChangeType == ReviewChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var result = new LineCorrectionUndoResult { Applied = apply };
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(500))
            {
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var recipeIds = chunk.Select(e => e.EntityId).Distinct().ToList();
                var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_db, recipeIds);
                var trail = await RecipeOrderTrail.LoadAsync(_db, recipeIds);
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

                    var match = LineProperty.Match(entry.PropertyName ?? string.Empty);
                    var ingredientId = match.Success ? long.Parse(match.Groups["i"].Value, CultureInfo.InvariantCulture) : 0;
                    var sortOrder = match.Success ? int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture) : 0;
                    if (!match.Success || !RecipeIngredientLineKind.IsExempt(entry.OldValue)
                        || trail.Follow(entry.EntityId, ingredientId, sortOrder, entry.Timestamp) is not { } at
                        || !rows.TryGetValue((entry.EntityId, ingredientId, at), out var row)
                        || row.LineKind != RecipeIngredientLineKind.Ingredient || !SameLine(entry.NewValue, row.RawLine))
                    {
                        result.Skipped++;
                        continue;
                    }

                    var kind = entry.OldValue;
                    trail.Carry(entry.EntityId, entry.Timestamp, ingredientId, sortOrder, r => r with { LineKind = kind });
                    row.LineKind = entry.OldValue;
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = personId;
                    if (apply) _db.AuditLogEntries.Remove(entry);
                    result.Restored++;
                }

                if (!apply) continue;
                await trail.StageAsync(_db);
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }

            if (!apply) _db.ChangeTracker.Clear();
            return result;
        }

        private static string Clamp(string value, int maxLength) => RecipeStepSplit.Clamp(value, maxLength);

        /// <summary>
        /// Whether the row still holds the line the review relabelled, as its journal entry
        /// recorded it after "): ". An unfold or a split that kept the row's place gives it
        /// another line, which the undo must not label again.
        /// </summary>
        private static bool SameLine(string? journaled, string? rawLine)
        {
            var at = (journaled ?? string.Empty).IndexOf("): ", StringComparison.Ordinal);
            if (at < 0) return false;
            var recorded = journaled![(at + 3)..];
            return string.Equals(recorded, rawLine ?? string.Empty, StringComparison.Ordinal)
                || (journaled.Length >= 3900 && (rawLine ?? string.Empty).StartsWith(recorded, StringComparison.Ordinal));
        }
    }
}
