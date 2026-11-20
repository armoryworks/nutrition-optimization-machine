using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public partial class LineRelinkService
    {
        public async Task<VerifiedWordingLinkResult> LinkVerifiedWordingsAsync(bool apply = false, int maxLines = 20000, int sampleSize = 50)
        {
            var verified = await _db.IngredientWordings.AsNoTracking()
                .Where(w => w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null)
                .Select(w => new { w.Wording, TargetId = w.IngredientId!.Value })
                .ToListAsync();
            var targetIds = verified.Select(w => w.TargetId).Distinct().ToList();
            var targets = await _db.Ingredients.AsNoTracking()
                .Where(i => targetIds.Contains(i.Id) && !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated
                    && i.IngredientNutrients.Any(n => !n.IsDeleted))
                .ToDictionaryAsync(i => i.Id, i => i.Name);
            var usable = verified.Where(w => targets.ContainsKey(w.TargetId)).ToDictionary(w => w.Wording, w => w.TargetId, StringComparer.Ordinal);
            var result = new VerifiedWordingLinkResult
            {
                Applied = apply,
                Wordings = usable.Count,
                TargetsUnusable = verified.Count - usable.Count,
            };

            var keys = usable.Keys.ToList();
            var sources = (await _db.Ingredients.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.FdcId == null && i.CurationStatusId != (long)CurationStatusEnum.Curated
                        && keys.Contains(i.Name.ToLower()))
                    .Select(i => new { i.Id, i.Name })
                    .ToListAsync())
                .Select(i => new { i.Id, i.Name, Wording = IngredientNameNormalizer.WordingKey(i.Name) })
                .Where(i => usable.TryGetValue(i.Wording, out var targetId) && targetId != i.Id)
                .ToDictionary(i => i.Id);
            result.Sources = sources.Count;

            var sourceIds = sources.Keys.ToList();
            var lines = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && sourceIds.Contains(ri.IngredientId)
                    && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient)
                    && !ri.Recipe.IsDeleted && ri.Recipe.ScrapedAtUtc != null)
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder)
                .Select(ri => new Line(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine))
                .ToListAsync();
            var moves = lines.Select(line => new Move(line, sources[line.IngredientId].Wording, usable[sources[line.IngredientId].Wording])).ToList();

            var names = sources.Values.ToDictionary(s => s.Id, s => s.Name);
            foreach (var target in targets) names[target.Key] = target.Value;
            result.Samples = Grouped(moves, id => names[id]).Take(Math.Clamp(sampleSize, 0, 5000)).ToList();

            var limit = Math.Clamp(maxLines, 1, 50000);
            if (apply)
            {
                var moved = await MoveAsync(moves, limit);
                result.Moved = moved.Moved;
                result.Repositioned = moved.Repositioned;
                result.Skipped = moved.Skipped;
                result.Remaining = moved.Remaining;
            }
            else
            {
                result.Remaining = moves.Count;
            }

            var planned = moves.Take(limit).GroupBy(m => m.Line.IngredientId).ToDictionary(g => g.Key, g => g.Count());
            var lineCounts = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => sourceIds.Contains(ri.IngredientId))
                .GroupBy(ri => ri.IngredientId)
                .Select(g => new { g.Key, Lines = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Lines);
            var personId = _currentUser.PersonIdOrSystem;
            var now = DateTime.UtcNow;
            foreach (var chunk in sourceIds.Chunk(500))
            {
                var retired = new List<long>();
                foreach (var id in chunk)
                {
                    var left = lineCounts.GetValueOrDefault(id) - (apply ? 0 : planned.GetValueOrDefault(id));
                    if (left > 0 || await IsUsedOutsideRecipeLinesAsync(id))
                    {
                        result.SourcesKept++;
                        continue;
                    }

                    retired.Add(id);
                }

                result.SourcesRetired += retired.Count;
                result.ProposalsWithdrawn += await _db.FoodCatalogProposals
                    .Where(p => p.IngredientId != null && retired.Contains(p.IngredientId.Value)).Where(UnreviewedLink).CountAsync();
                if (!apply || retired.Count == 0) continue;

                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                foreach (var source in await _db.Ingredients.Where(i => retired.Contains(i.Id)).ToListAsync())
                {
                    Retire(source, usable[sources[source.Id].Wording], now, personId);
                }

                foreach (var proposal in await _db.FoodCatalogProposals
                    .Where(p => p.IngredientId != null && retired.Contains(p.IngredientId.Value)).Where(UnreviewedLink).ToListAsync())
                {
                    proposal.IsDeleted = true;
                    proposal.DeletedAt = now;
                    proposal.DeletedByPersonId = personId;
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }

            return result;
        }

        private static readonly System.Linq.Expressions.Expression<Func<FoodCatalogProposalEntity, bool>> UnreviewedLink = p =>
            p.Field == FoodCatalogReviewService.FdcLinkField
            && (p.Status == FoodProposalStatus.Pending
                || (p.Status == FoodProposalStatus.Rejected && (p.ReviewedByPersonId == null || p.ReviewedByPersonId == SystemConstants.SystemPersonId)));

        public async Task<FoodRenameResult?> RenameAsync(long ingredientId, string name)
        {
            name = (name ?? string.Empty).Trim();
            var food = await _db.Ingredients
                .FirstOrDefaultAsync(i => i.Id == ingredientId && !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated);
            if (food == null || name.Length == 0 || name.Length > 2000 || food.Name == name) return null;

            var lowered = name.ToLowerInvariant();
            var result = new FoodRenameResult { Name = food.Name };
            var holder = await _db.Ingredients
                .Where(i => i.Id != ingredientId && !i.IsDeleted && i.Name.ToLower() == lowered)
                .OrderBy(i => i.Id)
                .Select(i => new { i.Id, Lines = _db.RecipeIngredients.Count(ri => ri.IngredientId == i.Id) })
                .FirstOrDefaultAsync();
            if (holder != null)
            {
                result.HeldByIngredientId = holder.Id;
                result.HeldByLines = holder.Lines;
                return result;
            }

            var personId = _currentUser.PersonIdOrSystem;
            var now = DateTime.UtcNow;
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            foreach (var deleted in await _db.Ingredients.IgnoreQueryFilters().Where(i => i.Id != ingredientId && i.IsDeleted && i.Name == name).ToListAsync())
            {
                var freed = Truncate($"{deleted.Name} (deleted #{deleted.Id})", 2000);
                _db.AuditLogEntries.Add(Renamed(deleted.Id, deleted.Name, freed, now, personId));
                deleted.Name = freed;
            }

            await _db.SaveChangesAsync();

            _db.AuditLogEntries.Add(Renamed(food.Id, food.Name, name, now, personId));
            food.Name = name;
            food.NameNormalized = IngredientNameNormalizer.Normalize(name);
            food.LastModifiedDate = now;
            food.LastModifiedByPersonId = personId;

            var competing = await _db.IngredientAliases
                .Where(a => a.IngredientId != ingredientId && a.AliasName.ToLower() == lowered)
                .ToListAsync();
            foreach (var alias in competing)
            {
                alias.IsDeleted = true;
                alias.DeletedAt = now;
                alias.DeletedByPersonId = personId;
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RetireEntityType,
                    EntityId = alias.IngredientId,
                    ChangeType = RetireChangeType,
                    PropertyName = nameof(IngredientEntity.Aliases),
                    OldValue = alias.AliasName,
                    NewValue = null,
                    Timestamp = now,
                    ChangedByPersonId = personId,
                });
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            result.Renamed = true;
            result.Name = name;
            result.AliasesRemoved = competing.Count;
            return result;
        }

        private static AuditLogEntryEntity Renamed(long foodId, string oldName, string newName, DateTime at, long personId) => new()
        {
            EntityType = RetireEntityType,
            EntityId = foodId,
            ChangeType = RetireChangeType,
            PropertyName = nameof(IngredientEntity.Name),
            OldValue = oldName,
            NewValue = newName,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        private static List<LineRelinkMove> Grouped(IEnumerable<Move> moves, Func<long, string> name) =>
            moves
                .GroupBy(m => (m.Line.IngredientId, m.Wording, m.TargetId))
                .Select(g => new LineRelinkMove
                {
                    FromIngredientId = g.Key.IngredientId,
                    FromName = name(g.Key.IngredientId),
                    Wording = g.Key.Wording,
                    ToIngredientId = g.Key.TargetId,
                    ToName = name(g.Key.TargetId),
                    Lines = g.Count(),
                })
                .OrderByDescending(m => m.Lines).ThenBy(m => m.FromIngredientId).ThenBy(m => m.Wording, StringComparer.Ordinal)
                .ToList();
    }
}
