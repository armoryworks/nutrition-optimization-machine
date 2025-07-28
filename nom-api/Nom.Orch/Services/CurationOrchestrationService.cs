using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Reference;
using Nom.Data.Recipe;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class CurationOrchestrationService : ICurationOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<CurationOrchestrationService> _logger;
        // private readonly ICommunicationOrchestrationService _communicationService; // To be injected for notifications

        public CurationOrchestrationService(ApplicationDbContext db, ILogger<CurationOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        private async Task<long> GetReferenceIdByNameAsync(string name)
        {
            var reference = await _db.References.FirstOrDefaultAsync(r => r.Name == name);
            if (reference == null)
                throw new InvalidOperationException($"Reference '{name}' not found");
            return reference.Id;
        }

        public async Task<List<CurationQueueItemModel>> GetCurationQueueAsync()
        {
            _logger.LogInformation("Retrieving curation queue items");

            var queueItems = new List<CurationQueueItemModel>();

            // Get pending recipes
            var pendingRecipes = await _db.Recipes
                .Include(r => r.Author)
                .Include(r => r.CurationStatus)
                .Where(r => r.CurationStatus!.Name == "PendingCuration")
                .Select(r => new CurationQueueItemModel
                {
                    Id = r.Id,
                    EntityType = "Recipe",
                    Name = r.Name,
                    AuthorName = r.Author!.Name,
                    DateSubmitted = r.DateSubmittedForCuration ?? r.CreatedDate,
                    Description = r.Description,
                    Instructions = r.Instructions,
                    RawIngredientsString = r.RawIngredientsString,
                    SourceUrl = r.SourceUrl,
                    AuthorId = r.AuthorId
                })
                .ToListAsync();

            queueItems.AddRange(pendingRecipes);

            // Get pending ingredients
            var pendingIngredients = await _db.Ingredients
                .Include(i => i.Author)
                .Include(i => i.CurationStatus)
                .Where(i => i.CurationStatus!.Name == "PendingCuration")
                .Select(i => new CurationQueueItemModel
                {
                    Id = i.Id,
                    EntityType = "Ingredient",
                    Name = i.Name,
                    AuthorName = i.Author!.Name,
                    DateSubmitted = i.CreatedDate,
                    Description = i.Description,
                    AuthorId = i.AuthorId ?? 0
                })
                .ToListAsync();

            queueItems.AddRange(pendingIngredients);

            // Get pending plans
            var pendingPlans = await _db.Plans
                .Include(p => p.Author)
                .Include(p => p.CurationStatus)
                .Where(p => p.CurationStatus!.Name == "PendingCuration")
                .Select(p => new CurationQueueItemModel
                {
                    Id = p.Id,
                    EntityType = "Plan",
                    Name = p.Name,
                    AuthorName = p.Author!.Name,
                    DateSubmitted = p.DateSubmittedForCuration ?? p.CreatedDate,
                    Description = p.Description,
                    AuthorId = p.AuthorId
                })
                .ToListAsync();

            queueItems.AddRange(pendingPlans);

            return queueItems.OrderByDescending(q => q.DateSubmitted).ToList();
        }

        public async Task SubmitForCurationAsync(SubmitForCurationRequest request, long authorId)
        {
            _logger.LogInformation("Submitting {EntityType} {EntityId} for curation by author {AuthorId}", request.EntityType, request.EntityId, authorId);

            if (request.EntityType == "Recipe")
            {
                var recipe = await _db.Recipes
                    .Include(r => r.Author)
                    .FirstOrDefaultAsync(r => r.Id == request.EntityId);

                if (recipe == null)
                    throw new ArgumentException($"Recipe with ID {request.EntityId} not found");

                if (recipe.AuthorId != authorId)
                    throw new UnauthorizedAccessException("You can only submit your own recipes for curation");

                recipe.CurationStatusId = 9001L; // PendingCuration
                recipe.DateSubmittedForCuration = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Ingredient")
            {
                var ingredient = await _db.Ingredients
                    .Include(i => i.Author)
                    .FirstOrDefaultAsync(i => i.Id == request.EntityId);

                if (ingredient == null)
                    throw new ArgumentException($"Ingredient with ID {request.EntityId} not found");

                if (ingredient.AuthorId != authorId)
                    throw new UnauthorizedAccessException("You can only submit your own ingredients for curation");

                ingredient.CurationStatusId = 9001L; // PendingCuration
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Plan")
            {
                var plan = await _db.Plans
                    .Include(p => p.Author)
                    .FirstOrDefaultAsync(p => p.Id == request.EntityId);

                if (plan == null)
                    throw new ArgumentException($"Plan with ID {request.EntityId} not found");

                if (plan.AuthorId != authorId)
                    throw new UnauthorizedAccessException("You can only submit your own plans for curation");

                plan.CurationStatusId = 9001L; // PendingCuration
                plan.DateSubmittedForCuration = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            else
            {
                throw new ArgumentException($"Invalid entity type: {request.EntityType}");
            }
        }

        public async Task ApproveAsync(CurationDecisionRequest request, long adminId)
        {
            _logger.LogInformation("Approving {EntityType} {EntityId} by admin {AdminId}", request.EntityType, request.EntityId, adminId);

            if (request.EntityType == "Recipe")
            {
                var recipe = await _db.Recipes
                    .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Ingredient)
                    .ThenInclude(i => i.CurationStatus)
                    .FirstOrDefaultAsync(r => r.Id == request.EntityId);

                if (recipe == null)
                    throw new ArgumentException($"Recipe with ID {request.EntityId} not found");

                // Check if all ingredients are curated
                var uncuratedIngredients = recipe.RecipeIngredients?
                    .Where(ri => ri.Ingredient != null && ri.Ingredient.CurationStatus != null && ri.Ingredient.CurationStatus.Name != "Curated")
                    .Select(ri => ri.Ingredient?.Name ?? "Unknown")
                    .ToList() ?? new List<string>();

                if (uncuratedIngredients.Any())
                {
                    throw new InvalidOperationException($"Cannot approve recipe: The following ingredients are not curated: {string.Join(", ", uncuratedIngredients)}");
                }

                recipe.CurationStatusId = 9003L; // Curated
                recipe.DateCurationCompleted = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // Create feedback if notes provided
                if (!string.IsNullOrWhiteSpace(request.DecisionNotes))
                {
                    var feedback = new CurationFeedbackEntity
                    {
                        EntityId = request.EntityId,
                        EntityTypeId = await GetReferenceIdByNameAsync("Recipe"),
                        AdminId = adminId,
                        FeedbackNotes = request.DecisionNotes,
                        FeedbackTypeId = await GetReferenceIdByNameAsync("Approval"),
                        DateCreated = DateTime.UtcNow
                    };
                    _db.CurationFeedbacks.Add(feedback);
                    await _db.SaveChangesAsync();
                }
            }
            else if (request.EntityType == "Ingredient")
            {
                var ingredient = await _db.Ingredients
                    .FirstOrDefaultAsync(i => i.Id == request.EntityId);

                if (ingredient == null)
                    throw new ArgumentException($"Ingredient with ID {request.EntityId} not found");

                ingredient.CurationStatusId = 9003L; // Curated
                await _db.SaveChangesAsync();

                // Create feedback if notes provided
                if (!string.IsNullOrWhiteSpace(request.DecisionNotes))
                {
                    var feedback = new CurationFeedbackEntity
                    {
                        EntityId = request.EntityId,
                        EntityTypeId = await GetReferenceIdByNameAsync("Ingredient"),
                        AdminId = adminId,
                        FeedbackNotes = request.DecisionNotes,
                        FeedbackTypeId = await GetReferenceIdByNameAsync("Approval"),
                        DateCreated = DateTime.UtcNow
                    };
                    _db.CurationFeedbacks.Add(feedback);
                    await _db.SaveChangesAsync();
                }
            }
            else if (request.EntityType == "Plan")
            {
                var plan = await _db.Plans
                    .Include(p => p.Meals)
                    .ThenInclude(m => m.Recipes)
                    .ThenInclude(r => r.CurationStatus)
                    .FirstOrDefaultAsync(p => p.Id == request.EntityId);

                if (plan == null)
                    throw new ArgumentException($"Plan with ID {request.EntityId} not found");

                // Check if all recipes in the plan are curated
                var uncuratedRecipes = plan.Meals?
                    .SelectMany(m => m.Recipes ?? new List<RecipeEntity>())
                    .Where(r => r.CurationStatus != null && r.CurationStatus.Name != "Curated")
                    .Select(r => r.Name)
                    .ToList() ?? new List<string>();

                if (uncuratedRecipes.Any())
                {
                    throw new InvalidOperationException($"Cannot approve plan: The following recipes are not curated: {string.Join(", ", uncuratedRecipes)}");
                }

                plan.CurationStatusId = 9003L; // Curated
                plan.DateCurationCompleted = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // Create feedback if notes provided
                if (!string.IsNullOrWhiteSpace(request.DecisionNotes))
                {
                    var feedback = new CurationFeedbackEntity
                    {
                        EntityId = request.EntityId,
                        EntityTypeId = await GetReferenceIdByNameAsync("Plan"),
                        AdminId = adminId,
                        FeedbackNotes = request.DecisionNotes,
                        FeedbackTypeId = await GetReferenceIdByNameAsync("Approval"),
                        DateCreated = DateTime.UtcNow
                    };
                    _db.CurationFeedbacks.Add(feedback);
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                throw new ArgumentException($"Invalid entity type: {request.EntityType}");
            }
        }

        public async Task RequestRevisionAsync(CurationDecisionRequest request, long adminId)
        {
            _logger.LogInformation("Requesting revision for {EntityType} {EntityId} by admin {AdminId}", request.EntityType, request.EntityId, adminId);

            if (string.IsNullOrWhiteSpace(request.DecisionNotes))
                throw new ArgumentException("Revision notes are required");

            if (request.EntityType == "Recipe")
            {
                var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == request.EntityId);
                if (recipe == null)
                    throw new ArgumentException($"Recipe with ID {request.EntityId} not found");

                recipe.CurationStatusId = 9002L; // RequiresRevision
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Ingredient")
            {
                var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == request.EntityId);
                if (ingredient == null)
                    throw new ArgumentException($"Ingredient with ID {request.EntityId} not found");

                ingredient.CurationStatusId = 9002L; // RequiresRevision
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Plan")
            {
                var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.EntityId);
                if (plan == null)
                    throw new ArgumentException($"Plan with ID {request.EntityId} not found");

                plan.CurationStatusId = 9002L; // RequiresRevision
                await _db.SaveChangesAsync();
            }
            else
            {
                throw new ArgumentException($"Invalid entity type: {request.EntityType}");
            }

            // Create feedback
            var feedback = new CurationFeedbackEntity
            {
                EntityId = request.EntityId,
                EntityTypeId = await GetReferenceIdByNameAsync(request.EntityType),
                AdminId = adminId,
                FeedbackNotes = request.DecisionNotes,
                FeedbackTypeId = await GetReferenceIdByNameAsync("Revision"),
                DateCreated = DateTime.UtcNow
            };
            _db.CurationFeedbacks.Add(feedback);
            await _db.SaveChangesAsync();
        }

        public async Task RejectAsync(CurationDecisionRequest request, long adminId)
        {
            _logger.LogInformation("Rejecting {EntityType} {EntityId} by admin {AdminId}", request.EntityType, request.EntityId, adminId);

            if (string.IsNullOrWhiteSpace(request.DecisionNotes))
                throw new ArgumentException("Rejection notes are required");

            if (request.EntityType == "Recipe")
            {
                var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == request.EntityId);
                if (recipe == null)
                    throw new ArgumentException($"Recipe with ID {request.EntityId} not found");

                recipe.CurationStatusId = 9004L; // Rejected
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Ingredient")
            {
                var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == request.EntityId);
                if (ingredient == null)
                    throw new ArgumentException($"Ingredient with ID {request.EntityId} not found");

                ingredient.CurationStatusId = 9004L; // Rejected
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Plan")
            {
                var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.EntityId);
                if (plan == null)
                    throw new ArgumentException($"Plan with ID {request.EntityId} not found");

                plan.CurationStatusId = 9004L; // Rejected
                await _db.SaveChangesAsync();
            }
            else
            {
                throw new ArgumentException($"Invalid entity type: {request.EntityType}");
            }

            // Create feedback
            var feedback = new CurationFeedbackEntity
            {
                EntityId = request.EntityId,
                EntityTypeId = await GetReferenceIdByNameAsync(request.EntityType),
                AdminId = adminId,
                FeedbackNotes = request.DecisionNotes,
                FeedbackTypeId = await GetReferenceIdByNameAsync("Rejection"),
                DateCreated = DateTime.UtcNow
            };
            _db.CurationFeedbacks.Add(feedback);
            await _db.SaveChangesAsync();
        }
    }
}