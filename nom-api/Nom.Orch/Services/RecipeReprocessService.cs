using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
        public const string RuleRejectIssue = "Rejected by rule: neither the source nor the rebuilt recipe is a recipe";
        public const int MaxRecipesPerCall = 500;

        private const int IdsPerRead = 1000;
        private const string LinksKey = "links=";
        private static readonly string[] NotARecipeProblems = { "Text is placeholder copy", "Text has no real words" };

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IRecipeScraperClient _scraper;
        private readonly IRecipeVettingService _vetting;
        private readonly IRecipeNutritionService _nutrition;
        private readonly ILogger<RecipeReprocessService> _logger;
        private readonly RecipeLineBuilder _builder;

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
            bool IsDeleted, DateTime? DeletedAt, long? DeletedByPersonId);

        /// <summary>A recipe's curation state and deletion, for the journal.</summary>
        public sealed record RecipeState(long CurationStatusId, string? VettingIssues, bool IsDeleted, DateTime? DeletedAt, long? DeletedByPersonId);

        private sealed record Source(long DocumentId, string Format, ScraperRecipe Record);
        private sealed record Copy(long Id, string Name, string? SourceUrl, string? SourceAttribution, long CurationStatusId, long Version);

        public static string NewBatchId(DateTime stamp) =>
            string.Create(CultureInfo.InvariantCulture, $"rp-{stamp:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid():N}")[..32];

        public async Task<RecipeReprocessResult> RebuildAsync(RecipeReprocessRequestModel request, bool apply = false, bool orderOnly = false)
        {
            var stamp = DateTime.UtcNow;
            var result = new RecipeReprocessResult
            {
                Applied = apply, OrderOnly = orderOnly, Stamp = stamp, BatchId = NewBatchId(stamp), RuleVersion = RecipeLineBuilder.Version,
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
                    case RecipeReprocessOutcome.Unchanged: result.Unchanged++; break;
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
            var method = steps.Select(s => s.Description ?? string.Empty).ToList();
            var pairs = Pair(source.Record.Ingredients.Select(i => RecipeScrapingService.Clamp(i.RawLine, 2047) ?? string.Empty).ToList(), live);
            var choose = Chooser(pairs, oldFoods);

            var built = await _builder.BuildAsync(source.Record.Ingredients, _currentUser.PersonId, write: false, method, choose);
            KeepKinds(built, pairs);
            var newRows = NewRows(built, pairs, stamp);
            Diff(one, built, pairs, live, oldFoods);

            var unchanged = built.All(b => b.Food != null) && SameRows(live, newRows);
            var today = DateOnly.FromDateTime(stamp);
            var planned = await _db.MealPlans.AnyAsync(m => m.RecipeId == recipeId && m.Date >= today);
            if (!unchanged && planned && !SameFoods(live, built)) return Hold(one, RecipeReprocessHold.PlannedFoodChange);

            var newIssues = await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(Shadow(recipe, built, steps)));
            var curated = recipe.CurationStatusId == (long)CurationStatusEnum.Curated;
            if (!unchanged && curated && await ApprovedWorseAsync(recipe, live, oldFoods, built, steps, newIssues) is { } worse) return Hold(one, worse);

            var reject = !curated && recipe.DateCurationCompleted == null
                && (recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated || recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision)
                && NotARecipe(RecipeVettingService.TextProblems(source.Record)) && NotARecipe(newIssues);
            one.Rejected = reject;
            one.Outcome = unchanged ? RecipeReprocessOutcome.Unchanged : RecipeReprocessOutcome.Rebuilt;
            if (unchanged && !reject) one.Lines.Clear();
            if (!apply || (unchanged && !reject)) return one;

            var by = _currentUser.PersonIdOrSystem;
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
            var now = DateTime.UtcNow;
            var oldState = State(recipe);
            if (!unchanged)
            {
                built = await _builder.BuildAsync(source.Record.Ingredients, _currentUser.PersonId, write: true, method, choose);
                KeepKinds(built, pairs);
                var written = NewRows(built, pairs, now);
                await ReplaceRowsAsync(recipeId, written);
                var links = new List<AuditLogEntryEntity>();
                foreach (var line in built.Where(l => l.Link != null))
                {
                    links.AddRange(LinkApplyJournal.LinesMoved(
                        line.Link!.ProposalId, line.Link.HolderId, line.Food!.Id, Array.Empty<LinkApplyJournal.LineShift>(),
                        new[] { new LinkApplyJournal.LinePlace { RecipeId = recipeId, SortOrder = line.SortOrder } }, now, by));
                }
                _db.AuditLogEntries.AddRange(links);

                if (!reject && recipe.DateCurationCompleted == null
                    && (recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated || recipe.CurationStatusId == (long)CurationStatusEnum.RequiresRevision))
                {
                    recipe.VettingIssues = newIssues.Count > 0 ? string.Join("\n", newIssues) : null;
                    recipe.CurationStatusId = newIssues.Count > 0 ? (long)CurationStatusEnum.RequiresRevision : (long)CurationStatusEnum.NonCurated;
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
                    OldRecipe = JsonSerializer.Serialize(oldState, Json),
                    NewRecipe = JsonSerializer.Serialize(rebuiltState, Json),
                    CreatedDate = now,
                    CreatedByPersonId = by,
                };
                await JournalAsync(rebuild, by, now, rejected: false, links);
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
            var positions = SourcePositions(source.Record.Ingredients.Select(i => RecipeScrapingService.Clamp(i.RawLine, 2047) ?? string.Empty).ToList(), live);
            if (positions == null) return Hold(one, RecipeReprocessHold.OrderUnpaired);

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

        private static void KeepKinds(List<RecipeLineBuilder.BuiltLine> built, Dictionary<int, LineRow> pairs)
        {
            foreach (var line in built)
            {
                if (line.LineKind != null || line.Quantity != 0 || !pairs.TryGetValue(line.SortOrder, out var row)) continue;
                if (row.LineKind != null && row.RawLine == line.RawLine) line.LineKind = row.LineKind;
            }
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

        private static void Diff(RecipeReprocessRecipeResult one, List<RecipeLineBuilder.BuiltLine> built, Dictionary<int, LineRow> pairs, List<LineRow> live, Dictionary<long, IngredientEntity> oldFoods)
        {
            var counts = one.Counts;
            counts.LinesBefore = live.Count;
            counts.LinesAfter = built.Count;
            counts.Dropped = live.Count(row => !pairs.Values.Contains(row));
            counts.NewFoods = built.Where(b => b.Food == null).Select(b => b.Wording.ToLowerInvariant()).Distinct().Count();
            foreach (var line in built)
            {
                var old = pairs.GetValueOrDefault(line.SortOrder);
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
        private static RecipeEntity Shadow(RecipeEntity recipe, IEnumerable<RecipeLineBuilder.BuiltLine> built, List<RecipeStepEntity> steps) => new()
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
                LineKind = b.LineKind,
                SortOrder = b.SortOrder,
            }).ToList(),
        };

        private async Task<string?> ApprovedWorseAsync(
            RecipeEntity recipe, List<LineRow> live, Dictionary<long, IngredientEntity> oldFoods, List<RecipeLineBuilder.BuiltLine> built, List<RecipeStepEntity> steps, List<string> newIssues)
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
            if (RecipeApprovalGate.RefusalReason(Shadow(recipe, built, steps)) != null && RecipeApprovalGate.RefusalReason(before) == null) return RecipeReprocessHold.ApprovedGate;
            return null;
        }

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
                    if (JsonSerializer.Deserialize<ScraperRecipe>(record.RawJsonLd, Json) is { } parsed) source = new Source(record.Id, record.Format, parsed);
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
                if (parsed.Success && parsed.Recipe != null) source = new Source(jsonLd.Id, jsonLd.Format, parsed.Recipe);
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
                    ri.CreatedDate, ri.CreatedByPersonId, ri.LastModifiedDate, ri.LastModifiedByPersonId, ri.IsDeleted, ri.DeletedAt, ri.DeletedByPersonId))
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
        private async Task ReplaceRowsAsync(long recipeId, IReadOnlyList<LineRow> rows)
        {
            if (_db.Database.IsRelational())
            {
                await _db.Database.ExecuteSqlAsync($"""DELETE FROM recipe."RecipeIngredient" WHERE "RecipeId" = {recipeId}""");
                foreach (var r in rows)
                {
                    await _db.Database.ExecuteSqlAsync($"""
                        INSERT INTO recipe."RecipeIngredient"
                            ("RecipeId", "IngredientId", "SortOrder", "Quantity", "MeasurementId", "RawLine", "LineKind", "IngredientEntityId",
                             "CreatedDate", "CreatedByPersonId", "LastModifiedDate", "LastModifiedByPersonId", "IsDeleted", "DeletedAt", "DeletedByPersonId")
                        VALUES ({recipeId}, {r.IngredientId}, {r.SortOrder}, {r.Quantity}, {r.MeasurementId}, {r.RawLine}, {r.LineKind}, {r.IngredientEntityId},
                                {r.CreatedDate}, {r.CreatedByPersonId}, {r.LastModifiedDate}, {r.LastModifiedByPersonId}, {r.IsDeleted}, {r.DeletedAt}, {r.DeletedByPersonId})
                        """);
                }
                return;
            }

            var existing = await _db.RecipeIngredients.IgnoreQueryFilters().Where(ri => ri.RecipeId == recipeId).ToListAsync();
            _db.RecipeIngredients.RemoveRange(existing);
            await _db.SaveChangesAsync();
            foreach (var gone in existing) _db.Entry(gone).State = EntityState.Detached;
            foreach (var r in rows)
            {
                var entity = new RecipeIngredientEntity
                {
                    RecipeId = recipeId, IngredientId = r.IngredientId, SortOrder = r.SortOrder, Quantity = r.Quantity, MeasurementId = r.MeasurementId,
                    RawLine = r.RawLine, LineKind = r.LineKind, CreatedDate = r.CreatedDate, CreatedByPersonId = r.CreatedByPersonId,
                    LastModifiedDate = r.LastModifiedDate, LastModifiedByPersonId = r.LastModifiedByPersonId, IsDeleted = r.IsDeleted,
                    DeletedAt = r.DeletedAt, DeletedByPersonId = r.DeletedByPersonId,
                };
                _db.RecipeIngredients.Add(entity);
                _db.Entry(entity).Property("IngredientEntityId").CurrentValue = r.IngredientEntityId;
            }
            await _db.SaveChangesAsync();
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

        public async Task<RecipeReprocessUndoResult> UndoAsync(DateTime? sinceUtc, string? batchId, string? kind = null, bool apply = false)
        {
            var result = new RecipeReprocessUndoResult { Applied = apply };
            if (sinceUtc == null && string.IsNullOrWhiteSpace(batchId)) return result;

            var query = _db.RecipeReprocessJournals.AsNoTracking().Where(j => j.UndoneAt == null);
            if (sinceUtc != null) query = query.Where(j => j.CreatedDate >= sinceUtc.Value);
            if (!string.IsNullOrWhiteSpace(batchId)) query = query.Where(j => j.BatchId == batchId);
            if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(j => j.Kind == kind);
            var entries = await query.OrderByDescending(j => j.Id).Select(j => j.Id).ToListAsync();
            result.Entries = entries.Count;

            var simulatedRows = new Dictionary<long, List<LineRow>>();
            var simulatedState = new Dictionary<long, RecipeState>();
            var by = _currentUser.PersonIdOrSystem;
            foreach (var id in entries)
            {
                var entry = await _db.RecipeReprocessJournals.FirstAsync(j => j.Id == id);
                var recipe = await _db.Recipes.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == entry.RecipeId);
                if (recipe == null)
                {
                    Skip(result, entry.RecipeId);
                    continue;
                }

                var oldRows = entry.OldRows == null ? null : JsonSerializer.Deserialize<List<LineRow>>(entry.OldRows, Json);
                var newRows = entry.NewRows == null ? null : JsonSerializer.Deserialize<List<LineRow>>(entry.NewRows, Json);
                var oldState = entry.OldRecipe == null ? null : JsonSerializer.Deserialize<RecipeState>(entry.OldRecipe, Json);
                var newState = entry.NewRecipe == null ? null : JsonSerializer.Deserialize<RecipeState>(entry.NewRecipe, Json);

                var rowsNow = simulatedRows.TryGetValue(recipe.Id, out var simulated) ? simulated : await RowsAsync(recipe.Id);
                var stateNow = simulatedState.TryGetValue(recipe.Id, out var simulatedRecipe) ? simulatedRecipe : State(recipe);
                var linesStand = newRows == null || SameRowsWithDeletion(rowsNow, newRows);
                var stateStands = newState == null || SameState(stateNow, newState);
                var approvedSince = !stateStands && stateNow.CurationStatusId == (long)CurationStatusEnum.Curated
                    && newState!.CurationStatusId != (long)CurationStatusEnum.Curated;
                if (!linesStand || (newRows == null && !stateStands) || approvedSince)
                {
                    Skip(result, recipe.Id);
                    continue;
                }

                result.Restored++;
                result.RestoredByKind[entry.Kind] = result.RestoredByKind.GetValueOrDefault(entry.Kind) + 1;
                if (!stateStands) result.StateKept++;
                if (oldRows != null) simulatedRows[recipe.Id] = oldRows;
                if (stateStands && oldState != null) simulatedState[recipe.Id] = oldState;
                if (!apply) continue;

                var now = DateTime.UtcNow;
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                if (oldRows != null) await ReplaceRowsAsync(recipe.Id, oldRows);
                if (stateStands && oldState != null)
                {
                    recipe.CurationStatusId = oldState.CurationStatusId;
                    recipe.VettingIssues = oldState.VettingIssues;
                    recipe.IsDeleted = oldState.IsDeleted;
                    recipe.DeletedAt = oldState.DeletedAt;
                    recipe.DeletedByPersonId = oldState.DeletedByPersonId;
                    recipe.LastModifiedDate = now;
                    recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }
                entry.UndoneAt = now;
                var marker = string.Create(CultureInfo.InvariantCulture, $"journal={entry.Id};");
                var headers = await _db.AuditLogEntries
                    .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType && a.EntityId == recipe.Id
                        && (a.ChangeType == ReprocessChangeType || a.ChangeType == ReprocessOrderChangeType)
                        && a.NewValue != null && a.NewValue.StartsWith(marker))
                    .ToListAsync();
                var linkIds = headers.SelectMany(h => LinkRowIds(h.NewValue)).ToList();
                var links = linkIds.Count == 0
                    ? new List<AuditLogEntryEntity>()
                    : await _db.AuditLogEntries.Where(a => linkIds.Contains(a.Id) && a.ChangeType == LinkApplyJournal.ChangeType).ToListAsync();
                foreach (var header in headers.Concat(links)) header.ChangeType = ReprocessUndoneChangeType;
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RecipeEntityType, EntityId = recipe.Id, ChangeType = UndoChangeType, PropertyName = entry.Kind,
                    OldValue = string.Create(CultureInfo.InvariantCulture, $"journal={entry.Id};batch={entry.BatchId}"),
                    NewValue = stateStands ? null : "state kept",
                    Timestamp = now, ChangedByPersonId = by,
                });
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                if (oldRows != null) await RecalculateAsync(recipe.Id);
            }

            return result;
        }

        private static IEnumerable<long> LinkRowIds(string? header) =>
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

        private static bool SameRowsWithDeletion(IReadOnlyCollection<LineRow> a, IReadOnlyCollection<LineRow> b) =>
            a.Count == b.Count && a.Select(r => (Key(r), r.IsDeleted)).OrderBy(k => k).SequenceEqual(b.Select(r => (Key(r), r.IsDeleted)).OrderBy(k => k));

        private static bool SameState(RecipeState a, RecipeState b) =>
            a.CurationStatusId == b.CurationStatusId && a.VettingIssues == b.VettingIssues && a.IsDeleted == b.IsDeleted;

        /// <summary>
        /// The newest standing reprocess of each of the given recipes that rewrote its lines, order-only
        /// writes included. An older
        /// line journal's undo skips a recipe rebuilt after that journal's entry: the rebuild replaced
        /// the rows the entry names, so it is undone first.
        /// </summary>
        public static async Task<Dictionary<long, DateTime>> RebuiltAtAsync(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds)
        {
            var found = new Dictionary<long, DateTime>();
            foreach (var chunk in recipeIds.Distinct().Chunk(IdsPerRead))
            {
                var headers = await db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType
                        && (a.ChangeType == ReprocessChangeType || a.ChangeType == ReprocessOrderChangeType) && chunk.Contains(a.EntityId))
                    .GroupBy(a => a.EntityId)
                    .Select(g => new { RecipeId = g.Key, At = g.Max(a => a.Timestamp) })
                    .ToListAsync();
                foreach (var header in headers) found[header.RecipeId] = header.At;
            }
            return found;
        }
    }
}
