// File: Nom.Orch/Services/RecipeOrchestrationService.cs

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Services
{
    public class RecipeOrchestrationService : IRecipeOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RecipeOrchestrationService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        private string GetCurrentUserId()
        {
            // This is a simplified implementation - in a real app, you'd get this from JWT token or session
            return _httpContextAccessor.HttpContext.User.FindFirst("UserId")?.Value.ToString();
        }

        private long? GetCurrentPersonId()
        {
            var personIdClaim = _httpContextAccessor.HttpContext?.User?.Claims?.FirstOrDefault(c => c.Type == "PersonId")?.Value;
            if (long.TryParse(personIdClaim, out long personId))
            {
                return personId;
            }
            return null;
        }

        private async Task<List<NutrientValueModel>> GetIngredientNutrientsAsync(long ingredientId)
        {
            var nutrients = await _context.IngredientNutrients
                .Include(in_ => in_.Nutrient)
                .Where(in_ => in_.IngredientId == ingredientId)
                .Select(in_ => new NutrientValueModel
                {
                    NutrientId = in_.NutrientId,
                    NutrientName = in_.Nutrient.Name,
                    Amount = in_.Amount,
                    UnitName = in_.Measurement != null ? in_.Measurement.Name : string.Empty
                })
                .ToListAsync();

            return nutrients;
        }

        public async Task<List<RecipeResponseModel>> GetAllRecipesAsync()
        {
            var recipes = await _context.Recipes
                .Include(r => r.Author)
                .Include(r => r.Comments)
                .Include(r => r.Ratings)
                .Include(r => r.CurationStatus)
                .ToListAsync();

            return recipes.Select(r => new RecipeResponseModel
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description ?? string.Empty,
                AuthorId = r.AuthorId,
                AuthorName = r.Author?.Name ?? "Unknown",
                Rating = r.Rating ?? 0,
                CommentCount = r.Comments?.Count ?? 0,
                RatingCount = r.Ratings?.Count ?? 0,
                CreatedDate = r.CreatedDate,
                ModifiedDate = r.LastModifiedDate,
                CurationStatus = r.CurationStatus?.Name ?? "Draft"
            }).ToList();
        }

        public async Task<List<RecipeResponseModel>> GetMyRecipesAsync(long personId)
        {
            var recipes = await _context.Recipes
                .Include(r => r.Author)
                .Include(r => r.Comments)
                .Include(r => r.Ratings)
                .Include(r => r.CurationStatus)
                .Where(r => r.AuthorId == personId)
                .OrderBy(r => r.Name)
                .ToListAsync();

            return recipes.Select(r => new RecipeResponseModel
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description ?? string.Empty,
                AuthorId = r.AuthorId,
                AuthorName = r.Author?.Name ?? "Unknown",
                Rating = r.Rating ?? 0,
                CommentCount = r.Comments?.Count ?? 0,
                RatingCount = r.Ratings?.Count ?? 0,
                CreatedDate = r.CreatedDate,
                ModifiedDate = r.LastModifiedDate,
                CurationStatus = r.CurationStatus?.Name ?? "Draft"
            }).ToList();
        }

        public async Task<RecipeCreateResponseModel> CreateRecipeAsync(RecipeCreateModel model, long currentPersonId)
        {
            // Validate that the current user exists
            var author = await _context.Persons.FindAsync(currentPersonId);
            if (author == null)
            {
                throw new ArgumentException($"Current user with ID {currentPersonId} does not exist.");
            }

            var recipe = new RecipeEntity
            {
                Name = model.Name,
                Description = model.Description,
                AuthorId = currentPersonId,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = currentPersonId
            };

            _context.Recipes.Add(recipe);
            await _context.SaveChangesAsync();

            return new RecipeCreateResponseModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Description = recipe.Description ?? string.Empty,
                AuthorId = recipe.AuthorId
            };
        }

        public async Task<RecipeResponseModel?> GetRecipeAsync(long id)
        {
            var recipe = await _context.Recipes
                .Include(r => r.Author)
                .Include(r => r.Comments)
                .Include(r => r.Ratings)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
                return null;

            return new RecipeResponseModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Description = recipe.Description ?? string.Empty,
                AuthorId = recipe.AuthorId,
                AuthorName = recipe.Author?.Name ?? "Unknown",
                Rating = recipe.Rating ?? 0,
                CommentCount = recipe.Comments?.Count ?? 0,
                RatingCount = recipe.Ratings?.Count ?? 0,
                CreatedDate = recipe.CreatedDate,
                ModifiedDate = recipe.LastModifiedDate
            };
        }

        public async Task<RecipeResponseModel?> UpdateRecipeAsync(long id, RecipeUpdateModel model)
        {
            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null)
                return null;

            recipe.Name = model.Name;
            recipe.Description = model.Description;
            recipe.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new RecipeResponseModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Description = recipe.Description ?? string.Empty,
                AuthorId = recipe.AuthorId,
                AuthorName = "Updated", // Would need to load author to get name
                Rating = recipe.Rating ?? 0,
                CommentCount = 0, // Would need to load comments to get count
                RatingCount = 0, // Would need to load ratings to get count
                CreatedDate = recipe.CreatedDate,
                ModifiedDate = recipe.LastModifiedDate
            };
        }

        public async Task<bool> DeleteRecipeAsync(long id)
        {
            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null)
                return false;

            _context.Recipes.Remove(recipe);
            await _context.SaveChangesAsync();
            return true;
        }

        // Recipe Comments Implementation
        public async Task<RecipeCommentResponseModel> AddCommentAsync(RecipeCommentCreateModel model)
        {
            var comment = new RecipeCommentEntity
            {
                RecipeId = model.RecipeId,
                AuthorId = GetCurrentPersonId() ?? 1, // Use current user instead of model.AuthorId
                Comment = model.Comment,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.RecipeComments.Add(comment);
            await _context.SaveChangesAsync();

            return new RecipeCommentResponseModel
            {
                Id = comment.Id,
                RecipeId = comment.RecipeId,
                AuthorId = comment.AuthorId,
                AuthorName = "Comment Author", // Would need to load author to get name
                Comment = comment.Comment,
                CreatedDate = comment.CreatedDate,
                LastModifiedDate = comment.LastModifiedDate
            };
        }

        public async Task<List<RecipeCommentResponseModel>> GetCommentsAsync(long recipeId)
        {
            var comments = await _context.RecipeComments
                .Include(c => c.Author)
                .Where(c => c.RecipeId == recipeId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync();

            return comments.Select(c => new RecipeCommentResponseModel
            {
                Id = c.Id,
                RecipeId = c.RecipeId,
                AuthorId = c.AuthorId,
                AuthorName = c.Author?.Name ?? "Unknown",
                Comment = c.Comment,
                CreatedDate = c.CreatedDate,
                LastModifiedDate = c.LastModifiedDate
            }).ToList();
        }

        public async Task<bool> DeleteCommentAsync(long commentId)
        {
            var comment = await _context.RecipeComments.FindAsync(commentId);
            if (comment == null)
                return false;

            _context.RecipeComments.Remove(comment);
            await _context.SaveChangesAsync();
            return true;
        }

        // Recipe Ratings Implementation
        public async Task<RecipeRatingResponseModel> AddRatingAsync(RecipeRatingCreateModel model)
        {
            var rating = new RecipeRatingEntity
            {
                RecipeId = model.RecipeId,
                RaterId = GetCurrentPersonId() ?? 1, // Use current user instead of model.AuthorId
                Rating = model.Rating,
                DateRated = DateTime.UtcNow
            };

            _context.RecipeRatings.Add(rating);
            await _context.SaveChangesAsync();

            return new RecipeRatingResponseModel
            {
                Id = rating.Id,
                RecipeId = rating.RecipeId,
                RaterId = rating.RaterId,
                RaterName = "Rating Author", // Would need to load author to get name
                Rating = rating.Rating,
                CreatedDate = rating.CreatedDate,
                LastModifiedDate = rating.LastModifiedDate
            };
        }

        public async Task<List<RecipeRatingResponseModel>> GetRatingsAsync(long recipeId)
        {
            var ratings = await _context.RecipeRatings
                .Include(r => r.Rater)
                .Where(r => r.RecipeId == recipeId)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            return ratings.Select(r => new RecipeRatingResponseModel
            {
                Id = r.Id,
                RecipeId = r.RecipeId,
                RaterId = r.RaterId,
                RaterName = r.Rater?.Name ?? "Unknown",
                Rating = r.Rating,
                CreatedDate = r.CreatedDate,
                LastModifiedDate = r.LastModifiedDate
            }).ToList();
        }

        public async Task<RecipeRatingResponseModel?> UpdateRatingAsync(long ratingId, RecipeRatingUpdateModel model)
        {
            var rating = await _context.RecipeRatings.FindAsync(ratingId);
            if (rating == null)
                return null;

            rating.Rating = model.Rating;
            rating.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new RecipeRatingResponseModel
            {
                Id = rating.Id,
                RecipeId = rating.RecipeId,
                RaterId = rating.RaterId,
                RaterName = "Updated Rating Author", // Would need to load author to get name
                Rating = rating.Rating,
                CreatedDate = rating.CreatedDate,
                LastModifiedDate = rating.LastModifiedDate
            };
        }

        public async Task<bool> DeleteRatingAsync(long ratingId)
        {
            var rating = await _context.RecipeRatings.FindAsync(ratingId);
            if (rating == null)
                return false;

            _context.RecipeRatings.Remove(rating);
            await _context.SaveChangesAsync();
            return true;
        }

        // Recipe Ingredients Implementation
        public async Task<IngredientEditModel?> GetIngredientForEditAsync(long ingredientId)
        {
            var ingredient = await _context.Ingredients
                .Include(i => i.CurationStatus)
                .FirstOrDefaultAsync(i => i.Id == ingredientId);

            if (ingredient == null)
                return null;

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.CreatedByPersonId,
                CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<IngredientEditModel> CreateIngredientAsync(CreateIngredientRequest model)
        {
            var currentPersonId = GetCurrentPersonId();
            
            var ingredient = new IngredientEntity
            {
                Name = model.Name,
                Description = model.Description,
                FdcDataType = "Custom", // Set default FDC data type
                CurationStatusId = (long)CurationStatusEnum.NonCurated, // Set default curation status
                CreatedDate = DateTime.UtcNow, // Set creation date
                CreatedByPersonId = currentPersonId, // Set creator
                AuthorId = currentPersonId // Set author
            };

            _context.Ingredients.Add(ingredient);
            await _context.SaveChangesAsync();

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.CreatedByPersonId,
                CurationStatus = "NonCurated",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<IngredientEditModel> UpdateIngredientAsync(UpdateIngredientRequest model)
        {
            var ingredient = await _context.Ingredients
                .Include(i => i.CurationStatus)
                .FirstOrDefaultAsync(i => i.Id == model.Id);
            if (ingredient == null)
                throw new ArgumentException("Ingredient not found");

            ingredient.Name = model.Name;
            ingredient.Description = model.Description;
            ingredient.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.CreatedByPersonId,
                CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<List<IngredientEditModel>> GetMyIngredientsAsync(long personId)
        {
            var ingredients = await _context.Ingredients
                .Where(i => i.CreatedByPersonId == personId)
                .Include(i => i.CurationStatus)
                .OrderBy(i => i.Name)
                .ToListAsync();

            var result = new List<IngredientEditModel>();
            foreach (var ingredient in ingredients)
            {
                result.Add(new IngredientEditModel
                {
                    Id = ingredient.Id,
                    Name = ingredient.Name,
                    Description = ingredient.Description,
                    AuthorId = ingredient.CreatedByPersonId,
                    CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                    Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
                });
            }

            return result;
        }
    }
}