using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Curation:AutoApprove:MaxUnquantifiedLines (default 0), Curation:AutoApprove:RequireOwnImage
    /// (default true), Curation:AutoApprove:RequireDietUnderstood (default true) and
    /// Curation:AutoApprove:RequireSecondReading (default true) set the <see cref="AutoApprovalOptions"/> the sweep
    /// evaluates with. A recipe the policy and vetting pass still waits, counted by its reason, while the diet gate does
    /// not understand it (<see cref="DietApprovalHolds"/>); a recipe already approved is never taken back.
    /// </summary>
    public class RecipeAutoCurationService : IRecipeAutoCurationService
    {
        internal static readonly string[] ExemptLineKinds =
        {
            RecipeIngredientLineKind.Reference, RecipeIngredientLineKind.Equipment, RecipeIngredientLineKind.Note,
            RecipeIngredientLineKind.Instruction, RecipeIngredientLineKind.Artefact,
        };

        private readonly ApplicationDbContext _db;
        private readonly IRecipeVettingService _vetting;
        private readonly ILogger<RecipeAutoCurationService> _logger;
        private readonly AutoApprovalOptions _options;
        private readonly IPlatformFeatureService? _features;
        private (long Recipe, long Approval)? _referenceIds;

        public RecipeAutoCurationService(ApplicationDbContext db, IRecipeVettingService vetting, ILogger<RecipeAutoCurationService> logger, IConfiguration? configuration = null,
            IPlatformFeatureService? features = null)
        {
            _db = db;
            _vetting = vetting;
            _logger = logger;
            _features = features;
            _options = OptionsFrom(configuration);
        }

        /// <summary>The <see cref="AutoApprovalOptions"/> the Curation:AutoApprove settings give (the defaults without a configuration).</summary>
        public static AutoApprovalOptions OptionsFrom(IConfiguration? configuration) =>
            configuration == null ? AutoApprovalOptions.Default : new AutoApprovalOptions(
                Math.Max(0, configuration.GetValue("Curation:AutoApprove:MaxUnquantifiedLines", 0)),
                configuration.GetValue("Curation:AutoApprove:RequireOwnImage", true),
                configuration.GetValue("Curation:AutoApprove:RequireDietUnderstood", true),
                configuration.GetValue("Curation:AutoApprove:RequireSecondReading", true));

        public AutoApprovalOptions Options => _options;

        public async Task<AutoApproveBatchResult> AutoApproveBatchAsync(long afterRecipeId, int batchSize, CancellationToken cancellationToken = default)
        {
            var query = Candidates(_db, _options.RequireOwnImageForUnknownLicense)
                .Where(r => r.Id > afterRecipeId)
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.RecipeSteps);
            var recipes = _db.Database.IsRelational()
                ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);
            return await ApproveAsync(recipes, afterRecipeId, cancellationToken);
        }

        public async Task<AutoApproveWaitingReport> WaitingAsync(int sample = 20, CancellationToken cancellationToken = default)
        {
            sample = Math.Clamp(sample, 0, 1000);
            var refused = new Dictionary<string, int>();
            var waiting = new Dictionary<string, int>();
            var samples = new Dictionary<string, List<long>>();
            int candidates = 0, passing = 0, approve = 0;
            void Count(Dictionary<string, int> tally, string key, long id)
            {
                Tally(tally, key);
                if (!samples.TryGetValue(key, out var ids)) samples[key] = ids = new List<long>();
                if (ids.Count < sample) ids.Add(id);
            }
            long after = 0;
            while (true)
            {
                var query = Candidates(_db, _options.RequireOwnImageForUnknownLicense).AsNoTracking()
                    .Where(r => r.Id > after)
                    .OrderBy(r => r.Id)
                    .Take(500)
                    .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                    .Include(r => r.RecipeSteps);
                var recipes = _db.Database.IsRelational()
                    ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                    : await query.ToListAsync(cancellationToken);
                if (recipes.Count == 0) break;
                after = recipes[^1].Id;
                candidates += recipes.Count;
                var passed = new List<RecipeEntity>();
                foreach (var recipe in recipes)
                {
                    if (await RefusalAsync(recipe, _options, _vetting) is { } refusal) Count(refused, refusal, recipe.Id);
                    else passed.Add(recipe);
                }
                passing += passed.Count;
                var holds = await DietApprovalHolds.ReasonsAsync(_db, passed, _options, cancellationToken);
                foreach (var recipe in passed)
                {
                    if (holds.TryGetValue(recipe.Id, out var hold)) Count(waiting, hold, recipe.Id);
                    else
                    {
                        approve++;
                        if (!samples.TryGetValue(WouldApproveKey, out var ids)) samples[WouldApproveKey] = ids = new List<long>();
                        if (ids.Count < sample) ids.Add(recipe.Id);
                    }
                }
                _db.ChangeTracker.Clear();
            }
            return new AutoApproveWaitingReport(candidates, refused.Values.Sum(), refused, passing, waiting.Values.Sum(), waiting, approve,
                samples.ToDictionary(p => p.Key, p => (IReadOnlyList<long>)p.Value), _options.MaxUnquantifiedLines, _options.RequireOwnImageForUnknownLicense,
                _options.RequireDietUnderstood, _options.RequireSecondReading);
        }

        /// <summary>The key of the sample of recipes the sweep would approve now, in <see cref="AutoApproveWaitingReport.Samples"/>.</summary>
        public const string WouldApproveKey = "would be approved";

        /// <summary>The recipes the sweep examines: public, never reviewed, with no vetting issue, no source prose and no withheld prose, of an auto-approvable licence, every line on a curated food or exempt.</summary>
        public static IQueryable<RecipeEntity> Candidates(ApplicationDbContext db, bool requireImage) =>
            db.Recipes
                .Where(r => r.CurationStatusId == (long)CurationStatusEnum.NonCurated
                    && r.Visibility == RecipeVisibilityEnum.Public
                    && r.DateCurationCompleted == null
                    && r.VettingIssues == null
                    && !r.ContainsSourceProse
                    && !r.ProseWithheld
                    && (r.LicenseStatus == RecipeLicenseStatus.PublicDomain
                        || (r.LicenseStatus == RecipeLicenseStatus.Unknown && r.ScrapedAtUtc != null && (!requireImage || (r.Image != null && r.Image != ""))))
                    && r.RecipeIngredients!.All(ri => ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated
                        || (ri.LineKind != null && ExemptLineKinds.Contains(ri.LineKind))
                        || ri.Quantity <= 0));

        /// <summary>The key of the policy's or vetting's refusal of a loaded recipe (its lines with their foods, and its steps), or null when both pass it.</summary>
        public static Task<string?> RefusalAsync(RecipeEntity recipe, AutoApprovalOptions options, IRecipeVettingService vetting) =>
            RefusalAsync(recipe, RecipeAutoApprovalPolicy.Evaluate(recipe, options), vetting);

        private static async Task<string?> RefusalAsync(RecipeEntity recipe, AutoApprovalDecision decision, IRecipeVettingService vetting)
        {
            if (!decision.Eligible) return decision.Key ?? "policy";
            return (await vetting.VetAsync(ScrapedRecipeRepairService.ToVettable(recipe))).Count > 0 ? "vetting" : null;
        }

        private async Task<AutoApproveBatchResult> ApproveAsync(List<RecipeEntity> recipes, long afterRecipeId, CancellationToken cancellationToken)
        {
            if (recipes.Count == 0) return new AutoApproveBatchResult(0, 0, afterRecipeId);

            var approved = 0;
            var refusals = new Dictionary<string, int>();
            var passed = new List<(RecipeEntity Recipe, AutoApprovalDecision Decision)>();
            foreach (var recipe in recipes)
            {
                var decision = RecipeAutoApprovalPolicy.Evaluate(recipe, _options);
                if (await RefusalAsync(recipe, decision, _vetting) is { } refusal)
                {
                    Tally(refusals, refusal);
                    continue;
                }
                passed.Add((recipe, decision));
            }

            var waiting = await DietApprovalHolds.ReasonsAsync(_db, passed.Select(p => p.Recipe).ToList(), _options, cancellationToken);
            foreach (var (recipe, decision) in passed)
            {
                if (waiting.TryGetValue(recipe.Id, out var hold))
                {
                    Tally(refusals, hold);
                    continue;
                }

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

            if (approved > 0 && _features != null && await _features.IsEnabledAsync(IPlatformFeatureService.Keys.AutoApprovePause))
            {
                _db.ChangeTracker.Clear();
                _logger.LogInformation("Recipe auto-approval paused before saving {Approved} approvals after recipe {Cursor}; none kept", approved, afterRecipeId);
                return new AutoApproveBatchResult(0, 0, afterRecipeId, Held: true);
            }

            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            if (approved > 0) _logger.LogInformation("Auto-approved {Approved} of {Examined} candidate recipes", approved, recipes.Count);
            return new AutoApproveBatchResult(recipes.Count, approved, recipes[^1].Id, refusals);
        }

        private static void Tally(Dictionary<string, int> refusals, string key) =>
            refusals[key] = refusals.GetValueOrDefault(key) + 1;

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
