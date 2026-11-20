using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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
    /// The undo of the clean-room prose lane's rewrites, for an operator
    /// (<c>POST api/ProseRewrite/undo</c>) and for the lane frame when the lane pauses itself. It
    /// restores, exactly as journaled (<see cref="ProseLaneJournal"/>), the description, the step
    /// rows (reviving the ones the rewrite removed and removing the ones it added) and the prose
    /// state, so a recipe goes back to withheld or quarantined and its old text is never shown
    /// again; a rewrite that changed only the description restores only the description and the
    /// state. A recipe a person edited since, or whose prose a later journal entry changed
    /// (<see cref="ProseLaneJournal.TouchesProse"/>), is skipped and counted. Each restore writes a <see cref="ProseLaneJournal.UndoChangeType"/> row, so a
    /// repeat dry run shows nothing left, and the rewrite's journal is kept.
    /// </summary>
    public class ProseRewriteUndoService : IProseRewriteUndoService, ILaneUndo
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public ProseRewriteUndoService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        public string Lane => AiLanes.ProseRewrite;

        public async Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken)
        {
            var result = await UndoAsync(sinceUtc, null, apply, cancellationToken);
            return new LaneUndoResult(result.Restored, result.Skipped, result.Remaining, $"prose rewrites since {sinceUtc:u}");
        }

        public async Task<ProseRewriteUndoResult> UndoAsync(DateTime sinceUtc, string? laneVersion = null, bool apply = false, CancellationToken cancellationToken = default)
        {
            var result = new ProseRewriteUndoResult { Applied = apply, SinceUtc = sinceUtc, LaneVersion = laneVersion };
            var states = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == ProseLaneJournal.RewriteChangeType
                    && a.PropertyName == ProseLaneJournal.StateProperty && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
            if (!string.IsNullOrEmpty(laneVersion))
                states = states.Where(s => (s.NewValue ?? string.Empty).Contains(" " + laneVersion + "]", StringComparison.Ordinal)).ToList();

            var recipeIds = states.Select(s => s.EntityId).Distinct().ToList();
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == ProseLaneJournal.UndoChangeType && recipeIds.Contains(a.EntityId))
                    .Select(a => a.OldValue)
                    .ToListAsync(cancellationToken))
                .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .ToHashSet();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var recipeId in recipeIds)
            {
                var journal = await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == recipeId)
                    .OrderBy(a => a.Id)
                    .ToListAsync(cancellationToken);
                var groupStates = journal.Where(a => a.ChangeType == ProseLaneJournal.RewriteChangeType && a.PropertyName == ProseLaneJournal.StateProperty).ToList();
                bool InUndoneGroup(AuditLogEntryEntity a) =>
                    a.ChangeType == ProseLaneJournal.UndoChangeType
                    || (a.ChangeType == ProseLaneJournal.RewriteChangeType && groupStates.Any(s => s.Timestamp == a.Timestamp && undone.Contains(s.Id)));

                var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);
                var steps = await _db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == recipeId).OrderBy(s => s.StepNumber).ThenBy(s => s.Id).ToListAsync(cancellationToken);

                foreach (var state in states.Where(s => s.EntityId == recipeId && !undone.Contains(s.Id)).OrderByDescending(s => s.Id))
                {
                    var group = journal.Where(a => a.ChangeType == ProseLaneJournal.RewriteChangeType && a.Timestamp == state.Timestamp).ToList();
                    var last = group.Max(a => a.Id);
                    var descriptionOnly = !group.Any(a => a.PropertyName != null && a.PropertyName.StartsWith(ProseLaneJournal.StepPrefix, StringComparison.Ordinal));
                    var personEdited = recipe == null
                        || steps.Any(s => !s.IsDeleted && s.LastModifiedByPersonId is { } by && by != SystemConstants.SystemPersonId && (!descriptionOnly || s.LastModifiedDate > state.Timestamp))
                        || (recipe.LastModifiedByPersonId is { } editor && editor != SystemConstants.SystemPersonId && recipe.LastModifiedDate > state.Timestamp);
                    if (personEdited)
                    {
                        result.Skipped++;
                        result.SkippedEditedByPerson++;
                        break;
                    }

                    if (journal.Any(a => a.Id > last && ProseLaneJournal.TouchesProse(a) && !InUndoneGroup(a)))
                    {
                        result.Skipped++;
                        result.SkippedChangedSince++;
                        break;
                    }

                    var old = ProseLaneJournal.OldValues(group);
                    if (ProseLaneJournal.ReadState(old.GetValueOrDefault(ProseLaneJournal.StateProperty)) is not { } before)
                    {
                        result.Skipped++;
                        result.SkippedChangedSince++;
                        break;
                    }

                    result.Restored++;
                    if (result.RecipeIds.Count < ProseRecheckService.MaxListedRecipeIds && !result.RecipeIds.Contains(recipeId)) result.RecipeIds.Add(recipeId);
                    undone.Add(state.Id);
                    if (!apply) continue;

                    var now = DateTime.UtcNow;
                    Restore(recipe!, steps, old, before, now);
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = ProseRecheckService.RecipeEntityType,
                        EntityId = recipeId,
                        ChangeType = ProseLaneJournal.UndoChangeType,
                        PropertyName = ProseLaneJournal.StateProperty,
                        OldValue = state.Id.ToString(CultureInfo.InvariantCulture),
                        NewValue = ProseRecheckService.Clamp(ProseLaneJournal.StateJson(before)),
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                }

                if (apply) await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            result.Remaining = apply ? 0 : result.Restored;
            return result;
        }

        private void Restore(RecipeEntity recipe, List<RecipeStepEntity> steps, Dictionary<string, string> old, ProseState before, DateTime now)
        {
            if (old.TryGetValue(ProseLaneJournal.DescriptionProperty, out var description)) recipe.Description = description;
            if (old.Keys.Any(k => k.StartsWith(ProseLaneJournal.StepPrefix, StringComparison.Ordinal)))
            {
                var texts = (before.StepNumbers ?? Array.Empty<int>()).Distinct()
                    .ToDictionary(n => n, n => old.GetValueOrDefault(ProseLaneJournal.StepPrefix + n.ToString(CultureInfo.InvariantCulture)) ?? string.Empty);
                ProseLaneJournal.SetSteps(_db, recipe.Id, steps, texts, now);
            }

            before.ApplyTo(recipe);
            recipe.LastModifiedDate = now;
            recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
        }
    }
}
