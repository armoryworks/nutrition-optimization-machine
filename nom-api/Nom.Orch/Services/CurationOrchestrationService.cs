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
using System.Text.Json;

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

            // Get pending recipes with structured data
            var pendingRecipes = await _db.Recipes
                .Include(r => r.Author)
                .Include(r => r.CurationStatus)
                .Include(r => r.RecipeIngredients)
                .ThenInclude(ri => ri.Ingredient)
                .Include(r => r.RecipeSteps)
                // RequiresRevision covers imports flagged by vetting — they need
                // admin eyes, so they surface in the same queue.
                .Where(r => r.CurationStatusId == (long)CurationStatusEnum.PendingCuration ||
                            r.CurationStatusId == (long)CurationStatusEnum.RequiresRevision)
                .Select(r => new CurationQueueItemModel
                {
                    Id = r.Id,
                    EntityType = "Recipe",
                    Name = r.Name,
                    AuthorName = r.Author!.Name,
                    DateSubmitted = r.DateSubmittedForCuration ?? r.CreatedDate,
                    Description = r.Description,
                    SourceUrl = r.SourceUrl,
                    AuthorId = r.AuthorId,
                    Status = r.CurationStatusId == (long)CurationStatusEnum.RequiresRevision
                        ? "RequiresRevision" : "PendingCuration",
                    VettingIssues = r.VettingIssues,
                    ContainsSourceProse = r.ContainsSourceProse,
                    SourceImageUrl = r.SourceImageUrl
                })
                .ToListAsync();

            queueItems.AddRange(pendingRecipes);

            // Get pending ingredients
            var pendingIngredients = await _db.Ingredients
                .Include(i => i.Author)
                .Include(i => i.CurationStatus)
                .Where(i => i.CurationStatusId == (long)CurationStatusEnum.PendingCuration)
                .Select(i => new CurationQueueItemModel
                {
                    Id = i.Id,
                    EntityType = "Ingredient",
                    Name = i.Name,
                    AuthorName = i.Author!.Name,
                    DateSubmitted = i.CreatedDate,
                    Description = i.Description,
                    AuthorId = i.AuthorId ?? 0,
                    Status = "PendingCuration"
                })
                .ToListAsync();

            queueItems.AddRange(pendingIngredients);

            // Get pending plans
            var pendingPlans = await _db.Plans
                .Include(p => p.Author)
                .Include(p => p.CurationStatus)
                .Where(p => p.CurationStatusId == (long)CurationStatusEnum.PendingCuration)
                .Select(p => new CurationQueueItemModel
                {
                    Id = p.Id,
                    EntityType = "Plan",
                    Name = p.Name,
                    AuthorName = p.Author!.Name,
                    DateSubmitted = p.DateSubmittedForCuration ?? p.CreatedDate,
                    Description = p.Description,
                    AuthorId = p.AuthorId,
                    Status = "PendingCuration"
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

                recipe.CurationStatusId = (long)CurationStatusEnum.PendingCuration;
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

                ingredient.CurationStatusId = (long)CurationStatusEnum.PendingCuration;
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

                plan.CurationStatusId = (long)CurationStatusEnum.PendingCuration;
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
                    .Where(ri => ri.Ingredient != null && ri.Ingredient.CurationStatusId != (long)CurationStatusEnum.Curated)
                    .Select(ri => ri.Ingredient?.Name ?? "Unknown")
                    .ToList() ?? new List<string>();

                if (uncuratedIngredients.Any())
                {
                    throw new InvalidOperationException($"Cannot approve recipe: The following ingredients are not curated: {string.Join(", ", uncuratedIngredients)}");
                }

                recipe.CurationStatusId = (long)CurationStatusEnum.Curated;
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
                        FeedbackTypeId = await GetReferenceIdByNameAsync("Approval")
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

                ingredient.CurationStatusId = (long)CurationStatusEnum.Curated;
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
                        FeedbackTypeId = await GetReferenceIdByNameAsync("Approval")
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
                    .Where(r => r.CurationStatusId != (long)CurationStatusEnum.Curated)
                    .Select(r => r.Name)
                    .ToList() ?? new List<string>();

                if (uncuratedRecipes.Any())
                {
                    throw new InvalidOperationException($"Cannot approve plan: The following recipes are not curated: {string.Join(", ", uncuratedRecipes)}");
                }

                plan.CurationStatusId = (long)CurationStatusEnum.Curated;
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
                        FeedbackTypeId = await GetReferenceIdByNameAsync("Approval")
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

                recipe.CurationStatusId = (long)CurationStatusEnum.RequiresRevision;
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Ingredient")
            {
                var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == request.EntityId);
                if (ingredient == null)
                    throw new ArgumentException($"Ingredient with ID {request.EntityId} not found");

                ingredient.CurationStatusId = (long)CurationStatusEnum.RequiresRevision;
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Plan")
            {
                var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.EntityId);
                if (plan == null)
                    throw new ArgumentException($"Plan with ID {request.EntityId} not found");

                plan.CurationStatusId = (long)CurationStatusEnum.RequiresRevision;
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
                FeedbackTypeId = await GetReferenceIdByNameAsync("Revision")
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

                recipe.CurationStatusId = (long)CurationStatusEnum.Rejected;
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Ingredient")
            {
                var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == request.EntityId);
                if (ingredient == null)
                    throw new ArgumentException($"Ingredient with ID {request.EntityId} not found");

                ingredient.CurationStatusId = (long)CurationStatusEnum.Rejected;
                await _db.SaveChangesAsync();
            }
            else if (request.EntityType == "Plan")
            {
                var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.EntityId);
                if (plan == null)
                    throw new ArgumentException($"Plan with ID {request.EntityId} not found");

                plan.CurationStatusId = (long)CurationStatusEnum.Rejected;
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
                FeedbackTypeId = await GetReferenceIdByNameAsync("Rejection")
            };
            _db.CurationFeedbacks.Add(feedback);
            await _db.SaveChangesAsync();
        }

        public async Task<bool> SetIngredientFoodGroupAsync(long ingredientId, long? foodGroupId)
        {
            var ingredient = await _db.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredientId);
            if (ingredient == null) return false;
            ingredient.FoodGroupId = foodGroupId;
            ingredient.LastModifiedDate = System.DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        // Ordered most-specific-first so "peanut butter" → Nuts/Seeds before Dairy, "chickpea" → Legumes
        // before Vegetables, etc. Heuristic and admin-overridable; not a substitute for curated data.
        private static readonly (long GroupId, string[] Keywords)[] FoodGroupKeywords = new[]
        {
            ((long)FoodGroupEnum.NutsSeeds, new[] { "almond", "walnut", "peanut", "cashew", "pistachio", "pecan", "hazelnut", "macadamia", "sesame", "sunflower seed", "pumpkin seed", "chia", "flax", " seed" }),
            ((long)FoodGroupEnum.Legumes, new[] { "lentil", "chickpea", "garbanzo", "black bean", "kidney bean", "pinto", "soybean", "soy ", "tofu", "edamame", "hummus", "split pea" }),
            ((long)FoodGroupEnum.Beverages, new[] { "juice", "soda", "cola", "coffee", "tea", "lemonade", "beer", "wine", "smoothie", " drink" }),
            ((long)FoodGroupEnum.SweetsSnacks, new[] { "candy", "chocolate", "cookie", "cake", "brownie", "donut", "doughnut", "chip", "pretzel", "sugar", "syrup", "honey", "jam", "jelly", "ice cream", "protein bar", "granola bar", "candy bar" }),
            ((long)FoodGroupEnum.Dairy, new[] { "milk", "cheese", "yogurt", "cheddar", "mozzarella", "parmesan", "cream ", "sour cream", "butter" }),
            ((long)FoodGroupEnum.ProteinFoods, new[] { "chicken", "beef", "pork", "turkey", "salmon", "tuna", "shrimp", "steak", "bacon", "sausage", " ham", "lamb", "cod", "tilapia", "sardine", " fish", "egg" }),
            ((long)FoodGroupEnum.Fruits, new[] { "apple", "banana", "berry", "strawberr", "blueberr", "raspberr", "orange", "grape", "melon", "mango", "peach", "pear", "pineapple", "plum", "cherry", "kiwi", "watermelon", "lemon", "lime", "apricot", " fig", "raisin", "cranberr", "pomegranate", "avocado" }),
            ((long)FoodGroupEnum.Vegetables, new[] { "lettuce", "spinach", "kale", "broccoli", "carrot", "tomato", "cucumber", "pepper", "onion", "garlic", "potato", "celery", "cabbage", "cauliflower", "zucchini", "squash", "asparagus", "mushroom", " corn", "eggplant", "beet", "radish", "brussels", "green bean" }),
            ((long)FoodGroupEnum.Grains, new[] { " rice", "bread", "pasta", " oat", "wheat", "flour", "cereal", "quinoa", "barley", "tortilla", "cracker", "bagel", "noodle", "couscous", "bulgur" }),
            ((long)FoodGroupEnum.FatsOils, new[] { " oil", "olive oil", "canola", "margarine", "lard", "shortening" }),
        };

        public async Task<int> AutoClassifyFoodGroupsAsync(bool overwrite)
        {
            var query = _db.Ingredients.AsQueryable();
            if (!overwrite)
                query = query.Where(i => i.FoodGroupId == null);

            var ingredients = await query.ToListAsync();
            int updated = 0;
            foreach (var ing in ingredients)
            {
                var name = (ing.NameNormalized ?? ing.Name ?? string.Empty).ToLowerInvariant();
                if (name.Length == 0) continue;
                long? match = null;
                foreach (var (groupId, keywords) in FoodGroupKeywords)
                {
                    if (keywords.Any(k => name.Contains(k)))
                    {
                        match = groupId;
                        break;
                    }
                }
                if (match.HasValue && ing.FoodGroupId != match)
                {
                    ing.FoodGroupId = match;
                    ing.LastModifiedDate = System.DateTime.UtcNow;
                    updated++;
                }
            }
            if (updated > 0) await _db.SaveChangesAsync();
            return updated;
        }
    }
}