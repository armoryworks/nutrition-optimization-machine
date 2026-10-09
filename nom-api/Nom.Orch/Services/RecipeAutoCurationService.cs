using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class RecipeAutoCurationService : IRecipeAutoCurationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IRecipeVettingService _vetting;
        private readonly ILogger<RecipeAutoCurationService> _logger;
        private (long Recipe, long Approval)? _referenceIds;

        public RecipeAutoCurationService(ApplicationDbContext db, IRecipeVettingService vetting, ILogger<RecipeAutoCurationService> logger)
        {
            _db = db;
            _vetting = vetting;
            _logger = logger;
        }

        public async Task<AutoApproveBatchResult> AutoApproveBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default)
        {
            var query = _db.Recipes
                .Where(r => r.Id > afterRecipeId
                    && r.CurationStatusId == (long)CurationStatusEnum.NonCurated
                    && r.Visibility == RecipeVisibilityEnum.Public
                    && r.DateCurationCompleted == null
                    && r.VettingIssues == null
                    && !r.ContainsSourceProse
                    && (r.LicenseStatus == RecipeLicenseStatus.PublicDomain
                        || (r.LicenseStatus == RecipeLicenseStatus.Unknown && r.ScrapedAtUtc != null && r.Image != null && r.Image != ""))
                    && r.RecipeIngredients!.All(ri => ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated))
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.RecipeSteps);
            var recipes = _db.Database.IsRelational()
                ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);
            if (recipes.Count == 0) return new AutoApproveBatchResult(0, 0, afterRecipeId);

            var approved = 0;
            foreach (var recipe in recipes)
            {
                var decision = RecipeAutoApprovalPolicy.Evaluate(recipe);
                if (!decision.Eligible) continue;

                var issues = await _vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(recipe));
                if (issues.Count > 0) continue;

                var (recipeTypeId, approvalTypeId) = _referenceIds ??= await LoadReferenceIdsAsync(cancellationToken);
                var now = DateTime.UtcNow;
                recipe.CurationStatusId = (long)CurationStatusEnum.Curated;
                recipe.DateCurationCompleted = now;
                recipe.LastModifiedDate = now;
                recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                _db.CurationFeedbacks.Add(new CurationFeedbackEntity
                {
                    EntityId = recipe.Id,
                    EntityTypeId = recipeTypeId,
                    AdminId = SystemConstants.SystemPersonId,
                    FeedbackTypeId = approvalTypeId,
                    FeedbackNotes = $"auto-approved: {decision.Rule}; vetting clean, no source prose, no source image, all {recipe.RecipeIngredients?.Count ?? 0} ingredients curated",
                    CreatedDate = now,
                    CreatedByPersonId = SystemConstants.SystemPersonId,
                });
                approved++;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            if (approved > 0) _logger.LogInformation("Auto-approved {Approved} of {Examined} candidate recipes", approved, recipes.Count);
            return new AutoApproveBatchResult(recipes.Count, approved, recipes[^1].Id);
        }

        private async Task<(long, long)> LoadReferenceIdsAsync(CancellationToken cancellationToken)
        {
            var ids = await _db.References.AsNoTracking()
                .Where(r => r.Name == "Recipe" || r.Name == "Approval")
                .Select(r => new { r.Name, r.Id })
                .ToListAsync(cancellationToken);
            var recipe = ids.FirstOrDefault(r => r.Name == "Recipe")
                ?? throw new InvalidOperationException("Reference 'Recipe' not found");
            var approval = ids.FirstOrDefault(r => r.Name == "Approval")
                ?? throw new InvalidOperationException("Reference 'Approval' not found");
            return (recipe.Id, approval.Id);
        }
    }
}
