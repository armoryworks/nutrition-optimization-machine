using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Extensions;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System.Linq;

namespace Nom.Orch.Services
{
    public class RecipeSearchOrchestrationService : IRecipeSearchOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RecipeSearchOrchestrationService> _logger;

        public RecipeSearchOrchestrationService(
            ApplicationDbContext context,
            ILogger<RecipeSearchOrchestrationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<RecipeSearchResponseModel> SearchRecipesAsync(RecipeSearchModel searchModel)
        {
            var query = _context.Recipes.AsQueryable();

            // Apply filters
            query = ApplySearchFilters(query, searchModel);

            // Apply sorting
            query = ApplySorting(query, searchModel.SortBy, searchModel.SortDirection);

            // Get total count for pagination
            var totalCount = await query.CountAsync();

            // Apply pagination
            var skip = (searchModel.Page - 1) * searchModel.PageSize;
            query = query.Skip(skip).Take(searchModel.PageSize);

            // Include related data based on request
            query = IncludeRelatedData(query, searchModel);

            var recipes = await query.ToListAsync();

            var results = recipes.Select(r => MapToSearchResult(r, searchModel)).ToList();

            return new RecipeSearchResponseModel
            {
                Results = results,
                TotalCount = totalCount,
                PageNumber = searchModel.Page,
                PageSize = searchModel.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / searchModel.PageSize)
            };
        }

        public async Task<List<string>> GetSearchSuggestionsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<string>();

            var suggestions = await _context.Recipes
                .Where(r => r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved") // Only suggest from public/approved recipes
                .Where(r => r.Name.Contains(query) || r.Description!.Contains(query))
                .Select(r => r.Name)
                .Distinct()
                .Take(10)
                .ToListAsync();

            return suggestions;
        }

        public async Task<RecipeSearchResponseModel> GetPopularRecipesAsync(int count = 10)
        {
            var popularRecipes = await _context.Recipes
                .Include(r => r.Ratings)
                .Include(r => r.RecipeCategories)
                    .ThenInclude(rc => rc.Category)
                .Include(r => r.RecipeTags)
                    .ThenInclude(rt => rt.Tag)
                .Include(r => r.Author)
                .AsNoTracking()
                .AsSplitQuery()
                .Where(r => r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved")
                .OrderByDescending(r => r.Ratings!.Count)
                .ThenByDescending(r => r.Ratings!.Average(rating => rating.Rating))
                .Take(count)
                .ToListAsync();

            var results = popularRecipes.Select(r => MapToSearchResult(r, new RecipeSearchModel())).ToList();

            return new RecipeSearchResponseModel
            {
                Results = results,
                TotalCount = results.Count,
                PageNumber = 1,
                PageSize = count,
                TotalPages = 1
            };
        }

        public async Task<RecipeSearchResponseModel> GetRecentRecipesAsync(int count = 10)
        {
            var recentRecipes = await _context.Recipes
                .Include(r => r.Ratings)
                .Include(r => r.RecipeCategories)
                    .ThenInclude(rc => rc.Category)
                .Include(r => r.RecipeTags)
                    .ThenInclude(rt => rt.Tag)
                .Include(r => r.Author)
                .AsNoTracking()
                .AsSplitQuery()
                .Where(r => r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved")
                .OrderByDescending(r => r.CreatedDate)
                .Take(count)
                .ToListAsync();

            var results = recentRecipes.Select(r => MapToSearchResult(r, new RecipeSearchModel())).ToList();

            return new RecipeSearchResponseModel
            {
                Results = results,
                TotalCount = results.Count,
                PageNumber = 1,
                PageSize = count,
                TotalPages = 1
            };
        }

        public async Task<RecipeSearchResponseModel> GetRandomRecipesAsync(int count = 1, long? householdId = null, int? minCalories = null, int? maxCalories = null, long? recipeTypeId = null)
        {
            var query = _context.Recipes
                .Include(r => r.Ratings)
                .Include(r => r.RecipeCategories)
                    .ThenInclude(rc => rc.Category)
                .Include(r => r.RecipeTags)
                    .ThenInclude(rt => rt.Tag)
                .Include(r => r.Author)
                .AsNoTracking()
                .AsSplitQuery()
                .Where(r => r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved");

            // Exclude recipes that contain restricted ingredients for household members
            if (householdId.HasValue)
            {
                var restrictedIngredientIds = await _context.HouseholdMembers
                    .Where(hm => hm.HouseholdId == householdId.Value && hm.IsActive)
                    .SelectMany(hm => hm.Person.Restrictions)
                    .Where(r => r.IngredientId.HasValue
                        && (r.EndDate == null || r.EndDate >= DateOnly.FromDateTime(DateTime.UtcNow))
                        && (r.BeginDate == null || r.BeginDate <= DateOnly.FromDateTime(DateTime.UtcNow)))
                    .Select(r => r.IngredientId!.Value)
                    .Distinct()
                    .ToListAsync();

                if (restrictedIngredientIds.Count > 0)
                {
                    query = query.Where(r => !r.RecipeIngredients!
                        .Any(ri => restrictedIngredientIds.Contains(ri.IngredientId)));
                }
            }

            // Filter by calorie range using the recipe's nutrition data
            if (minCalories.HasValue)
            {
                query = query.Where(r => r.Nutrition != null && r.Nutrition
                    .Any(n => n.Nutrient != null
                        && (n.Nutrient.Name.ToLower().Contains("calories") || n.Nutrient.Name.ToLower().Contains("energy") || n.Nutrient.Name.ToLower().Contains("kcal"))
                        && n.Amount >= minCalories.Value));
            }

            if (maxCalories.HasValue)
            {
                query = query.Where(r => r.Nutrition != null && r.Nutrition
                    .Any(n => n.Nutrient != null
                        && (n.Nutrient.Name.ToLower().Contains("calories") || n.Nutrient.Name.ToLower().Contains("energy") || n.Nutrient.Name.ToLower().Contains("kcal"))
                        && n.Amount <= maxCalories.Value));
            }

            // Filter by recipe type
            if (recipeTypeId.HasValue)
            {
                query = query.Where(r => r.RecipeTypes!.Any(rt => rt.Id == recipeTypeId.Value));
            }

            var randomRecipes = await query
                .OrderBy(r => EF.Functions.Random())
                .Take(count)
                .ToListAsync();

            var results = randomRecipes.Select(r => MapToSearchResult(r, new RecipeSearchModel())).ToList();

            return new RecipeSearchResponseModel
            {
                Results = results,
                TotalCount = results.Count,
                PageNumber = 1,
                PageSize = count,
                TotalPages = 1
            };
        }

        public async Task<RecipeSearchResponseModel> GetRecipesByIngredientsAsync(List<long> ingredientIds, int count = 20)
        {
            var recipes = await _context.Recipes
                .Include(r => r.RecipeIngredients)
                .ThenInclude(ri => ri.Ingredient)
                .Where(r => r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved")
                .Where(r => r.RecipeIngredients!.Any(ri => ingredientIds.Contains(ri.IngredientId)))
                .OrderByDescending(r => r.RecipeIngredients!.Count(ri => ingredientIds.Contains(ri.IngredientId)))
                .Take(count)
                .ToListAsync();

            var results = recipes.Select(r => MapToSearchResult(r, new RecipeSearchModel())).ToList();

            return new RecipeSearchResponseModel
            {
                Results = results,
                TotalCount = results.Count,
                PageNumber = 1,
                PageSize = count,
                TotalPages = 1
            };
        }

        // Advanced search features (from Mealie)
        public async Task<RecipeSearchResponseModel> FuzzySearchAsync(string query, int page = 1, int pageSize = 20)
        {
            _logger.LogInformation("Performing fuzzy search for: {Query}", query);

            var searchModel = new RecipeSearchModel
            {
                Query = query,
                Page = page,
                PageSize = pageSize
            };

            return await SearchRecipesAsync(searchModel);
        }

        public async Task<RecipeSearchResponseModel> AdvancedSearchAsync(RecipeAdvancedSearchModel searchModel)
        {
            _logger.LogInformation("Performing advanced search");

            var basicSearchModel = new RecipeSearchModel
            {
                Query = searchModel.Query,
                CategoryIds = searchModel.CategoryIds,
                TagIds = searchModel.TagIds,
                ToolIds = searchModel.ToolIds,
                IngredientIds = searchModel.IngredientIds,
                MinRating = searchModel.MinRating,
                MaxPrepTime = searchModel.MaxPrepTime,
                MaxCookTime = searchModel.MaxCookTime,
                MaxTotalTime = searchModel.MaxTotalTime,
                IsPublic = searchModel.IsPublic,
                IsApproved = searchModel.IsApproved,
                SortBy = searchModel.SortBy,
                SortDirection = searchModel.SortDirection,
                Page = searchModel.Page,
                PageSize = searchModel.PageSize,
                IncludeIngredients = searchModel.IncludeIngredients,
                IncludeSteps = searchModel.IncludeSteps,
                IncludeNutrition = searchModel.IncludeNutrition
            };

            return await SearchRecipesAsync(basicSearchModel);
        }

        public async Task<RecipeSuggestionResponseModel> SuggestRecipesAsync(RecipeSuggestionModel suggestionModel)
        {
            _logger.LogInformation("Getting recipe suggestions");

            var query = _context.Recipes
                .Include(r => r.RecipeCategories)
                .ThenInclude(rc => rc.Category)
                .Include(r => r.RecipeTags)
                .ThenInclude(rt => rt.Tag)
                .Where(r => r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved");

            // Apply filters
            if (!string.IsNullOrWhiteSpace(suggestionModel.Query))
            {
                var searchTerm = suggestionModel.Query.ToLower();
                query = query.Where(r => r.Name.ToLower().Contains(searchTerm) ||
                                       (r.Description != null && r.Description.ToLower().Contains(searchTerm)));
            }

            if (suggestionModel.FoodIds != null && suggestionModel.FoodIds.Any())
            {
                query = query.Where(r => r.RecipeIngredients!.Any(ri => suggestionModel.FoodIds!.Contains(ri.IngredientId)));
            }

            if (suggestionModel.ToolIds != null && suggestionModel.ToolIds.Any())
            {
                query = query.Where(r => r.RecipeTools!.Any(rt => suggestionModel.ToolIds!.Contains(rt.ToolId)));
            }

            var recipes = await query
                .OrderByDescending(r => r.Ratings!.Average(rating => rating.Rating))
                .ThenByDescending(r => r.CreatedDate)
                .Take(suggestionModel.Limit)
                .ToListAsync();

            var suggestions = recipes.Select(r => new RecipeSuggestionResultModel
            {
                Id = (int)r.Id,
                Name = r.Name,
                Description = r.Description,
                ImageUrl = r.Image,
                Rating = r.Ratings?.Any() == true ? r.Ratings.Average(rating => rating.Rating) : null,
                RatingCount = r.Ratings?.Count ?? 0,
                Categories = r.RecipeCategories?.Select(rc => rc.Category?.Name ?? "").ToList() ?? new List<string>(),
                Tags = r.RecipeTags?.Select(rt => rt.Tag?.Name ?? "").ToList() ?? new List<string>()
            }).ToList();

            return new RecipeSuggestionResponseModel
            {
                Suggestions = suggestions,
                TotalCount = suggestions.Count
            };
        }

        public async Task<RecipeSearchResponseModel> SearchByCategoriesAsync(List<long> categoryIds, int page = 1, int pageSize = 20)
        {
            var searchModel = new RecipeSearchModel
            {
                CategoryIds = categoryIds,
                Page = page,
                PageSize = pageSize
            };

            return await SearchRecipesAsync(searchModel);
        }

        public async Task<RecipeSearchResponseModel> SearchByTagsAsync(List<long> tagIds, int page = 1, int pageSize = 20)
        {
            var searchModel = new RecipeSearchModel
            {
                TagIds = tagIds,
                Page = page,
                PageSize = pageSize
            };

            return await SearchRecipesAsync(searchModel);
        }

        public async Task<RecipeSearchResponseModel> SearchByToolsAsync(List<long> toolIds, int page = 1, int pageSize = 20)
        {
            var searchModel = new RecipeSearchModel
            {
                ToolIds = toolIds,
                Page = page,
                PageSize = pageSize
            };

            return await SearchRecipesAsync(searchModel);
        }

        private IQueryable<RecipeEntity> ApplySearchFilters(IQueryable<RecipeEntity> query, RecipeSearchModel searchModel)
        {
            // Text search
            if (!string.IsNullOrWhiteSpace(searchModel.Query))
            {
                var searchTerm = searchModel.Query.ToLower();
                query = query.Where(r => r.Name.ToLower().Contains(searchTerm) ||
                                       (r.Description != null && r.Description.ToLower().Contains(searchTerm)));
            }

            // Ingredient filter
            if (searchModel.IngredientIds != null && searchModel.IngredientIds.Any())
            {
                query = query.Where(r => r.RecipeIngredients!.Any(ri => searchModel.IngredientIds!.Contains(ri.IngredientId)));
            }

            // Category filter
            if (searchModel.CategoryIds != null && searchModel.CategoryIds.Any())
            {
                query = query.Where(r => r.RecipeCategories!.Any(rc => searchModel.CategoryIds!.Contains(rc.CategoryId)));
            }

            // Tag filter
            if (searchModel.TagIds != null && searchModel.TagIds.Any())
            {
                query = query.Where(r => r.RecipeTags!.Any(rt => searchModel.TagIds!.Contains(rt.TagId)));
            }

            // Tool filter
            if (searchModel.ToolIds != null && searchModel.ToolIds.Any())
            {
                query = query.Where(r => r.RecipeTools!.Any(rt => searchModel.ToolIds!.Contains(rt.ToolId)));
            }

            // Cuisine type filter
            if (searchModel.CuisineTypeIds != null && searchModel.CuisineTypeIds.Any())
            {
                query = query.Where(r => r.RecipeTypes!.Any(rc => searchModel.CuisineTypeIds!.Contains(rc.Id)));
            }

            // Rating filter
            if (searchModel.MinRating.HasValue)
            {
                query = query.Where(r => r.Ratings!.Average(rating => rating.Rating) >= searchModel.MinRating.Value);
            }

            // Time filters
            if (searchModel.MaxPrepTime.HasValue)
            {
                query = query.Where(r => r.PrepTimeMinutes <= searchModel.MaxPrepTime.Value);
            }

            if (searchModel.MaxCookTime.HasValue)
            {
                query = query.Where(r => r.CookTimeMinutes <= searchModel.MaxCookTime.Value);
            }

            if (searchModel.MaxTotalTime.HasValue)
            {
                query = query.Where(r => (r.PrepTimeMinutes + r.CookTimeMinutes) <= searchModel.MaxTotalTime.Value);
            }

            // Visibility is ALWAYS applied — the requester (or anonymous) sees
            // only what the central rule allows. Previously, authenticated
            // callers omitting IsPublic/IsApproved got an unfiltered query.
            query = query.VisibleTo(_context, searchModel.RequesterPersonId);

            // Explicit public-pool narrowing (also forced for anonymous callers).
            if (searchModel.IsPublic.HasValue || searchModel.IsApproved.HasValue)
            {
                query = query.Where(r =>
                    r.Visibility == RecipeVisibilityEnum.Public
                    && r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved");
            }

            return query;
        }

        private IQueryable<RecipeEntity> ApplySorting(IQueryable<RecipeEntity> query, string? sortBy, string? sortDirection)
        {
            var isDescending = sortDirection?.ToLower() == "desc";

            return sortBy?.ToLower() switch
            {
                "name" => isDescending ? query.OrderByDescending(r => r.Name) : query.OrderBy(r => r.Name),
                "rating" => isDescending ? query.OrderByDescending(r => r.Ratings!.Average(rating => rating.Rating)) : query.OrderBy(r => r.Ratings!.Average(rating => rating.Rating)),
                "date" => isDescending ? query.OrderByDescending(r => r.CreatedDate) : query.OrderBy(r => r.CreatedDate),
                "preptime" => isDescending ? query.OrderByDescending(r => r.PrepTimeMinutes) : query.OrderBy(r => r.PrepTimeMinutes),
                "cooktime" => isDescending ? query.OrderByDescending(r => r.CookTimeMinutes) : query.OrderBy(r => r.CookTimeMinutes),
                _ => query.OrderByDescending(r => r.CreatedDate) // Default sort
            };
        }

        private IQueryable<RecipeEntity> IncludeRelatedData(IQueryable<RecipeEntity> query, RecipeSearchModel searchModel)
        {
            query = query
                .Include(r => r.Author)
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.Ratings)
                .Include(r => r.RecipeCategories)
                .ThenInclude(rc => rc.Category)
                .Include(r => r.RecipeTags)
                .ThenInclude(rt => rt.Tag)
                .Include(r => r.RecipeTypes);

            if (searchModel.IncludeIngredients)
            {
                query = query
                    .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Ingredient)
                    .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Measurement);
            }

            if (searchModel.IncludeSteps)
            {
                query = query.Include(r => r.RecipeSteps);
            }

            if (searchModel.IncludeNutrition)
            {
                query = query.Include(r => r.Nutrition!)
                    .ThenInclude(n => n.Nutrient);
            }

            return query;
        }

        private RecipeSearchResultModel MapToSearchResult(RecipeEntity recipe, RecipeSearchModel searchModel)
        {
            var result = new RecipeSearchResultModel
            {
                Id = (int)recipe.Id,
                Name = recipe.Name,
                Description = recipe.Description,
                ImageUrl = recipe.Image,
                PrepTime = (int)(recipe.PrepTimeMinutes ?? 0),
                CookTime = (int)(recipe.CookTimeMinutes ?? 0),
                TotalTime = (int)((recipe.PrepTimeMinutes ?? 0) + (recipe.CookTimeMinutes ?? 0)),
                PrepTimeMinutes = (int)(recipe.PrepTimeMinutes ?? 0),
                CookTimeMinutes = (int)(recipe.CookTimeMinutes ?? 0),
                TotalTimeMinutes = (int)((recipe.PrepTimeMinutes ?? 0) + (recipe.CookTimeMinutes ?? 0)),
                Servings = (int)(recipe.Servings ?? 0),
                Rating = recipe.Ratings?.Any() == true ? recipe.Ratings.Average(r => r.Rating) : (decimal?)null,
                AverageRating = recipe.Ratings?.Any() == true ? recipe.Ratings.Average(r => r.Rating) : 0,
                RatingCount = recipe.Ratings?.Count ?? 0,
                IsPublic = recipe.CurationStatus?.Name == "Approved",
                IsApproved = recipe.CurationStatus?.Name == "Approved",
                AuthorId = (int)recipe.AuthorId,
                AuthorName = recipe.Author?.Name ?? "Unknown",
                Categories = recipe.RecipeCategories?.Select(rc => rc.Category?.Name ?? "").ToList() ?? new List<string>(),
                Tags = recipe.RecipeTags?.Select(rt => rt.Tag?.Name ?? "").ToList() ?? new List<string>(),
                CuisineTypes = recipe.RecipeTypes?.Select(rc => rc.Name ?? "").ToList() ?? new List<string>()
            };

            // Map ingredients if requested
            if (searchModel.IncludeIngredients && recipe.RecipeIngredients != null)
            {
                result.Ingredients = recipe.RecipeIngredients.Select(ri => new RecipeIngredientSearchModel
                {
                    Id = (int)ri.Id,
                    Name = ri.Ingredient?.Name ?? "Unknown",
                    Quantity = ri.Quantity,
                    Measurement = ri.Measurement?.Name,
                    Notes = ri.RawLine
                }).ToList();
            }

            // Map steps if requested
            if (searchModel.IncludeSteps && recipe.RecipeSteps != null)
            {
                result.Steps = recipe.RecipeSteps.Select(rs => new RecipeStepSearchModel
                {
                    Id = (int)rs.Id,
                    StepNumber = rs.StepNumber,
                    Instructions = rs.Description,
                    ImageUrl = string.Empty // RecipeStepEntity doesn't have ImageUrl
                }).ToList();
            }

            // Map nutrition if requested
            if (searchModel.IncludeNutrition && recipe.Nutrition != null)
            {
                result.Nutrition = recipe.Nutrition.Select(n => new RecipeNutritionSearchModel
                {
                    Id = n.Id,
                    NutrientName = n.Nutrient?.Name ?? string.Empty,
                    Amount = n.Amount,
                    Unit = n.Unit ?? string.Empty,
                    DailyValuePercent = n.DailyValuePercentage
                }).ToList();
            }

            return result;
        }
    }
}