using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
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
                .Where(r => r.CurationStatus!.Name == "Approved")
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
                .Where(r => r.CurationStatus!.Name == "Approved")
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

        public async Task<RecipeSearchResponseModel> GetRecipesByIngredientsAsync(List<long> ingredientIds, int count = 20)
        {
            var recipes = await _context.Recipes
                .Include(r => r.RecipeIngredients)
                .ThenInclude(ri => ri.Ingredient)
                .Where(r => r.CurationStatus!.Name == "Approved")
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

            // Public/Approved filters
            if (searchModel.IsPublic.HasValue)
            {
                query = query.Where(r => r.CurationStatus!.Name == "Approved");
            }

            if (searchModel.IsApproved.HasValue)
            {
                query = query.Where(r => r.CurationStatus!.Name == "Approved");
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
                    .ThenInclude(ri => ri.MeasurementType);
            }

            if (searchModel.IncludeSteps)
            {
                query = query.Include(r => r.RecipeSteps);
            }

            if (searchModel.IncludeNutrition)
            {
                query = query.Include(r => r.Nutrition);
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
                Servings = (int)(recipe.Servings ?? 0),
                AverageRating = recipe.Ratings?.Any() == true ? recipe.Ratings.Average(r => r.Rating) : 0,
                RatingCount = recipe.Ratings?.Count ?? 0,
                IsPublic = recipe.CurationStatus?.Name == "Approved",
                IsApproved = recipe.CurationStatus?.Name == "Approved",
                CreatedDate = recipe.CreatedDate,
                LastModifiedDate = recipe.DateUpdated,
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
                    MeasurementType = ri.MeasurementType?.Name,
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
                    ImageUrl = null // RecipeStepEntity doesn't have ImageUrl
                }).ToList();
            }

            // Map nutrition if requested
            if (searchModel.IncludeNutrition && recipe.Nutrition != null)
            {
                // Note: RecipeNutritionEntity stores nutrients by NutrientId, not by specific nutrition fields
                // This would need to be expanded to map specific nutrients to nutrition values
                // For now, we'll leave this as null since the nutrition structure is different
                result.Nutrition = null;
            }

            return result;
        }
    }
}