using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class RecipeNonFoodScreeningService : IRecipeNonFoodScreeningService
    {
        public const string AttemptChangeType = "NonFoodScreenAttempt";

        /// <summary>Bumped when the pre-filter or prompt changes; recipes screened by an older version are screened once more.</summary>
        public const int ScreenVersion = 1;

        public static readonly string AttemptVersionPrefix = $"v{ScreenVersion} ";

        private const int ScanPageSize = 500;

        private readonly ApplicationDbContext _db;
        private readonly IRecipeClassificationModel _model;
        private readonly ILogger<RecipeNonFoodScreeningService> _logger;
        private (long Recipe, long Rejection)? _referenceIds;

        public RecipeNonFoodScreeningService(ApplicationDbContext db, IRecipeClassificationModel model, ILogger<RecipeNonFoodScreeningService> logger)
        {
            _db = db;
            _model = model;
            _logger = logger;
        }

        public async Task<NonFoodBatchResult> ScreenBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default)
        {
            if (!_model.IsConfigured) return new NonFoodBatchResult(0, 0, 0, 0, 0, afterRecipeId);

            var page = await _db.Recipes
                .AsNoTracking()
                .Where(r => r.Id > afterRecipeId
                    && (r.CurationStatusId == (long)CurationStatusEnum.RequiresRevision || r.CurationStatusId == (long)CurationStatusEnum.NonCurated)
                    && r.LicenseStatus == RecipeLicenseStatus.PublicDomain
                    && r.DateCurationCompleted == null
                    && !_db.AuditLogEntries.Any(a => a.EntityType == "Recipe" && a.EntityId == r.Id && a.ChangeType == AttemptChangeType
                        && a.NewValue != null && a.NewValue.StartsWith(AttemptVersionPrefix))
                    && !_db.CurationFeedbacks.Any(f => f.EntityId == r.Id && f.EntityType!.Name == "Recipe" && f.AdminId != SystemConstants.SystemPersonId))
                .OrderBy(r => r.Id)
                .Take(ScanPageSize)
                .Select(r => new { r.Id, r.Name, Lines = r.RecipeIngredients!.Select(ri => ri.RawLine).ToList() })
                .ToListAsync(cancellationToken);
            if (page.Count == 0) return new NonFoodBatchResult(0, 0, 0, 0, 0, afterRecipeId);

            var candidates = new List<(long Id, string Name, List<string> Lines)>();
            var lastScanned = afterRecipeId;
            var scanned = 0;
            foreach (var recipe in page)
            {
                lastScanned = recipe.Id;
                scanned++;
                if (!NonFoodRecipeFilter.IsCandidate(recipe.Name, recipe.Lines)) continue;
                candidates.Add((recipe.Id, recipe.Name, recipe.Lines));
                if (candidates.Count >= batchSize) break;
            }

            int rejected = 0, unsure = 0;
            foreach (var candidate in candidates)
            {
                var verdict = await _model.IsFoodAsync(candidate.Name, candidate.Lines, cancellationToken);
                var now = DateTime.UtcNow;
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = "Recipe",
                    EntityId = candidate.Id,
                    ChangeType = AttemptChangeType,
                    PropertyName = "CurationStatusId",
                    NewValue = $"{AttemptVersionPrefix}[{_model.ModelName}] {verdict}",
                    Timestamp = now,
                    ChangedByPersonId = SystemConstants.SystemPersonId,
                });

                if (verdict == FoodVerdict.Unsure) unsure++;
                if (verdict == FoodVerdict.NotFood && await RejectAsync(candidate.Id, now, cancellationToken)) rejected++;
                await _db.SaveChangesAsync(cancellationToken);
            }

            _db.ChangeTracker.Clear();
            if (rejected > 0)
            {
                _logger.LogInformation("Non-food screening: {Screened} candidates screened, {Rejected} rejected as not food, {Unsure} unsure",
                    candidates.Count, rejected, unsure);
            }
            return new NonFoodBatchResult(scanned, candidates.Count, rejected, unsure, candidates.Count, lastScanned);
        }

        private async Task<bool> RejectAsync(long recipeId, DateTime now, CancellationToken cancellationToken)
        {
            var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);
            if (recipe == null || recipe.DateCurationCompleted != null
                || (recipe.CurationStatusId != (long)CurationStatusEnum.RequiresRevision && recipe.CurationStatusId != (long)CurationStatusEnum.NonCurated))
            {
                return false;
            }

            var (recipeTypeId, rejectionTypeId) = _referenceIds ??= await LoadReferenceIdsAsync(cancellationToken);
            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = "Recipe",
                EntityId = recipe.Id,
                ChangeType = "Update",
                PropertyName = "CurationStatusId",
                OldValue = recipe.CurationStatusId.ToString(),
                NewValue = ((long)CurationStatusEnum.Rejected).ToString(),
                Timestamp = now,
                ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            recipe.CurationStatusId = (long)CurationStatusEnum.Rejected;
            recipe.LastModifiedDate = now;
            recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            _db.CurationFeedbacks.Add(new CurationFeedbackEntity
            {
                EntityId = recipe.Id,
                EntityTypeId = recipeTypeId,
                AdminId = SystemConstants.SystemPersonId,
                FeedbackTypeId = rejectionTypeId,
                FeedbackNotes = $"auto-rejected: not a food recipe (model {_model.ModelName})",
                CreatedDate = now,
                CreatedByPersonId = SystemConstants.SystemPersonId,
            });
            return true;
        }

        private async Task<(long, long)> LoadReferenceIdsAsync(CancellationToken cancellationToken)
        {
            var ids = await _db.References.AsNoTracking()
                .Where(r => r.Name == "Recipe" || r.Name == "Rejection")
                .Select(r => new { r.Name, r.Id })
                .ToListAsync(cancellationToken);
            var recipe = ids.FirstOrDefault(r => r.Name == "Recipe")
                ?? throw new InvalidOperationException("Reference 'Recipe' not found");
            var rejection = ids.FirstOrDefault(r => r.Name == "Rejection")
                ?? throw new InvalidOperationException("Reference 'Rejection' not found");
            return (recipe.Id, rejection.Id);
        }
    }
}
