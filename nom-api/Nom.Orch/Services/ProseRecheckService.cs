using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Runs <see cref="ProseCheck"/> over the scraped recipes whose prose is shown, comparing the
    /// recipe's description and steps with the description and method of its stored JSON-LD, and
    /// withholds the prose of the ones that fail (<see cref="RecipeEntity.ProseWithheld"/>): the
    /// recipe stays listed and usable, its text is never served, and the rewrite lane takes it up.
    /// Each recipe withheld is journaled (<see cref="WithholdChangeType"/>) with the finding and the
    /// check version it had, and the undo releases it again. A full run is recorded
    /// (<see cref="RunChangeType"/>) so the weekly job knows which check and stock-index versions
    /// have already been run.
    /// </summary>
    public class ProseRecheckService : IProseRecheckService
    {
        public const string RecipeEntityType = "Recipe";
        public const string RewriteChangeType = "ProseRewrite";
        public const string RecheckChangeType = "ProseRecheck";
        public const string WithholdChangeType = "ProseWithhold";
        public const string WithholdUndoChangeType = "ProseWithholdUndo";
        public const string WithholdEndChangeType = "ProseWithholdEnd";
        public const string RunEntityType = "ProseRecheck";
        public const string RunChangeType = "ProseRecheckRun";
        public const int JournalLength = 4000;
        public const int WithholdBatch = 500;
        public const int MaxListedRecipeIds = 1000;

        private const int RecipesPerRead = 500;
        private const int RecipesPerTransaction = 200;

        private static readonly Regex RunVersions = new(@"^check (?<check>\d+) stock (?<stock>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IProseStockService _stock;

        public ProseRecheckService(ApplicationDbContext db, ICurrentUserService currentUser, IProseStockService stock)
        {
            _db = db;
            _currentUser = currentUser;
            _stock = stock;
        }

        private sealed record Candidate(long Id, string Name, long CurationStatusId, string? Description, int? CheckVersion);
        private sealed record Failure(Candidate Recipe, ProseCheck.Finding Finding);

        public async Task<ProseRecheckResult> RecheckAsync(bool apply = false, int maxRecipes = WithholdBatch, int sampleSize = 50, Func<CancellationToken, Task<bool>>? hold = null, CancellationToken cancellationToken = default)
        {
            var stock = await _stock.LoadAsync(cancellationToken);
            var result = new ProseRecheckResult { Applied = apply, CheckVersion = ProseCheck.Version, StockVersion = stock.Version };
            if (apply && stock.Version == 0)
            {
                result.Applied = false;
                result.Refused = "The stock-phrase index has not been built, so every stock phrase would count as copied; run POST api/ProseRewrite/stock/rebuild?apply=true first.";
            }

            var candidates = await _db.Recipes.AsNoTracking()
                .Where(r => !r.ContainsSourceProse && !r.ProseWithheld && r.ScrapedAtUtc != null && r.LicenseStatus == RecipeLicenseStatus.Unknown)
                .OrderBy(r => r.Id)
                .Select(r => new Candidate(r.Id, r.Name, r.CurationStatusId, r.Description, r.ProseCheckVersion))
                .ToListAsync(cancellationToken);
            result.Examined = candidates.Count;

            var failures = new List<Failure>();
            var unstamped = new List<long>();
            foreach (var chunk in candidates.Chunk(RecipesPerRead))
            {
                var ids = chunk.Select(c => c.Id).ToList();
                var steps = (await _db.RecipeSteps.AsNoTracking()
                        .Where(s => ids.Contains(s.RecipeId))
                        .Select(s => new { s.RecipeId, s.StepNumber, s.Description, s.LastModifiedByPersonId })
                        .ToListAsync(cancellationToken))
                    .ToLookup(s => s.RecipeId);
                var sources = (await _db.ScrapedDocuments.AsNoTracking()
                        .Where(d => ids.Contains(d.RecipeId) && d.Format == ScrapedDocumentFormat.JsonLd)
                        .OrderBy(d => d.Id)
                        .Select(d => new { d.RecipeId, d.RawJsonLd })
                        .ToListAsync(cancellationToken))
                    .GroupBy(d => d.RecipeId)
                    .ToDictionary(g => g.Key, g => g.Last().RawJsonLd);
                var ended = (await _db.AuditLogEntries.AsNoTracking()
                        .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == WithholdEndChangeType && ids.Contains(a.EntityId))
                        .Select(a => a.EntityId)
                        .ToListAsync(cancellationToken))
                    .ToHashSet();

                foreach (var candidate in chunk)
                {
                    var own = steps[candidate.Id].OrderBy(s => s.StepNumber).ToList();
                    if (ended.Contains(candidate.Id) || own.Any(s => s.LastModifiedByPersonId is { } by && by != SystemConstants.SystemPersonId))
                    {
                        result.SkippedEditedByPerson++;
                        if (result.SkippedEditedRecipeIds.Count < MaxListedRecipeIds) result.SkippedEditedRecipeIds.Add(candidate.Id);
                        continue;
                    }

                    var raw = sources.GetValueOrDefault(candidate.Id);
                    if (RewriteFidelity.Source(raw) is not { } source)
                    {
                        result.SkippedNoSource++;
                        continue;
                    }

                    var finding = ProseCheck.Check(
                        source,
                        new RewriteFidelity.SourceText(candidate.Description ?? string.Empty, own.Select(s => s.Description).ToList(), candidate.Name ?? string.Empty),
                        ProseCheck.SourceLines(raw),
                        stock);
                    if (finding.Passed)
                    {
                        result.Passed++;
                        if (candidate.CheckVersion != ProseCheck.Version) unstamped.Add(candidate.Id);
                        continue;
                    }

                    failures.Add(new Failure(candidate, finding));
                    Count(result, candidate, finding);
                }
            }

            var ordered = failures
                .OrderBy(f => f.Recipe.CurationStatusId == (long)CurationStatusEnum.Curated ? 0 : 1)
                .ThenBy(f => f.Recipe.Id)
                .ToList();
            result.Samples = ordered.Take(Math.Clamp(sampleSize, 0, 5000)).Select(Sample).ToList();

            if (result.Applied)
            {
                var personId = _currentUser.PersonIdOrSystem;
                var applied = new List<Failure>();
                foreach (var chunk in ordered.Take(Math.Clamp(maxRecipes, 1, 100000)).Chunk(WithholdBatch))
                {
                    var ids = chunk.Select(f => f.Recipe.Id).ToList();
                    await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                    var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
                    var now = DateTime.UtcNow;
                    var batch = new List<Failure>();
                    foreach (var failure in chunk)
                    {
                        if (!recipes.TryGetValue(failure.Recipe.Id, out var recipe) || recipe.ContainsSourceProse || recipe.ProseWithheld) continue;

                        _db.AuditLogEntries.Add(new AuditLogEntryEntity
                        {
                            EntityType = RecipeEntityType,
                            EntityId = recipe.Id,
                            ChangeType = WithholdChangeType,
                            PropertyName = nameof(RecipeEntity.ProseWithheld),
                            OldValue = recipe.ProseCheckVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                            NewValue = Clamp($"check {ProseCheck.Version} stock {stock.Version}: {failure.Finding}"),
                            Timestamp = now,
                            ChangedByPersonId = personId,
                        });
                        recipe.ProseWithheld = true;
                        recipe.ProseCheckVersion = ProseCheck.Version;
                        recipe.LastModifiedDate = now;
                        recipe.LastModifiedByPersonId = personId;
                        batch.Add(failure);
                    }

                    if (hold != null && await hold(cancellationToken))
                    {
                        _db.ChangeTracker.Clear();
                        result.Held = true;
                        break;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    if (tx != null) await tx.CommitAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    result.Withheld += batch.Count;
                    applied.AddRange(batch);
                }

                foreach (var chunk in result.Held ? Array.Empty<long[]>() : unstamped.Chunk(RecipesPerTransaction))
                {
                    var recipes = await _db.Recipes.Where(r => chunk.Contains(r.Id) && !r.ProseWithheld && !r.ContainsSourceProse).ToListAsync(cancellationToken);
                    foreach (var recipe in recipes)
                    {
                        recipe.ProseCheckVersion = ProseCheck.Version;
                        result.Stamped++;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                }

                result.Samples = applied.Take(Math.Clamp(sampleSize, 0, 5000)).Select(Sample).ToList();
                result.WithheldByStatus = applied
                    .GroupBy(f => StatusName(f.Recipe.CurationStatusId))
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            result.Remaining = result.Failed - result.Withheld;
            if (result.Applied && !result.Held && result.Remaining == 0)
            {
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RunEntityType,
                    EntityId = ProseCheck.Version,
                    ChangeType = RunChangeType,
                    PropertyName = nameof(ProseRecheckResult.StockVersion),
                    NewValue = Clamp($"check {ProseCheck.Version} stock {stock.Version}: examined {result.Examined}, passed {result.Passed}, withheld {result.Withheld}, skipped edited {result.SkippedEditedByPerson}, no source {result.SkippedNoSource}"),
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = _currentUser.PersonIdOrSystem,
                });
                await _db.SaveChangesAsync(cancellationToken);
                result.RunRecorded = true;
            }

            return result;
        }

        public async Task<ProseRecheckRun?> LastRunAsync(CancellationToken cancellationToken = default)
        {
            var last = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RunEntityType && a.ChangeType == RunChangeType)
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.Timestamp, a.NewValue })
                .FirstOrDefaultAsync(cancellationToken);
            if (last == null) return null;
            var m = RunVersions.Match(last.NewValue ?? string.Empty);
            return m.Success
                ? new ProseRecheckRun(last.Timestamp, int.Parse(m.Groups["check"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["stock"].Value, CultureInfo.InvariantCulture))
                : new ProseRecheckRun(last.Timestamp, 0, 0);
        }

        public async Task<string?> DueAsync(TimeSpan interval, DateTime now, CancellationToken cancellationToken = default)
        {
            var stockVersion = await _db.ProseStockPhrases.AsNoTracking().MaxAsync(p => (int?)p.Version, cancellationToken) ?? 0;
            return Due(await LastRunAsync(cancellationToken), ProseCheck.Version, stockVersion, interval, now);
        }

        /// <summary>Why a full run is due, or null when none is: never while no stock-phrase index exists; then when none has run, when the check or the index has a new version, or when <paramref name="interval"/> has passed since the last.</summary>
        public static string? Due(ProseRecheckRun? last, int checkVersion, int stockVersion, TimeSpan interval, DateTime now)
        {
            if (stockVersion == 0) return null;
            if (last == null) return "no full run recorded";
            if (last.CheckVersion != checkVersion) return $"prose check version {last.CheckVersion} -> {checkVersion}";
            if (last.StockVersion != stockVersion) return $"stock index version {last.StockVersion} -> {stockVersion}";
            return now - last.At >= interval ? $"last full run {last.At:yyyy-MM-dd HH:mm} UTC" : null;
        }

        private static ProseRecheckSample Sample(Failure f) => new()
        {
            RecipeId = f.Recipe.Id,
            Name = f.Recipe.Name,
            Status = StatusName(f.Recipe.CurationStatusId),
            Reasons = f.Finding.Reasons.ToList(),
            Detail = f.Finding.ToString(),
        };

        public async Task<ProseRecheckUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false)
        {
            var result = new ProseRecheckUndoResult { Applied = apply };
            await UndoRequarantineAsync(sinceUtc, apply, result);
            await UndoWithholdAsync(sinceUtc, apply, result);
            return result;
        }

        private async Task UndoWithholdAsync(DateTime sinceUtc, bool apply, ProseRecheckUndoResult result)
        {
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == WithholdUndoChangeType && a.Timestamp >= sinceUtc)
                    .Select(a => a.OldValue)
                    .ToListAsync())
                .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .ToHashSet();
            var entries = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == WithholdChangeType && a.Timestamp >= sinceUtc)
                    .OrderByDescending(a => a.Id)
                    .ToListAsync())
                .Where(a => !undone.Contains(a.Id))
                .ToList();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(RecipesPerTransaction))
            {
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var ids = chunk.Select(e => e.EntityId).Distinct().ToList();
                var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
                var rewrittenAfter = (await _db.AuditLogEntries.AsNoTracking()
                        .Where(a => a.EntityType == RecipeEntityType && ids.Contains(a.EntityId) && a.ChangeType == RewriteChangeType)
                        .Select(a => new { a.EntityId, a.Id })
                        .ToListAsync())
                    .GroupBy(a => a.EntityId)
                    .ToDictionary(g => g.Key, g => g.Max(a => a.Id));
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (!recipes.TryGetValue(entry.EntityId, out var recipe))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (!recipe.ProseWithheld || rewrittenAfter.GetValueOrDefault(entry.EntityId) > entry.Id)
                    {
                        result.Skipped++;
                        result.SkippedRewritten++;
                        continue;
                    }

                    result.Restored++;
                    result.RestoredWithheld++;
                    if (!apply) continue;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RecipeEntityType,
                        EntityId = recipe.Id,
                        ChangeType = WithholdUndoChangeType,
                        PropertyName = nameof(RecipeEntity.ProseWithheld),
                        OldValue = entry.Id.ToString(CultureInfo.InvariantCulture),
                        NewValue = "false",
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    recipe.ProseWithheld = false;
                    recipe.ProseCheckVersion = int.TryParse(entry.OldValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ? version : null;
                    recipe.LastModifiedDate = now;
                    recipe.LastModifiedByPersonId = personId;
                }

                if (!apply) continue;
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                _db.ChangeTracker.Clear();
            }

            result.Remaining = apply ? 0 : result.Restored;
        }

        private async Task UndoRequarantineAsync(DateTime sinceUtc, bool apply, ProseRecheckUndoResult result)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == RecheckChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(RecipesPerTransaction))
            {
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var ids = chunk.Select(e => e.EntityId).Distinct().ToList();
                var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (!recipes.TryGetValue(entry.EntityId, out var recipe))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (!recipe.ContainsSourceProse)
                    {
                        result.Skipped++;
                        result.SkippedRewritten++;
                        continue;
                    }

                    result.Restored++;
                    if (recipe.ProseRewriteAttemptedAt != null) result.RestoredAfterFailedAttempt++;
                    if (!apply) continue;
                    recipe.ContainsSourceProse = false;
                    recipe.ProseRewriteAttemptedAt = DateTime.TryParse(entry.OldValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                        ? at
                        : null;
                    recipe.LastModifiedDate = now;
                    recipe.LastModifiedByPersonId = personId;
                    _db.AuditLogEntries.Remove(entry);
                }

                if (!apply) continue;
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }
        }

        public static string Clamp(string value) => value.Length <= JournalLength ? value : value[..JournalLength];

        private static void Count(ProseRecheckResult result, Candidate candidate, ProseCheck.Finding finding)
        {
            var approved = candidate.CurationStatusId == (long)CurationStatusEnum.Curated;
            result.Failed++;
            if (approved) result.FailedApproved++;
            foreach (var reason in finding.Reasons)
            {
                result.FailedByReason[reason] = result.FailedByReason.GetValueOrDefault(reason) + 1;
                if (approved) result.FailedApprovedByReason[reason] = result.FailedApprovedByReason.GetValueOrDefault(reason) + 1;
            }

            var status = StatusName(candidate.CurationStatusId);
            result.FailedByStatus[status] = result.FailedByStatus.GetValueOrDefault(status) + 1;
        }

        private static string StatusName(long statusId) =>
            Enum.IsDefined(typeof(CurationStatusEnum), statusId)
                ? ((CurationStatusEnum)statusId).ToString()
                : statusId.ToString(CultureInfo.InvariantCulture);
    }
}
