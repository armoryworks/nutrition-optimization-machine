using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Npgsql;
using NpgsqlTypes;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Rebuilds recipes in place from their stored source. The source is the newest record the
    /// staged import or the source attach stored (<c>scraper-recipe</c>, <c>staged-cookbook</c>),
    /// else the JSON-LD document read through the scraper's parse call (nothing is fetched). The
    /// lines are built by <see cref="RecipeLineBuilder"/>, the import's own path, with the method
    /// fill, and each new line is paired with the stored row of the same text (a " + " fold with each
    /// of its parts). A paired line keeps its current food, so the links, relinks and approvals made
    /// since the import stand, unless that food is retired, prepared, an uncurated food named with an
    /// amount or unit, or an uncurated food where the import now finds a curated one. Guards, each a
    /// reason to keep the stored lines: an editor edit; a compound split no rule here repeats; a
    /// recipe in a meal plan from today on whose foods would change; an approved recipe that would
    /// end with fewer lines with an amount or on a curated food, or that would newly fail vetting or
    /// the approval gate. A recipe whose source and rebuilt version both fail the text rules
    /// (placeholder copy, no real words) is set to Rejected by rule. Every write is one transaction
    /// and one journal row with the rows before and after.
    /// </summary>
    public class RecipeReprocessService : IRecipeReprocessService
    {
        public const string RecipeEntityType = "Recipe";
        public const string ReprocessChangeType = "Reprocess";
        public const string ReprocessOrderChangeType = "ReprocessOrder";
        public const string ReprocessUndoneChangeType = "ReprocessUndone";
        public const string UndoChangeType = "ReprocessUndo";
        public const string RuleRejectChangeType = "RuleReject";
        public const string DuplicateRetireChangeType = "DuplicateRetire";
        public const string FoodEntityType = "Ingredient";
        public const string FoodCreateChangeType = "ReprocessFoodCreate";
        public const string FoodCreateUndoneChangeType = "ReprocessFoodCreateUndone";
        public const string RuleRejectIssue = "Rejected by rule: neither the source nor the rebuilt recipe is a recipe";
        public const int MaxRecipesPerCall = 500;

        /// <summary>The audit entity of an undo run's progress rows; the property name is the run id.</summary>
        public const string UndoRunEntityType = "RecipeReprocessUndoRun";
        public const string UndoRunChangeType = "ReprocessUndoRun";
        public const string UndoBatchChangeType = "ReprocessUndoBatch";
        public const string UndoRunEndChangeType = "ReprocessUndoRunEnd";
        public const string UndoStopChangeType = "ReprocessUndoStop";
        public const int UndoBatchSize = 500;
        public const int MaxUndoBatchSize = 2000;

        /// <summary>The start of the batch id of every write the reprocess lane makes, so its undo takes back only the lane's writes.</summary>
        public const string LaneBatchPrefix = "rl-";

        /// <summary>
        /// The start of the batch id of the order write an undo makes when it puts back the positions of an
        /// order-only write whose lines a later step changed otherwise; the rest is the id of the write it takes back.
        /// </summary>
        public const string OrderFollowPrefix = "rp-order-follow-";

        private const string OperatorBatchPrefix = "rp-";
        private const int IdsPerRead = 1000;
        private const string LinksKey = "links=";
        private const string JournalKey = "journal=";
        private static readonly string[] NotARecipeProblems = { "Text is placeholder copy", "Text has no real words" };

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IRecipeScraperClient _scraper;
        private readonly IRecipeVettingService _vetting;
        private readonly IRecipeNutritionService _nutrition;
        private readonly ILogger<RecipeReprocessService> _logger;
        private readonly RecipeLineBuilder _builder;
        private Dictionary<long, string>? _units;

        public RecipeReprocessService(
            ApplicationDbContext db,
            ICurrentUserService currentUser,
            IRecipeScraperClient scraper,
            IRecipeVettingService vetting,
            IRecipeNutritionService nutrition,
            ILogger<RecipeReprocessService> logger,
            IConfiguration? configuration = null)
        {
            _db = db;
            _currentUser = currentUser;
            _scraper = scraper;
            _vetting = vetting;
            _nutrition = nutrition;
            _logger = logger;
            _builder = new RecipeLineBuilder(db, logger, configuration?.GetValue(RecipeScrapingService.ResolveOnImportSetting, false) ?? false);
        }

        /// <summary>A line row exactly as stored, for the journal.</summary>
        public sealed record LineRow(
            long IngredientId, int SortOrder, decimal Quantity, long MeasurementId, string RawLine, string? LineKind, long? IngredientEntityId,
            DateTime CreatedDate, long? CreatedByPersonId, DateTime? LastModifiedDate, long? LastModifiedByPersonId,
            bool IsDeleted, DateTime? DeletedAt, long? DeletedByPersonId,
            decimal? AlcoholRetention = null, string? AlcoholRetentionBasis = null);

        /// <summary>A recipe's curation state and deletion, for the journal.</summary>
        public sealed record RecipeState(long CurationStatusId, string? VettingIssues, bool IsDeleted, DateTime? DeletedAt, long? DeletedByPersonId);

        /// <summary>A step exactly as stored, for the journal.</summary>
        public sealed record StepRow(
            int StepNumber, long? StepTypeId, string Summary, string Description, long Id,
            DateTime CreatedDate, long? CreatedByPersonId, DateTime? LastModifiedDate, long? LastModifiedByPersonId,
            bool IsDeleted, DateTime? DeletedAt, long? DeletedByPersonId);

        private sealed record Source(long DocumentId, string Format, int Version, ScraperRecipe Record);
        private sealed record Copy(long Id, string Name, string? SourceUrl, string? SourceAttribution, long CurationStatusId, long Version);

        public static string NewBatchId(DateTime stamp, string prefix = OperatorBatchPrefix) =>
            string.Create(CultureInfo.InvariantCulture, $"{prefix}{stamp:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid():N}")[..32];

        public async Task<RecipeReprocessResult> RebuildAsync(RecipeReprocessRequestModel request, bool apply = false, bool orderOnly = false, string? batchId = null)
        {
            var stamp = DateTime.UtcNow;
            var result = new RecipeReprocessResult
            {
                Applied = apply, OrderOnly = orderOnly, Stamp = stamp, BatchId = batchId ?? NewBatchId(stamp), RuleVersion = RecipeLineBuilder.Version,
            };

            foreach (var recipeId in request.RecipeIds.Distinct().OrderBy(id => id))
            {
                RecipeReprocessRecipeResult one;
                try
                {
                    one = orderOnly ? await OrderOneAsync(recipeId, apply, result.BatchId) : await RebuildOneAsync(recipeId, apply, result.BatchId, stamp);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reprocess of recipe {RecipeId} failed", recipeId);
                    _db.ChangeTracker.Clear();
                    one = new RecipeReprocessRecipeResult { RecipeId = recipeId, Outcome = RecipeReprocessOutcome.Failed, Reason = ex.Message };
                }

                result.Results.Add(one);
                result.Recipes++;
                switch (one.Outcome)
                {
                    case RecipeReprocessOutcome.Rebuilt: result.Rebuilt++; result.Counts.Add(one.Counts); break;
                    case RecipeReprocessOutcome.Unchanged: result.Unchanged++; result.Counts.AddGuard(one.Counts); break;
                    case RecipeReprocessOutcome.Failed: result.Failed++; break;
                    default:
                        result.Held++;
                        result.HeldByReason[one.Reason ?? string.Empty] = result.HeldByReason.GetValueOrDefault(one.Reason ?? string.Empty) + 1;
                        break;
                }
                if (one.Rejected) result.Rejected++;
            }

            return result;
        }

        private static RecipeReprocessRecipeResult Hold(RecipeReprocessRecipeResult one, string reason)
        {
            one.Outcome = RecipeReprocessOutcome.Held;
            one.Reason = reason;
            return one;
        }

        private async Task<RecipeReprocessRecipeResult> RebuildOneAsync(long recipeId, bool apply, string batchId, DateTime stamp)
        {
            var one = new RecipeReprocessRecipeResult { RecipeId = recipeId };
            var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId);
            if (recipe == null) return Hold(one, RecipeReprocessHold.NotFound);
            if (recipe.CurationStatusId == (long)CurationStatusEnum.Rejected) return Hold(one, RecipeReprocessHold.Rejected);
            if (recipe.Version > 1) return Hold(one, RecipeReprocessHold.PersonEdit);

            var (source, unread) = await SourceAsync(recipe);
            if (source == null) return Hold(one, unread!);
            one.SourceFormat = source.Format;
            one.SourceDocumentId = source.DocumentId;

            if (await _db.AuditLogEntries.AnyAsync(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType && a.EntityId == recipeId
                    && (a.ChangeType == CompoundLineService.SplitChangeType || a.ChangeType == CompoundLineService.AlternativeChangeType)))
            {
                return Hold(one, RecipeReprocessHold.CompoundSplit);
            }

            var rows = await RowsAsync(recipeId);
            var live = rows.Where(r => !r.IsDeleted).ToList();
            var oldFoods = await FoodsAsync(live.Select(r => r.IngredientId));
            var steps = await _db.RecipeSteps.AsNoTracking().Where(s => s.RecipeId == recipeId).OrderBy(s => s.StepNumber).ToListAsync();
            var sourceSteps = ReextractedSteps(source, steps);
            if (sourceSteps != null && steps.Any(s => s.LastModifiedByPersonId is { } by && by != SystemConstants.SystemPersonId))
                return Hold(one, RecipeReprocessHold.PersonStepEdit);
            if (sourceSteps != null && await ProseLaneRewroteStepsAsync(recipeId))
                return Hold(one, RecipeReprocessHold.ProseRewritten);
            var newSteps = sourceSteps == null
                ? steps
                : sourceSteps.Select((s, i) => new RecipeStepEntity { RecipeId = recipeId, StepNumber = i + 1, Summary = s.Summary, Description = s.Description }).ToList();
            var method = newSteps.Select(s => s.Description ?? string.Empty).ToList();
            var pairs = Pair(source.Record.Ingredients.Select(i => RecipeScrapingService.Clamp(i.RawLine, 2047) ?? string.Empty).ToList(), live);
            var choose = Chooser(pairs, oldFoods);

            var units = _units ??= await _db.Measurements.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name);
            var built = await _builder.BuildAsync(source.Record.Ingredients, _currentUser.PersonId, write: false, method, choose);
            KeepKinds(built, pairs);
            var guarded = Guard(built, pairs, oldFoods, units, method);
            var newRows = NewRows(built, pairs, stamp);
            one.SourceLines = source.Record.Ingredients.Select(i => RecipeScrapingService.Clamp(i.RawLine, 2047) ?? string.Empty).ToList();
            Diff(one, built, _builder.Dropped, pairs, live, oldFoods, guarded);
            one.StepsReplaced = sourceSteps != null;

            var linesUnchanged = built.All(b => b.Food != null) && SameRows(live, newRows);
            var unchanged = linesUnchanged && sourceSteps == null;
            var today = DateOnly.FromDateTime(stamp);
            var planned = await _db.MealPlans.AnyAsync(m => m.RecipeId == recipeId && m.Date >= today);
            if (!unchanged && planned && !SameFoods(live, built)) return Hold(one, RecipeReprocessHold.PlannedFoodChange);

            var newIssues = await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(Shadow(recipe, built, newSteps)));
            var curated = recipe.CurationStatusId == (long)CurationStatusEnum.Curated;
            if (!unchanged && curated && await ApprovedWorseAsync(recipe, live, oldFoods, built, steps, newSteps, newIssues) is { } worse) return Hold(one, worse);

            var reject = !curated && recipe.DateCurationCompleted == null
                && (recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated || recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision)
                && NotARecipe(RecipeVettingService.TextProblems(source.Record)) && NotARecipe(newIssues)
                && !RecipeVettingService.HasRealStep(source.Record) && !RecipeVettingService.HasRealStep(ScrapedRecipeRepairService.ToVettable(Shadow(recipe, built, newSteps)));
            var statusIssues = newIssues.Count > 0 && recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated
                && (await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(Shadow(recipe, built, newSteps, CarriedKind(pairs))))).Count == 0
                    ? new List<string>()
                    : newIssues;
            one.Rejected = reject;
            one.Outcome = unchanged ? RecipeReprocessOutcome.Unchanged : RecipeReprocessOutcome.Rebuilt;
            var holds = Holds(recipeId, one, units, _ => null);
            if (unchanged && !reject) one.Lines.Clear();
            if (!apply) return one;
            if (unchanged && !reject)
            {
                await LineGuardHolds.RecordAsync(_db, LineGuardHoldOrigin.Rebuild, new[] { recipeId }, holds, RecipeLineBuilder.Version, DateTime.UtcNow,
                    new Dictionary<string, string> { [LineGuardHoldOrigin.Rebuild] = LineGuardHolds.Released });
                await _db.SaveChangesAsync();
                return one;
            }

            var by = _currentUser.PersonIdOrSystem;
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
            var now = DateTime.UtcNow;
            var oldState = State(recipe);
            if (!unchanged)
            {
                built = await _builder.BuildAsync(source.Record.Ingredients, _currentUser.PersonId, write: true, method, choose);
                KeepKinds(built, pairs);
                Guard(built, pairs, oldFoods, units, method);
                var builtFoods = built.GroupBy(l => l.SortOrder).ToDictionary(g => g.Key, g => g.First().Food?.Id);
                await LineGuardHolds.RecordAsync(_db, LineGuardHoldOrigin.Rebuild, new[] { recipeId }, Holds(recipeId, one, units, builtFoods.GetValueOrDefault),
                    RecipeLineBuilder.Version, now, RebuildResolves);
                var createdFoods = built.Where(l => l.Created && l.Food != null).Select(l => l.Food!).DistinctBy(f => f.Id).ToList();
                var written = linesUnchanged ? rows : NewRows(built, pairs, now);
                if (!linesUnchanged) await ReplaceRowsAsync(recipeId, written);
                var (oldStepRows, newStepRows) = sourceSteps == null ? (null, null) : await ReplaceStepsAsync(recipeId, sourceSteps, now);
                var links = new List<AuditLogEntryEntity>();
                foreach (var line in built.Where(l => l.Link != null))
                {
                    links.AddRange(LinkApplyJournal.LinesMoved(
                        line.Link!.ProposalId, line.Link.HolderId, line.Food!.Id, Array.Empty<LinkApplyJournal.LineShift>(),
                        new[] { new LinkApplyJournal.LinePlace { RecipeId = recipeId, SortOrder = line.SortOrder } }, now, by));
                }
                links.AddRange(built.Where(l => l.ByWording != null)
                    .Select(l => LineRelinkService.WordingResolved(recipeId, l.ByWording!, l.Food!.Id, l.SortOrder, now, by)));
                _db.AuditLogEntries.AddRange(links);

                if (!reject && recipe.DateCurationCompleted == null
                    && (recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated || recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision))
                {
                    recipe.VettingIssues = statusIssues.Count > 0 ? string.Join("\n", statusIssues) : null;
                    recipe.CurationStatusId = statusIssues.Count > 0 ? (long)CurationStatusEnum.RequiresRevision : (long)CurationStatusEnum.NonCurated;
                }
                var rebuiltState = Stamped(recipe, oldState, now);
                var rebuild = new RecipeReprocessJournalEntity
                {
                    RecipeId = recipeId,
                    BatchId = batchId,
                    Kind = RecipeReprocessKind.Rebuild,
                    RuleVersion = RecipeLineBuilder.Version,
                    SourceDocumentId = source.DocumentId,
                    OldRows = JsonSerializer.Serialize(rows, Json),
                    NewRows = JsonSerializer.Serialize(written, Json),
                    OldSteps = oldStepRows == null ? null : JsonSerializer.Serialize(oldStepRows, Json),
                    NewSteps = newStepRows == null ? null : JsonSerializer.Serialize(newStepRows, Json),
                    OldRecipe = JsonSerializer.Serialize(oldState, Json),
                    NewRecipe = JsonSerializer.Serialize(rebuiltState, Json),
                    CreatedDate = now,
                    CreatedByPersonId = by,
                };
                await JournalAsync(rebuild, by, now, rejected: false, links);
                if (createdFoods.Count > 0)
                {
                    _db.AuditLogEntries.AddRange(createdFoods.Select(food => new AuditLogEntryEntity
                    {
                        EntityType = FoodEntityType, EntityId = food.Id, ChangeType = FoodCreateChangeType, PropertyName = nameof(IngredientEntity.Name),
                        OldValue = string.Create(CultureInfo.InvariantCulture, $"journal={rebuild.Id};batch={batchId}"), NewValue = food.Name,
                        Timestamp = now, ChangedByPersonId = by,
                    }));
                    await _db.SaveChangesAsync();
                }
                one.JournalId = rebuild.Id;
                oldState = rebuiltState;
            }

            if (reject)
            {
                recipe.CurationStatusId = (long)CurationStatusEnum.Rejected;
                recipe.VettingIssues = string.Join("\n", newIssues.Append(RuleRejectIssue));
                var rejection = new RecipeReprocessJournalEntity
                {
                    RecipeId = recipeId,
                    BatchId = batchId,
                    Kind = RecipeReprocessKind.RuleReject,
                    RuleVersion = RecipeLineBuilder.Version,
                    SourceDocumentId = source.DocumentId,
                    OldRecipe = JsonSerializer.Serialize(oldState, Json),
                    NewRecipe = JsonSerializer.Serialize(Stamped(recipe, oldState, now), Json),
                    CreatedDate = now,
                    CreatedByPersonId = by,
                };
                await JournalAsync(rejection, by, now, rejected: true);
                one.JournalId ??= rejection.Id;
            }

            if (tx != null) await tx.CommitAsync();
            await RecalculateAsync(recipeId);
            return one;
        }

        private static RecipeState Stamped(RecipeEntity recipe, RecipeState before, DateTime now)
        {
            var state = State(recipe);
            if (state != before)
            {
                recipe.LastModifiedDate = now;
                recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }
            return state;
        }

        private async Task<RecipeReprocessRecipeResult> OrderOneAsync(long recipeId, bool apply, string batchId)
        {
            var one = new RecipeReprocessRecipeResult { RecipeId = recipeId };
            var recipe = await _db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipeId);
            if (recipe == null) return Hold(one, RecipeReprocessHold.NotFound);
            if (recipe.Version > 1) return Hold(one, RecipeReprocessHold.PersonEdit);

            var (source, unread) = await SourceAsync(recipe);
            if (source == null) return Hold(one, unread!);
            one.SourceFormat = source.Format;
            one.SourceDocumentId = source.DocumentId;

            var rows = await RowsAsync(recipeId);
            var live = rows.Where(r => !r.IsDeleted).ToList();
            var sourceLines = source.Record.Ingredients.Select(i => RecipeScrapingService.Clamp(i.RawLine, 2047) ?? string.Empty).ToList();
            var positions = SourcePositions(sourceLines, live);
            if (positions == null) return Hold(one, RecipeReprocessHold.OrderUnpaired);
            one.SourceLines = sourceLines;

            var ranked = live.Select((row, i) => (Row: row, Position: positions[i])).OrderBy(p => p.Position).Select((p, rank) => (p.Row, Rank: rank)).ToList();
            one.Counts.LinesBefore = live.Count;
            one.Counts.LinesAfter = ranked.Count;
            one.Counts.OrderChanged = ranked.Count(p => p.Row.SortOrder != p.Rank);
            if (one.Counts.OrderChanged == 0)
            {
                one.Outcome = RecipeReprocessOutcome.Unchanged;
                return one;
            }

            one.Outcome = RecipeReprocessOutcome.Rebuilt;
            one.Lines = ranked.OrderBy(p => p.Rank).Select(p => new RecipeReprocessLineDiff
            {
                SortOrder = p.Rank, RawLine = p.Row.RawLine, OldSortOrder = p.Row.SortOrder,
                OldIngredientId = p.Row.IngredientId, NewIngredientId = p.Row.IngredientId,
                OldQuantity = p.Row.Quantity, NewQuantity = p.Row.Quantity, OldMeasurementId = p.Row.MeasurementId, NewMeasurementId = p.Row.MeasurementId,
                OldLineKind = p.Row.LineKind, NewLineKind = p.Row.LineKind,
                Changes = p.Row.SortOrder != p.Rank ? new List<string> { "order" } : new List<string>(),
            }).ToList();
            if (!apply) return one;

            var by = _currentUser.PersonIdOrSystem;
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
            var now = DateTime.UtcNow;
            var written = ranked.Select(p => p.Row.SortOrder == p.Rank ? p.Row : p.Row with { SortOrder = p.Rank, LastModifiedDate = now, LastModifiedByPersonId = SystemConstants.SystemPersonId })
                .OrderBy(r => r.SortOrder)
                .ToList();
            await ReplaceRowsAsync(recipeId, written);
            var journal = new RecipeReprocessJournalEntity
            {
                RecipeId = recipeId,
                BatchId = batchId,
                Kind = RecipeReprocessKind.Order,
                RuleVersion = RecipeLineBuilder.Version,
                SourceDocumentId = source.DocumentId,
                OldRows = JsonSerializer.Serialize(rows, Json),
                NewRows = JsonSerializer.Serialize(written, Json),
                CreatedDate = now,
                CreatedByPersonId = by,
            };
            await JournalAsync(journal, by, now, rejected: false);
            if (tx != null) await tx.CommitAsync();
            one.JournalId = journal.Id;
            return one;
        }

        /// <summary>
        /// The source position of each live row, by its text: the first unused source line of the same
        /// text, else, for a " + " fold, the first of its parts; null when a row has no source line.
        /// </summary>
        public static int[]? SourcePositions(IReadOnlyList<string> sourceLines, IReadOnlyList<LineRow> live)
        {
            var keys = sourceLines.Select(FoldedLine.Key).ToList();
            var used = new bool[keys.Count];
            var positions = new int[live.Count];
            var order = Enumerable.Range(0, live.Count).OrderBy(i => live[i].SortOrder).ThenBy(i => live[i].IngredientId).ToList();
            foreach (var i in order)
            {
                var at = Unused(keys, used, FoldedLine.Key(live[i].RawLine));
                if (at < 0 && live[i].RawLine.Contains(FoldedLine.Separator, StringComparison.Ordinal))
                {
                    var parts = live[i].RawLine.Split(FoldedLine.Separator).Select(FoldedLine.Key).ToList();
                    var found = parts.Select(part => Unused(keys, used, part)).ToList();
                    if (found.All(p => p >= 0) && found.Distinct().Count() == found.Count)
                    {
                        foreach (var p in found) used[p] = true;
                        at = found.Min();
                    }
                }
                else if (at >= 0)
                {
                    used[at] = true;
                }
                if (at < 0) return null;
                positions[i] = at;
            }
            return positions;
        }

        private static int Unused(List<string> keys, bool[] used, string key)
        {
            for (var p = 0; p < keys.Count; p++)
            {
                if (!used[p] && keys[p] == key && key.Length > 0) return p;
            }
            return -1;
        }

        /// <summary>
        /// Pairs each source line with the stored row it came from: a row of the same text first, then
        /// a " + " fold that holds the line as one of its parts (a fold pairs with each of its parts).
        /// </summary>
        public static Dictionary<int, LineRow> Pair(IReadOnlyList<string> sourceLines, IReadOnlyList<LineRow> live)
        {
            var pairs = new Dictionary<int, LineRow>();
            var ordered = live.OrderBy(r => r.SortOrder).ThenBy(r => r.IngredientId).ToList();
            var used = new HashSet<LineRow>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < sourceLines.Count; i++)
            {
                var key = FoldedLine.Key(sourceLines[i]);
                if (key.Length == 0) continue;
                var row = ordered.FirstOrDefault(r => !used.Contains(r) && FoldedLine.Key(r.RawLine) == key);
                if (row == null) continue;
                used.Add(row);
                pairs[i] = row;
            }

            for (var i = 0; i < sourceLines.Count; i++)
            {
                if (pairs.ContainsKey(i)) continue;
                var key = FoldedLine.Key(sourceLines[i]);
                if (key.Length == 0) continue;
                var fold = ordered.FirstOrDefault(r => !used.Contains(r) && r.RawLine.Contains(FoldedLine.Separator, StringComparison.Ordinal)
                    && r.RawLine.Split(FoldedLine.Separator).Any(part => FoldedLine.Key(part) == key));
                if (fold != null) pairs[i] = fold;
            }

            return pairs;
        }

        private static Func<int, IngredientEntity?, IngredientEntity?> Chooser(Dictionary<int, LineRow> pairs, Dictionary<long, IngredientEntity> oldFoods) =>
            (index, found) =>
            {
                if (!pairs.TryGetValue(index, out var row) || !oldFoods.TryGetValue(row.IngredientId, out var current)) return null;
                if (current.IsDeleted || PreparedFoods.IsPrepared(current)) return null;
                if (current.CurationStatusId != (long)CurationStatusEnum.Curated)
                {
                    if (IngredientNameNormalizer.IsResidue(current.Name)) return null;
                    if (found != null && found.Id != current.Id && found.CurationStatusId == (long)CurationStatusEnum.Curated) return null;
                }
                return current;
            };

        /// <summary>
        /// Keeps the kind a stored row had (the line-classify lane's, as a rule) on the line built from
        /// the same text, spacing, case and entities aside, while the line has no amount or the same
        /// amount as before. A line that now reads an amount it did not have loses its kind.
        /// </summary>
        private static void KeepKinds(List<RecipeLineBuilder.BuiltLine> built, Dictionary<int, LineRow> pairs)
        {
            foreach (var line in built)
            {
                if (!pairs.TryGetValue(line.SortOrder, out var row) || row.LineKind == null || FoldedLine.Key(row.RawLine) != FoldedLine.Key(line.RawLine)) continue;
                if (line.Quantity == 0 || (line.Quantity == row.Quantity && line.MeasurementId == row.MeasurementId)) line.LineKind = row.LineKind;
            }
        }

        /// <summary>
        /// The kind a line with no amount and no kind of its own had on the row it is paired with, so
        /// vetting can tell whether a recipe would fail only because the rebuild dropped kinds.
        /// </summary>
        private static Func<RecipeLineBuilder.BuiltLine, string?> CarriedKind(Dictionary<int, LineRow> pairs) =>
            line => line.LineKind == null && line.Quantity == 0 && pairs.TryGetValue(line.SortOrder, out var row) ? row.LineKind : null;

        /// <summary>
        /// Puts each line built from a stored row through <see cref="LineAmountGuard"/>: where the guard keeps or
        /// holds the stored amount, the line takes the stored row's amount and kind back. The verdicts other than
        /// "same", by position, each with the amount the builder gave.
        /// </summary>
        private sealed record Guarded(LineAmountGuard.Verdict Verdict, string Reason, decimal Quantity, long MeasurementId, string? Unit, string? Kind, bool FromMethod);

        /// <summary>
        /// The lines the guard held, as holds of the rows the recipe carries after the call: a held line keeps the
        /// stored amount and kind on the row at its built position, whose food is <paramref name="foodAt"/> of it.
        /// </summary>
        private static List<LineGuardHolds.Held> Holds(long recipeId, RecipeReprocessRecipeResult one, IReadOnlyDictionary<long, string> units, Func<int, long?> foodAt) =>
            one.Lines.Where(l => l.Guard == nameof(LineAmountGuard.Verdict.Hold).ToLowerInvariant() && l.OldIngredientId != null)
                .Select(l => new LineGuardHolds.Held(
                    recipeId, foodAt(l.SortOrder) ?? l.OldIngredientId!.Value, l.SortOrder, l.RawLine, l.GuardReason ?? string.Empty,
                    l.OldFood, l.OldQuantity ?? 0, l.OldMeasurementId is { } unit ? units.GetValueOrDefault(unit) : null, l.OldLineKind,
                    l.NewIngredientId, l.NewFood, l.HeldQuantity ?? 0, l.HeldUnit, l.HeldLineKind, l.HeldFromMethod))
                .ToList();

        private static readonly Dictionary<string, string> RebuildResolves = new()
        {
            [LineGuardHoldOrigin.Rebuild] = LineGuardHolds.Released,
            [LineGuardHoldOrigin.StartupFill] = LineGuardHolds.Rebuilt,
        };

        private static Dictionary<int, Guarded> Guard(
            List<RecipeLineBuilder.BuiltLine> built, Dictionary<int, LineRow> pairs, Dictionary<long, IngredientEntity> oldFoods,
            IReadOnlyDictionary<long, string> units, IReadOnlyList<string> method)
        {
            var verdicts = new Dictionary<int, Guarded>();
            var seen = new HashSet<LineRow>(ReferenceEqualityComparer.Instance);
            foreach (var line in built)
            {
                if (!pairs.TryGetValue(line.SortOrder, out var row) || !seen.Add(row)) continue;
                var oldFood = oldFoods.GetValueOrDefault(row.IngredientId);
                var stored = new LineAmountGuard.Amount(row.Quantity, units.GetValueOrDefault(row.MeasurementId), row.LineKind, oldFood?.Name, oldFood?.GramsPerItem);
                var proposed = new LineAmountGuard.Amount(
                    line.Quantity, units.GetValueOrDefault(line.MeasurementId) ?? line.MeasurementName, line.LineKind, line.FoodName, line.Food?.GramsPerItem, line.FilledFromMethod);
                var decision = LineAmountGuard.Judge(line.RawLine, stored, proposed, method);
                var verdict = decision.Verdict;
                if (verdict == LineAmountGuard.Verdict.Same) continue;
                verdicts[line.SortOrder] = new Guarded(verdict, decision.Reason, line.Quantity, line.MeasurementId, proposed.Unit, line.LineKind, line.FilledFromMethod);
                if (verdict is LineAmountGuard.Verdict.Replace) continue;
                line.Quantity = row.Quantity;
                line.MeasurementId = row.MeasurementId;
                line.MeasurementName = stored.Unit;
                line.LineKind = row.LineKind;
                line.FilledFromMethod = false;
            }
            return verdicts;
        }

        private static List<LineRow> NewRows(List<RecipeLineBuilder.BuiltLine> built, Dictionary<int, LineRow> pairs, DateTime stamp) =>
            built.Select(line =>
            {
                var paired = pairs.GetValueOrDefault(line.SortOrder);
                return new LineRow(
                    line.Food?.Id ?? 0, line.SortOrder, line.Quantity, line.MeasurementId, line.RawLine, line.LineKind, null,
                    paired?.CreatedDate ?? stamp, paired?.CreatedByPersonId ?? SystemConstants.SystemPersonId, stamp, SystemConstants.SystemPersonId,
                    false, null, null);
            }).ToList();

        private static void Diff(RecipeReprocessRecipeResult one, List<RecipeLineBuilder.BuiltLine> built, IReadOnlyList<int> unbuilt, Dictionary<int, LineRow> pairs, List<LineRow> live, Dictionary<long, IngredientEntity> oldFoods,
            Dictionary<int, Guarded> guarded)
        {
            var counts = one.Counts;
            counts.LinesBefore = live.Count;
            counts.LinesAfter = built.Count;
            var builtAt = built.Select(b => b.SortOrder).ToHashSet();
            var kept = pairs.Where(p => builtAt.Contains(p.Key)).Select(p => p.Value).ToHashSet(ReferenceEqualityComparer.Instance);
            counts.Dropped = live.Count(row => !kept.Contains(row)) + unbuilt.Count(position => !pairs.ContainsKey(position));
            counts.NewFoods = built.Where(b => b.Food == null).Select(b => b.Wording.ToLowerInvariant()).Distinct().Count();
            var unfolded = new HashSet<LineRow>(ReferenceEqualityComparer.Instance);
            foreach (var line in built)
            {
                var old = pairs.GetValueOrDefault(line.SortOrder);
                if (old != null && !unfolded.Add(old)) old = null;
                var diff = new RecipeReprocessLineDiff
                {
                    SortOrder = line.SortOrder, RawLine = line.RawLine, NewIngredientId = line.Food?.Id, NewFood = line.FoodName,
                    NewQuantity = line.Quantity, NewMeasurementId = line.MeasurementId, NewLineKind = line.LineKind, FilledFromMethod = line.FilledFromMethod,
                    OldSortOrder = old?.SortOrder, OldIngredientId = old?.IngredientId, OldQuantity = old?.Quantity, OldMeasurementId = old?.MeasurementId,
                    OldLineKind = old?.LineKind, OldFood = old != null && oldFoods.TryGetValue(old.IngredientId, out var food) ? food.Name : null,
                };
                if (old == null)
                {
                    diff.Changes.Add("added");
                    counts.Added++;
                }
                else
                {
                    if (old.IngredientId != line.Food?.Id) { diff.Changes.Add("food"); counts.FoodChanged++; }
                    if (old.Quantity != line.Quantity || old.MeasurementId != line.MeasurementId) { diff.Changes.Add("amount"); counts.AmountChanged++; }
                    if (old.SortOrder != line.SortOrder) { diff.Changes.Add("order"); counts.OrderChanged++; }
                    if (old.LineKind != line.LineKind) { diff.Changes.Add("kind"); counts.KindChanged++; }
                    if (old.RawLine != line.RawLine) diff.Changes.Add("text");
                    if (guarded.TryGetValue(line.SortOrder, out var guard))
                    {
                        diff.Guard = guard.Verdict.ToString().ToLowerInvariant();
                        diff.GuardReason = guard.Reason;
                        switch (guard.Verdict)
                        {
                            case LineAmountGuard.Verdict.Keep: counts.GuardKept++; break;
                            case LineAmountGuard.Verdict.Replace: counts.GuardReplaced++; break;
                            default:
                                counts.GuardHeld++;
                                counts.GuardHeldByReason[guard.Reason] = counts.GuardHeldByReason.GetValueOrDefault(guard.Reason) + 1;
                                diff.Changes.Add(RecipeReprocessLineDiff.Held);
                                (diff.HeldQuantity, diff.HeldMeasurementId, diff.HeldUnit, diff.HeldLineKind, diff.HeldFromMethod) =
                                    (guard.Quantity, guard.MeasurementId, guard.Unit, guard.Kind, guard.FromMethod);
                                break;
                        }
                    }
                }
                if (line.FilledFromMethod) counts.FilledFromMethod++;
                if (line.Food != null && PreparedFoods.IsPrepared(line.Food))
                {
                    diff.Changes.Add(RecipeReprocessLineDiff.PreparedFood);
                    counts.OnPreparedFood++;
                }
                one.Lines.Add(diff);
            }
        }

        private static (long, int, decimal, long, string, string?) Key(LineRow r) => (r.IngredientId, r.SortOrder, r.Quantity, r.MeasurementId, r.RawLine, r.LineKind);

        private static bool SameRows(IReadOnlyCollection<LineRow> a, IReadOnlyCollection<LineRow> b) =>
            a.Count == b.Count && a.Select(Key).OrderBy(k => k).SequenceEqual(b.Select(Key).OrderBy(k => k));

        private static bool SameFoods(List<LineRow> live, List<RecipeLineBuilder.BuiltLine> built) =>
            built.All(b => b.Food != null) && live.Select(r => r.IngredientId).ToHashSet().SetEquals(built.Select(b => b.Food!.Id));

        private static bool NotARecipe(IEnumerable<string> issues) =>
            issues.Any(issue => NotARecipeProblems.Any(p => issue.StartsWith(p, StringComparison.Ordinal)));

        /// <summary>The recipe as it would be with the built lines, for vetting and the approval gate; never saved.</summary>
        private static RecipeEntity Shadow(RecipeEntity recipe, IEnumerable<RecipeLineBuilder.BuiltLine> built, List<RecipeStepEntity> steps,
            Func<RecipeLineBuilder.BuiltLine, string?>? kind = null) => new()
        {
            Name = recipe.Name,
            LicenseStatus = recipe.LicenseStatus,
            PrepTimeMinutes = recipe.PrepTimeMinutes,
            CookTimeMinutes = recipe.CookTimeMinutes,
            RecipeServings = recipe.RecipeServings,
            ContainsSourceProse = recipe.ContainsSourceProse,
            RecipeSteps = steps,
            RecipeIngredients = built.Select(b => new RecipeIngredientEntity
            {
                IngredientId = b.Food?.Id ?? 0,
                Ingredient = b.Food ?? new IngredientEntity { Name = b.Wording, CurationStatusId = (long)CurationStatusEnum.NonCurated },
                Quantity = b.Quantity,
                MeasurementId = b.MeasurementId,
                RawLine = b.RawLine,
                LineKind = kind?.Invoke(b) ?? b.LineKind,
                SortOrder = b.SortOrder,
            }).ToList(),
        };

        private async Task<string?> ApprovedWorseAsync(
            RecipeEntity recipe, List<LineRow> live, Dictionary<long, IngredientEntity> oldFoods, List<RecipeLineBuilder.BuiltLine> built,
            List<RecipeStepEntity> steps, List<RecipeStepEntity> newSteps, List<string> newIssues)
        {
            bool Curated(IngredientEntity? food) => food is { IsDeleted: false } && food.CurationStatusId == (long)CurationStatusEnum.Curated;
            if (built.Count(b => b.Quantity > 0) < live.Count(r => r.Quantity > 0)) return RecipeReprocessHold.ApprovedFewerAmounts;
            if (built.Count(b => Curated(b.Food)) < live.Count(r => Curated(oldFoods.GetValueOrDefault(r.IngredientId)))) return RecipeReprocessHold.ApprovedFewerCurated;

            var before = new RecipeEntity
            {
                Name = recipe.Name, LicenseStatus = recipe.LicenseStatus, PrepTimeMinutes = recipe.PrepTimeMinutes, CookTimeMinutes = recipe.CookTimeMinutes,
                RecipeServings = recipe.RecipeServings, ContainsSourceProse = recipe.ContainsSourceProse, RecipeSteps = steps,
                RecipeIngredients = live.Select(r => new RecipeIngredientEntity
                {
                    IngredientId = r.IngredientId, Ingredient = oldFoods.GetValueOrDefault(r.IngredientId)!, Quantity = r.Quantity, MeasurementId = r.MeasurementId,
                    RawLine = r.RawLine, LineKind = r.LineKind, SortOrder = r.SortOrder,
                }).ToList(),
            };
            if (newIssues.Count > 0)
            {
                var oldKinds = (await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(before))).Select(IssueKind).ToHashSet(StringComparer.Ordinal);
                if (newIssues.Any(issue => !oldKinds.Contains(IssueKind(issue)))) return RecipeReprocessHold.ApprovedVetting;
            }
            if (RecipeApprovalGate.RefusalReason(Shadow(recipe, built, newSteps)) != null && RecipeApprovalGate.RefusalReason(before) == null) return RecipeReprocessHold.ApprovedGate;
            return null;
        }

        /// <summary>
        /// The steps of a newer extraction of a book (a <c>staged-cookbook</c> record of version 2 or
        /// later) as the import would store them, when they differ from the recipe's live steps; null
        /// otherwise. A web or JSON-LD source never replaces steps: the prose lane owns those.
        /// </summary>
        private static List<(string Summary, string Description)>? ReextractedSteps(Source source, List<RecipeStepEntity> live)
        {
            if (source.Format != ScrapedDocumentFormat.StagedCookbook || source.Version <= 1) return null;
            var steps = source.Record.Steps
                .OrderBy(s => s.Order)
                .Where(s => !string.IsNullOrWhiteSpace(s.Instruction))
                .Select(s => (
                    Summary: RecipeScrapingService.Clamp(s.Instruction, 255)!,
                    Description: RecipeScrapingService.Clamp(string.IsNullOrWhiteSpace(s.Section) ? s.Instruction : $"[{s.Section}] {s.Instruction}", 2047)!))
                .ToList();
            if (steps.Count == 0) return null;
            return steps.Select(s => s.Description).SequenceEqual(live.Select(s => s.Description ?? string.Empty), StringComparer.Ordinal) ? null : steps;
        }

        /// <summary>
        /// Whether the clean-room prose lane's rewrite of the recipe's steps still stands (a
        /// <see cref="ProseLaneJournal.RewriteChangeType"/> group with step rows that no
        /// <see cref="ProseLaneJournal.UndoChangeType"/> row names). A newer extraction then holds the
        /// recipe instead of putting the source's steps back over the rewrite.
        /// </summary>
        private async Task<bool> ProseLaneRewroteStepsAsync(long recipeId)
        {
            var rewrites = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == recipeId
                    && a.ChangeType == ProseLaneJournal.RewriteChangeType
                    && (a.PropertyName == ProseLaneJournal.StateProperty || a.PropertyName!.StartsWith(ProseLaneJournal.StepPrefix)))
                .Select(a => new { a.Id, a.PropertyName, a.Timestamp })
                .ToListAsync();
            var states = rewrites.Where(a => a.PropertyName == ProseLaneJournal.StateProperty).ToList();
            if (states.Count == 0) return false;
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == recipeId && a.ChangeType == ProseLaneJournal.UndoChangeType)
                    .Select(a => a.OldValue)
                    .ToListAsync())
                .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .ToHashSet();
            return states.Any(s => !undone.Contains(s.Id)
                && rewrites.Any(a => a.Timestamp == s.Timestamp && a.PropertyName != ProseLaneJournal.StateProperty));
        }

        private Task<List<StepRow>> StepRowsAsync(long recipeId) =>
            _db.RecipeSteps.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.RecipeId == recipeId)
                .OrderBy(s => s.StepNumber)
                .Select(s => new StepRow(s.StepNumber, s.StepTypeId, s.Summary, s.Description, s.Id, s.CreatedDate, s.CreatedByPersonId,
                    s.LastModifiedDate, s.LastModifiedByPersonId, s.IsDeleted, s.DeletedAt, s.DeletedByPersonId))
                .ToListAsync();

        /// <summary>
        /// Puts the given step texts on the recipe as its steps 1, 2, 3 ..., each stamped as last
        /// changed by the system person; a step keeps the type and creation stamps of the live step
        /// at its place. The step key is (recipe, step number), so every row, deleted ones too, is
        /// replaced, as the line rows are. The recipe's prose flags are left as they are. Returns every
        /// step row before and after.
        /// </summary>
        private async Task<(List<StepRow> Old, List<StepRow> New)> ReplaceStepsAsync(long recipeId, List<(string Summary, string Description)> texts, DateTime now)
        {
            var old = await StepRowsAsync(recipeId);
            var live = old.Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).ToList();
            var rows = texts.Select((t, i) =>
            {
                var at = i < live.Count ? live[i] : null;
                return new StepRow(i + 1, at?.StepTypeId, t.Summary, t.Description, at?.Id ?? 0, at?.CreatedDate ?? now, at?.CreatedByPersonId ?? SystemConstants.SystemPersonId,
                    now, SystemConstants.SystemPersonId, false, null, null);
            }).ToList();
            await ReplaceStepRowsAsync(recipeId, rows);
            return (old, rows);
        }

        private async Task ReplaceStepRowsAsync(long recipeId, IReadOnlyList<StepRow> rows)
        {
            if (_db.Database.IsRelational())
            {
                await _db.Database.ExecuteSqlAsync($"""DELETE FROM recipe."RecipeStep" WHERE "RecipeId" = {recipeId}""");
                foreach (var r in rows)
                {
                    await _db.Database.ExecuteSqlAsync($"""
                        INSERT INTO recipe."RecipeStep"
                            ("RecipeId", "StepNumber", "StepTypeId", "Summary", "Description", "Id",
                             "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId", "IsDeleted", "DeletedAt", "DeletedByPersonId")
                        VALUES ({recipeId}, {r.StepNumber}, {r.StepTypeId}, {r.Summary}, {r.Description}, {r.Id},
                                {r.CreatedDate}, {r.CreatedByPersonId}, {r.LastModifiedDate}, {r.LastModifiedByPersonId}, {r.IsDeleted}, {r.DeletedAt}, {r.DeletedByPersonId})
                        """);
                }
                return;
            }

            var existing = await _db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == recipeId).ToListAsync();
            _db.RecipeSteps.RemoveRange(existing);
            await _db.SaveChangesAsync();
            foreach (var gone in existing) _db.Entry(gone).State = EntityState.Detached;
            _db.RecipeSteps.AddRange(rows.Select(r => new RecipeStepEntity
            {
                RecipeId = recipeId, StepNumber = r.StepNumber, StepTypeId = r.StepTypeId, Summary = r.Summary, Description = r.Description, Id = r.Id,
                CreatedDate = r.CreatedDate, CreatedByPersonId = r.CreatedByPersonId, LastModifiedDate = r.LastModifiedDate, LastModifiedByPersonId = r.LastModifiedByPersonId,
                IsDeleted = r.IsDeleted, DeletedAt = r.DeletedAt, DeletedByPersonId = r.DeletedByPersonId,
            }));
            await _db.SaveChangesAsync();
        }

        private static bool SameSteps(IReadOnlyCollection<StepRow> a, IReadOnlyCollection<StepRow> b) =>
            a.Count == b.Count
            && a.OrderBy(s => s.StepNumber).Select(s => (s.StepNumber, s.Summary, s.Description, s.IsDeleted))
                .SequenceEqual(b.OrderBy(s => s.StepNumber).Select(s => (s.StepNumber, s.Summary, s.Description, s.IsDeleted)));

        /// <summary>A vetting issue with its numbers taken out, so "Only 1 step" and "Only 2 steps" are one kind.</summary>
        public static string IssueKind(string issue) => Regex.Replace(issue, @"\d+", "#");

        private async Task<(Source? Source, string? Hold)> SourceAsync(RecipeEntity recipe)
        {
            var documents = await _db.ScrapedDocuments.AsNoTracking()
                .Where(d => d.RecipeId == recipe.Id)
                .Select(d => new { d.Id, d.Format, d.Version, d.RawJsonLd, d.SourceUrl })
                .ToListAsync();
            if (documents.Count == 0) return (null, RecipeReprocessHold.NoSource);

            Source? source = null;
            var record = documents.Where(d => d.Format != ScrapedDocumentFormat.JsonLd).OrderByDescending(d => d.Version).ThenByDescending(d => d.Id).FirstOrDefault();
            if (record != null)
            {
                try
                {
                    if (JsonSerializer.Deserialize<ScraperRecipe>(record.RawJsonLd, Json) is { } parsed) source = new Source(record.Id, record.Format, record.Version, parsed);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Stored record {DocumentId} of recipe {RecipeId} is not readable", record.Id, recipe.Id);
                }
            }

            var jsonLd = documents.Where(d => d.Format == ScrapedDocumentFormat.JsonLd).OrderByDescending(d => d.Version).ThenByDescending(d => d.Id).FirstOrDefault();
            if (source == null && jsonLd != null && _scraper.IsConfigured)
            {
                var parsed = await _scraper.ParseAsync(jsonLd.RawJsonLd, string.IsNullOrWhiteSpace(jsonLd.SourceUrl) ? recipe.SourceUrl : jsonLd.SourceUrl);
                if (parsed.Success && parsed.Recipe != null) source = new Source(jsonLd.Id, jsonLd.Format, jsonLd.Version, parsed.Recipe);
            }

            if (source == null) return (null, RecipeReprocessHold.SourceUnreadable);
            return source.Record.Ingredients.Any(i => !string.IsNullOrWhiteSpace(i.RawLine)) ? (source, null) : (null, RecipeReprocessHold.SourceEmpty);
        }

        private Task<List<LineRow>> RowsAsync(long recipeId) =>
            _db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .Where(ri => ri.RecipeId == recipeId)
                .OrderBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new LineRow(
                    ri.IngredientId, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine, ri.LineKind, EF.Property<long?>(ri, "IngredientEntityId"),
                    ri.CreatedDate, ri.CreatedByPersonId, ri.LastModifiedDate, ri.LastModifiedByPersonId, ri.IsDeleted, ri.DeletedAt, ri.DeletedByPersonId,
                    ri.AlcoholRetention, ri.AlcoholRetentionBasis))
                .ToListAsync();

        private async Task<Dictionary<long, IngredientEntity>> FoodsAsync(IEnumerable<long> ids)
        {
            var wanted = ids.Distinct().ToList();
            return await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().Where(i => wanted.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        }

        /// <summary>
        /// Puts exactly these rows on the recipe: every row it has, deleted ones too, is removed for
        /// good first, since a deleted row keeps its key taken. On the database this is plain SQL, so the
        /// rows keep the stamps they are given; the request's audit stamping would overwrite them.
        /// </summary>
        private Task ReplaceRowsAsync(long recipeId, IReadOnlyList<LineRow> rows) => ReplaceRowsOnAsync(_db, recipeId, rows);

        private static async Task ReplaceRowsOnAsync(ApplicationDbContext db, long recipeId, IReadOnlyList<LineRow> rows)
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlAsync($"""DELETE FROM recipe."RecipeIngredient" WHERE "RecipeId" = {recipeId}""");
                foreach (var r in rows)
                {
                    await db.Database.ExecuteSqlAsync($"""
                        INSERT INTO recipe."RecipeIngredient"
                            ("RecipeId", "IngredientId", "SortOrder", "Quantity", "MeasurementId", "RawLine", "LineKind", "IngredientEntityId",
                             "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId", "IsDeleted", "DeletedAt", "DeletedByPersonId",
                             "AlcoholRetention", "AlcoholRetentionBasis")
                        VALUES ({recipeId}, {r.IngredientId}, {r.SortOrder}, {r.Quantity}, {r.MeasurementId}, {r.RawLine}, {r.LineKind}, {r.IngredientEntityId},
                                {r.CreatedDate}, {r.CreatedByPersonId}, {r.LastModifiedDate}, {r.LastModifiedByPersonId}, {r.IsDeleted}, {r.DeletedAt}, {r.DeletedByPersonId},
                                {r.AlcoholRetention}, {r.AlcoholRetentionBasis})
                        """);
                }
                return;
            }

            var existing = await db.RecipeIngredients.IgnoreQueryFilters().Where(ri => ri.RecipeId == recipeId).ToListAsync();
            db.RecipeIngredients.RemoveRange(existing);
            await db.SaveChangesAsync();
            foreach (var gone in existing) db.Entry(gone).State = EntityState.Detached;
            foreach (var r in rows)
            {
                var entity = new RecipeIngredientEntity
                {
                    RecipeId = recipeId, IngredientId = r.IngredientId, SortOrder = r.SortOrder, Quantity = r.Quantity, MeasurementId = r.MeasurementId,
                    RawLine = r.RawLine, LineKind = r.LineKind, CreatedDate = r.CreatedDate, CreatedByPersonId = r.CreatedByPersonId,
                    LastModifiedDate = r.LastModifiedDate, LastModifiedByPersonId = r.LastModifiedByPersonId, IsDeleted = r.IsDeleted,
                    DeletedAt = r.DeletedAt, DeletedByPersonId = r.DeletedByPersonId,
                    AlcoholRetention = r.AlcoholRetention, AlcoholRetentionBasis = r.AlcoholRetentionBasis,
                };
                db.RecipeIngredients.Add(entity);
                db.Entry(entity).Property("IngredientEntityId").CurrentValue = r.IngredientEntityId;
            }
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Saves the journal row and its audit rows. The header of a line write names the link journal
        /// rows the rebuild wrote (<c>links=</c>), so its undo takes them back with the lines.
        /// </summary>
        private async Task JournalAsync(RecipeReprocessJournalEntity journal, long by, DateTime stamp, bool rejected, IReadOnlyCollection<AuditLogEntryEntity>? links = null)
        {
            _db.RecipeReprocessJournals.Add(journal);
            await _db.SaveChangesAsync();
            var value = string.Create(CultureInfo.InvariantCulture, $"journal={journal.Id};batch={journal.BatchId};rules={journal.RuleVersion};kind={journal.Kind}");
            if (journal.NewRows != null)
            {
                var header = links is { Count: > 0 }
                    ? string.Create(CultureInfo.InvariantCulture, $"{value};{LinksKey}{string.Join(",", links.Select(l => l.Id))}")
                    : value;
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = journal.RecipeId,
                    ChangeType = journal.Kind == RecipeReprocessKind.Order ? ReprocessOrderChangeType : ReprocessChangeType,
                    PropertyName = journal.Kind, NewValue = header, Timestamp = stamp, ChangedByPersonId = by,
                });
            }
            var recipeChange = journal.Kind == RecipeReprocessKind.DuplicateRetire ? DuplicateRetireChangeType : rejected ? RuleRejectChangeType : null;
            if (recipeChange != null)
            {
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RecipeEntityType, EntityId = journal.RecipeId, ChangeType = recipeChange,
                    PropertyName = journal.Kind, OldValue = journal.OldRecipe, NewValue = journal.KeptRecipeId is { } kept ? $"{value};kept={kept}" : value,
                    Timestamp = stamp, ChangedByPersonId = by,
                });
            }
            await _db.SaveChangesAsync();
        }

        private static RecipeState State(RecipeEntity recipe) =>
            new(recipe.CurationStatusId, recipe.VettingIssues, recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId);

        private async Task RecalculateAsync(long recipeId)
        {
            try
            {
                await _nutrition.RecalculateAsync(recipeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nutrition of recipe {RecipeId} was not recalculated after its reprocess", recipeId);
            }
        }

        public async Task<RecipeDuplicateResult> RetireDuplicatesAsync(RecipeDuplicateRequestModel request, bool apply = false)
        {
            var stamp = DateTime.UtcNow;
            var result = new RecipeDuplicateResult { Applied = apply, Stamp = stamp, BatchId = NewBatchId(stamp) };
            var wanted = request.RecipeIds.Distinct().ToList();
            var recipes = new List<Copy>();
            IQueryable<Copy> Copies(IQueryable<RecipeEntity> query) =>
                query.AsNoTracking().Where(r => r.ScrapedAtUtc != null || _db.ScrapedDocuments.Any(d => d.RecipeId == r.Id)).Select(r => new Copy(r.Id, r.Name, r.SourceUrl, r.SourceAttribution, r.CurationStatusId, r.Version));
            if (wanted.Count == 0)
            {
                recipes = await Copies(_db.Recipes).ToListAsync();
            }
            else
            {
                foreach (var chunk in wanted.Chunk(IdsPerRead)) recipes.AddRange(await Copies(_db.Recipes.Where(r => chunk.Contains(r.Id))).ToListAsync());
            }

            var groups = new List<(string By, List<Copy> Members)>();
            foreach (var g in recipes.Where(r => !string.IsNullOrWhiteSpace(r.SourceUrl)).GroupBy(r => r.SourceUrl!, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                groups.Add(("url", g.ToList()));
            }
            var inUrlGroup = groups.SelectMany(g => g.Members).Select(r => r.Id).ToHashSet();
            foreach (var g in recipes.Where(r => !inUrlGroup.Contains(r.Id) && !string.IsNullOrWhiteSpace(r.SourceAttribution))
                         .GroupBy(r => (Name: r.Name.Trim().ToLowerInvariant(), Attribution: request.AnyAttribution ? null : r.SourceAttribution))
                         .Where(g => g.Count() > 1))
            {
                groups.Add(("name", g.ToList()));
            }
            result.Groups = groups.Count;
            if (groups.Count == 0) return result;

            var ids = groups.SelectMany(g => g.Members).Select(r => r.Id).ToList();
            var referenced = await ReferencedAsync(ids);
            var lines = await LiveLinesAsync(ids);
            var by = _currentUser.PersonIdOrSystem;

            foreach (var (groupedBy, members) in groups)
            {
                var keeper = members
                    .OrderByDescending(r => referenced.Contains(r.Id))
                    .ThenByDescending(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .ThenBy(r => r.Id)
                    .First();
                foreach (var copy in members.Where(r => r.Id != keeper.Id).OrderBy(r => r.Id))
                {
                    var agreement = Agreement(lines.GetValueOrDefault(copy.Id), lines.GetValueOrDefault(keeper.Id));
                    var entry = new RecipeDuplicateCopy { RecipeId = copy.Id, KeptRecipeId = keeper.Id, GroupedBy = groupedBy, Agreement = Math.Round(agreement, 3), Outcome = RecipeDuplicateResult.Retired };
                    result.Copies.Add(entry);
                    entry.Reason = agreement < RecipeSourceService.MinimumLineAgreement ? RecipeDuplicateResult.HeldLinesDiffer
                        : referenced.Contains(copy.Id) ? RecipeDuplicateResult.HeldReferenced
                        : copy.Version > 1 ? RecipeDuplicateResult.HeldPersonEdit
                        : null;
                    if (entry.Reason != null)
                    {
                        entry.Outcome = RecipeDuplicateResult.Held;
                        result.HeldCount++;
                        continue;
                    }

                    result.RetiredCount++;
                    if (!apply) continue;

                    await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                    var tracked = await _db.Recipes.FirstAsync(r => r.Id == copy.Id);
                    var oldState = State(tracked);
                    tracked.IsDeleted = true;
                    tracked.DeletedAt = stamp;
                    tracked.DeletedByPersonId = by;
                    var journal = new RecipeReprocessJournalEntity
                    {
                        RecipeId = copy.Id, BatchId = result.BatchId, Kind = RecipeReprocessKind.DuplicateRetire, RuleVersion = RecipeLineBuilder.Version,
                        OldRecipe = JsonSerializer.Serialize(oldState, Json), NewRecipe = JsonSerializer.Serialize(State(tracked), Json),
                        KeptRecipeId = keeper.Id, CreatedDate = stamp, CreatedByPersonId = by,
                    };
                    await JournalAsync(journal, by, stamp, rejected: false);
                    if (tx != null) await tx.CommitAsync();
                    entry.JournalId = journal.Id;
                }
            }

            return result;
        }

        /// <summary>The smaller share of each copy's lines that the other copy also has (a fold counts by its parts too).</summary>
        public static double Agreement(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
        {
            a ??= Array.Empty<string>();
            b ??= Array.Empty<string>();
            HashSet<string> Keys(IEnumerable<string> lines) => lines.SelectMany(RecipeSourceService.RowKeys).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
            List<string> Own(IEnumerable<string> lines) => lines.Select(FoldedLine.Key).Where(k => k.Length > 0).ToList();
            return Math.Min(RecipeSourceService.Score(Own(a), Keys(b)), RecipeSourceService.Score(Own(b), Keys(a)));
        }

        private async Task<HashSet<long>> ReferencedAsync(List<long> ids)
        {
            var found = new HashSet<long>();
            foreach (var chunk in ids.Chunk(IdsPerRead))
            {
                found.UnionWith(await _db.MealPlans.Where(m => m.RecipeId != null && chunk.Contains(m.RecipeId.Value)).Select(m => m.RecipeId!.Value).ToListAsync());
                found.UnionWith(await _db.HouseholdRecipes.Where(h => chunk.Contains(h.RecipeId)).Select(h => h.RecipeId).ToListAsync());
                found.UnionWith(await _db.HouseholdCookbookRecipes.Where(h => chunk.Contains(h.RecipeId)).Select(h => h.RecipeId).ToListAsync());
                found.UnionWith(await _db.RecipeRatings.Where(r => chunk.Contains(r.RecipeId)).Select(r => r.RecipeId).ToListAsync());
                found.UnionWith(await _db.RecipeComments.Where(c => chunk.Contains(c.RecipeId)).Select(c => c.RecipeId).ToListAsync());
                found.UnionWith(await _db.RecipeNotes.Where(n => chunk.Contains(n.RecipeId)).Select(n => n.RecipeId).ToListAsync());
                found.UnionWith(await _db.RecipeShareTokens.Where(t => chunk.Contains(t.RecipeId)).Select(t => t.RecipeId).ToListAsync());
                found.UnionWith(await _db.ShoppingListItems.Where(s => s.RecipeId != null && chunk.Contains(s.RecipeId.Value)).Select(s => s.RecipeId!.Value).ToListAsync());
                found.UnionWith(await _db.Recipes.Where(r => r.ParentRecipeId != null && chunk.Contains(r.ParentRecipeId.Value)).Select(r => r.ParentRecipeId!.Value).ToListAsync());
            }
            return found;
        }

        private async Task<Dictionary<long, List<string>>> LiveLinesAsync(List<long> ids)
        {
            var lines = new Dictionary<long, List<string>>();
            foreach (var chunk in ids.Chunk(IdsPerRead))
            {
                var rows = await _db.RecipeIngredients.AsNoTracking()
                    .Where(ri => chunk.Contains(ri.RecipeId))
                    .OrderBy(ri => ri.SortOrder)
                    .Select(ri => new { ri.RecipeId, ri.RawLine })
                    .ToListAsync();
                foreach (var row in rows)
                {
                    if (!lines.TryGetValue(row.RecipeId, out var list)) lines[row.RecipeId] = list = new List<string>();
                    list.Add(row.RawLine);
                }
            }
            return lines;
        }

        public Task<RecipeReprocessUndoResult> UndoAsync(DateTime? sinceUtc, string? batchId, string? kind = null, bool apply = false, bool laneOnly = false) =>
            UndoAsync(new RecipeReprocessUndoRequest { SinceUtc = sinceUtc, BatchId = batchId, Kind = kind, Apply = apply, LaneOnly = laneOnly });

        private sealed record UndoFilter(DateTime? SinceUtc, string? BatchId, string? Kind, bool LaneOnly);

        private sealed record UndoBatchRecord(
            long LastEntryId, int Entries, int Restored, int Skipped, int PositionsFollowed, int StateKept, int FoodsRemoved, int FoodsKept,
            Dictionary<string, int> RestoredByKind, int AlreadyUndone = 0, double Seconds = 0);

        private sealed record UndoRunHandle(CancellationTokenSource Stop, bool LaneOnly);

        /// <summary>What one call of the undo has restored so far, carried from batch to batch as the row-by-row undo carried it from entry to entry.</summary>
        private sealed class UndoState
        {
            public Dictionary<long, List<LineRow>> Rows { get; } = new();
            public Dictionary<long, List<StepRow>> Steps { get; } = new();
            public Dictionary<long, RecipeState> States { get; } = new();
            public HashSet<long> RestoredHere { get; } = new();
            public HashSet<long> FollowedRecipes { get; } = new();
        }

        private static readonly ConcurrentDictionary<string, UndoRunHandle> UndoRuns = new(StringComparer.Ordinal);
        private static readonly SemaphoreSlim UndoStartGate = new(1, 1);
        private static readonly TimeSpan LaneUndoWait = TimeSpan.FromSeconds(1);

        /// <summary>
        /// An apply run of this API process that is going on this database, lane or operator, or null. Only
        /// one apply takes journal entries back at a time: an operator apply is refused while another runs,
        /// and the lane's own undo waits for it, so no entry is judged by two runs.
        /// </summary>
        private async Task<string?> GoingRunAsync()
        {
            var running = UndoRuns.Keys.ToList();
            if (running.Count == 0) return null;
            return await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == UndoRunEntityType && a.ChangeType == UndoRunChangeType && running.Contains(a.PropertyName!))
                .Select(a => a.PropertyName)
                .FirstOrDefaultAsync(CancellationToken.None);
        }

        /// <summary>
        /// Takes the matching journal entries back newest first, in batches. Each entry is judged exactly as
        /// one at a time: against the rows, steps and state the entries before it left, so the counts, the
        /// rows restored and the skips are the same for any batch size. Each batch reads its journals, recipes,
        /// lines, steps and later writes in a few set-based reads; an apply writes the batch's lines with one
        /// delete and one insert, its journal and audit rows with one save, the labels of the recipes whose
        /// lines it put back and a progress row, all in one transaction. Between batches it ends when asked to
        /// stop or when its caller went away, and a later call with the run id goes on below the last entry
        /// reached. One apply runs at a time on a database: an operator apply is refused while another runs, and
        /// the lane's own undo waits for it.
        /// </summary>
        public async Task<RecipeReprocessUndoResult> UndoAsync(RecipeReprocessUndoRequest request, CancellationToken cancellationToken = default)
        {
            var result = new RecipeReprocessUndoResult { Applied = request.Apply };
            var filter = new UndoFilter(request.SinceUtc, request.BatchId, request.Kind, request.LaneOnly);
            long? below = null;
            var batchNumber = 0;
            if (!string.IsNullOrWhiteSpace(request.RunId))
            {
                var earlier = await UndoProgressAsync(request.RunId);
                if (earlier == null)
                {
                    result.Refused = "There is no undo run " + request.RunId + ".";
                    return result;
                }
                if (earlier.State is RecipeReprocessUndoProgress.States.Running or RecipeReprocessUndoProgress.States.Finished)
                {
                    result.Refused = "The undo run " + request.RunId + " is " + earlier.State + ".";
                    return result;
                }
                filter = new UndoFilter(earlier.SinceUtc, earlier.BatchId, earlier.Kind, earlier.LaneOnly);
                below = earlier.LastEntryId;
                batchNumber = earlier.Batches;
            }
            if (filter.SinceUtc == null && string.IsNullOrWhiteSpace(filter.BatchId)) return result;

            Task<List<long>> ListAsync() => EntriesQuery(filter, below).OrderByDescending(j => j.Id).Select(j => j.Id).ToListAsync(CancellationToken.None);
            var by = _currentUser.PersonIdOrSystem;
            var called = DateTime.UtcNow;
            UndoRunHandle? handle = null;
            var entries = request.Apply ? new List<long>() : await ListAsync();
            while (request.Apply && handle == null)
            {
                await UndoStartGate.WaitAsync(CancellationToken.None);
                try
                {
                    var going = await GoingRunAsync();
                    if (going == null)
                    {
                        entries = await ListAsync();
                        result.RunId = request.RunId ?? NewBatchId(called, "ru-");
                        var registered = new UndoRunHandle(new CancellationTokenSource(), filter.LaneOnly);
                        if (!UndoRuns.TryAdd(result.RunId, registered))
                        {
                            registered.Stop.Dispose();
                            result.Refused = "The undo run " + result.RunId + " is going.";
                            return result;
                        }
                        handle = registered;
                        if (request.RunId == null)
                        {
                            try
                            {
                                _db.AuditLogEntries.Add(UndoRunRow(result.RunId, 0, UndoRunChangeType, JsonSerializer.Serialize(filter, Json),
                                    JsonSerializer.Serialize(new { entries = entries.Count }, Json), called, by));
                                await _db.SaveChangesAsync(CancellationToken.None);
                                _db.ChangeTracker.Clear();
                            }
                            catch
                            {
                                UndoRuns.TryRemove(result.RunId, out _);
                                registered.Stop.Dispose();
                                throw;
                            }
                        }
                        break;
                    }
                    if (!filter.LaneOnly)
                    {
                        result.Refused = "The undo run " + going + " is going; stop it or wait for it before starting another.";
                        return result;
                    }
                }
                finally
                {
                    UndoStartGate.Release();
                }
                await Task.Delay(LaneUndoWait, cancellationToken);
            }
            result.Entries = entries.Count;

            try
            {
                var state = new UndoState();
                var reached = 0;
                foreach (var batch in entries.Chunk(Math.Clamp(request.BatchSize, 1, MaxUndoBatchSize)))
                {
                    result.Stopped = cancellationToken.IsCancellationRequested ? RecipeReprocessUndoStop.ClientGone
                        : request.MaxBatches is { } most && result.Batches >= most ? RecipeReprocessUndoStop.MaxBatches
                        : handle != null && await StopAskedAsync(result.RunId!, handle, called) ? RecipeReprocessUndoStop.StopRequested
                        : null;
                    if (result.Stopped != null) break;

                    await UndoBatchAsync(batch, state, result, request.Apply, by, result.RunId, ++batchNumber);
                    result.Batches++;
                    reached += batch.Length;
                }
                result.Remaining = entries.Count - reached;
                if (handle != null)
                {
                    _db.AuditLogEntries.Add(UndoRunRow(result.RunId!, batchNumber, UndoRunEndChangeType, null, result.Stopped ?? RecipeReprocessUndoProgress.States.Finished, DateTime.UtcNow, by));
                    await _db.SaveChangesAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();
                }
            }
            finally
            {
                if (handle != null)
                {
                    UndoRuns.TryRemove(result.RunId!, out _);
                    handle.Stop.Dispose();
                }
            }

            return result;
        }

        private IQueryable<RecipeReprocessJournalEntity> EntriesQuery(UndoFilter filter, long? below)
        {
            var query = _db.RecipeReprocessJournals.AsNoTracking().Where(j => j.UndoneAt == null);
            if (filter.SinceUtc != null) query = query.Where(j => j.CreatedDate >= filter.SinceUtc.Value);
            if (!string.IsNullOrWhiteSpace(filter.BatchId)) query = query.Where(j => j.BatchId == filter.BatchId);
            if (!string.IsNullOrWhiteSpace(filter.Kind)) query = query.Where(j => j.Kind == filter.Kind);
            if (filter.LaneOnly) query = query.Where(j => j.BatchId.StartsWith(LaneBatchPrefix));
            if (FollowedWriteId(filter.BatchId) == null) query = query.Where(j => !j.BatchId.StartsWith(OrderFollowPrefix));
            if (below != null) query = query.Where(j => j.Id < below.Value);
            return query;
        }

        private async Task<bool> StopAskedAsync(string runId, UndoRunHandle handle, DateTime called) =>
            handle.Stop.IsCancellationRequested
            || await _db.AuditLogEntries.AsNoTracking().AnyAsync(a => a.EntityType == UndoRunEntityType && a.ChangeType == UndoStopChangeType
                && a.PropertyName == runId && a.Timestamp >= called);

        private static AuditLogEntryEntity UndoRunRow(string runId, long number, string changeType, string? oldValue, string? newValue, DateTime at, long by) =>
            new()
            {
                EntityType = UndoRunEntityType, EntityId = number, ChangeType = changeType, PropertyName = runId,
                OldValue = oldValue, NewValue = newValue, Timestamp = at, ChangedByPersonId = by,
            };

        /// <summary>
        /// Judges one batch of entries, newest first, as the row-by-row undo judged them, and with
        /// <paramref name="apply"/> writes the batch in one transaction with its progress row.
        /// </summary>
        private async Task UndoBatchAsync(long[] ids, UndoState state, RecipeReprocessUndoResult result, bool apply, long by, string? runId, int batchNumber)
        {
            var clock = Stopwatch.StartNew();
            var counted = (result.Restored, result.Skipped, result.PositionsFollowed, result.StateKept, result.FoodsRemoved, result.FoodsKept, result.AlreadyUndone,
                Kinds: new Dictionary<string, int>(result.RestoredByKind, StringComparer.Ordinal));
            await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            var entries = await _db.RecipeReprocessJournals.Where(j => ids.Contains(j.Id) && j.UndoneAt == null).ToDictionaryAsync(j => j.Id);
            var recipeIds = entries.Values.Select(j => j.RecipeId).Distinct().ToList();
            var recipes = await _db.Recipes.IgnoreQueryFilters().Where(r => recipeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
            var rowsRead = await RowsOfAsync(recipeIds);
            var stepRecipes = entries.Values.Where(j => j.NewSteps != null).Select(j => j.RecipeId).Distinct().ToList();
            var stepsRead = stepRecipes.Count == 0 ? new Dictionary<long, List<StepRow>>() : await StepRowsOfAsync(stepRecipes);
            var orderRecipes = entries.Values.Where(j => j.Kind == RecipeReprocessKind.Order).Select(j => j.RecipeId).Distinct().ToList();
            var standing = orderRecipes.Count == 0
                ? new Dictionary<long, List<long>>()
                : (await _db.RecipeReprocessJournals.AsNoTracking()
                        .Where(j => orderRecipes.Contains(j.RecipeId) && j.UndoneAt == null && j.NewRows != null && j.NewRows != j.OldRows)
                        .Select(j => new { j.Id, j.RecipeId })
                        .ToListAsync())
                    .GroupBy(j => j.RecipeId)
                    .ToDictionary(g => g.Key, g => g.Select(j => j.Id).ToList());
            var headers = !apply
                ? new Dictionary<long, List<AuditLogEntryEntity>>()
                : (await _db.AuditLogEntries
                        .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType
                            && (a.ChangeType == ReprocessChangeType || a.ChangeType == ReprocessOrderChangeType) && recipeIds.Contains(a.EntityId))
                        .ToListAsync())
                    .GroupBy(a => a.EntityId)
                    .ToDictionary(g => g.Key, g => g.ToList());

            var writeRows = new Dictionary<long, List<LineRow>>();
            var writeSteps = new Dictionary<long, List<StepRow>>();
            var recalculate = new HashSet<long>();
            var foodsGone = new List<AuditLogEntryEntity>();
            var linkIds = new List<long>();
            var follows = new List<RecipeReprocessJournalEntity>();
            var rebuildsUndone = new HashSet<long>();

            async Task SaveFollowsAsync()
            {
                if (follows.Count == 0) return;
                await _db.SaveChangesAsync();
                foreach (var follow in follows)
                {
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = follow.RecipeId, ChangeType = ReprocessOrderChangeType, PropertyName = follow.Kind,
                        NewValue = string.Create(CultureInfo.InvariantCulture, $"journal={follow.Id};batch={follow.BatchId};rules={follow.RuleVersion};kind={follow.Kind}"),
                        Timestamp = follow.CreatedDate, ChangedByPersonId = by,
                    });
                }
                await _db.SaveChangesAsync();
                follows.Clear();
            }

            foreach (var id in ids)
            {
                if (!entries.TryGetValue(id, out var entry))
                {
                    result.AlreadyUndone++;
                    continue;
                }
                if (!recipes.TryGetValue(entry.RecipeId, out var recipe))
                {
                    Skip(result, entry.RecipeId);
                    continue;
                }

                var rowsKept = entry.OldRows != null && entry.OldRows == entry.NewRows;
                var oldRows = entry.OldRows == null || rowsKept ? null : JsonSerializer.Deserialize<List<LineRow>>(entry.OldRows, Json);
                var newRows = entry.NewRows == null || rowsKept ? null : JsonSerializer.Deserialize<List<LineRow>>(entry.NewRows, Json);
                var oldState = entry.OldRecipe == null ? null : JsonSerializer.Deserialize<RecipeState>(entry.OldRecipe, Json);
                var newState = entry.NewRecipe == null ? null : JsonSerializer.Deserialize<RecipeState>(entry.NewRecipe, Json);
                var oldSteps = entry.OldSteps == null ? null : JsonSerializer.Deserialize<List<StepRow>>(entry.OldSteps, Json);
                var newSteps = entry.NewSteps == null ? null : JsonSerializer.Deserialize<List<StepRow>>(entry.NewSteps, Json);

                var rowsNow = state.Rows.TryGetValue(recipe.Id, out var simulated) ? simulated : rowsRead.GetValueOrDefault(recipe.Id) ?? new List<LineRow>();
                var stateNow = state.States.TryGetValue(recipe.Id, out var simulatedRecipe) ? simulatedRecipe : State(recipe);
                var linesStand = newRows == null || StandsAsWritten(rowsNow, newRows);
                var stepsStand = newSteps == null
                    || SameSteps(state.Steps.TryGetValue(recipe.Id, out var stepsSimulated) ? stepsSimulated : stepsRead.GetValueOrDefault(recipe.Id) ?? new List<StepRow>(), newSteps);
                var stateStands = newState == null || SameState(stateNow, newState);
                var approvedSince = !stateStands && stateNow.CurationStatusId == (long)CurationStatusEnum.Curated
                    && newState!.CurationStatusId != (long)CurationStatusEnum.Curated;
                if (!linesStand && stepsStand && oldRows != null && newRows != null && entry.Kind == RecipeReprocessKind.Order
                    && !entry.BatchId.StartsWith(OrderFollowPrefix, StringComparison.Ordinal)
                    && !LaterRowWriteStands(entry, standing, state)
                    && OrderFollowing(rowsNow, oldRows, newRows) is { } followed)
                {
                    result.Restored++;
                    result.PositionsFollowed++;
                    state.RestoredHere.Add(entry.Id);
                    result.RestoredByKind[entry.Kind] = result.RestoredByKind.GetValueOrDefault(entry.Kind) + 1;
                    state.Rows[recipe.Id] = followed;
                    if (apply)
                    {
                        var now = DateTime.UtcNow;
                        var written = followed.Select(r => rowsNow.Any(n => n.IngredientId == r.IngredientId && n.SortOrder == r.SortOrder && n.RawLine == r.RawLine && n.CreatedDate == r.CreatedDate)
                                ? r
                                : r with { LastModifiedDate = now, LastModifiedByPersonId = SystemConstants.SystemPersonId })
                            .ToList();
                        writeRows[recipe.Id] = written;
                        entry.UndoneAt = now;
                        var marker = string.Create(CultureInfo.InvariantCulture, $"journal={entry.Id};");
                        foreach (var header in headers.GetValueOrDefault(recipe.Id) ?? new List<AuditLogEntryEntity>())
                        {
                            if (header.ChangeType == ReprocessOrderChangeType && header.NewValue != null && header.NewValue.StartsWith(marker, StringComparison.Ordinal))
                                header.ChangeType = ReprocessUndoneChangeType;
                        }
                        _db.AuditLogEntries.Add(new AuditLogEntryEntity
                        {
                            EntityType = RecipeEntityType, EntityId = recipe.Id, ChangeType = UndoChangeType, PropertyName = entry.Kind,
                            OldValue = string.Create(CultureInfo.InvariantCulture, $"journal={entry.Id};batch={entry.BatchId}"),
                            NewValue = "positions followed",
                            Timestamp = now, ChangedByPersonId = by,
                        });
                        var follow = new RecipeReprocessJournalEntity
                        {
                            RecipeId = recipe.Id,
                            BatchId = OrderFollowPrefix + entry.Id.ToString(CultureInfo.InvariantCulture),
                            Kind = RecipeReprocessKind.Order,
                            RuleVersion = entry.RuleVersion,
                            SourceDocumentId = entry.SourceDocumentId,
                            OldRows = JsonSerializer.Serialize(rowsNow, Json),
                            NewRows = JsonSerializer.Serialize(written, Json),
                            CreatedDate = now,
                            CreatedByPersonId = by,
                        };
                        _db.RecipeReprocessJournals.Add(follow);
                        follows.Add(follow);
                        state.FollowedRecipes.Add(recipe.Id);
                    }
                    continue;
                }

                if (!linesStand || !stepsStand || (entry.NewRows == null && !stateStands) || approvedSince)
                {
                    Skip(result, recipe.Id);
                    continue;
                }

                result.Restored++;
                state.RestoredHere.Add(entry.Id);
                result.RestoredByKind[entry.Kind] = result.RestoredByKind.GetValueOrDefault(entry.Kind) + 1;
                if (!stateStands) result.StateKept++;
                if (oldRows != null) state.Rows[recipe.Id] = oldRows;
                if (oldSteps != null) state.Steps[recipe.Id] = oldSteps;
                if (stateStands && oldState != null) state.States[recipe.Id] = oldState;
                var restoredMarker = string.Create(CultureInfo.InvariantCulture, $"journal={entry.Id};");
                var removable = new List<AuditLogEntryEntity>();
                if (entry.Kind == RecipeReprocessKind.Rebuild)
                {
                    if (apply) await SaveFollowsAsync();
                    foreach (var created in await _db.AuditLogEntries
                        .Where(a => a.EntityType == FoodEntityType && a.ChangeType == FoodCreateChangeType && a.OldValue != null && a.OldValue.StartsWith(restoredMarker))
                        .ToListAsync())
                    {
                        if (await RemovableAsync(created, state.Rows, state.RestoredHere)) removable.Add(created);
                        else result.FoodsKept++;
                    }
                    result.FoodsRemoved += removable.Count;
                }
                if (!apply) continue;

                var at = DateTime.UtcNow;
                if (oldRows != null)
                {
                    recalculate.Add(recipe.Id);
                    writeRows[recipe.Id] = oldRows;
                }
                if (oldSteps != null) writeSteps[recipe.Id] = oldSteps;
                if (stateStands && oldState != null)
                {
                    recipe.CurationStatusId = oldState.CurationStatusId;
                    recipe.VettingIssues = oldState.VettingIssues;
                    recipe.IsDeleted = oldState.IsDeleted;
                    recipe.DeletedAt = oldState.DeletedAt;
                    recipe.DeletedByPersonId = oldState.DeletedByPersonId;
                    recipe.LastModifiedDate = at;
                    recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }
                entry.UndoneAt = at;
                if (entry.Kind == RecipeReprocessKind.Rebuild) rebuildsUndone.Add(recipe.Id);
                foreach (var created in removable)
                {
                    foodsGone.Add(created);
                    created.ChangeType = FoodCreateUndoneChangeType;
                }
                var restoredHeaders = (headers.GetValueOrDefault(recipe.Id) ?? new List<AuditLogEntryEntity>())
                    .Where(h => (h.ChangeType == ReprocessChangeType || h.ChangeType == ReprocessOrderChangeType) && h.NewValue != null && h.NewValue.StartsWith(restoredMarker, StringComparison.Ordinal))
                    .ToList();
                linkIds.AddRange(restoredHeaders.SelectMany(h => LinkRowIds(h.NewValue)));
                foreach (var header in restoredHeaders) header.ChangeType = ReprocessUndoneChangeType;
                if (entry.Kind == RecipeReprocessKind.Order && FollowedWriteId(entry.BatchId) is { } followedId
                    && await _db.RecipeReprocessJournals.FirstOrDefaultAsync(j => j.Id == followedId && j.UndoneAt != null) is { } followedWrite)
                {
                    followedWrite.UndoneAt = null;
                    var followedMarker = string.Create(CultureInfo.InvariantCulture, $"journal={followedWrite.Id};");
                    foreach (var header in await _db.AuditLogEntries
                        .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType && a.EntityId == recipe.Id && a.ChangeType == ReprocessUndoneChangeType
                            && a.NewValue != null && a.NewValue.StartsWith(followedMarker))
                        .ToListAsync())
                    {
                        header.ChangeType = ReprocessOrderChangeType;
                    }
                }
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RecipeEntityType, EntityId = recipe.Id, ChangeType = UndoChangeType, PropertyName = entry.Kind,
                    OldValue = string.Create(CultureInfo.InvariantCulture, $"journal={entry.Id};batch={entry.BatchId}"),
                    NewValue = stateStands ? null : "state kept",
                    Timestamp = at, ChangedByPersonId = by,
                });
            }

            if (!apply)
            {
                _db.ChangeTracker.Clear();
                return;
            }

            await SaveFollowsAsync();
            await ReplaceRowsAsync(_db, writeRows);
            foreach (var (recipeId, steps) in writeSteps) await ReplaceStepRowsAsync(recipeId, steps);
            foreach (var created in foodsGone) _db.Ingredients.Remove(await _db.Ingredients.IgnoreQueryFilters().FirstAsync(i => i.Id == created.EntityId));
            foreach (var part in linkIds.Distinct().Chunk(IdsPerRead))
            {
                foreach (var link in await _db.AuditLogEntries.Where(a => part.Contains(a.Id)
                    && (a.ChangeType == LinkApplyJournal.ChangeType || a.ChangeType == LineRelinkService.WordingResolveChangeType)).ToListAsync())
                {
                    link.ChangeType = ReprocessUndoneChangeType;
                }
            }
            await LineGuardHolds.ResolveAsync(_db, LineGuardHoldOrigin.Rebuild, rebuildsUndone, LineGuardHolds.Undone, DateTime.UtcNow);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            await RecalculateLabelsAsync(recalculate, tx);
            if (runId != null)
            {
                var kinds = result.RestoredByKind
                    .Select(k => (k.Key, Count: k.Value - counted.Kinds.GetValueOrDefault(k.Key)))
                    .Where(k => k.Count > 0)
                    .ToDictionary(k => k.Key, k => k.Count, StringComparer.Ordinal);
                var record = new UndoBatchRecord(ids[^1], ids.Length, result.Restored - counted.Restored, result.Skipped - counted.Skipped,
                    result.PositionsFollowed - counted.PositionsFollowed, result.StateKept - counted.StateKept, result.FoodsRemoved - counted.FoodsRemoved,
                    result.FoodsKept - counted.FoodsKept, kinds, result.AlreadyUndone - counted.AlreadyUndone, Math.Round(clock.Elapsed.TotalSeconds, 3));
                _db.AuditLogEntries.Add(UndoRunRow(runId, batchNumber, UndoBatchChangeType, null, JsonSerializer.Serialize(record, Json), DateTime.UtcNow, by));
                await _db.SaveChangesAsync();
            }
            if (tx != null) await tx.CommitAsync();
            _db.ChangeTracker.Clear();
        }

        /// <summary>
        /// Calculates the labels of the recipes whose lines the batch put back, from their lines and their foods'
        /// current nutrients, as the row-by-row undo did after each one, inside the batch's transaction so a batch
        /// is committed with its labels or not at all. A concurrency conflict with another label writer (the API's own,
        /// staging checkpoint 5, Q16) is retried from fresh reads, up to <see cref="LabelAttempts"/> times; any other
        /// failure, or a conflict that outlasts the retries, is logged and leaves those labels as they were, and the recipes
        /// are then calculated one at a time so one recipe's failure keeps no other label back.
        /// </summary>
        internal async Task RecalculateLabelsAsync(IReadOnlyCollection<long> recipeIds, IDbContextTransaction? tx)
        {
            if (recipeIds.Count == 0) return;
            if (await TryLabelsAsync(tx, () => _nutrition.RecalculateManyAsync(recipeIds))) return;
            foreach (var recipeId in recipeIds) await TryLabelsAsync(tx, () => _nutrition.RecalculateAsync(recipeId));
        }

        private async Task<bool> TryLabelsAsync(IDbContextTransaction? tx, Func<Task<int>> calculate)
        {
            for (var attempt = 1; ; attempt++)
            {
                if (tx != null) await tx.CreateSavepointAsync(LabelSavepoint);
                try
                {
                    await calculate();
                    return true;
                }
                catch (DbUpdateConcurrencyException ex) when (attempt < LabelAttempts)
                {
                    _logger.LogInformation(ex, "Labels of a reprocess undo batch met another label writer; calculating again (attempt {Attempt} of {Attempts})", attempt + 1, LabelAttempts);
                    if (tx != null) await tx.RollbackToSavepointAsync(LabelSavepoint);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Labels were not recalculated after a reprocess undo batch");
                    if (tx != null) await tx.RollbackToSavepointAsync(LabelSavepoint);
                    return false;
                }
                finally
                {
                    _db.ChangeTracker.Clear();
                }
                await Task.Delay(LabelRetryDelay * attempt);
            }
        }

        private const string LabelSavepoint = "undo_labels";

        /// <summary>How many times one label calculation is tried when another label writer changes the same rows.</summary>
        internal const int LabelAttempts = 4;

        private static readonly TimeSpan LabelRetryDelay = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Whether a reprocess write of rows made after <paramref name="entry"/> on the same recipe still stands:
        /// one standing when the batch was read and not restored since in this call, or an order write this call
        /// made to put positions back. Its own undo does not follow lines through an order write, so the
        /// order-only write is not followed over it.
        /// </summary>
        private static bool LaterRowWriteStands(RecipeReprocessJournalEntity entry, Dictionary<long, List<long>> standing, UndoState state) =>
            state.FollowedRecipes.Contains(entry.RecipeId)
            || (standing.TryGetValue(entry.RecipeId, out var ids) && ids.Any(id => id > entry.Id && !state.RestoredHere.Contains(id)));

        private async Task<Dictionary<long, List<LineRow>>> RowsOfAsync(IReadOnlyCollection<long> recipeIds)
        {
            var found = new Dictionary<long, List<LineRow>>();
            foreach (var part in recipeIds.Chunk(IdsPerRead))
            {
                var rows = await _db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(ri => part.Contains(ri.RecipeId))
                    .Select(ri => new
                    {
                        ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine, ri.LineKind, EntityId = EF.Property<long?>(ri, "IngredientEntityId"),
                        ri.CreatedDate, ri.CreatedByPersonId, ri.LastModifiedDate, ri.LastModifiedByPersonId, ri.IsDeleted, ri.DeletedAt, ri.DeletedByPersonId,
                        ri.AlcoholRetention, ri.AlcoholRetentionBasis,
                    })
                    .ToListAsync();
                foreach (var r in rows.OrderBy(r => r.RecipeId).ThenBy(r => r.SortOrder).ThenBy(r => r.IngredientId))
                {
                    if (!found.TryGetValue(r.RecipeId, out var list)) found[r.RecipeId] = list = new List<LineRow>();
                    list.Add(new LineRow(r.IngredientId, r.SortOrder, r.Quantity, r.MeasurementId, r.RawLine, r.LineKind, r.EntityId,
                        r.CreatedDate, r.CreatedByPersonId, r.LastModifiedDate, r.LastModifiedByPersonId, r.IsDeleted, r.DeletedAt, r.DeletedByPersonId,
                        r.AlcoholRetention, r.AlcoholRetentionBasis));
                }
            }
            return found;
        }

        private async Task<Dictionary<long, List<StepRow>>> StepRowsOfAsync(IReadOnlyCollection<long> recipeIds)
        {
            var found = new Dictionary<long, List<StepRow>>();
            foreach (var part in recipeIds.Chunk(IdsPerRead))
            {
                var steps = await _db.RecipeSteps.IgnoreQueryFilters().AsNoTracking().Where(s => part.Contains(s.RecipeId)).ToListAsync();
                foreach (var s in steps.OrderBy(s => s.RecipeId).ThenBy(s => s.StepNumber))
                {
                    if (!found.TryGetValue(s.RecipeId, out var list)) found[s.RecipeId] = list = new List<StepRow>();
                    list.Add(new StepRow(s.StepNumber, s.StepTypeId, s.Summary, s.Description, s.Id, s.CreatedDate, s.CreatedByPersonId,
                        s.LastModifiedDate, s.LastModifiedByPersonId, s.IsDeleted, s.DeletedAt, s.DeletedByPersonId));
                }
            }
            return found;
        }

        /// <summary>
        /// Puts exactly the given rows on each recipe, as <see cref="ReplaceRowsOnAsync"/> does
        /// for one: on the database one delete of every row the recipes have and one insert of all the rows,
        /// each row with the stamps it is given.
        /// </summary>
        internal static async Task ReplaceRowsAsync(ApplicationDbContext db, IReadOnlyDictionary<long, List<LineRow>> rowsByRecipe)
        {
            if (rowsByRecipe.Count == 0) return;
            if (!db.Database.IsRelational())
            {
                foreach (var (recipeId, rows) in rowsByRecipe) await ReplaceRowsOnAsync(db, recipeId, rows);
                return;
            }

            var recipeIds = rowsByRecipe.Keys.ToArray();
            await db.Database.ExecuteSqlAsync($"""DELETE FROM recipe."RecipeIngredient" WHERE "RecipeId" = ANY({recipeIds})""");
            var all = rowsByRecipe.SelectMany(p => p.Value.Select(r => (RecipeId: p.Key, Row: r))).ToList();
            if (all.Count == 0) return;
            static DateTime? Utc(DateTime? at) => at switch
            {
                null => null,
                { Kind: DateTimeKind.Utc } utc => utc,
                { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
                { } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
            };
            static NpgsqlParameter Column<T>(string name, NpgsqlDbType type, T[] values) => new(name, NpgsqlDbType.Array | type) { Value = values };
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO recipe."RecipeIngredient"
                    ("RecipeId", "IngredientId", "SortOrder", "Quantity", "MeasurementId", "RawLine", "LineKind", "IngredientEntityId",
                     "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId", "IsDeleted", "DeletedAt", "DeletedByPersonId",
                     "AlcoholRetention", "AlcoholRetentionBasis")
                SELECT * FROM unnest(@recipe, @food, @position, @quantity, @measurement, @line, @kind, @entity,
                                     @created, @createdBy, @modified, @modifiedBy, @deleted, @deletedAt, @deletedBy, @retention, @basis)
                """,
                Column("recipe", NpgsqlDbType.Bigint, all.Select(a => a.RecipeId).ToArray()),
                Column("food", NpgsqlDbType.Bigint, all.Select(a => a.Row.IngredientId).ToArray()),
                Column("position", NpgsqlDbType.Integer, all.Select(a => a.Row.SortOrder).ToArray()),
                Column("quantity", NpgsqlDbType.Numeric, all.Select(a => a.Row.Quantity).ToArray()),
                Column("measurement", NpgsqlDbType.Bigint, all.Select(a => a.Row.MeasurementId).ToArray()),
                Column("line", NpgsqlDbType.Text, all.Select(a => a.Row.RawLine).ToArray()),
                Column("kind", NpgsqlDbType.Text, all.Select(a => a.Row.LineKind).ToArray()),
                Column("entity", NpgsqlDbType.Bigint, all.Select(a => a.Row.IngredientEntityId).ToArray()),
                Column("created", NpgsqlDbType.TimestampTz, all.Select(a => Utc(a.Row.CreatedDate)!.Value).ToArray()),
                Column("createdBy", NpgsqlDbType.Bigint, all.Select(a => a.Row.CreatedByPersonId).ToArray()),
                Column("modified", NpgsqlDbType.TimestampTz, all.Select(a => Utc(a.Row.LastModifiedDate)).ToArray()),
                Column("modifiedBy", NpgsqlDbType.Bigint, all.Select(a => a.Row.LastModifiedByPersonId).ToArray()),
                Column("deleted", NpgsqlDbType.Boolean, all.Select(a => a.Row.IsDeleted).ToArray()),
                Column("deletedAt", NpgsqlDbType.TimestampTz, all.Select(a => Utc(a.Row.DeletedAt)).ToArray()),
                Column("deletedBy", NpgsqlDbType.Bigint, all.Select(a => a.Row.DeletedByPersonId).ToArray()),
                Column("retention", NpgsqlDbType.Numeric, all.Select(a => a.Row.AlcoholRetention).ToArray()),
                Column("basis", NpgsqlDbType.Text, all.Select(a => a.Row.AlcoholRetentionBasis).ToArray()));
        }

        public async Task<RecipeReprocessUndoProgress?> UndoProgressAsync(string? runId)
        {
            var starts = _db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == UndoRunEntityType && a.ChangeType == UndoRunChangeType);
            var start = string.IsNullOrWhiteSpace(runId)
                ? (await starts.OrderByDescending(a => a.Id).ToListAsync())
                    .FirstOrDefault(a => !(JsonSerializer.Deserialize<UndoFilter>(a.OldValue ?? "{}", Json)?.LaneOnly ?? false))
                : await starts.FirstOrDefaultAsync(a => a.PropertyName == runId);
            if (start?.PropertyName == null) return null;

            var id = start.PropertyName;
            var rows = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == UndoRunEntityType && a.PropertyName == id)
                .OrderBy(a => a.Id)
                .ToListAsync();
            var filter = JsonSerializer.Deserialize<UndoFilter>(start.OldValue ?? "{}", Json) ?? new UndoFilter(null, null, null, false);
            using var counted = JsonDocument.Parse(start.NewValue ?? "{}");
            var progress = new RecipeReprocessUndoProgress
            {
                RunId = id, StartedAt = start.Timestamp, SinceUtc = filter.SinceUtc, BatchId = filter.BatchId, Kind = filter.Kind, LaneOnly = filter.LaneOnly,
                EntriesAtStart = counted.RootElement.TryGetProperty("entries", out var entries) ? entries.GetInt32() : 0,
            };
            foreach (var row in rows.Where(r => r.ChangeType == UndoBatchChangeType))
            {
                var batch = JsonSerializer.Deserialize<UndoBatchRecord>(row.NewValue ?? "{}", Json);
                if (batch == null) continue;
                progress.Batches++;
                progress.Reached += batch.Entries;
                progress.Restored += batch.Restored;
                progress.Skipped += batch.Skipped;
                progress.PositionsFollowed += batch.PositionsFollowed;
                progress.StateKept += batch.StateKept;
                progress.FoodsRemoved += batch.FoodsRemoved;
                progress.FoodsKept += batch.FoodsKept;
                progress.AlreadyUndone += batch.AlreadyUndone;
                progress.WorkingSeconds += batch.Seconds;
                foreach (var (kind, count) in batch.RestoredByKind ?? new Dictionary<string, int>())
                    progress.RestoredByKind[kind] = progress.RestoredByKind.GetValueOrDefault(kind) + count;
                progress.LastEntryId = batch.LastEntryId;
                progress.LastBatchAt = row.Timestamp;
            }

            var last = rows.LastOrDefault(r => r.ChangeType is UndoBatchChangeType or UndoRunEndChangeType or UndoRunChangeType);
            progress.Outcome = rows.LastOrDefault(r => r.ChangeType == UndoRunEndChangeType)?.NewValue;
            progress.State = UndoRuns.ContainsKey(id) ? RecipeReprocessUndoProgress.States.Running
                : last?.ChangeType != UndoRunEndChangeType ? RecipeReprocessUndoProgress.States.Interrupted
                : last.NewValue == RecipeReprocessUndoProgress.States.Finished ? RecipeReprocessUndoProgress.States.Finished
                : RecipeReprocessUndoProgress.States.Stopped;
            var lastEnd = rows.LastOrDefault(r => r.ChangeType is UndoRunEndChangeType)?.Timestamp ?? DateTime.MinValue;
            progress.StopRequested = rows.Any(r => r.ChangeType == UndoStopChangeType && r.Timestamp >= lastEnd);
            progress.Remaining = progress.State == RecipeReprocessUndoProgress.States.Finished
                ? 0
                : await EntriesQuery(filter, progress.LastEntryId).CountAsync();
            progress.WorkingSeconds = Math.Round(progress.WorkingSeconds, 3);
            if (progress.WorkingSeconds > 0) progress.EntriesPerMinute = Math.Round(progress.Reached / (progress.WorkingSeconds / 60), 1);
            return progress;
        }

        public async Task<RecipeReprocessUndoProgress?> StopUndoAsync(string? runId)
        {
            var progress = await UndoProgressAsync(runId);
            if (progress == null) return null;
            _db.AuditLogEntries.Add(UndoRunRow(progress.RunId, 0, UndoStopChangeType, null, "stop asked", DateTime.UtcNow, _currentUser.PersonIdOrSystem));
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            if (UndoRuns.TryGetValue(progress.RunId, out var handle))
            {
                try
                {
                    handle.Stop.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            return await UndoProgressAsync(progress.RunId);
        }

        /// <summary>The id of the order-only write an order write of <see cref="OrderFollowPrefix"/> took back, or null for any other batch.</summary>
        public static long? FollowedWriteId(string? batchId) =>
            batchId != null && batchId.StartsWith(OrderFollowPrefix, StringComparison.Ordinal)
            && long.TryParse(batchId[OrderFollowPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;

        /// <summary>
        /// The recipe's rows as they are now with the positions an order-only write found, where a later step
        /// changed its lines only in ways that leave positions alone (a food link, an amount, a kind): every
        /// live row now is one the write left, known by its text and creation time, and none was added or
        /// removed since. A row whose food is taken at its old position by another row goes to the next free
        /// position. Null when the rows cannot be paired that way.
        /// </summary>
        public static List<LineRow>? OrderFollowing(IReadOnlyCollection<LineRow> now, IReadOnlyCollection<LineRow> before, IReadOnlyCollection<LineRow> written)
        {
            if (now.Any(r => r.IsDeleted) || written.Any(r => r.IsDeleted) || now.Count != written.Count) return null;

            var open = before.Where(r => !r.IsDeleted).OrderBy(r => r.SortOrder).ThenBy(r => r.IngredientId).ToList();
            var placeBefore = new Dictionary<LineRow, int>(ReferenceEqualityComparer.Instance);
            foreach (var row in written.OrderBy(r => r.SortOrder))
            {
                var match = open.FirstOrDefault(o => o.IngredientId == row.IngredientId && o.RawLine == row.RawLine && o.CreatedDate == row.CreatedDate
                    && o.Quantity == row.Quantity && o.MeasurementId == row.MeasurementId);
                if (match == null) return null;
                open.Remove(match);
                placeBefore[row] = match.SortOrder;
            }

            var unpaired = written.ToList();
            var pairs = new List<(LineRow Now, LineRow Written)>();
            foreach (var row in now.OrderBy(r => r.SortOrder).ThenBy(r => r.IngredientId))
            {
                var same = unpaired.FirstOrDefault(w => w.RawLine == row.RawLine && w.CreatedDate == row.CreatedDate && w.SortOrder == row.SortOrder);
                if (same == null)
                {
                    var byText = unpaired.Where(w => w.RawLine == row.RawLine && w.CreatedDate == row.CreatedDate).ToList();
                    if (byText.Count != 1 || now.Count(n => n.RawLine == row.RawLine && n.CreatedDate == row.CreatedDate) != 1) return null;
                    same = byText[0];
                }
                unpaired.Remove(same);
                pairs.Add((row, same));
            }

            var taken = new HashSet<(long, int)>();
            var next = Math.Max(now.Max(r => (int?)r.SortOrder) ?? 0, placeBefore.Values.DefaultIfEmpty(0).Max()) + 1;
            var followed = new List<LineRow>();
            foreach (var (row, write) in pairs.OrderBy(p => placeBefore[p.Written]).ThenBy(p => p.Now.IngredientId))
            {
                var place = placeBefore[write];
                if (!taken.Add((row.IngredientId, place)))
                {
                    place = next++;
                    taken.Add((row.IngredientId, place));
                }
                followed.Add(row with { SortOrder = place });
            }
            return followed.OrderBy(r => r.SortOrder).ThenBy(r => r.IngredientId).ToList();
        }

        /// <summary>
        /// Whether a food a rebuild created can go again with that rebuild's undo: it is as the rebuild
        /// made it (uncurated, same name, no other journal entry), and once the undone recipes have their
        /// rows back no recipe line, alias, proposal, nutrient, wording, measure or household row refers to it,
        /// and no standing journal would put a line back on it: the rows before of another reprocess not
        /// undone in this call, or the place before of a line relink or an applied link.
        /// </summary>
        private async Task<bool> RemovableAsync(AuditLogEntryEntity created, Dictionary<long, List<LineRow>> simulatedRows, IReadOnlyCollection<long> restoredHere)
        {
            var id = created.EntityId;
            var food = await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            if (food == null || food.IsDeleted || food.CurationStatusId != (long)CurationStatusEnum.NonCurated || food.FdcId != null || food.Name != created.NewValue) return false;
            if (simulatedRows.Values.Any(rows => rows.Any(r => r.IngredientId == id || r.IngredientEntityId == id))) return false;
            var restored = simulatedRows.Keys.ToList();
            return !await _db.RecipeIngredients.IgnoreQueryFilters()
                    .AnyAsync(ri => (ri.IngredientId == id || EF.Property<long?>(ri, "IngredientEntityId") == id) && !restored.Contains(ri.RecipeId))
                && !await _db.IngredientAliases.IgnoreQueryFilters().AnyAsync(a => a.IngredientId == id)
                && !await _db.FoodCatalogProposals.IgnoreQueryFilters().AnyAsync(p => p.IngredientId == id)
                && !await _db.IngredientNutrients.IgnoreQueryFilters().AnyAsync(n => n.IngredientId == id)
                && !await _db.IngredientWordings.IgnoreQueryFilters().AnyAsync(w => w.IngredientId == id)
                && !await _db.IngredientWordingRejections.IgnoreQueryFilters().AnyAsync(w => w.IngredientId == id)
                && !await _db.IngredientVerifications.IgnoreQueryFilters().AnyAsync(v => v.IngredientId == id)
                && !await _db.IngredientExtras.IgnoreQueryFilters().AnyAsync(e => e.IngredientId == id)
                && !await _db.IngredientMeasurements.IgnoreQueryFilters().AnyAsync(m => m.IngredientId == id)
                && !await _db.IngredientContainerSizes.IgnoreQueryFilters().AnyAsync(c => c.IngredientId == id)
                && !await _db.AuditLogEntries.AnyAsync(a => a.EntityId == id && a.Id != created.Id && a.EntityType == FoodEntityType)
                && !await LineRelinkService.IsUsedOutsideRecipeLinesAsync(_db, id)
                && !await NamedByLineJournalAsync(id)
                && !await NamedByStandingRowsAsync(id, restoredHere);
        }

        private Task<bool> NamedByLineJournalAsync(long id)
        {
            var place = string.Create(CultureInfo.InvariantCulture, $"{id}@");
            var linkPlace = string.Create(CultureInfo.InvariantCulture, $":{id}@");
            return _db.AuditLogEntries.AnyAsync(a =>
                (a.EntityType == ScrapedRecipeRepairService.LineEntityType && a.OldValue != null && a.OldValue.StartsWith(place))
                || (a.EntityType == LinkApplyJournal.EntityType && a.ChangeType == LinkApplyJournal.ChangeType && a.PropertyName == LinkApplyJournal.Line
                    && a.OldValue != null && a.OldValue.Contains(linkPlace)));
        }

        private async Task<bool> NamedByStandingRowsAsync(long id, IReadOnlyCollection<long> restoredHere)
        {
            var skip = restoredHere.ToArray();
            if (_db.Database.IsRelational())
            {
                var byFood = JsonSerializer.Serialize(new[] { new { IngredientId = id } }, Json);
                var byEntity = JsonSerializer.Serialize(new[] { new { IngredientEntityId = id } }, Json);
                return (await _db.Database.SqlQuery<bool>(
                    $"""
                     SELECT EXISTS (SELECT 1 FROM recipe."RecipeReprocessJournal" j
                                    WHERE j."UndoneAt" IS NULL AND j."OldRows" IS NOT NULL AND NOT (j."Id" = ANY({skip}))
                                      AND (j."OldRows" @> {byFood}::jsonb OR j."OldRows" @> {byEntity}::jsonb)) AS "Value"
                     """).ToListAsync())[0];
            }

            var standing = await _db.RecipeReprocessJournals.AsNoTracking()
                .Where(j => j.UndoneAt == null && j.OldRows != null && !skip.Contains(j.Id))
                .Select(j => j.OldRows!)
                .ToListAsync();
            return standing.Any(rows => (JsonSerializer.Deserialize<List<LineRow>>(rows, Json) ?? new List<LineRow>())
                .Any(r => r.IngredientId == id || r.IngredientEntityId == id));
        }

        /// <summary>The ids of the link journal rows a reprocess header names (<c>links=</c>), which its rebuild wrote.</summary>
        public static IEnumerable<long> LinkRowIds(string? header) =>
            (header ?? string.Empty).Split(';')
                .Where(part => part.StartsWith(LinksKey, StringComparison.Ordinal))
                .SelectMany(part => part[LinksKey.Length..].Split(','))
                .Select(id => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
                .Where(id => id > 0);

        private static void Skip(RecipeReprocessUndoResult result, long recipeId)
        {
            result.Skipped++;
            if (result.SkippedRecipeIds.Count < 200) result.SkippedRecipeIds.Add(recipeId);
        }

        /// <summary>
        /// Whether the recipe's rows are still the rows a write left, deleted ones included. A kind the
        /// line-classify lane gave since to a row the write left without one does not count as a
        /// change: that lane only labels unclassified rows and no undo reads its labels, so the undo
        /// still restores the rows as they were before the write.
        /// </summary>
        public static bool StandsAsWritten(IReadOnlyCollection<LineRow> now, IReadOnlyCollection<LineRow> written)
        {
            if (now.Count != written.Count) return false;
            static (long, int, decimal, long, string, bool) Place(LineRow r) => (r.IngredientId, r.SortOrder, r.Quantity, r.MeasurementId, r.RawLine, r.IsDeleted);
            var a = now.OrderBy(Place).ToList();
            var b = written.OrderBy(Place).ToList();
            for (var i = 0; i < a.Count; i++)
            {
                if (Place(a[i]) != Place(b[i])) return false;
                if (a[i].LineKind != b[i].LineKind && b[i].LineKind != null) return false;
            }
            return true;
        }

        /// <summary>
        /// The recipes among <paramref name="recipeIds"/> whose lines a full rebuild wrote and whose
        /// rebuild stands (not undone). The start-up repair leaves these recipes alone: their amounts
        /// came from the rebuild, which journals them, and a new rule reaches them by the next rebuild.
        /// </summary>
        public static async Task<HashSet<long>> RebuiltRecipesAsync(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds, System.Threading.CancellationToken cancellationToken = default)
        {
            var found = new HashSet<long>();
            foreach (var chunk in recipeIds.Distinct().Chunk(IdsPerRead))
            {
                found.UnionWith(await db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType && a.ChangeType == ReprocessChangeType && chunk.Contains(a.EntityId))
                    .Select(a => a.EntityId)
                    .Distinct()
                    .ToListAsync(cancellationToken));
            }
            return found;
        }

        private static bool SameState(RecipeState a, RecipeState b) =>
            a.CurationStatusId == b.CurationStatusId && a.VettingIssues == b.VettingIssues && a.IsDeleted == b.IsDeleted;

        /// <summary>
        /// The newest standing reprocess of each of the given recipes that rewrote its lines. An
        /// order-only write does not count: it changes positions and nothing else, so an older line
        /// journal's undo follows it (<see cref="Support.RecipeOrderTrail"/>). A rebuild that replaced
        /// only the steps and left the rows as they were does not count either. An older line
        /// journal's undo skips a recipe rebuilt after that journal's entry: the rebuild replaced the
        /// rows the entry names, so it is undone first.
        /// </summary>
        public static async Task<Dictionary<long, DateTime>> RebuiltAtAsync(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds)
        {
            var found = new Dictionary<long, DateTime>();
            foreach (var chunk in recipeIds.Distinct().Chunk(IdsPerRead))
            {
                var headers = db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType
                        && a.ChangeType == ReprocessChangeType && chunk.Contains(a.EntityId));
                var newest = await headers
                    .GroupBy(a => a.EntityId)
                    .Select(g => new { RecipeId = g.Key, At = g.Max(a => a.Timestamp) })
                    .ToListAsync();
                foreach (var header in newest) found[header.RecipeId] = header.At;

                var rowsKept = await db.RecipeReprocessJournals.AsNoTracking()
                    .Where(j => chunk.Contains(j.RecipeId) && j.UndoneAt == null && j.OldRows != null && j.OldRows == j.NewRows)
                    .Select(j => new { j.Id, j.RecipeId })
                    .ToListAsync();
                if (rowsKept.Count == 0) continue;
                var keptIds = rowsKept.Select(j => j.Id).ToHashSet();
                var keptRecipes = rowsKept.Select(j => j.RecipeId).Distinct().ToList();
                var theirs = await headers.Where(a => keptRecipes.Contains(a.EntityId))
                    .Select(a => new { RecipeId = a.EntityId, At = a.Timestamp, a.NewValue })
                    .ToListAsync();
                foreach (var recipeId in keptRecipes)
                {
                    var rewrote = theirs.Where(h => h.RecipeId == recipeId && (JournalIdOf(h.NewValue) is not { } id || !keptIds.Contains(id))).ToList();
                    if (rewrote.Count == 0) found.Remove(recipeId);
                    else found[recipeId] = rewrote.Max(h => h.At);
                }
            }
            return found;
        }

        /// <summary>The journal id a reprocess header names (<c>journal=</c>), or null.</summary>
        internal static long? JournalIdOf(string? header) =>
            (header ?? string.Empty).Split(';')
                .Where(part => part.StartsWith(JournalKey, StringComparison.Ordinal))
                .Select(part => long.TryParse(part[JournalKey.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : (long?)null)
                .FirstOrDefault();
    }
}
