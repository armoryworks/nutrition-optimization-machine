using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <inheritdoc cref="IReprocessDetectorService"/>
    public class ReprocessDetectorService : IReprocessDetectorService
    {
        private static readonly TimeSpan VocabularyLife = TimeSpan.FromHours(1);
        private static readonly object VocabularyLock = new();
        private static (CompoundVocabulary Vocabulary, DateTime At)? _vocabulary;

        private readonly ApplicationDbContext _db;
        private readonly IRecipeReprocessService _reprocess;
        private readonly ILogger<ReprocessDetectorService> _logger;

        public ReprocessDetectorService(ApplicationDbContext db, IRecipeReprocessService reprocess, ILogger<ReprocessDetectorService> logger)
        {
            _db = db;
            _reprocess = reprocess;
            _logger = logger;
        }

        /// <summary>A stored line row as the detector reads it.</summary>
        public sealed record LiveRow(long IngredientId, int SortOrder, string RawLine, decimal Quantity);

        public async Task<ReprocessDetectBatchResult> DetectBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default)
        {
            var ids = await UnreadQuery(_db, afterRecipeId, RecipeLineBuilder.Version).Take(batchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0) return new ReprocessDetectBatchResult(0, 0, new Dictionary<string, int>(), afterRecipeId);
            var report = await ReadAsync(ids, write: true, sampleSize: 0, cancellationToken);
            return new ReprocessDetectBatchResult(ids.Count, report.Candidates, report.ByReason, ids[^1]);
        }

        public async Task<ReprocessDetectReport> DetectAsync(long afterRecipeId, int maxRecipes, bool apply, int sampleSize, CancellationToken cancellationToken = default)
        {
            var take = Math.Clamp(maxRecipes, 1, RecipeReprocessService.MaxRecipesPerCall);
            var ids = await (apply ? UnreadQuery(_db, afterRecipeId, RecipeLineBuilder.Version) : SourcedQuery(_db, afterRecipeId))
                .Take(take).ToListAsync(cancellationToken);
            if (ids.Count == 0) return new ReprocessDetectReport { Applied = apply, RuleVersion = RecipeLineBuilder.Version };
            var report = await ReadAsync(ids, apply, Math.Max(0, sampleSize), cancellationToken);
            report.Applied = apply;
            report.LastRecipeId = ids[^1];
            _logger.LogInformation("Reprocess detector ({Version}, operator{Mode}): {Read} recipes read, {Candidates} candidates",
                report.RuleVersion, apply ? string.Empty : ", dry run", report.Read, report.Candidates);
            return report;
        }

        /// <summary>
        /// Reads the given recipes with the current line rules as a dry run of the rebuild and gives
        /// each its reasons; with <paramref name="write"/> stores them as candidate rows.
        /// </summary>
        private async Task<ReprocessDetectReport> ReadAsync(List<long> ids, bool write, int sampleSize, CancellationToken cancellationToken)
        {
            var version = RecipeLineBuilder.Version;
            var preview = await _reprocess.RebuildAsync(new RecipeReprocessRequestModel { RecipeIds = ids });
            var rows = (await _db.RecipeIngredients.AsNoTracking()
                    .Where(ri => ids.Contains(ri.RecipeId))
                    .Select(ri => new { ri.RecipeId, Row = new LiveRow(ri.IngredientId, ri.SortOrder, ri.RawLine, ri.Quantity) })
                    .ToListAsync(cancellationToken))
                .ToLookup(r => r.RecipeId, r => r.Row);
            var documents = await _db.ScrapedDocuments.AsNoTracking()
                .Where(d => ids.Contains(d.RecipeId))
                .Select(d => new { d.Id, d.RecipeId, d.Version })
                .ToListAsync(cancellationToken);
            var newest = documents.GroupBy(d => d.RecipeId).ToDictionary(g => g.Key, g => g.Max(d => d.Id));
            var documentVersion = documents.ToDictionary(d => d.Id, d => d.Version);
            var recipes = await _db.Recipes.AsNoTracking()
                .Where(r => ids.Contains(r.Id))
                .Select(r => new { r.Id, r.Name, r.CurationStatusId })
                .ToDictionaryAsync(r => r.Id, cancellationToken);
            var vocabulary = await VocabularyAsync(cancellationToken);
            var existing = write
                ? await _db.ReprocessCandidates.Where(c => ids.Contains(c.RecipeId)).ToDictionaryAsync(c => c.RecipeId, cancellationToken)
                : await _db.ReprocessCandidates.AsNoTracking().Where(c => ids.Contains(c.RecipeId)).ToDictionaryAsync(c => c.RecipeId, cancellationToken);

            var now = DateTime.UtcNow;
            var report = new ReprocessDetectReport { RuleVersion = version, Read = ids.Count };
            foreach (var (reason, count) in preview.HeldByReason) report.HeldByReason[reason] = count;
            foreach (var result in preview.Results)
            {
                var sourceVersion = result.SourceDocumentId is { } used && documentVersion.TryGetValue(used, out var v) ? v : 1;
                var reasons = Reasons(result, rows[result.RecipeId].ToList(), sourceVersion,
                    raw => CompoundLineSplitter.Plan(raw, vocabulary) is { Rule: CompoundLineSplitter.OrSharedHead, Kind: CompoundKind.PickAlternative } plan && plan.Parts.Count == 1 ? plan.Parts[0].FoodId : null);
                if ((existing.GetValueOrDefault(result.RecipeId)?.Reasons & ReprocessReason.Duplicate) is > 0) reasons |= ReprocessReason.Duplicate;
                var tier = OrderOnly(result, reasons) ? ReprocessTier.Order : ReprocessTier.Rebuild;
                var approved = recipes.TryGetValue(result.RecipeId, out var recipe) && recipe.CurationStatusId == (long)CurationStatusEnum.Curated;
                if (reasons != 0)
                {
                    report.Candidates++;
                    if (approved) report.ApprovedCandidates++;
                    if (tier == ReprocessTier.Order) report.OrderTier++;
                    else report.RebuildTier++;
                    var codes = ReprocessReason.All.Where(r => (reasons & r.Flag) != 0).Select(r => r.Code).ToList();
                    foreach (var code in codes) report.ByReason[code] = report.ByReason.GetValueOrDefault(code) + 1;
                    if (report.Samples.Count < sampleSize)
                    {
                        report.Samples.Add(new ReprocessDetectSample
                        {
                            RecipeId = result.RecipeId, Name = recipe?.Name ?? string.Empty, Approved = approved, Reasons = codes, Tier = tier,
                            Outcome = result.Outcome, Hold = result.Outcome == RecipeReprocessOutcome.Held ? result.Reason : null,
                            Lines = result.Lines.Where(l => l.Changes.Count > 0).ToList(),
                        });
                    }
                }
                if (!write) continue;

                if (!existing.TryGetValue(result.RecipeId, out var row))
                {
                    row = new ReprocessCandidateEntity { RecipeId = result.RecipeId, CreatedDate = now, CreatedByPersonId = SystemConstants.SystemPersonId };
                    _db.ReprocessCandidates.Add(row);
                }
                else
                {
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }
                row.Reasons = reasons;
                row.RuleVersion = version;
                row.SourceDocumentId = newest.GetValueOrDefault(result.RecipeId);
                row.Tier = tier;
                row.Priority = Priority(reasons, approved);
                row.DetectedAt = now;
                row.Outcome = null;
                row.OutcomeReason = null;
                row.OutcomeAt = null;
                row.JournalId = null;
            }

            if (write) await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return report;
        }

        /// <summary>
        /// The recipes after <paramref name="afterRecipeId"/>, by id, that have a stored source, read
        /// or not: what an operator's dry run reads. A recipe a person edited and a rejected one are
        /// left out, as in <see cref="UnreadQuery"/>.
        /// </summary>
        public static IQueryable<long> SourcedQuery(ApplicationDbContext db, long afterRecipeId) =>
            db.Recipes.AsNoTracking()
                .Where(r => r.Id > afterRecipeId && r.Version <= 1 && r.CurationStatusId != (long)CurationStatusEnum.Rejected)
                .Where(r => db.ScrapedDocuments.Any(d => d.RecipeId == r.Id))
                .OrderBy(r => r.Id)
                .Select(r => r.Id);

        /// <summary>
        /// The recipes after <paramref name="afterRecipeId"/>, by id, that have a stored source and that
        /// the line rules of <paramref name="version"/> have not read with their newest source. A recipe
        /// a person edited (it is never rebuilt) and a rejected one are left out.
        /// </summary>
        public static IQueryable<long> UnreadQuery(ApplicationDbContext db, long afterRecipeId, string version) =>
            db.Recipes.AsNoTracking()
                .Where(r => r.Id > afterRecipeId && r.Version <= 1 && r.CurationStatusId != (long)CurationStatusEnum.Rejected)
                .Where(r => db.ScrapedDocuments.Any(d => d.RecipeId == r.Id))
                .Where(r => !db.ReprocessCandidates.Any(c => c.RecipeId == r.Id && c.RuleVersion == version
                    && c.SourceDocumentId == db.ScrapedDocuments.Where(d => d.RecipeId == r.Id).Max(d => (long?)d.Id)))
                .OrderBy(r => r.Id)
                .Select(r => r.Id);

        /// <summary>
        /// The reasons a rebuild's dry run gives for one recipe (reprocess.md 2c): D1 for lines all at
        /// position 0, a " + " fold (a row whose text no source line holds as it is, so "salt + pepper to
        /// taste" printed so in the source is no fold) or a changed order; D2 for an amount read anew from a line's own
        /// text; D3 for an amount the text does not state (a method fill, new or stored) that changes;
        /// D4 for a changed food; D5 for a changed kind; D6 for a stored line the compound rules read
        /// as a list of whole foods sharing a product (<paramref name="compoundPick"/>) not on that
        /// product; D7 for new, dropped or reworded lines or steps from a newer extraction. Without a
        /// newer extraction, new and dropped lines come from merges and splits of the stored rows (D1).
        /// </summary>
        public static int Reasons(RecipeReprocessRecipeResult result, IReadOnlyList<LiveRow> live, int sourceVersion, Func<string, long?>? compoundPick = null)
        {
            var reasons = 0;
            var structure = sourceVersion > 1 ? ReprocessReason.Source : ReprocessReason.OrderLost;
            if (live.Count > 1 && live.All(l => l.SortOrder == 0)) reasons |= ReprocessReason.OrderLost;
            var sourceKeys = result.SourceLines?.Select(FoldedLine.Key).ToHashSet(StringComparer.Ordinal);
            if (live.Any(l => l.RawLine.Contains(FoldedLine.Separator, StringComparison.Ordinal) && sourceKeys?.Contains(FoldedLine.Key(l.RawLine)) != true)) reasons |= ReprocessReason.OrderLost;

            foreach (var line in result.Lines)
            {
                foreach (var change in line.Changes)
                {
                    switch (change)
                    {
                        case "order": reasons |= ReprocessReason.OrderLost; break;
                        case "food": reasons |= ReprocessReason.Food; break;
                        case "kind": reasons |= ReprocessReason.Kind; break;
                        case "added":
                        case "text": reasons |= structure; break;
                        case "amount":
                            var stated = IngredientLineParser.Parse(line.RawLine.Split(FoldedLine.Separator, 2)[0]) != null;
                            reasons |= line.FilledFromMethod || (!stated && line.OldQuantity is > 0) ? ReprocessReason.MethodFill : ReprocessReason.Amount;
                            break;
                    }
                }
            }
            if (result.Counts.Dropped > 0) reasons |= structure;
            if (result.StepsReplaced) reasons |= ReprocessReason.Source;
            if (compoundPick != null && live.Any(l => compoundPick(l.RawLine) is { } product && product != l.IngredientId)) reasons |= ReprocessReason.Compound;
            return reasons;
        }

        /// <summary>Whether the rebuild would change nothing but the order of the lines, so the light order-only pass (R4) does it.</summary>
        public static bool OrderOnly(RecipeReprocessRecipeResult result, int reasons) =>
            reasons == ReprocessReason.OrderLost
            && result.Outcome == RecipeReprocessOutcome.Rebuilt
            && !result.Rejected && !result.StepsReplaced && result.Counts.Dropped == 0
            && result.Lines.All(l => l.Changes.All(c => c is "order" or RecipeReprocessLineDiff.Held));

        /// <summary>An amount or a food first, then a newer extraction, then kinds and compound lines, order alone last; approved recipes before others.</summary>
        public static int Priority(int reasons, bool approved)
        {
            var priority = (reasons & (ReprocessReason.Amount | ReprocessReason.MethodFill | ReprocessReason.Food)) != 0 ? 300
                : (reasons & ReprocessReason.Source) != 0 ? 200
                : (reasons & (ReprocessReason.Kind | ReprocessReason.Compound)) != 0 ? 100
                : 0;
            return priority > 0 && approved ? priority + 50 : priority;
        }

        public async Task<int> MarkDuplicatesAsync(CancellationToken cancellationToken = default)
        {
            var dry = await _reprocess.RetireDuplicatesAsync(new RecipeDuplicateRequestModel(), apply: false);
            var retired = dry.Copies.Where(c => c.Outcome == RecipeDuplicateResult.Retired).Select(c => c.RecipeId).ToHashSet();
            var now = DateTime.UtcNow;
            var marked = await _db.ReprocessCandidates.Where(c => (c.Reasons & ReprocessReason.Duplicate) != 0).ToListAsync(cancellationToken);
            foreach (var row in marked.Where(r => !retired.Contains(r.RecipeId)))
            {
                row.Reasons &= ~ReprocessReason.Duplicate;
                row.LastModifiedDate = now;
                row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }

            var ids = retired.ToList();
            var rows = new Dictionary<long, ReprocessCandidateEntity>();
            foreach (var chunk in ids.Chunk(1000))
            {
                foreach (var row in await _db.ReprocessCandidates.Where(c => chunk.Contains(c.RecipeId)).ToListAsync(cancellationToken)) rows[row.RecipeId] = row;
            }
            foreach (var id in ids)
            {
                if (!rows.TryGetValue(id, out var row))
                {
                    row = new ReprocessCandidateEntity
                    {
                        RecipeId = id, RuleVersion = string.Empty, Tier = ReprocessTier.Rebuild, DetectedAt = now,
                        CreatedDate = now, CreatedByPersonId = SystemConstants.SystemPersonId,
                    };
                    _db.ReprocessCandidates.Add(row);
                }
                else if ((row.Reasons & ReprocessReason.Duplicate) == 0)
                {
                    row.LastModifiedDate = now;
                    row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }
                row.Reasons |= ReprocessReason.Duplicate;
            }
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            _logger.LogInformation("Reprocess detector: {Copies} recipes are second copies the duplicate retire would retire (D8), in {Groups} groups", retired.Count, dry.Groups);
            return retired.Count;
        }

        private async Task<CompoundVocabulary> VocabularyAsync(CancellationToken cancellationToken)
        {
            lock (VocabularyLock)
            {
                if (_vocabulary is { } cached && DateTime.UtcNow - cached.At < VocabularyLife && _db.Database.IsRelational()) return cached.Vocabulary;
            }
            var vocabulary = await CompoundLineService.VocabularyAsync(_db);
            cancellationToken.ThrowIfCancellationRequested();
            lock (VocabularyLock) _vocabulary = (vocabulary, DateTime.UtcNow);
            return vocabulary;
        }
    }
}
