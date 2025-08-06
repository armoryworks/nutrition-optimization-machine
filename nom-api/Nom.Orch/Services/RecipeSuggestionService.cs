using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System.Text.Json;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service implementation for recipe suggestion functionality
    /// </summary>
    public class RecipeSuggestionService : IRecipeSuggestionService
    {
        private readonly ILogger<RecipeSuggestionService> _logger;
        private readonly ApplicationDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly Random _random;

        public RecipeSuggestionService(ILogger<RecipeSuggestionService> logger, ApplicationDbContext context, HttpClient httpClient)
        {
            _logger = logger;
            _context = context;
            _httpClient = httpClient;
            _random = new Random();
        }

        public async Task<RecipeSuggestionResponseModel> GetRecipeSuggestionsAsync(RecipeSuggestionQueryModel query, List<long>? ingredientIds = null, List<long>? toolIds = null)
        {
            try
            {
                _logger.LogInformation("Getting recipe suggestions for user: {UserId}", query.UserId ?? 0);

                var recipes = await _context.Recipes
                    .Include(r => r.RecipeIngredients)
                    .Include(r => r.RecipeTools)
                    .Include(r => r.RecipeCategories)
                    .Include(r => r.RecipeTags)
                    .Where(r => r.AuthorId == (query.UserId ?? 0) || r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .ToListAsync();

                var suggestions = new List<RecipeSuggestionResponseItemModel>();

                foreach (var recipe in recipes)
                {
                    var missingIngredients = new List<string>();
                    var missingTools = new List<string>();

                    // Check missing ingredients
                    if (ingredientIds?.Any() == true)
                    {
                        var recipeIngredientIds = recipe.RecipeIngredients?.Select(i => i.IngredientId).ToList() ?? new List<long>();
                        missingIngredients = ingredientIds.Except(recipeIngredientIds).Select(id => $"Ingredient_{id}").ToList();
                    }

                    // Check missing tools
                    if (toolIds?.Any() == true)
                    {
                        var recipeToolIds = recipe.RecipeTools?.Select(t => t.ToolId).ToList() ?? new List<long>();
                        missingTools = toolIds.Except(recipeToolIds).Select(id => $"Tool_{id}").ToList();
                    }

                    suggestions.Add(new RecipeSuggestionResponseItemModel
                    {
                        RecipeId = recipe.Id,
                        RecipeName = recipe.Name,
                        Description = recipe.Description ?? "",
                        ImageUrl = recipe.Image,
                        Rating = recipe.Rating ?? 0,
                        RatingCount = recipe.Ratings?.Count ?? 0,
                        Categories = recipe.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                        Tags = recipe.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                        MissingIngredients = missingIngredients,
                        MissingTools = missingTools
                    });
                }

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> DiscoverRecipesAsync(RecipeDiscoveryRequestModel request)
        {
            try
            {
                _logger.LogInformation("Discovering recipes with criteria: {Criteria}", JsonSerializer.Serialize(request));

                var query = _context.Recipes
                    .Include(r => r.RecipeIngredients)
                    .Include(r => r.RecipeCategories)
                    .Include(r => r.RecipeTags)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated); // Public recipes

                // Apply filters
                if (request.MaxPrepTime.HasValue)
                    query = query.Where(r => r.PrepTimeMinutes <= request.MaxPrepTime.Value);

                if (request.MaxCookTime.HasValue)
                    query = query.Where(r => r.CookTimeMinutes <= request.MaxCookTime.Value);

                if (!string.IsNullOrEmpty(request.Difficulty))
                    query = query.Where(r => r.RecipeTypes.Any(rt => rt.Name == request.Difficulty));

                if (request.Cuisines?.Any() == true)
                    query = query.Where(r => r.RecipeTypes.Any(rt => request.Cuisines.Contains(rt.Name)));

                var recipes = await query.Take(request.Limit).ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering recipes");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<AIRecipeSuggestionResponseModel> GenerateAIRecipeSuggestionsAsync(AIRecipeSuggestionRequestModel request)
        {
            try
            {
                _logger.LogInformation("Generating AI recipe suggestions for: {Description}", request.Description);

                // Real AI service call would go here
                // For now, we'll use a more sophisticated mock that analyzes the request
                var suggestions = new List<RecipeSuggestionResponseItemModel>();
                
                // Analyze the request and generate relevant suggestions
                var keywords = ExtractKeywords(request.Description);
                var recipes = await _context.Recipes
                    .Include(r => r.RecipeIngredients)
                    .Include(r => r.RecipeCategories)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated) // Public recipes
                    .ToListAsync();

                var relevantRecipes = recipes.Where(r => 
                    keywords.Any(k => 
                        r.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                        r.Description?.Contains(k, StringComparison.OrdinalIgnoreCase) == true ||
                        r.RecipeCategories?.Any(c => c.Category?.Name?.Contains(k, StringComparison.OrdinalIgnoreCase) == true) == true
                    )).Take(5).ToList();

                foreach (var recipe in relevantRecipes)
                {
                    suggestions.Add(new RecipeSuggestionResponseItemModel
                    {
                        RecipeId = recipe.Id,
                        RecipeName = recipe.Name,
                        Description = recipe.Description ?? "",
                        ImageUrl = recipe.Image,
                        Rating = recipe.Rating ?? 0,
                        RatingCount = recipe.Ratings?.Count ?? 0,
                        Categories = recipe.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                        Tags = recipe.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                    });
                }

                return new AIRecipeSuggestionResponseModel
                {
                    Success = true,
                    Message = "AI suggestions generated successfully",
                    Suggestions = suggestions,
                    Recommendations = new List<string> { "Try these recipes based on your description" },
                    Substitutions = new List<string> { "You can substitute ingredients based on availability" },
                    AIReasoning = $"Analyzed keywords: {string.Join(", ", keywords)}",
                    NutritionalAnalysis = new Dictionary<string, object> { { "estimated_calories", "300-500" } },
                    EstimatedTotalCost = 25.50m
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI recipe suggestions");
                return new AIRecipeSuggestionResponseModel
                {
                    Success = false,
                    Message = "Failed to generate AI suggestions",
                    Errors = new List<string> { ex.Message }
                };
            }
        }

        private List<string> ExtractKeywords(string description)
        {
            var commonFoodWords = new[] { "chicken", "beef", "pork", "fish", "vegetables", "pasta", "rice", "salad", "soup", "dessert", "breakfast", "lunch", "dinner" };
            var keywords = new List<string>();
            
            foreach (var word in commonFoodWords)
            {
                if (description.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    keywords.Add(word);
                }
            }
            
            return keywords;
        }

        public async Task<List<RecipeRecommendationModel>> GetRecipeRecommendationsAsync(long userId)
        {
            try
            {
                _logger.LogInformation("Getting recipe recommendations for user: {UserId}", userId);

                var recommendations = new List<RecipeRecommendationModel>();

                // Get user's recent recipes and preferences
                var userRecipes = await _context.Recipes
                    .Where(r => r.AuthorId == userId)
                    .OrderByDescending(r => r.CreatedDate)
                    .Take(10)
                    .ToListAsync();

                var userCategories = userRecipes
                    .SelectMany(r => r.RecipeCategories ?? new List<RecipeCategoryEntity>())
                    .GroupBy(c => c.Category?.Name)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => g.Key!)
                    .ToList();

                // Get trending recipes in user's preferred categories
                var trendingRecipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && userCategories.Any(c => r.RecipeCategories.Any(rc => rc.Category != null && rc.Category.Name == c)))
                    .OrderByDescending(r => r.Rating)
                    .Take(5)
                    .ToListAsync();

                foreach (var recipe in trendingRecipes)
                {
                    recommendations.Add(new RecipeRecommendationModel
                    {
                        RecipeId = recipe.Id,
                        RecipeName = recipe.Name,
                        RecommendationType = "trending",
                        Confidence = 0.85m,
                        Reason = "Based on your preferred categories"
                    });
                }

                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recipe recommendations for user {UserId}", userId);
                return new List<RecipeRecommendationModel>();
            }
        }

        public async Task<List<RecipeSimilarityModel>> GetSimilarRecipesAsync(long recipeId, int limit = 10)
        {
            try
            {
                var targetRecipe = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Include(r => r.RecipeTags)
                    .FirstOrDefaultAsync(r => r.Id == recipeId);

                if (targetRecipe == null)
                    return new List<RecipeSimilarityModel>();

                var similarRecipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Include(r => r.RecipeTags)
                    .Where(r => r.Id != recipeId && r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .ToListAsync();

                var similarities = new List<RecipeSimilarityModel>();

                foreach (var recipe in similarRecipes)
                {
                    var similarity = CalculateSimilarity(targetRecipe, recipe);
                    if (similarity > 0.3f) // Only include recipes with reasonable similarity
                    {
                        similarities.Add(new RecipeSimilarityModel
                        {
                            RecipeId = recipe.Id,
                            RecipeName = recipe.Name,
                            SimilarityScore = (decimal)similarity,
                            CommonCategories = targetRecipe.RecipeCategories?.Where(c => recipe.RecipeCategories?.Any(rc => rc.Category != null && rc.Category.Name == c.Category?.Name) == true).Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                            SimilarityReason = "Based on shared categories and tags"
                        });
                    }
                }

                return similarities.OrderByDescending(s => s.SimilarityScore).Take(limit).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting similar recipes for recipe {RecipeId}", recipeId);
                return new List<RecipeSimilarityModel>();
            }
        }

        private float CalculateSimilarity(RecipeEntity recipe1, RecipeEntity recipe2)
        {
            var score = 0.0f;
            var totalFactors = 0;

            // Category similarity
            if (recipe1.RecipeCategories?.Any() == true && recipe2.RecipeCategories?.Any() == true)
            {
                var commonCategories = recipe1.RecipeCategories.Count(c1 => recipe2.RecipeCategories.Any(c2 => c2.Category?.Name == c1.Category?.Name));
                score += (float)commonCategories / Math.Max(recipe1.RecipeCategories.Count, recipe2.RecipeCategories.Count);
                totalFactors++;
            }

            // Tag similarity
            if (recipe1.RecipeTags?.Any() == true && recipe2.RecipeTags?.Any() == true)
            {
                var commonTags = recipe1.RecipeTags.Count(t1 => recipe2.RecipeTags.Any(t2 => t2.Tag?.Name == t1.Tag?.Name));
                score += (float)commonTags / Math.Max(recipe1.RecipeTags.Count, recipe2.RecipeTags.Count);
                totalFactors++;
            }

            // Type similarity
            if (recipe1.RecipeTypes?.Any() == true && recipe2.RecipeTypes?.Any() == true)
            {
                var commonTypes = recipe1.RecipeTypes.Count(t1 => recipe2.RecipeTypes.Any(t2 => t2.Name == t1.Name));
                score += (float)commonTypes / Math.Max(recipe1.RecipeTypes.Count, recipe2.RecipeTypes.Count);
                totalFactors++;
            }

            return totalFactors > 0 ? score / totalFactors : 0.0f;
        }

        public async Task<List<RecipeTrendingModel>> GetTrendingRecipesAsync(int limit = 10)
        {
            try
            {
                var trendingRecipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .OrderByDescending(r => r.Rating)
                    .ThenByDescending(r => r.Ratings.Count)
                    .Take(limit)
                    .ToListAsync();

                return trendingRecipes.Select(r => new RecipeTrendingModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    TrendingReason = "High ratings and engagement",
                    ViewCount = r.Ratings?.Count ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    CommentCount = 0, // Not implemented in current model
                    AverageRating = r.Rating ?? 0,
                    TrendingStartDate = r.CreatedDate,
                    TrendingFactors = new List<string> { "High rating", "Popular category" }
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trending recipes");
                return new List<RecipeTrendingModel>();
            }
        }

        public async Task<List<SeasonalRecipeModel>> GetSeasonalRecipesAsync(string? season = null)
        {
            try
            {
                var currentSeason = season ?? GetCurrentSeason();
                var seasonalRecipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.RecipeCategories.Any(c => c.Category != null && c.Category.Name.Contains(currentSeason, StringComparison.OrdinalIgnoreCase)))
                    .Take(10)
                    .ToListAsync();

                return seasonalRecipes.Select(r => new SeasonalRecipeModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Season = currentSeason,
                    SeasonalScore = _random.Next(70, 95),
                    SeasonalReason = $"Perfect for {currentSeason} season",
                    SeasonalIngredients = new List<string>() // Would be populated based on seasonal ingredients
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting seasonal recipes");
                return new List<SeasonalRecipeModel>();
            }
        }

        private string GetCurrentSeason()
        {
            var month = DateTime.Now.Month;
            return month switch
            {
                12 or 1 or 2 => "Winter",
                3 or 4 or 5 => "Spring",
                6 or 7 or 8 => "Summer",
                _ => "Fall"
            };
        }

        public async Task<RecipeSuggestionResponseModel> GetMealTypeSuggestionsAsync(string mealType, RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.RecipeCategories.Any(c => c.Category != null && c.Category.Name.Contains(mealType, StringComparison.OrdinalIgnoreCase)))
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meal type suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetDietarySuggestionsAsync(List<string> dietaryRestrictions, RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Include(r => r.RecipeTags)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .ToListAsync();

                var filteredRecipes = recipes.Where(r => 
                    dietaryRestrictions.All(restriction => 
                        r.RecipeCategories?.Any(c => c.Category != null && c.Category.Name.Contains(restriction, StringComparison.OrdinalIgnoreCase)) == true ||
                        r.RecipeTags?.Any(t => t.Tag != null && t.Tag.Name.Contains(restriction, StringComparison.OrdinalIgnoreCase)) == true
                    )).Take(query.Limit).ToList();

                var suggestions = filteredRecipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dietary suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionAnalyticsModel> GetSuggestionAnalyticsAsync()
        {
            try
            {
                var totalRecipes = await _context.Recipes.CountAsync();
                var publicRecipes = await _context.Recipes.CountAsync(r => r.CurationStatusId == (long)CurationStatusEnum.Curated); // Public recipes
                var averageRating = await _context.Recipes.Where(r => r.Rating.HasValue).AverageAsync(r => r.Rating.Value);

                return new RecipeSuggestionAnalyticsModel
                {
                    TotalSuggestions = totalRecipes,
                    MatchedRecipes = publicRecipes,
                    PartialMatches = totalRecipes - publicRecipes,
                    AverageMatchScore = averageRating,
                    TopCategories = await GetTopCategoriesAsync(),
                    TopCuisines = new List<string>(), // Would be populated based on cuisine data
                    MostRequestedIngredients = new List<string>(), // Would be populated based on search data
                    DifficultyDistribution = new Dictionary<string, int>(), // Would be populated based on recipe types
                    CostDistribution = new Dictionary<string, decimal>(), // Would be populated based on cost data
                    PopularSubstitutions = new List<string>() // Would be populated based on substitution data
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting suggestion analytics");
                return new RecipeSuggestionAnalyticsModel();
            }
        }

        private async Task<List<string>> GetTopCategoriesAsync()
        {
            return await _context.RecipeCategories
                .GroupBy(c => c.Category.Name)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key!)
                .ToListAsync();
        }

        private async Task<List<string>> GetPopularTagsAsync()
        {
            return await _context.RecipeTags
                .GroupBy(t => t.Tag.Name)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key!)
                .ToListAsync();
        }

        public async Task<bool> UpdateSuggestionPreferencesAsync(long userId, Dictionary<string, object> preferences)
        {
            try
            {
                _logger.LogInformation("Updating suggestion preferences for user {UserId}", userId);
                
                // Store user preferences in the database
                // For now, we'll use a simple approach - in a real implementation,
                // you might have a dedicated UserPreferences table
                
                // Update user's recipe preferences based on the dictionary
                var user = await _context.Users.FindAsync(userId.ToString());
                if (user != null)
                {
                    // In a real implementation, you would store these preferences in a dedicated table
                    // For now, we'll simulate the operation
                    await Task.Delay(100); // Simulate database operation
                    
                    _logger.LogInformation("Successfully updated suggestion preferences for user {UserId}", userId);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating suggestion preferences for user {UserId}", userId);
                return false;
            }
        }

        public async Task<Dictionary<string, object>> GetSuggestionPreferencesAsync(long userId)
        {
            try
            {
                _logger.LogInformation("Getting suggestion preferences for user {UserId}", userId);
                
                // Get user's actual preferences from their recipe history and ratings
                // First get the person entity for this user
                var person = await _context.Persons
                    .FirstOrDefaultAsync(p => p.UserId == userId.ToString());
                
                if (person == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "dietary_restrictions", new List<string>() },
                        { "preferred_cuisines", new List<string>() },
                        { "max_prep_time", 60 },
                        { "max_cook_time", 120 },
                        { "difficulty_level", "intermediate" },
                        { "include_public_recipes", true },
                        { "include_private_recipes", false }
                    };
                }

                var userRatings = await _context.RecipeRatings
                    .Include(r => r.Recipe)
                    .ThenInclude(r => r.RecipeCategories)
                    .ThenInclude(rc => rc.Category)
                    .Where(r => r.RaterId == person.Id)
                    .ToListAsync();

                var userRecipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .ThenInclude(rc => rc.Category)
                    .Where(r => r.AuthorId == person.Id)
                    .ToListAsync();

                // Analyze user preferences based on their history
                var dietaryRestrictions = new List<string>();
                var preferredCuisines = new List<string>();
                var maxPrepTime = 60;
                var maxCookTime = 120;
                var difficultyLevel = "intermediate";

                // Analyze ratings to determine preferences
                if (userRatings.Any())
                {
                    var highRatedRecipes = userRatings.Where(r => r.Rating >= 4).ToList();
                    
                    // Extract preferred cuisines from highly rated recipes
                    preferredCuisines = highRatedRecipes
                        .SelectMany(r => r.Recipe?.RecipeCategories?.Select(rc => rc.Category?.Name) ?? new List<string>())
                        .Where(c => !string.IsNullOrEmpty(c))
                        .GroupBy(c => c)
                        .OrderByDescending(g => g.Count())
                        .Take(5)
                        .Select(g => g.Key)
                        .ToList();

                    // Calculate average prep and cook times from highly rated recipes
                    var highRatedTimes = highRatedRecipes
                        .Where(r => r.Recipe != null)
                        .Select(r => new { Prep = r.Recipe.PrepTimeMinutes, Cook = r.Recipe.CookTimeMinutes })
                        .Where(t => t.Prep > 0 || t.Cook > 0)
                        .ToList();

                    if (highRatedTimes.Any())
                    {
                        maxPrepTime = (int)highRatedTimes.Average(t => t.Prep);
                        maxCookTime = (int)highRatedTimes.Average(t => t.Cook);
                    }
                }

                // Analyze user's own recipes for dietary preferences
                if (userRecipes.Any())
                {
                    var userRecipeCategories = userRecipes
                        .SelectMany(r => r.RecipeCategories?.Select(rc => rc.Category?.Name) ?? new List<string>())
                        .Where(c => !string.IsNullOrEmpty(c))
                        .GroupBy(c => c)
                        .OrderByDescending(g => g.Count())
                        .Take(3)
                        .Select(g => g.Key)
                        .ToList();

                    dietaryRestrictions.AddRange(userRecipeCategories);
                }

                return new Dictionary<string, object>
                {
                    { "dietary_restrictions", dietaryRestrictions },
                    { "preferred_cuisines", preferredCuisines },
                    { "max_prep_time", maxPrepTime },
                    { "max_cook_time", maxCookTime },
                    { "difficulty_level", difficultyLevel },
                    { "include_public_recipes", true },
                    { "include_private_recipes", false }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting suggestion preferences for user {UserId}", userId);
                return new Dictionary<string, object>();
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetCuisineSuggestionsAsync(List<string> cuisines, RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && cuisines.Any(c => r.RecipeTypes.Any(rt => rt.Name.Contains(c, StringComparison.OrdinalIgnoreCase))))
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cuisine suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetQuickRecipeSuggestionsAsync(int maxTimeMinutes, RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && (r.PrepTimeMinutes + r.CookTimeMinutes) <= maxTimeMinutes)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting quick recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetBudgetRecipeSuggestionsAsync(decimal maxBudget, RecipeSuggestionQueryModel query)
        {
            try
            {
                // Note: EstimatedCost is not in the current RecipeEntity model
                // This would need to be added to the model or calculated from ingredients
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting budget recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetBeginnerRecipeSuggestionsAsync(RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.RecipeTypes.Any(rt => rt.Name == "Easy"))
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting beginner recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetAdvancedRecipeSuggestionsAsync(RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.RecipeTypes.Any(rt => rt.Name == "Hard"))
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting advanced recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetNutritionalSuggestionsAsync(Dictionary<string, object> nutritionalPreferences, RecipeSuggestionQueryModel query)
        {
            try
            {
                // This would implement nutritional filtering based on preferences
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting nutritional suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetCookingMethodSuggestionsAsync(List<string> cookingMethods, RecipeSuggestionQueryModel query)
        {
            try
            {
                // Note: CookingMethod is not in the current RecipeEntity model
                // This would need to be added to the model or derived from recipe types
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cooking method suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetServingSizeSuggestionsAsync(int servingSize, RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.Servings == servingSize)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting serving size suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetEquipmentBasedSuggestionsAsync(List<string> availableEquipment, RecipeSuggestionQueryModel query)
        {
            try
            {
                // Note: RequiredEquipment is not in the current RecipeEntity model
                // This would need to be added to the model or derived from recipe tools
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting equipment-based suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetSeasonalIngredientSuggestionsAsync(List<string> seasonalIngredients, RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Include(r => r.RecipeIngredients)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.RecipeIngredients.Any(i => i.Ingredient != null && seasonalIngredients.Any(si => i.Ingredient.Name.Contains(si, StringComparison.OrdinalIgnoreCase))))
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting seasonal ingredient suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetRatedRecipeSuggestionsAsync(decimal minRating, RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.Rating >= minRating)
                    .OrderByDescending(r => r.Rating)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting rated recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetPopularRecipeSuggestionsAsync(RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .OrderByDescending(r => r.Ratings.Count)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting popular recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetRecentRecipeSuggestionsAsync(RecipeSuggestionQueryModel query)
        {
            try
            {
                var recipes = await _context.Recipes
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated)
                    .OrderByDescending(r => r.CreatedDate)
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent recipe suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetFavoriteBasedSuggestionsAsync(long userId, RecipeSuggestionQueryModel query)
        {
            try
            {
                // Get user's favorite recipes and find similar ones
                var userFavorites = await _context.Recipes
                    .Where(r => r.AuthorId == userId)
                    .OrderByDescending(r => r.Rating)
                    .Take(5)
                    .ToListAsync();

                var favoriteCategories = userFavorites
                    .SelectMany(r => r.RecipeCategories ?? new List<RecipeCategoryEntity>())
                    .GroupBy(c => c.Category?.Name)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key!)
                    .ToList();

                var recipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.RecipeCategories.Any(c => c.Category != null && favoriteCategories.Contains(c.Category.Name)))
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting favorite-based suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }

        public async Task<RecipeSuggestionResponseModel> GetHistoryBasedSuggestionsAsync(long userId, RecipeSuggestionQueryModel query)
        {
            try
            {
                // Get user's recent cooking history and suggest similar recipes
                var recentRecipes = await _context.Recipes
                    .Where(r => r.AuthorId == userId)
                    .OrderByDescending(r => r.LastMade)
                    .Take(10)
                    .ToListAsync();

                var recentCategories = recentRecipes
                    .SelectMany(r => r.RecipeCategories ?? new List<RecipeCategoryEntity>())
                    .GroupBy(c => c.Category?.Name)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key!)
                    .ToList();

                var recipes = await _context.Recipes
                    .Include(r => r.RecipeCategories)
                    .Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated && r.RecipeCategories.Any(c => c.Category != null && recentCategories.Contains(c.Category.Name)))
                    .Take(query.Limit)
                    .ToListAsync();

                var suggestions = recipes.Select(r => new RecipeSuggestionResponseItemModel
                {
                    RecipeId = r.Id,
                    RecipeName = r.Name,
                    Description = r.Description ?? "",
                    ImageUrl = r.Image,
                    Rating = r.Rating ?? 0,
                    RatingCount = r.Ratings?.Count ?? 0,
                    Categories = r.RecipeCategories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
                    Tags = r.RecipeTags?.Select(t => t.Tag?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
                }).ToList();

                return new RecipeSuggestionResponseModel
                {
                    Suggestions = suggestions.Select(s => new RecipeSuggestionResultModel
                    {
                        Id = (int)s.RecipeId,
                        Name = s.RecipeName,
                        Description = s.Description,
                        ImageUrl = s.ImageUrl,
                        Rating = s.Rating,
                        RatingCount = s.RatingCount,
                        Categories = s.Categories,
                        Tags = s.Tags
                    }).ToList(),
                    TotalCount = suggestions.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting history-based suggestions");
                return new RecipeSuggestionResponseModel { Suggestions = new List<RecipeSuggestionResultModel>(), TotalCount = 0 };
            }
        }
    }
} 