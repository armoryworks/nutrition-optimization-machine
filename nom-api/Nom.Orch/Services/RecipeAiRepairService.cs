using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class RecipeAiRepairService : IRecipeAiRepairService
    {
        public const string AttemptChangeType = "AiRepairAttempt";
        public const string RepairChangeType = "AiRepair";

        /// <summary>
        /// Bumped when the repair logic or the ingredient line parser improves. Each attempt's audit row
        /// starts with its version, and a recipe whose last attempt is older (or unversioned) is attempted
        /// once more.
        /// </summary>
        public const int RepairVersion = 3;

        /// <summary>
        /// The quantity prompt and its grounding last changed in this version; a recipe already asked by
        /// this version or later is not asked again, whatever <see cref="RepairVersion"/> says.
        /// </summary>
        public const int QuantityPromptVersion = 2;

        public static readonly string AttemptVersionPrefix = $"v{RepairVersion} ";

        private const string StepsIssueMarker = "instruction step(s)";
        private const string QuantityIssueMarker = "have no parseable quantity";

        private readonly ApplicationDbContext _db;
        private readonly IRecipeVettingService _vetting;
        private readonly IRecipeRepairModel _model;
        private readonly ILogger<RecipeAiRepairService> _logger;
        private Dictionary<string, long>? _measurementIds;

        public RecipeAiRepairService(ApplicationDbContext db, IRecipeVettingService vetting, IRecipeRepairModel model, ILogger<RecipeAiRepairService> logger)
        {
            _db = db;
            _vetting = vetting;
            _model = model;
            _logger = logger;
        }

        public async Task<AiRepairBatchResult> RepairBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default)
        {
            var measurements = _measurementIds ??= await _db.Measurements
                .AsNoTracking()
                .Where(m => !m.IsDeleted)
                .GroupBy(m => m.Name)
                .Select(g => new { Name = g.Key, Id = g.Min(m => m.Id) })
                .ToDictionaryAsync(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var query = _db.Recipes
                .Where(r => r.Id > afterRecipeId
                    && r.CurationStatusId == (long)CurationStatusEnum.RequiresRevision
                    && r.DateCurationCompleted == null
                    && r.ScrapedAtUtc != null
                    && r.VettingIssues != null
                    && !_db.AuditLogEntries.Any(a => a.EntityType == "Recipe" && a.EntityId == r.Id && a.ChangeType == AttemptChangeType
                        && a.NewValue != null && a.NewValue.StartsWith(AttemptVersionPrefix))
                    && !_db.CurationFeedbacks.Any(f => f.EntityId == r.Id && f.EntityType!.Name == "Recipe" && f.AdminId != SystemConstants.SystemPersonId))
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.RecipeSteps);
            var recipes = _db.Database.IsRelational()
                ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);
            if (recipes.Count == 0) return new AiRepairBatchResult(0, 0, 0, 0, 0, 0, afterRecipeId);

            var ids = recipes.Select(r => r.Id).ToList();
            var quantitiesAsked = (await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == "Recipe" && ids.Contains(a.EntityId) && a.ChangeType == AttemptChangeType && a.NewValue != null)
                .Select(a => new { a.EntityId, a.NewValue })
                .ToListAsync(cancellationToken))
                .Where(a => AttemptVersion(a.NewValue) >= QuantityPromptVersion)
                .Select(a => a.EntityId)
                .ToHashSet();

            int split = 0, filled = 0, rejected = 0, cleared = 0, calls = 0;
            foreach (var recipe in recipes)
            {
                var before = recipe.VettingIssues;
                var steps = (recipe.RecipeSteps ?? new List<RecipeStepEntity>()).OrderBy(s => s.StepNumber).ToList();
                var singleStep = steps.Count == 1
                    && await _db.RecipeSteps.IgnoreQueryFilters().CountAsync(s => s.RecipeId == recipe.Id, cancellationToken) == 1;
                var audit = new List<AuditLogEntryEntity>();
                if (singleStep && RecipeRepairGrounding.SplitSentences(steps[0].Description) is { } sentences)
                {
                    audit.Add(Audit("Recipe", recipe.Id, "RecipeSteps", steps[0].Description, $"[{RecipeLineClassifier.DeterministicSource}] {JsonSerializer.Serialize(sentences)}"));
                    RecipeStepSplit.Apply(recipe, steps[0], sentences);
                    recipe.LastModifiedDate = DateTime.UtcNow;
                    recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    singleStep = false;
                    split++;
                }

                var issues = await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(recipe));
                var stepsIssue = issues.Any(i => i.Contains(StepsIssueMarker));
                var quantityIssue = issues.Any(i => i.Contains(QuantityIssueMarker));
                var canSplit = stepsIssue && singleStep;
                var repairable = issues.Count > 0
                    && issues.All(i => i.Contains(StepsIssueMarker) || i.Contains(QuantityIssueMarker))
                    && (!stepsIssue || canSplit);

                if (repairable && _model.IsConfigured)
                {
                    try
                    {
                        if (canSplit)
                        {
                            calls++;
                            var original = steps[0].Description;
                            var accepted = RecipeRepairGrounding.AcceptSplit(original, await _model.SplitStepsAsync(original, cancellationToken));
                            if (accepted == null)
                            {
                                rejected++;
                            }
                            else
                            {
                                RecipeStepSplit.Apply(recipe, steps[0], accepted);
                                audit.Add(Audit("Recipe", recipe.Id, "RecipeSteps", original, $"[{_model.ModelName}] {JsonSerializer.Serialize(accepted)}"));
                                split++;
                            }
                        }

                        if (quantityIssue && !quantitiesAsked.Contains(recipe.Id))
                        {
                            var method = string.Join(" ", (recipe.RecipeSteps ?? new List<RecipeStepEntity>()).OrderBy(s => s.StepNumber).Select(s => s.Description));
                            var missing = (recipe.RecipeIngredients ?? new List<RecipeIngredientEntity>())
                                .Where(ri => ri.Quantity == 0 && !RecipeVettingService.IsAcceptablyUnquantified(ri.RawLine, ri.LineKind))
                                .ToList();
                            if (missing.Count > 0)
                            {
                                calls++;
                                var quotes = await _model.QuoteQuantitiesAsync(missing.Select(m => m.RawLine).ToList(), method, cancellationToken);
                                foreach (var (index, quote) in quotes)
                                {
                                    var row = missing[index];
                                    var grounded = RecipeRepairGrounding.GroundQuantity(row.RawLine, row.Ingredient?.Name, quote, new[] { method });
                                    if (grounded == null || !measurements.TryGetValue(grounded.MeasurementName, out var measurementId))
                                    {
                                        rejected++;
                                        continue;
                                    }

                                    audit.Add(Audit("RecipeIngredient", recipe.Id, $"Quantity[{row.IngredientId}:{row.SortOrder}]",
                                        $"{row.Quantity.ToString(CultureInfo.InvariantCulture)} (measurement {row.MeasurementId})",
                                        $"{grounded.Quantity.ToString(CultureInfo.InvariantCulture)} {grounded.MeasurementName} from \"{RecipeRepairGrounding.Normalize(quote)}\""));
                                    row.Quantity = grounded.Quantity;
                                    row.MeasurementId = measurementId;
                                    row.LastModifiedDate = DateTime.UtcNow;
                                    row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                                    filled++;
                                }
                            }
                        }

                        issues = await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(recipe));
                        audit.Add(Audit("Recipe", recipe.Id, "VettingIssues", before,
                            $"{AttemptVersionPrefix}[{_model.ModelName}] " + (issues.Count > 0 ? string.Join("\n", issues) : "(clean)"), AttemptChangeType));
                    }
                    catch (Exception ex) when (IsModelFailure(ex, cancellationToken))
                    {
                        _logger.LogWarning(ex, "AI recipe repair of recipe {RecipeId} failed ({Failure}); recorded and skipped", recipe.Id, ex.GetType().Name);
                        issues = await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(recipe));
                        audit.Add(Audit("Recipe", recipe.Id, "VettingIssues", before,
                            $"{AttemptVersionPrefix}[{_model.ModelName}] failed: {ex.GetType().Name}", AttemptChangeType));
                    }
                }
                else if (issues.Count > 0 && !repairable)
                {
                    audit.Add(Audit("Recipe", recipe.Id, "VettingIssues", before,
                        $"{AttemptVersionPrefix}not repairable: " + string.Join("\n", issues), AttemptChangeType));
                }
                _db.AuditLogEntries.AddRange(audit);

                recipe.VettingIssues = issues.Count > 0 ? string.Join("\n", issues) : null;
                if (issues.Count == 0)
                {
                    recipe.CurationStatusId = (long)CurationStatusEnum.NonCurated;
                    cleared++;
                }
                if (recipe.VettingIssues != before)
                {
                    recipe.LastModifiedDate = DateTime.UtcNow;
                    recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            _db.ChangeTracker.Clear();
            if (split + filled + cleared > 0)
            {
                _logger.LogInformation("AI recipe repair: {Examined} examined, {Split} methods split, {Filled} quantities filled, {Rejected} proposals rejected, {Cleared} cleared vetting",
                    recipes.Count, split, filled, rejected, cleared);
            }
            return new AiRepairBatchResult(recipes.Count, split, filled, rejected, cleared, calls, recipes[^1].Id);
        }

        private static bool IsModelFailure(Exception ex, CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException;

        private static int AttemptVersion(string? attempt)
        {
            if (attempt == null || attempt.Length < 2 || attempt[0] != 'v') return 0;
            var end = attempt.IndexOf(' ');
            return end > 1 && int.TryParse(attempt.AsSpan(1, end - 1), out var version) ? version : 0;
        }

        private static AuditLogEntryEntity Audit(string entityType, long entityId, string property, string? oldValue, string newValue, string changeType = RepairChangeType) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            PropertyName = property,
            OldValue = oldValue == null ? null : Clamp(oldValue, 4000),
            NewValue = Clamp(newValue, 4000),
            Timestamp = DateTime.UtcNow,
            ChangedByPersonId = SystemConstants.SystemPersonId,
        };

        private static string Clamp(string value, int maxLength) => RecipeStepSplit.Clamp(value, maxLength);
    }
}
