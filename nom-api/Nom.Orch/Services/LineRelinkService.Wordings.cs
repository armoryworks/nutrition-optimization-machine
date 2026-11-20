using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public partial class LineRelinkService
    {
        public async Task<VerifiedWordingLinkResult> LinkVerifiedWordingsAsync(bool apply = false, int maxLines = 20000, int sampleSize = 50, string? batch = null)
        {
            batch = string.IsNullOrWhiteSpace(batch) ? null : batch.Trim();
            var verified = await _db.IngredientWordings.AsNoTracking()
                .Where(w => w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null && (batch == null || w.Batch == batch))
                .Select(w => new { w.Wording, TargetId = w.IngredientId!.Value })
                .ToListAsync();
            var targetIds = verified.Select(w => w.TargetId).Distinct().ToList();
            var targets = await _db.Ingredients.AsNoTracking()
                .Where(i => targetIds.Contains(i.Id) && !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated
                    && i.IngredientNutrients.Any(n => !n.IsDeleted))
                .ExceptPrepared()
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
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new Line(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine))
                .ToListAsync();
            var all = lines.Select(line => new Move(line, sources[line.IngredientId].Wording, usable[sources[line.IngredientId].Wording])).ToList();
            var moves = all.Where(m => NamesWording(m.Line.RawLine, m.Wording)).ToList();
            var behind = all.Where(m => !NamesWording(m.Line.RawLine, m.Wording)).ToList();
            result.LeftBehind = behind.Count;
            var leftStep = Math.Max(1, behind.Count / Math.Max(1, Math.Clamp(sampleSize, 0, 5000)));
            result.LeftBehindSamples = behind.Where((_, i) => i % leftStep == 0).Take(Math.Clamp(sampleSize, 0, 5000))
                .Select(m => new VerifiedWordingLeftLine { RecipeId = m.Line.RecipeId, IngredientId = m.Line.IngredientId, Wording = m.Wording, Line = m.Line.RawLine })
                .ToList();

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

            var planned = moves.GroupBy(m => m.Line.IngredientId).ToDictionary(g => g.Key, g => g.Count());
            var lineCounts = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => sourceIds.Contains(ri.IngredientId))
                .GroupBy(ri => ri.IngredientId)
                .Select(g => new { g.Key, Lines = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Lines);
            var personId = _currentUser.PersonIdOrSystem;
            var now = DateTime.UtcNow;
            var allRetired = new List<long>();
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
                allRetired.AddRange(retired);
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
                    _db.AuditLogEntries.Add(Withdrawn(proposal.Id, ProposalWithdrawChangeType, now, personId));
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }

            await WithdrawContradictedPicksAsync(apply, allRetired, result, now, personId, batch);
            return result;
        }

        /// <summary>
        /// Withdraws each pending link proposal no person has reviewed whose wording the table has
        /// judged (in the given batch, when one is given) and whose pick is not the table's food, so it
        /// can no longer be applied from the queue. Each withdrawal is journaled
        /// (<see cref="PickWithdrawChangeType"/>) and taken back by the undo sweep.
        /// </summary>
        private async Task WithdrawContradictedPicksAsync(bool apply, List<long> retired, VerifiedWordingLinkResult result, DateTime now, long personId, string? batch)
        {
            var table = await _db.IngredientWordings.AsNoTracking()
                .Where(w => batch == null || w.Batch == batch)
                .Select(w => new { w.Wording, w.Verdict, w.IngredientId })
                .ToDictionaryAsync(w => w.Wording, StringComparer.Ordinal);
            if (table.Count == 0) return;

            var pending = await _db.FoodCatalogProposals.AsNoTracking()
                .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Pending
                    && p.IngredientId != null && p.CurrentValue != null
                    && (p.ReviewedByPersonId == null || p.ReviewedByPersonId == SystemConstants.SystemPersonId))
                .Select(p => new { p.Id, p.IngredientId, p.CurrentValue, p.ProposedValue })
                .ToListAsync();
            var contradicted = pending
                .Where(p => !retired.Contains(p.IngredientId!.Value)
                    && table.TryGetValue(IngredientNameNormalizer.WordingKey(p.CurrentValue!), out var verdict)
                    && !(verdict.Verdict == IngredientWordingVerdict.Food && verdict.IngredientId?.ToString(System.Globalization.CultureInfo.InvariantCulture) == p.ProposedValue))
                .Select(p => p.Id)
                .ToList();
            result.PicksWithdrawn = contradicted.Count;
            if (!apply || contradicted.Count == 0) return;

            foreach (var chunk in contradicted.Chunk(500))
            {
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                foreach (var proposal in await _db.FoodCatalogProposals.Where(p => chunk.Contains(p.Id)).ToListAsync())
                {
                    proposal.IsDeleted = true;
                    proposal.DeletedAt = now;
                    proposal.DeletedByPersonId = personId;
                    _db.AuditLogEntries.Add(Withdrawn(proposal.Id, PickWithdrawChangeType, now, personId));
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }
        }

        /// <summary>
        /// The journal entry of a recipe line an import or a rebuild put on a food through a verified
        /// wording: the recipe as EntityId, the wording key as OldValue and food@position as NewValue.
        /// </summary>
        public static AuditLogEntryEntity WordingResolved(long recipeId, string wording, long foodId, int sortOrder, DateTime at, long personId) => new()
        {
            EntityType = RelinkEntityType,
            EntityId = recipeId,
            ChangeType = WordingResolveChangeType,
            PropertyName = nameof(RecipeIngredientEntity.IngredientId),
            OldValue = wording,
            NewValue = Place(foodId, sortOrder),
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        public async Task<WordingLinesUndoResult> UndoForSourcesAsync(IReadOnlyCollection<WordingUndoScope> wordings, bool apply = false)
        {
            var result = new WordingLinesUndoResult { Applied = apply };
            var undo = new List<AuditLogEntryEntity>();
            var landings = new List<Landing>();
            var keys = wordings.Select(w => w.Wording).ToHashSet(StringComparer.Ordinal);

            foreach (var (wording, targetId) in wordings.Where(w => w.TargetId != null).Select(w => (w.Wording, w.TargetId!.Value)).Distinct())
            {
                var retiredInto = Place(targetId, 0);
                var retirements = (await _db.AuditLogEntries
                        .Where(a => a.EntityType == RetireEntityType && a.ChangeType == RetireChangeType
                            && a.PropertyName == nameof(IngredientEntity.IsDeleted) && a.NewValue == retiredInto)
                        .ToListAsync())
                    .Where(a => IngredientNameNormalizer.WordingKey(a.OldValue ?? string.Empty) == wording)
                    .ToList();
                var retiredIds = retirements.Select(a => a.EntityId).Distinct().ToList();
                var uncuratedRetired = await _db.Ingredients.IgnoreQueryFilters()
                    .Where(i => retiredIds.Contains(i.Id) && i.FdcId == null && i.CurationStatusId != (long)CurationStatusEnum.Curated)
                    .Select(i => i.Id)
                    .ToListAsync();
                retirements = retirements.Where(a => uncuratedRetired.Contains(a.EntityId)).ToList();
                var live = (await _db.Ingredients.AsNoTracking()
                        .Where(i => !i.IsDeleted && i.Id != targetId && i.FdcId == null && i.CurationStatusId != (long)CurationStatusEnum.Curated
                            && i.Name.ToLower() == wording)
                        .Select(i => new { i.Id, i.Name })
                        .ToListAsync())
                    .Where(i => IngredientNameNormalizer.WordingKey(i.Name) == wording)
                    .Select(i => i.Id);
                var sources = uncuratedRetired.Concat(live).Distinct().ToList();
                result.SourceIds.AddRange(sources);
                result.Retirements += retirements.Count;
                undo.AddRange(retirements);

                var onTarget = targetId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "@";
                var moves = (await _db.AuditLogEntries
                        .Where(a => a.EntityType == RelinkEntityType && a.ChangeType == RelinkChangeType && a.NewValue != null && a.NewValue.StartsWith(onTarget))
                        .ToListAsync())
                    .Where(a => ReadPlace(a.OldValue) is { } was && sources.Contains(was.IngredientId))
                    .ToList();
                result.LineMoves += moves.Count;
                undo.AddRange(moves);

                var proposalIds = await _db.FoodCatalogProposals.IgnoreQueryFilters()
                    .Where(p => p.IngredientId != null && sources.Contains(p.IngredientId.Value))
                    .Select(p => p.Id)
                    .ToListAsync();
                var withdrawals = await _db.AuditLogEntries
                    .Where(a => a.EntityType == PickWithdrawEntityType && a.ChangeType == ProposalWithdrawChangeType && proposalIds.Contains(a.EntityId))
                    .ToListAsync();
                result.ProposalWithdrawals += withdrawals.Count;
                undo.AddRange(withdrawals);

                var resolved = await _db.AuditLogEntries
                    .Where(a => a.EntityType == RelinkEntityType && a.ChangeType == WordingResolveChangeType && a.OldValue == wording
                        && a.NewValue != null && a.NewValue.StartsWith(onTarget))
                    .ToListAsync();
                result.ImportLandings += resolved.Count;
                landings.AddRange(resolved.Select(a => new Landing(a, wording, targetId)));
            }

            var pickEntries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == PickWithdrawEntityType && a.ChangeType == PickWithdrawChangeType)
                .Select(a => new { a.Id, a.EntityId })
                .ToListAsync();
            var judged = new HashSet<long>();
            foreach (var chunk in pickEntries.Select(a => a.EntityId).Distinct().Chunk(1000))
            {
                var picks = await _db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => chunk.Contains(p.Id))
                    .Select(p => new { p.Id, p.CurrentValue })
                    .ToListAsync();
                judged.UnionWith(picks.Where(p => keys.Contains(IngredientNameNormalizer.WordingKey(p.CurrentValue ?? string.Empty))).Select(p => p.Id));
            }
            var picksBack = new List<AuditLogEntryEntity>();
            foreach (var chunk in pickEntries.Where(a => judged.Contains(a.EntityId)).Select(a => a.Id).Chunk(1000))
                picksBack.AddRange(await _db.AuditLogEntries.Where(a => chunk.Contains(a.Id)).ToListAsync());
            result.PickWithdrawals = picksBack.Count;
            undo.AddRange(picksBack);
            undo = undo.DistinctBy(a => a.Id).ToList();

            var lineEntries = undo.Where(a => a.EntityType == RelinkEntityType).Concat(landings.Select(l => l.Entry)).ToList();
            var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_db, lineEntries.Select(a => a.EntityId).ToList());
            result.OnRebuiltRecipes = lineEntries.Count(a => rebuilt.TryGetValue(a.EntityId, out var at) && at > a.Timestamp);
            if (!apply) return result;

            result.Undone = await UndoEntriesAsync(undo);
            await PutBackLandingsAsync(landings, rebuilt, result);
            return result;
        }

        private sealed record Landing(AuditLogEntryEntity Entry, string Wording, long TargetId);

        /// <summary>
        /// Moves each line an import put on a wording's food through the wording to the food the
        /// import finds without it, newest first, journaled as <see cref="WordingRetractChangeType"/>.
        /// A line no longer where it landed, or on a recipe rebuilt since, stays with its entry.
        /// </summary>
        private async Task PutBackLandingsAsync(List<Landing> landings, IReadOnlyDictionary<long, DateTime> rebuilt, WordingLinesUndoResult result)
        {
            if (landings.Count == 0) return;
            var builder = new RecipeLineBuilder(_db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, resolveVerifiedWordings: false);
            var homes = new Dictionary<string, (IngredientEntity Food, RecipeLineBuilder.FollowedLink? Link)>(StringComparer.Ordinal);
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in landings.OrderByDescending(l => l.Entry.Id).Chunk(500))
            {
                await using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
                foreach (var landing in chunk)
                {
                    var entry = landing.Entry;
                    if (rebuilt.TryGetValue(entry.EntityId, out var rebuiltAt) && rebuiltAt > entry.Timestamp)
                    {
                        result.LandingsSkipped++;
                        result.LandingsSkippedRebuilt++;
                        continue;
                    }

                    if (ReadPlace(entry.NewValue) is not { } place
                        || !await _db.RecipeIngredients.AnyAsync(ri => ri.RecipeId == entry.EntityId && ri.IngredientId == place.IngredientId && ri.SortOrder == place.SortOrder && !ri.IsDeleted))
                    {
                        result.LandingsSkipped++;
                        continue;
                    }

                    if (!homes.TryGetValue(landing.Wording, out var home))
                        homes[landing.Wording] = home = await builder.FoodWithoutWordingAsync(landing.Wording, personId);

                    if (home.Food.Id == landing.TargetId)
                    {
                        _db.AuditLogEntries.Remove(entry);
                        result.LandingsKept++;
                        continue;
                    }

                    var position = await MoveAsync(entry.EntityId, place.IngredientId, place.SortOrder, home.Food.Id, place.SortOrder, personId);
                    if (position == null)
                    {
                        result.LandingsSkipped++;
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    _db.AuditLogEntries.Remove(entry);
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RelinkEntityType,
                        EntityId = entry.EntityId,
                        ChangeType = WordingRetractChangeType,
                        PropertyName = nameof(RecipeIngredientEntity.IngredientId),
                        OldValue = Place(place.IngredientId, place.SortOrder),
                        NewValue = Place(home.Food.Id, position.Value),
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    if (home.Link is { } link)
                    {
                        _db.AuditLogEntries.AddRange(LinkApplyJournal.LinesMoved(link.ProposalId, link.HolderId, home.Food.Id,
                            Array.Empty<LinkApplyJournal.LineShift>(),
                            new[] { new LinkApplyJournal.LinePlace { RecipeId = entry.EntityId, SortOrder = position.Value } }, now, personId));
                    }
                    result.LandingsMovedBack++;
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }
        }

        private static AuditLogEntryEntity Withdrawn(long proposalId, string changeType, DateTime at, long personId) => new()
        {
            EntityType = PickWithdrawEntityType,
            EntityId = proposalId,
            ChangeType = changeType,
            PropertyName = nameof(FoodCatalogProposalEntity.IsDeleted),
            OldValue = bool.FalseString,
            NewValue = bool.TrueString,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        /// <summary>
        /// Whether a recipe line's own text names a judged wording: the line, normalised as the
        /// wording key is, is the wording or its singular or plural.
        /// </summary>
        internal static bool NamesWording(string? rawLine, string wording) =>
            IngredientNameNormalizer.ExactKeys(rawLine ?? string.Empty).Any(k => string.Equals(k, wording, StringComparison.OrdinalIgnoreCase))
            || IngredientNameNormalizer.ExactKeys(wording).Any(k => string.Equals(k, IngredientNameNormalizer.WordingKey(rawLine ?? string.Empty), StringComparison.OrdinalIgnoreCase));

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
