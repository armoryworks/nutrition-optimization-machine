using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// Runs <see cref="RewriteFidelity"/> over the scraped recipes whose source-prose quarantine
    /// was lifted, comparing the recipe's description and steps with the description and method
    /// of its stored JSON-LD, and puts the ones that fail back into quarantine for the rewrite
    /// lane. Each one put back is journaled with the rewrite stamp it had.
    /// </summary>
    public class ProseRecheckService : IProseRecheckService
    {
        public const string RecipeEntityType = "Recipe";
        public const string RewriteChangeType = "ProseRewrite";
        public const string RecheckChangeType = "ProseRecheck";
        public const int JournalLength = 4000;

        private const int RecipesPerRead = 500;
        private const int RecipesPerTransaction = 200;

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public ProseRecheckService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Candidate(long Id, string Name, long CurationStatusId, string? Description, DateTime? AttemptedAt);
        private sealed record Failure(Candidate Recipe, RewriteFidelity.Finding Finding);

        public async Task<ProseRecheckResult> RecheckAsync(bool apply = false, int maxRecipes = 50000, int sampleSize = 50)
        {
            var result = new ProseRecheckResult { Applied = apply };
            var candidates = await _db.Recipes.AsNoTracking()
                .Where(r => !r.ContainsSourceProse && r.ScrapedAtUtc != null && r.LicenseStatus == RecipeLicenseStatus.Unknown)
                .OrderBy(r => r.Id)
                .Select(r => new Candidate(r.Id, r.Name, r.CurationStatusId, r.Description, r.ProseRewriteAttemptedAt))
                .ToListAsync();
            result.Examined = candidates.Count;

            var failures = new List<Failure>();
            foreach (var chunk in candidates.Chunk(RecipesPerRead))
            {
                var ids = chunk.Select(c => c.Id).ToList();
                var steps = (await _db.RecipeSteps.AsNoTracking()
                        .Where(s => ids.Contains(s.RecipeId))
                        .Select(s => new { s.RecipeId, s.StepNumber, s.Description, s.LastModifiedByPersonId })
                        .ToListAsync())
                    .ToLookup(s => s.RecipeId);
                var sources = (await _db.ScrapedDocuments.AsNoTracking()
                        .Where(d => ids.Contains(d.RecipeId))
                        .OrderBy(d => d.Id)
                        .Select(d => new { d.RecipeId, d.RawJsonLd })
                        .ToListAsync())
                    .GroupBy(d => d.RecipeId)
                    .ToDictionary(g => g.Key, g => g.Last().RawJsonLd);

                foreach (var candidate in chunk)
                {
                    var own = steps[candidate.Id].OrderBy(s => s.StepNumber).ToList();
                    if (own.Any(s => s.LastModifiedByPersonId is { } by && by != SystemConstants.SystemPersonId))
                    {
                        result.SkippedEditedByPerson++;
                        continue;
                    }

                    if (RewriteFidelity.Source(sources.GetValueOrDefault(candidate.Id)) is not { } source)
                    {
                        result.SkippedNoSource++;
                        continue;
                    }

                    var finding = RewriteFidelity.Check(
                        source,
                        new RewriteFidelity.SourceText(candidate.Description ?? string.Empty, own.Select(s => s.Description).ToList()));
                    if (finding.Passed)
                    {
                        result.Passed++;
                        continue;
                    }

                    failures.Add(new Failure(candidate, finding));
                    Count(result, candidate, finding);
                }
            }

            result.Samples = failures
                .OrderBy(f => f.Recipe.CurationStatusId == (long)CurationStatusEnum.Curated ? 0 : 1)
                .ThenBy(f => f.Recipe.Id)
                .Take(Math.Clamp(sampleSize, 0, 5000))
                .Select(f => new ProseRecheckSample
                {
                    RecipeId = f.Recipe.Id,
                    Name = f.Recipe.Name,
                    Status = StatusName(f.Recipe.CurationStatusId),
                    Reasons = f.Finding.Reasons.ToList(),
                    Detail = f.Finding.ToString(),
                })
                .ToList();

            if (apply)
            {
                var batch = failures.Take(Math.Clamp(maxRecipes, 1, 100000)).ToList();
                var personId = _currentUser.PersonIdOrSystem;
                foreach (var chunk in batch.Chunk(RecipesPerTransaction))
                {
                    var ids = chunk.Select(f => f.Recipe.Id).ToList();
                    await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                    var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
                    var now = DateTime.UtcNow;
                    foreach (var failure in chunk)
                    {
                        if (!recipes.TryGetValue(failure.Recipe.Id, out var recipe) || recipe.ContainsSourceProse) continue;

                        _db.AuditLogEntries.Add(new AuditLogEntryEntity
                        {
                            EntityType = RecipeEntityType,
                            EntityId = recipe.Id,
                            ChangeType = RecheckChangeType,
                            PropertyName = nameof(RecipeEntity.ContainsSourceProse),
                            OldValue = recipe.ProseRewriteAttemptedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                            NewValue = Clamp(failure.Finding.ToString()),
                            Timestamp = now,
                            ChangedByPersonId = personId,
                        });
                        recipe.ContainsSourceProse = true;
                        recipe.ProseRewriteAttemptedAt = null;
                        recipe.LastModifiedDate = now;
                        recipe.LastModifiedByPersonId = personId;
                        result.Requarantined++;
                    }

                    await _db.SaveChangesAsync();
                    if (tx != null) await tx.CommitAsync();
                    _db.ChangeTracker.Clear();
                }
            }

            result.Remaining = result.Failed - result.Requarantined;
            return result;
        }

        public async Task<ProseRecheckUndoResult> UndoAsync(DateTime sinceUtc)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == RecheckChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var result = new ProseRecheckUndoResult();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(RecipesPerTransaction))
            {
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var ids = chunk.Select(e => e.EntityId).Distinct().ToList();
                var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (!recipes.TryGetValue(entry.EntityId, out var recipe)
                        || !recipe.ContainsSourceProse || recipe.ProseRewriteAttemptedAt != null)
                    {
                        result.Skipped++;
                        continue;
                    }

                    recipe.ContainsSourceProse = false;
                    recipe.ProseRewriteAttemptedAt = DateTime.TryParse(entry.OldValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                        ? at
                        : null;
                    recipe.LastModifiedDate = now;
                    recipe.LastModifiedByPersonId = personId;
                    _db.AuditLogEntries.Remove(entry);
                    result.Restored++;
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }

            return result;
        }

        public static string Clamp(string value) => value.Length <= JournalLength ? value : value[..JournalLength];

        private static void Count(ProseRecheckResult result, Candidate candidate, RewriteFidelity.Finding finding)
        {
            var approved = candidate.CurationStatusId == (long)CurationStatusEnum.Curated;
            var copied = finding.Copied != null;
            result.Failed++;
            if (approved) result.FailedApproved++;
            if (approved && copied) result.FailedApprovedCopied++;
            if (copied) result.FailedCopied++;
            if (finding.Lost.Count > 0) result.FailedLostNumber++;
            if (finding.Added.Count > 0) result.FailedAddedNumber++;
            if (finding.IsUnchanged) result.FailedUnchanged++;
            var status = StatusName(candidate.CurationStatusId);
            result.FailedByStatus[status] = result.FailedByStatus.GetValueOrDefault(status) + 1;
        }

        private static string StatusName(long statusId) =>
            Enum.IsDefined(typeof(CurationStatusEnum), statusId)
                ? ((CurationStatusEnum)statusId).ToString()
                : statusId.ToString(CultureInfo.InvariantCulture);
    }
}
