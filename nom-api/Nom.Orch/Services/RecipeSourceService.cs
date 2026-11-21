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
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Stores the records the staged import was fed from on the recipes they became. The keys
    /// are the import's own: the normalized URL, else the name and attribution the import gave the
    /// recipe. A key alone is not enough, because books repeat names: at least
    /// <see cref="MinimumLineAgreement"/> of the record's ingredient lines must equal one of the
    /// recipe's lines or a fold part of one, after entity and spacing normalization, and one
    /// recipe must agree better than every other. Each stored document is journaled
    /// (<see cref="AttachChangeType"/>) and the undo soft-deletes it.
    /// </summary>
    public class RecipeSourceService : IRecipeSourceService
    {
        public const string DocumentEntityType = "ScrapedDocument";
        public const string AttachChangeType = "SourceAttach";
        public const string UndoChangeType = "SourceAttachUndo";
        public const double MinimumLineAgreement = 0.8;

        /// <summary>How a record named by its recipe (a newer extraction of a book) was matched.</summary>
        public const string PinnedBy = "pin";

        private const int IdsPerRead = 1000;

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public RecipeSourceService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Candidate(long Id, string? SourceUrl, string Name, string? SourceAttribution);
        private sealed record Document(long RecipeId, string Format, string? ContentSha256, int Version);

        private sealed class Prepared
        {
            public required int Index { get; init; }
            public required ScraperRecipe Record { get; init; }
            public required string? Url { get; init; }
            public required string Name { get; init; }
            public required string? Attribution { get; init; }
            public required string Content { get; init; }
            public required string Format { get; init; }
            public required string Sha256 { get; init; }
            public required List<string> LineKeys { get; init; }
            public RecipeSourceAttachRecordResult Result { get; } = new();
        }

        public async Task<RecipeSourceAttachResult> AttachAsync(RecipeSourceAttachRequestModel request, bool apply = false, bool newVersion = false)
        {
            var stamp = DateTime.UtcNow;
            var result = new RecipeSourceAttachResult { Applied = apply, Stamp = stamp, Records = request.Records.Count };
            var attribution = string.IsNullOrWhiteSpace(request.SourceAttribution) ? null : request.SourceAttribution;

            var prepared = request.Records.Select((record, index) =>
            {
                var content = SourceRecord.Content(record);
                return new Prepared
                {
                    Index = index,
                    Record = record,
                    Url = string.IsNullOrWhiteSpace(record.SourceUrl) ? null : RecipeScrapingService.Clamp(RecipeScrapingService.NormalizeUrl(record.SourceUrl), 2047),
                    Name = RecipeScrapingService.Clamp(string.IsNullOrEmpty(record.Name) ? "Untitled Recipe" : record.Name, 511)!,
                    Attribution = attribution ?? RecipeScrapingService.BuildAttribution(record),
                    Content = content,
                    Format = SourceRecord.Format(record),
                    Sha256 = SourceRecord.Sha256(content),
                    LineKeys = record.Ingredients
                        .Select(i => FoldedLine.Key(RecipeScrapingService.Clamp(i.RawLine, 2047)))
                        .Where(k => k.Length > 0)
                        .ToList(),
                };
            }).ToList();

            var byUrl = await CandidatesAsync(prepared.Where(p => p.Url != null).Select(p => p.Url!).Distinct().ToList(), byUrl: true);
            var byName = await CandidatesAsync(
                prepared.Where(p => p.Url == null || !byUrl.Contains(p.Url)).Select(p => p.Name).Distinct().ToList(), byUrl: false);

            var pins = request.RecipeIds ?? new List<long?>();
            var pinnedIds = pins.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            var pinned = pinnedIds.Count == 0
                ? new Dictionary<long, Candidate>()
                : await _db.Recipes.AsNoTracking().Where(r => pinnedIds.Contains(r.Id))
                    .Select(r => new Candidate(r.Id, r.SourceUrl, r.Name, r.SourceAttribution)).ToDictionaryAsync(c => c.Id);
            var pinnedTwice = pins.Where(id => id != null).GroupBy(id => id!.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

            var candidatesOf = new Dictionary<int, (string By, List<Candidate> Recipes)>();
            foreach (var p in prepared)
            {
                if (p.Index < pins.Count && pins[p.Index] is { } pin)
                {
                    if (pinned.TryGetValue(pin, out var recipe)) candidatesOf[p.Index] = (PinnedBy, new List<Candidate> { recipe });
                    continue;
                }

                if (p.Url != null && byUrl[p.Url].ToList() is { Count: > 0 } urlMatches)
                {
                    candidatesOf[p.Index] = ("url", urlMatches);
                    continue;
                }

                var nameMatches = byName[p.Name].Where(c => string.Equals(c.SourceAttribution, p.Attribution, StringComparison.Ordinal)).ToList();
                if (nameMatches.Count > 0) candidatesOf[p.Index] = ("name", nameMatches);
            }

            var candidateIds = candidatesOf.Values.SelectMany(c => c.Recipes).Select(c => c.Id).Distinct().ToList();
            var lines = await LineKeysAsync(candidateIds);
            var documents = (await DocumentsAsync(candidateIds)).ToDictionary(g => g.Key, g => g.ToList());
            List<Document> Held(long recipeId) => documents.TryGetValue(recipeId, out var held) ? held : documents[recipeId] = new List<Document>();
            var taken = new HashSet<long>();
            var toStore = new List<(Prepared Source, ScrapedDocumentEntity Document)>();

            foreach (var p in prepared)
            {
                var outcome = p.Result;
                outcome.Index = p.Index;
                outcome.Name = p.Record.Name;
                outcome.SourceUrl = p.Record.SourceUrl;
                outcome.Format = p.Format;
                outcome.ContentSha256 = p.Sha256;

                if (!candidatesOf.TryGetValue(p.Index, out var found))
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.Unmatched;
                    continue;
                }

                outcome.MatchedBy = found.By;
                outcome.Candidates = found.Recipes
                    .Select(c => new RecipeSourceCandidate { RecipeId = c.Id, Score = Score(p.LineKeys, lines.GetValueOrDefault(c.Id)) })
                    .OrderByDescending(c => c.Score).ThenBy(c => c.RecipeId)
                    .ToList();
                var best = outcome.Candidates[0];
                outcome.Score = best.Score;

                if (found.By == PinnedBy && pinnedTwice.Contains(best.RecipeId))
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.DoubleMatch;
                    outcome.RecipeId = best.RecipeId;
                    continue;
                }

                if (found.Recipes.FirstOrDefault(c => Held(c.Id).Any(d => d.Format == p.Format && d.ContentSha256 == p.Sha256)) is { } holder)
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.AlreadyAttached;
                    outcome.RecipeId = holder.Id;
                    taken.Add(holder.Id);
                    continue;
                }

                if (found.By == PinnedBy && Held(best.RecipeId).All(d => d.Format != p.Format))
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.Unmatched;
                    continue;
                }

                if (best.Score < MinimumLineAgreement && found.By != PinnedBy)
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.LowScore;
                    continue;
                }

                if (outcome.Candidates.Count > 1 && outcome.Candidates[1].Score == best.Score)
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.Ambiguous;
                    continue;
                }

                outcome.RecipeId = best.RecipeId;
                if (taken.Contains(best.RecipeId))
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.DoubleMatch;
                    continue;
                }

                var recipe = found.Recipes.Single(c => c.Id == best.RecipeId);
                var held = Held(recipe.Id);
                var sameFormat = held.Where(d => d.Format == p.Format).ToList();
                if (sameFormat.Count > 0 && !newVersion)
                {
                    outcome.Outcome = RecipeSourceAttachOutcome.Conflict;
                    continue;
                }

                outcome.Outcome = RecipeSourceAttachOutcome.Matched;
                outcome.Version = sameFormat.Count == 0 ? 1 : sameFormat.Max(d => d.Version) + 1;
                taken.Add(recipe.Id);
                var queued = new List<ScrapedDocumentEntity> { NewDocument(recipe, p.Content, p.Format, p.Sha256, outcome.Version.Value, stamp) };
                if (!string.IsNullOrWhiteSpace(p.Record.RawJsonLd) && held.All(d => d.Format != ScrapedDocumentFormat.JsonLd))
                {
                    queued.Add(NewDocument(recipe, p.Record.RawJsonLd, ScrapedDocumentFormat.JsonLd, SourceRecord.Sha256(p.Record.RawJsonLd), 1, stamp));
                }

                foreach (var document in queued)
                {
                    held.Add(new Document(recipe.Id, document.Format, document.ContentSha256, document.Version));
                    toStore.Add((p, document));
                }
            }

            foreach (var (source, _) in toStore) source.Result.Documents++;
            result.Results = prepared.Select(p => p.Result).ToList();
            result.Matched = result.Results.Count(r => r.Outcome == RecipeSourceAttachOutcome.Matched);
            result.AlreadyAttached = result.Results.Count(r => r.Outcome == RecipeSourceAttachOutcome.AlreadyAttached);
            result.Unmatched = result.Results.Count(r => r.Outcome == RecipeSourceAttachOutcome.Unmatched);
            result.Ambiguous = result.Results.Count(r => r.Outcome == RecipeSourceAttachOutcome.Ambiguous);
            result.LowScore = result.Results.Count(r => r.Outcome == RecipeSourceAttachOutcome.LowScore);
            result.DoubleMatch = result.Results.Count(r => r.Outcome == RecipeSourceAttachOutcome.DoubleMatch);
            result.Conflict = result.Results.Count(r => r.Outcome == RecipeSourceAttachOutcome.Conflict);
            result.Documents = toStore.Count;

            if (!apply || toStore.Count == 0) return result;

            await using var transaction = await _db.Database.BeginTransactionAsync();
            _db.ScrapedDocuments.AddRange(toStore.Select(s => s.Document));
            await _db.SaveChangesAsync();
            var by = _currentUser.PersonIdOrSystem;
            _db.AuditLogEntries.AddRange(toStore.Select(s => new AuditLogEntryEntity
            {
                EntityType = DocumentEntityType,
                EntityId = s.Document.Id,
                ChangeType = AttachChangeType,
                PropertyName = s.Document.Format,
                NewValue = string.Create(CultureInfo.InvariantCulture,
                    $"recipe={s.Document.RecipeId};version={s.Document.Version};sha256={s.Document.ContentSha256};score={s.Source.Result.Score:0.###};by={s.Source.Result.MatchedBy}"),
                Timestamp = stamp,
                ChangedByPersonId = by,
            }));
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return result;
        }

        public async Task<RecipeSourceUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false)
        {
            var result = new RecipeSourceUndoResult { Applied = apply };
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == DocumentEntityType && a.ChangeType == AttachChangeType && a.Timestamp >= sinceUtc)
                .OrderBy(a => a.Id)
                .ToListAsync();
            if (entries.Count == 0) return result;

            var now = DateTime.UtcNow;
            var by = _currentUser.PersonIdOrSystem;
            await using var transaction = apply ? await _db.Database.BeginTransactionAsync() : null;
            foreach (var chunk in entries.Chunk(IdsPerRead))
            {
                var ids = chunk.Select(e => e.EntityId).ToList();
                var live = await _db.ScrapedDocuments
                    .Where(d => ids.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id);
                foreach (var entry in chunk)
                {
                    if (!live.Remove(entry.EntityId, out var document))
                    {
                        result.Skipped++;
                        continue;
                    }

                    result.Removed++;
                    if (!apply) continue;

                    document.IsDeleted = true;
                    document.DeletedAt = now;
                    document.DeletedByPersonId = by;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = DocumentEntityType,
                        EntityId = document.Id,
                        ChangeType = UndoChangeType,
                        PropertyName = document.Format,
                        OldValue = entry.NewValue,
                        Timestamp = now,
                        ChangedByPersonId = by,
                    });
                }

                if (apply) await _db.SaveChangesAsync();
            }

            if (transaction != null) await transaction.CommitAsync();
            return result;
        }

        public static double Score(IReadOnlyList<string> recordLines, IReadOnlySet<string>? recipeLines)
        {
            if (recordLines.Count == 0) return recipeLines == null || recipeLines.Count == 0 ? 1 : 0;
            if (recipeLines == null) return 0;
            return (double)recordLines.Count(recipeLines.Contains) / recordLines.Count;
        }

        public static IEnumerable<string> RowKeys(string? rawLine)
        {
            yield return FoldedLine.Key(rawLine);
            var line = rawLine ?? string.Empty;
            if (!line.Contains(FoldedLine.Separator, StringComparison.Ordinal)) yield break;
            foreach (var part in line.Split(FoldedLine.Separator)) yield return FoldedLine.Key(part);
        }

        private ScrapedDocumentEntity NewDocument(Candidate recipe, string content, string format, string sha256, int version, DateTime stamp) => new()
        {
            RecipeId = recipe.Id,
            SourceUrl = recipe.SourceUrl ?? string.Empty,
            RawJsonLd = content,
            Format = format,
            ContentSha256 = sha256,
            Version = version,
            FetchedAtUtc = stamp,
            CreatedDate = stamp,
            CreatedByPersonId = _currentUser.PersonId,
        };

        private async Task<ILookup<string, Candidate>> CandidatesAsync(List<string> keys, bool byUrl)
        {
            var found = new List<Candidate>();
            foreach (var chunk in keys.Chunk(IdsPerRead))
            {
                var query = _db.Recipes.AsNoTracking();
                query = byUrl ? query.Where(r => chunk.Contains(r.SourceUrl!)) : query.Where(r => chunk.Contains(r.Name));
                found.AddRange(await query
                    .Select(r => new Candidate(r.Id, r.SourceUrl, r.Name, r.SourceAttribution))
                    .ToListAsync());
            }

            return found.ToLookup(c => byUrl ? c.SourceUrl! : c.Name, StringComparer.Ordinal);
        }

        private async Task<Dictionary<long, HashSet<string>>> LineKeysAsync(List<long> recipeIds)
        {
            var keys = new Dictionary<long, HashSet<string>>();
            foreach (var chunk in recipeIds.Chunk(IdsPerRead))
            {
                var rows = await _db.RecipeIngredients.AsNoTracking()
                    .Where(ri => chunk.Contains(ri.RecipeId))
                    .Select(ri => new { ri.RecipeId, ri.RawLine })
                    .ToListAsync();
                foreach (var row in rows)
                {
                    if (!keys.TryGetValue(row.RecipeId, out var set)) keys[row.RecipeId] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.UnionWith(RowKeys(row.RawLine).Where(k => k.Length > 0));
                }
            }

            return keys;
        }

        private async Task<ILookup<long, Document>> DocumentsAsync(List<long> recipeIds)
        {
            var found = new List<Document>();
            foreach (var chunk in recipeIds.Chunk(IdsPerRead))
            {
                found.AddRange(await _db.ScrapedDocuments.AsNoTracking()
                    .Where(d => chunk.Contains(d.RecipeId))
                    .Select(d => new Document(d.RecipeId, d.Format, d.ContentSha256, d.Version))
                    .ToListAsync());
            }

            return found.ToLookup(d => d.RecipeId);
        }
    }
}
