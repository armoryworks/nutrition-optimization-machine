using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                    && !_db.AuditLogEntries.Any(a => a.EntityType == "Recipe" && a.EntityId == r.Id && a.ChangeType == AttemptChangeType)
                    && !_db.CurationFeedbacks.Any(f => f.EntityId == r.Id && f.EntityType!.Name == "Recipe" && f.AdminId != SystemConstants.SystemPersonId))
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.RecipeSteps);
            var recipes = _db.Database.IsRelational()
                ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);
            if (recipes.Count == 0) return new AiRepairBatchResult(0, 0, 0, 0, 0, 0, afterRecipeId);

            int split = 0, filled = 0, rejected = 0, cleared = 0, calls = 0;
            foreach (var recipe in recipes)
            {
                var before = recipe.VettingIssues;
                var issues = await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(recipe));
                var steps = (recipe.RecipeSteps ?? new List<RecipeStepEntity>()).OrderBy(s => s.StepNumber).ToList();
                var stepsIssue = issues.Any(i => i.Contains(StepsIssueMarker));
                var quantityIssue = issues.Any(i => i.Contains(QuantityIssueMarker));
                var canSplit = stepsIssue && steps.Count == 1
                    && await _db.RecipeSteps.IgnoreQueryFilters().CountAsync(s => s.RecipeId == recipe.Id, cancellationToken) == 1;
                var repairable = issues.Count > 0
                    && issues.All(i => i.Contains(StepsIssueMarker) || i.Contains(QuantityIssueMarker))
                    && (!stepsIssue || canSplit);

                if (repairable && _model.IsConfigured)
                {
                    var audit = new List<AuditLogEntryEntity>();
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
                            ApplySplit(recipe, steps[0], accepted);
                            audit.Add(Audit("Recipe", recipe.Id, "RecipeSteps", original, JsonSerializer.Serialize(accepted)));
                            split++;
                        }
                    }

                    if (quantityIssue)
                    {
                        var method = string.Join(" ", (recipe.RecipeSteps ?? new List<RecipeStepEntity>()).OrderBy(s => s.StepNumber).Select(s => s.Description));
                        var missing = (recipe.RecipeIngredients ?? new List<RecipeIngredientEntity>())
                            .Where(ri => ri.Quantity == 0 && !RecipeVettingService.IsAcceptablyUnquantified(ri.RawLine))
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

                                audit.Add(Audit("RecipeIngredient", recipe.Id, $"Quantity[{row.IngredientId}]",
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
                        $"[{_model.ModelName}] " + (issues.Count > 0 ? string.Join("\n", issues) : "(clean)"), AttemptChangeType));
                    _db.AuditLogEntries.AddRange(audit);
                }

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

        private void ApplySplit(RecipeEntity recipe, RecipeStepEntity first, IReadOnlyList<string> steps)
        {
            var now = DateTime.UtcNow;
            first.Description = steps[0];
            first.Summary = Clamp(steps[0], 255);
            first.LastModifiedDate = now;
            first.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            for (var i = 1; i < steps.Count; i++)
            {
                var step = new RecipeStepEntity
                {
                    RecipeId = recipe.Id,
                    StepNumber = i + 1,
                    StepTypeId = first.StepTypeId,
                    Summary = Clamp(steps[i], 255),
                    Description = steps[i],
                    CreatedDate = now,
                    CreatedByPersonId = SystemConstants.SystemPersonId,
                };
                _db.RecipeSteps.Add(step);
                recipe.RecipeSteps!.Add(step);
            }
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

        private static string Clamp(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
    }
}
