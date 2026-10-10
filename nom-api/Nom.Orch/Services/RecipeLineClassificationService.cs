using System;
using System.Collections.Generic;
using System.Linq;
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
    public class RecipeLineClassificationService : IRecipeLineClassificationService
    {
        public const string AttemptChangeType = "LineClassifyAttempt";
        public const string ClassifyChangeType = "LineClassify";

        /// <summary>Bumped when the pre-pass or prompt changes; recipes attempted by an older version are attempted once more.</summary>
        public const int ClassifierVersion = 1;

        public static readonly string AttemptVersionPrefix = $"v{ClassifierVersion} ";

        private const int LinesPerCall = 12;

        private readonly ApplicationDbContext _db;
        private readonly IRecipeVettingService _vetting;
        private readonly IRecipeClassificationModel _model;
        private readonly ILogger<RecipeLineClassificationService> _logger;

        public RecipeLineClassificationService(ApplicationDbContext db, IRecipeVettingService vetting, IRecipeClassificationModel model, ILogger<RecipeLineClassificationService> logger)
        {
            _db = db;
            _vetting = vetting;
            _model = model;
            _logger = logger;
        }

        public async Task<LineClassifyBatchResult> ClassifyBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default)
        {
            var query = _db.Recipes
                .Where(r => r.Id > afterRecipeId
                    && (r.CurationStatusId == (long)CurationStatusEnum.RequiresRevision || r.CurationStatusId == (long)CurationStatusEnum.NonCurated)
                    && r.DateCurationCompleted == null
                    && r.RecipeIngredients!.Any(ri => ri.Quantity <= 0 && ri.LineKind == null)
                    && !_db.AuditLogEntries.Any(a => a.EntityType == "Recipe" && a.EntityId == r.Id && a.ChangeType == AttemptChangeType
                        && a.NewValue != null && a.NewValue.StartsWith(AttemptVersionPrefix))
                    && !_db.CurationFeedbacks.Any(f => f.EntityId == r.Id && f.EntityType!.Name == "Recipe" && f.AdminId != SystemConstants.SystemPersonId))
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Include(r => r.RecipeIngredients)
                .Include(r => r.RecipeSteps);
            var recipes = _db.Database.IsRelational()
                ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);
            if (recipes.Count == 0) return new LineClassifyBatchResult(0, 0, 0, 0, 0, 0, afterRecipeId);

            var ids = recipes.Select(r => r.Id).ToList();
            var screened = (await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == "Recipe" && ids.Contains(a.EntityId) && a.ChangeType == RecipeNonFoodScreeningService.AttemptChangeType)
                .Select(a => a.EntityId)
                .ToListAsync(cancellationToken)).ToHashSet();

            int deterministic = 0, classified = 0, unclassified = 0, cleared = 0, calls = 0;
            foreach (var recipe in recipes)
            {
                var lines = recipe.RecipeIngredients ?? new List<RecipeIngredientEntity>();
                if (recipe.LicenseStatus == RecipeLicenseStatus.PublicDomain && !screened.Contains(recipe.Id)
                    && NonFoodRecipeFilter.IsCandidate(recipe.Name, lines.Select(l => l.RawLine)))
                {
                    continue;
                }

                var pending = lines
                    .Where(ri => ri.Quantity <= 0 && ri.LineKind == null && !RecipeVettingService.IsAcceptablyUnquantified(ri.RawLine))
                    .OrderBy(ri => ri.IngredientId)
                    .ToList();
                var audit = new List<AuditLogEntryEntity>();
                var forModel = new List<RecipeIngredientEntity>();
                foreach (var row in pending)
                {
                    if (RecipeLineClassifier.Classify(row.RawLine) is { } kind)
                    {
                        audit.Add(Apply(recipe, row, kind, RecipeLineClassifier.DeterministicSource));
                        deterministic++;
                    }
                    else
                    {
                        forModel.Add(row);
                    }
                }

                var answered = 0;
                if (forModel.Count > 0 && _model.IsConfigured)
                {
                    foreach (var chunk in forModel.Chunk(LinesPerCall))
                    {
                        calls++;
                        var kinds = await _model.ClassifyLinesAsync(recipe.Name, chunk.Select(r => r.RawLine).ToList(), cancellationToken);
                        for (var i = 0; i < chunk.Length; i++)
                        {
                            if (kinds.TryGetValue(i, out var kind) && RecipeIngredientLineKind.All.Contains(kind))
                            {
                                audit.Add(Apply(recipe, chunk[i], kind, _model.ModelName));
                                answered++;
                            }
                        }
                    }
                    classified += answered;
                    unclassified += forModel.Count - answered;
                }

                var attempted = forModel.Count == 0 || _model.IsConfigured;
                if (attempted)
                {
                    audit.Add(new AuditLogEntryEntity
                    {
                        EntityType = "Recipe",
                        EntityId = recipe.Id,
                        ChangeType = AttemptChangeType,
                        PropertyName = "LineKind",
                        NewValue = $"{AttemptVersionPrefix}[{(forModel.Count > 0 ? _model.ModelName : RecipeLineClassifier.DeterministicSource)}] " +
                            $"{pending.Count - forModel.Count} deterministic, {answered} by model, {forModel.Count - answered} unclassified",
                        Timestamp = DateTime.UtcNow,
                        ChangedByPersonId = SystemConstants.SystemPersonId,
                    });
                }

                if (audit.Count == 0) continue;
                if (audit.Count > (attempted ? 1 : 0))
                {
                    var before = (recipe.CurationStatusId, recipe.VettingIssues);
                    if (await ScrapedRecipeRepairService.RevetAsync(_vetting, recipe)) cleared++;
                    if ((recipe.CurationStatusId, recipe.VettingIssues) != before)
                    {
                        recipe.LastModifiedDate = DateTime.UtcNow;
                        recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    }
                }
                _db.AuditLogEntries.AddRange(audit);
                await _db.SaveChangesAsync(cancellationToken);
            }

            _db.ChangeTracker.Clear();
            if (deterministic + classified + cleared > 0)
            {
                _logger.LogInformation("Line classification: {Examined} recipes examined, {Deterministic} lines classified deterministically, {Classified} by model, {Unclassified} unclassified, {Cleared} cleared vetting",
                    recipes.Count, deterministic, classified, unclassified, cleared);
            }
            return new LineClassifyBatchResult(recipes.Count, deterministic, classified, unclassified, cleared, calls, recipes[^1].Id);
        }

        private static AuditLogEntryEntity Apply(RecipeEntity recipe, RecipeIngredientEntity row, string kind, string source)
        {
            row.LineKind = kind;
            row.LastModifiedDate = DateTime.UtcNow;
            row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            return new AuditLogEntryEntity
            {
                EntityType = "RecipeIngredient",
                EntityId = recipe.Id,
                ChangeType = ClassifyChangeType,
                PropertyName = $"LineKind[{row.IngredientId}]",
                NewValue = $"{kind} ({source}): {Clamp(row.RawLine, 3800)}",
                Timestamp = DateTime.UtcNow,
                ChangedByPersonId = SystemConstants.SystemPersonId,
            };
        }

        private static string Clamp(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
    }
}
