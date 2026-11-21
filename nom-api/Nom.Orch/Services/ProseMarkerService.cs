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
    /// Takes the step markers <see cref="ProseQuantities.HasStepMarker"/> finds out of the steps of
    /// scraped recipes (<see cref="ProseQuantities.StripStepMarkers"/>), journaled per step as
    /// <see cref="StripChangeType"/> on the recipe with the property <c>RecipeStep:{number}</c> and
    /// the whole old text, and puts them back on undo.
    /// </summary>
    public class ProseMarkerService : IProseMarkerService
    {
        public const string StripChangeType = "ProseMarkerStrip";
        public const string StripUndoChangeType = "ProseMarkerStripUndo";
        public const string StepPropertyPrefix = "RecipeStep:";

        private const int RecipesPerRead = 2000;
        private const int RecipesPerTransaction = 200;

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public ProseMarkerService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Change(long RecipeId, int StepNumber, string Before, string After, bool Shown);

        public static string StepProperty(int stepNumber) => StepPropertyPrefix + stepNumber.ToString(CultureInfo.InvariantCulture);

        public async Task<ProseMarkerStripResult> StripAsync(bool apply = false, int maxRecipes = 1000, int sampleSize = 20, CancellationToken cancellationToken = default)
        {
            var result = new ProseMarkerStripResult { Applied = apply };
            var changes = new List<Change>();
            var recipes = new HashSet<long>();
            var cursor = long.MinValue;
            while (true)
            {
                var page = await _db.Recipes.AsNoTracking()
                    .Where(r => r.Id > cursor && r.ScrapedAtUtc != null)
                    .OrderBy(r => r.Id)
                    .Take(RecipesPerRead)
                    .Select(r => new { r.Id, Hidden = r.ProseWithheld || r.ContainsSourceProse, r.LicenseStatus })
                    .ToListAsync(cancellationToken);
                if (page.Count == 0) break;
                cursor = page[^1].Id;
                var ids = page.Select(r => r.Id).ToList();
                var hidden = page.Where(r => r.Hidden).Select(r => r.Id).ToHashSet();
                var licences = page.ToDictionary(r => r.Id, r => string.IsNullOrEmpty(r.LicenseStatus) ? "none" : r.LicenseStatus);

                var steps = await _db.RecipeSteps.AsNoTracking()
                    .Where(s => ids.Contains(s.RecipeId))
                    .OrderBy(s => s.RecipeId).ThenBy(s => s.StepNumber)
                    .Select(s => new { s.RecipeId, s.StepNumber, s.Description, s.LastModifiedByPersonId })
                    .ToListAsync(cancellationToken);
                foreach (var step in steps)
                {
                    if (!ProseQuantities.HasStepMarker(step.Description)) continue;
                    result.StepsWithMarker++;
                    var licence = licences[step.RecipeId];
                    result.StepsByLicence[licence] = result.StepsByLicence.GetValueOrDefault(licence) + 1;
                    var shownUnknown = licence == RecipeLicenseStatus.Unknown && !hidden.Contains(step.RecipeId);
                    if (shownUnknown) result.StepsShownUnknownLicence++;
                    if (recipes.Add(step.RecipeId))
                    {
                        result.RecipesByLicence[licence] = result.RecipesByLicence.GetValueOrDefault(licence) + 1;
                        if (shownUnknown) result.RecipesShownUnknownLicence++;
                    }
                    if (step.LastModifiedByPersonId is { } by && by != SystemConstants.SystemPersonId)
                    {
                        result.SkippedEditedByPerson++;
                        continue;
                    }

                    if (step.Description.Length > ProseRecheckService.JournalLength)
                    {
                        result.SkippedTooLong++;
                        continue;
                    }

                    var after = ProseQuantities.StripStepMarkers(step.Description);
                    if (after == step.Description)
                    {
                        result.Unmatched++;
                        continue;
                    }

                    changes.Add(new Change(step.RecipeId, step.StepNumber, step.Description, after, !hidden.Contains(step.RecipeId)));
                }
            }

            result.Recipes = recipes.Count;
            var taken = changes
                .GroupBy(c => c.RecipeId)
                .OrderBy(g => g.Key)
                .Take(Math.Clamp(maxRecipes, 1, 100000))
                .SelectMany(g => g)
                .ToList();
            result.Stripped = taken.Count;
            result.Removed = taken.Count(c => c.After.Length == 0);
            result.RecipesChanged = taken.Select(c => c.RecipeId).Distinct().Count();
            result.Remaining = changes.Count - taken.Count;
            result.Samples = taken.Where(c => c.Shown).Take(Math.Clamp(sampleSize, 0, 1000)).Select(c => new ProseMarkerSample
            {
                RecipeId = c.RecipeId, StepNumber = c.StepNumber, Before = c.Before, After = c.After,
            }).ToList();
            if (!apply) return result;

            var personId = _currentUser.PersonIdOrSystem;
            var written = 0;
            var removed = 0;
            foreach (var chunk in taken.GroupBy(c => c.RecipeId).Chunk(RecipesPerTransaction))
            {
                var ids = chunk.Select(g => g.Key).ToList();
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                var steps = (await _db.RecipeSteps.Where(s => ids.Contains(s.RecipeId)).ToListAsync(cancellationToken))
                    .ToDictionary(s => (s.RecipeId, s.StepNumber));
                var now = DateTime.UtcNow;
                foreach (var change in chunk.SelectMany(g => g))
                {
                    if (!steps.TryGetValue((change.RecipeId, change.StepNumber), out var step) || step.Description != change.Before) continue;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = ProseRecheckService.RecipeEntityType,
                        EntityId = change.RecipeId,
                        ChangeType = StripChangeType,
                        PropertyName = StepProperty(change.StepNumber),
                        OldValue = change.Before,
                        NewValue = change.After,
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    step.Description = change.After;
                    step.LastModifiedDate = now;
                    step.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    if (change.After.Length == 0)
                    {
                        step.IsDeleted = true;
                        step.DeletedAt = now;
                        step.DeletedByPersonId = SystemConstants.SystemPersonId;
                        removed++;
                    }

                    written++;
                }

                await _db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            result.Remaining += result.Stripped - written;
            result.Stripped = written;
            result.Removed = removed;
            return result;
        }

        public async Task<ProseMarkerUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false, CancellationToken cancellationToken = default)
        {
            var result = new ProseMarkerUndoResult { Applied = apply };
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == StripUndoChangeType && a.Timestamp >= sinceUtc)
                    .Select(a => a.OldValue)
                    .ToListAsync(cancellationToken))
                .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .ToHashSet();
            var entries = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == StripChangeType && a.Timestamp >= sinceUtc)
                    .OrderByDescending(a => a.Id)
                    .ToListAsync(cancellationToken))
                .Where(a => !undone.Contains(a.Id))
                .ToList();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(RecipesPerTransaction))
            {
                var ids = chunk.Select(e => e.EntityId).Distinct().ToList();
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                var steps = (await _db.RecipeSteps.IgnoreQueryFilters().Where(s => ids.Contains(s.RecipeId)).ToListAsync(cancellationToken))
                    .ToDictionary(s => (s.RecipeId, s.StepNumber));
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    var removed = string.IsNullOrEmpty(entry.NewValue);
                    if (!TryStepNumber(entry.PropertyName, out var stepNumber)
                        || !steps.TryGetValue((entry.EntityId, stepNumber), out var step)
                        || step.Description != (entry.NewValue ?? string.Empty)
                        || step.IsDeleted != removed)
                    {
                        result.Skipped++;
                        continue;
                    }

                    result.Restored++;
                    if (!apply) continue;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = ProseRecheckService.RecipeEntityType,
                        EntityId = entry.EntityId,
                        ChangeType = StripUndoChangeType,
                        PropertyName = entry.PropertyName,
                        OldValue = entry.Id.ToString(CultureInfo.InvariantCulture),
                        NewValue = entry.OldValue,
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    step.Description = entry.OldValue ?? string.Empty;
                    step.LastModifiedDate = now;
                    step.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    if (removed)
                    {
                        step.IsDeleted = false;
                        step.DeletedAt = null;
                        step.DeletedByPersonId = null;
                    }
                }

                if (!apply) continue;
                await _db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            result.Remaining = apply ? 0 : result.Restored;
            return result;
        }

        private static bool TryStepNumber(string? property, out int stepNumber)
        {
            stepNumber = 0;
            return property != null && property.StartsWith(StepPropertyPrefix, StringComparison.Ordinal)
                && int.TryParse(property[StepPropertyPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out stepNumber);
        }
    }
}
