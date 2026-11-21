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

            var (returning, removed) = await ReturnLandingsAsync(usable, apply, result);
            var keys = usable.Keys.ToList();
            var sources = (await _db.Ingredients.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.FdcId == null && i.CurationStatusId != (long)CurationStatusEnum.Curated
                        && keys.Contains(i.Name.ToLower()))
                    .Select(i => new { i.Id, i.Name })
                    .ToListAsync())
                .Select(i => new { i.Id, i.Name, Wording = IngredientNameNormalizer.WordingKey(i.Name) })
                .Where(i => usable.TryGetValue(i.Wording, out var targetId) && targetId != i.Id && !removed.Contains(i.Id))
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
            lines = lines.Where(l => !returning.Contains((l.RecipeId, l.IngredientId, l.SortOrder))).ToList();
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
                    var left = lineCounts.GetValueOrDefault(id) - (apply ? 0 : planned.GetValueOrDefault(id) + returning.Count(r => r.IngredientId == id));
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

                var retiredWith = retirements.Select(a => (a.EntityId, a.Timestamp, a.ChangedByPersonId)).ToHashSet();
                var owners = await _db.FoodCatalogProposals.IgnoreQueryFilters()
                    .Where(p => p.IngredientId != null && uncuratedRetired.Contains(p.IngredientId.Value))
                    .ToDictionaryAsync(p => p.Id, p => p.IngredientId!.Value);
                var proposalIds = owners.Keys.ToList();
                var withdrawals = (await _db.AuditLogEntries
                        .Where(a => a.EntityType == PickWithdrawEntityType && a.ChangeType == ProposalWithdrawChangeType && proposalIds.Contains(a.EntityId))
                        .ToListAsync())
                    .Where(a => retiredWith.Contains((owners[a.EntityId], a.Timestamp, a.ChangedByPersonId)))
                    .ToList();
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
            var earlier = wordings.GroupBy(w => w.Wording, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Earlier, StringComparer.Ordinal);
            var judged = new HashSet<long>();
            foreach (var chunk in pickEntries.Select(a => a.EntityId).Distinct().Chunk(1000))
            {
                var picks = await _db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => chunk.Contains(p.Id))
                    .Select(p => new { p.Id, p.CurrentValue, p.ProposedValue })
                    .ToListAsync();
                judged.UnionWith(picks
                    .Where(p => earlier.TryGetValue(IngredientNameNormalizer.WordingKey(p.CurrentValue ?? string.Empty), out var back)
                        && (back == null || (back.Verdict == IngredientWordingVerdict.Food
                            && back.IngredientId?.ToString(System.Globalization.CultureInfo.InvariantCulture) == p.ProposedValue)))
                    .Select(p => p.Id));
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

        private static AuditLogEntryEntity CreatedForLandings(long ingredientId, string wording, DateTime at, long personId) => new()
        {
            EntityType = RetireEntityType,
            EntityId = ingredientId,
            ChangeType = WordingRetractChangeType,
            PropertyName = nameof(IngredientEntity.Name),
            OldValue = null,
            NewValue = wording,
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        /// <summary>
        /// Moves each import landing a retract or an unload took off a wording's food back onto it,
        /// now that the table gives the wording that food again, and journals it as a landing
        /// (<see cref="WordingResolveChangeType"/>); an ingredient the retract created for such
        /// landings that holds nothing once they are back is deleted. A line no longer where the
        /// retract put it, or on a recipe rebuilt since, stays with its entry. Returns the lines a dry
        /// run would move and the ingredients that are, or would be, deleted.
        /// </summary>
        private async Task<(HashSet<(long RecipeId, long IngredientId, int SortOrder)> Returning, HashSet<long> Removed)> ReturnLandingsAsync(
            Dictionary<string, long> usable, bool apply, VerifiedWordingLinkResult result)
        {
            var returning = new HashSet<(long RecipeId, long IngredientId, int SortOrder)>();
            var removed = new HashSet<long>();
            var entries = (await _db.AuditLogEntries
                    .Where(a => a.EntityType == RelinkEntityType && a.ChangeType == WordingRetractChangeType)
                    .ToListAsync())
                .Where(a => ReadPlace(a.OldValue) != null && ReadPlace(a.NewValue) != null)
                .ToLookup(a => ReadPlace(a.OldValue)!.Value.IngredientId);
            var created = (await _db.AuditLogEntries
                    .Where(a => a.EntityType == RetireEntityType && a.ChangeType == WordingRetractChangeType && a.NewValue != null)
                    .ToListAsync())
                .Where(a => usable.ContainsKey(a.NewValue!))
                .ToList();
            if (entries.Count == 0) return (returning, removed);

            var builder = new RecipeLineBuilder(_db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, resolveVerifiedWordings: false);
            var plan = new List<Landing>();
            foreach (var (wording, targetId) in usable.Where(u => entries.Contains(u.Value)))
            {
                var homes = created.Where(a => a.NewValue == wording).Select(a => a.EntityId).ToHashSet();
                if (await builder.FindWithoutWordingAsync(wording) is { } found) homes.Add(found.Food.Id);
                plan.AddRange(entries[targetId].Where(a => homes.Contains(ReadPlace(a.NewValue)!.Value.IngredientId)).Select(a => new Landing(a, wording, targetId)));
            }

            plan = plan.DistinctBy(l => l.Entry.Id).OrderByDescending(l => l.Entry.Id).ToList();
            var rebuilt = await RecipeReprocessService.RebuiltAtAsync(_db, plan.Select(l => l.Entry.EntityId).Distinct().ToList());
            var skipped = plan.Where(l => rebuilt.TryGetValue(l.Entry.EntityId, out var at) && at > l.Entry.Timestamp).ToList();
            result.LandingsReturnSkippedRebuilt = skipped.Count;
            plan = plan.Except(skipped).ToList();
            var trail = await RecipeOrderTrail.LoadAsync(_db, plan.Select(l => l.Entry.EntityId));
            var standing = new Dictionary<long, int>();
            foreach (var landing in plan)
            {
                var now = ReadPlace(landing.Entry.NewValue)!.Value;
                if (trail.Follow(landing.Entry.EntityId, now.IngredientId, now.SortOrder, landing.Entry.Timestamp) is not { } at) continue;
                if (await _db.RecipeIngredients.AnyAsync(ri => ri.RecipeId == landing.Entry.EntityId && ri.IngredientId == now.IngredientId && ri.SortOrder == at && !ri.IsDeleted))
                {
                    returning.Add((landing.Entry.EntityId, now.IngredientId, at));
                    standing[landing.Entry.Id] = at;
                }
            }

            foreach (var creation in created.Where(a => plan.Any(l => l.Wording == a.NewValue)).DistinctBy(a => a.EntityId))
            {
                var id = creation.EntityId;
                var onIt = await _db.RecipeIngredients.IgnoreQueryFilters().CountAsync(ri => ri.IngredientId == id);
                var placeOf = id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "@";
                if (onIt == returning.Count(r => r.IngredientId == id)
                    && await _db.Ingredients.AnyAsync(i => i.Id == id && !i.IsDeleted)
                    && !await _db.FoodCatalogProposals.IgnoreQueryFilters().AnyAsync(p => p.IngredientId == id)
                    && !await _db.IngredientAliases.IgnoreQueryFilters().AnyAsync(a => a.IngredientId == id)
                    && !await _db.IngredientNutrients.IgnoreQueryFilters().AnyAsync(n => n.IngredientId == id)
                    && !await _db.AuditLogEntries.AnyAsync(a => a.EntityType == RelinkEntityType && (a.ChangeType == RelinkChangeType || a.ChangeType == LinkRecheckService.LineChangeType)
                        && a.OldValue != null && a.OldValue.StartsWith(placeOf))
                    && !await IsUsedOutsideRecipeLinesAsync(id))
                    removed.Add(id);
            }

            result.LandingsReturned = returning.Count;
            result.IngredientsRemoved = removed.Count;
            if (!apply) return (returning, removed);

            var personId = _currentUser.PersonIdOrSystem;
            result.LandingsReturned = 0;
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
            foreach (var landing in plan)
            {
                var entry = landing.Entry;
                var was = ReadPlace(entry.OldValue)!.Value;
                var now = ReadPlace(entry.NewValue)!.Value;
                if (!standing.TryGetValue(entry.Id, out var stands) || !returning.Contains((entry.EntityId, now.IngredientId, stands))) continue;
                var position = await MoveAsync(entry.EntityId, now.IngredientId, stands, landing.TargetId, trail.Moved(entry.EntityId, entry.Timestamp) ? stands : was.SortOrder, personId);
                if (position == null) continue;

                var at = DateTime.UtcNow;
                var followed = LinkApplyJournal.Place(entry.EntityId, now.IngredientId, now.SortOrder);
                _db.AuditLogEntries.RemoveRange(await _db.AuditLogEntries
                    .Where(a => a.EntityType == LinkApplyJournal.EntityType && a.ChangeType == LinkApplyJournal.ChangeType && a.PropertyName == LinkApplyJournal.Line
                        && a.NewValue == followed && a.Timestamp == entry.Timestamp)
                    .ToListAsync());
                _db.AuditLogEntries.Remove(entry);
                _db.AuditLogEntries.Add(WordingResolved(entry.EntityId, landing.Wording, landing.TargetId, position.Value, at, personId));
                result.LandingsReturned++;
            }

            await _db.SaveChangesAsync();
            var gone = new HashSet<long>();
            foreach (var id in removed)
            {
                if (await _db.RecipeIngredients.IgnoreQueryFilters().AnyAsync(ri => ri.IngredientId == id)) continue;
                _db.Ingredients.Remove(await _db.Ingredients.SingleAsync(i => i.Id == id));
                _db.AuditLogEntries.RemoveRange(created.Where(a => a.EntityId == id));
                gone.Add(id);
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
            result.IngredientsRemoved = gone.Count;
            return (new HashSet<(long RecipeId, long IngredientId, int SortOrder)>(), gone);
        }

        /// <summary>
        /// Moves each line an import put on a wording's food through the wording to the food the
        /// import finds without it, newest first, journaled as <see cref="WordingRetractChangeType"/>.
        /// A line no longer where it landed, or on a recipe rebuilt since, stays with its entry.
        /// </summary>
        private async Task PutBackLandingsAsync(List<Landing> landings, IReadOnlyDictionary<long, DateTime> rebuilt, WordingLinesUndoResult result)
        {
            if (landings.Count == 0) return;
            var builder = new RecipeLineBuilder(_db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, resolveVerifiedWordings: false);
            var homes = new Dictionary<string, (IngredientEntity Food, RecipeLineBuilder.FollowedLink? Link, bool Created)>(StringComparer.Ordinal);
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in landings.OrderByDescending(l => l.Entry.Id).Chunk(500))
            {
                await using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
                var trail = await RecipeOrderTrail.LoadAsync(_db, chunk.Select(l => l.Entry.EntityId));
                foreach (var landing in chunk)
                {
                    var entry = landing.Entry;
                    if (rebuilt.TryGetValue(entry.EntityId, out var rebuiltAt) && rebuiltAt > entry.Timestamp)
                    {
                        result.LandingsSkipped++;
                        result.LandingsSkippedRebuilt++;
                        continue;
                    }

                    if (ReadPlace(entry.NewValue) is not { } landed
                        || trail.Follow(entry.EntityId, landed.IngredientId, landed.SortOrder, entry.Timestamp) is not { } at
                        || !await _db.RecipeIngredients.AnyAsync(ri => ri.RecipeId == entry.EntityId && ri.IngredientId == landed.IngredientId && ri.SortOrder == at && !ri.IsDeleted))
                    {
                        result.LandingsSkipped++;
                        continue;
                    }

                    if (!homes.TryGetValue(landing.Wording, out var home))
                    {
                        homes[landing.Wording] = home = await builder.FoodWithoutWordingAsync(landing.Wording, personId);
                        if (home.Created) _db.AuditLogEntries.Add(CreatedForLandings(home.Food.Id, landing.Wording, DateTime.UtcNow, personId));
                    }

                    if (home.Food.Id == landing.TargetId)
                    {
                        _db.AuditLogEntries.Remove(entry);
                        result.LandingsKept++;
                        continue;
                    }

                    var position = await MoveAsync(entry.EntityId, landed.IngredientId, at, home.Food.Id, at, personId);
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
                        OldValue = Place(landed.IngredientId, at),
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

        public async Task<FoodRenameResult?> RenameAsync(long ingredientId, string name, DateTime? at = null)
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
            var now = at ?? DateTime.UtcNow;
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
