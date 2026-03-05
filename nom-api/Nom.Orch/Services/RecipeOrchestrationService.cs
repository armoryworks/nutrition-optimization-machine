// File: Nom.Orch/Services/RecipeOrchestrationService.cs

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Nutrient;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

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

        public async Task<List<IngredientSearchResponseModel>> SearchIngredientsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<IngredientSearchResponseModel>();

            var searchTerm = query.ToLower().Trim();

            var ingredients = await _context.Ingredients
                .Include(i => i.CurationStatus)
                .Include(i => i.Aliases)
                .Where(i => i.Name.ToLower().Contains(searchTerm) ||
                            (i.NameNormalized != null && i.NameNormalized.ToLower().Contains(searchTerm)) ||
                            i.Aliases.Any(a => a.AliasName.ToLower().Contains(searchTerm)))
                .OrderBy(i => i.Name.Length) // Prioritize shorter names (exact matches)
                .ThenBy(i => i.Name)
                .Take(20) // Limit results for performance
                .ToListAsync();

            return ingredients.Select(i => new IngredientSearchResponseModel
            {
                Id = i.Id,
                Name = i.Name,
                FdcId = i.FdcId,
                MatchedAlias = i.Aliases.FirstOrDefault(a => a.AliasName.ToLower().Contains(searchTerm))?.AliasName
            }).ToList();
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

        private async Task UpdateIngredientNutrientsAsync(long ingredientId, List<NutrientValueModel> nutrients)
        {
            Console.WriteLine($"UpdateIngredientNutrientsAsync called for ingredient {ingredientId} with {nutrients.Count} nutrients");
            
            // Remove existing nutrients for this ingredient
            var existingNutrients = await _context.IngredientNutrients
                .Where(in_ => in_.IngredientId == ingredientId)
                .ToListAsync();
            
            Console.WriteLine($"Found {existingNutrients.Count} existing nutrients to remove");
            _context.IngredientNutrients.RemoveRange(existingNutrients);

            // Add new nutrients
            int addedCount = 0;
            foreach (var nutrient in nutrients)
            {
                Console.WriteLine($"Processing nutrient: NutrientId={nutrient.NutrientId}, Amount={nutrient.Amount}");
                
                // Skip empty nutrients (nutrientId = 0 or empty)
                if (nutrient.NutrientId <= 0)
                {
                    Console.WriteLine($"Skipping nutrient with NutrientId={nutrient.NutrientId} (<= 0)");
                    continue;
                }

                var ingredientNutrient = new IngredientNutrientEntity
                {
                    IngredientId = ingredientId,
                    NutrientId = nutrient.NutrientId,
                    Amount = nutrient.Amount,
                    MeasurementId = 1, // Default measurement ID - you may want to make this configurable
                    CreatedDate = DateTime.UtcNow,
                    LastModifiedDate = DateTime.UtcNow
                };

                _context.IngredientNutrients.Add(ingredientNutrient);
                addedCount++;
                Console.WriteLine($"Added nutrient: IngredientId={ingredientId}, NutrientId={nutrient.NutrientId}, Amount={nutrient.Amount}");
            }
            
            Console.WriteLine($"Total nutrients added: {addedCount}");
        }

        public async Task<List<RecipeResponseModel>> GetAllRecipesAsync(long? currentPersonId = null)
        {
            var query = _context.Recipes
                .Include(r => r.Author)
                .Include(r => r.Comments)
                .Include(r => r.Ratings)
                .Include(r => r.CurationStatus)
                .AsQueryable();

            if (currentPersonId.HasValue)
                query = query.Where(r => r.CurationStatus!.Name == "Approved" || r.AuthorId == currentPersonId.Value);

            var recipes = await query.ToListAsync();

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

            // Add ingredients
            foreach (var ingredient in model.Ingredients)
            {
                var recipeIngredient = new RecipeIngredientEntity
                {
                    RecipeId = recipe.Id,
                    IngredientId = ingredient.IngredientId,
                    Quantity = ingredient.Quantity,
                    MeasurementId = ingredient.MeasurementId,
                    RawLine = string.Empty // Default empty since not in model
                };
                _context.RecipeIngredients.Add(recipeIngredient);
            }

            // Add steps
            foreach (var step in model.Steps)
            {
                var recipeStep = new RecipeStepEntity
                {
                    RecipeId = recipe.Id,
                    Summary = step.Description, // Use Description as Summary
                    Description = step.Description,
                    StepNumber = step.Order // Use Order as StepNumber
                };
                _context.RecipeSteps.Add(recipeStep);
            }

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
                .Include(r => r.CurationStatus)
                .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Ingredient)
                .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Measurement)
                .Include(r => r.RecipeSteps)
                .Include(r => r.Nutrition)
                    .ThenInclude(n => n.Nutrient)
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
                ImageUrl = recipe.Image,
                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Servings = recipe.Servings,
                Rating = recipe.Rating ?? 0,
                CommentCount = recipe.Comments?.Count ?? 0,
                RatingCount = recipe.Ratings?.Count ?? 0,
                CreatedDate = recipe.CreatedDate,
                ModifiedDate = recipe.LastModifiedDate,
                CurationStatus = recipe.CurationStatus?.Name ?? "Draft",
                Ingredients = recipe.RecipeIngredients?.Select(ri => new RecipeIngredientModel
                {
                    IngredientId = ri.IngredientId,
                    Quantity = ri.Quantity,
                    MeasurementId = ri.MeasurementId,
                    Name = ri.Ingredient?.Name ?? string.Empty,
                    Measurement = ri.Measurement?.Name ?? string.Empty,
                    Notes = ri.RawLine
                }).ToList() ?? new List<RecipeIngredientModel>(),
                Steps = recipe.RecipeSteps?.Select(rs => new RecipeStepModel
                {
                    Description = rs.Description ?? string.Empty,
                    Order = rs.StepNumber
                }).OrderBy(s => s.Order).ToList() ?? new List<RecipeStepModel>(),
                Nutrition = recipe.Nutrition?.Select(n => new RecipeNutritionSearchModel
                {
                    Id = n.NutrientId,
                    NutrientName = n.Nutrient?.Name ?? string.Empty,
                    Amount = n.Amount,
                    Unit = n.Unit ?? string.Empty,
                    DailyValuePercent = n.DailyValuePercentage
                }).ToList() ?? new List<RecipeNutritionSearchModel>()
            };
        }

        public async Task<RecipeResponseModel?> UpdateRecipeAsync(long id, UpdateRecipeRequest model)
        {
            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null)
                return null;

            // Update basic recipe properties
            recipe.Name = model.Name;
            recipe.Description = model.Description;
            recipe.LastModifiedDate = DateTime.UtcNow;
            recipe.Version++;

            // Remove existing ingredients and steps
            var existingIngredients = await _context.RecipeIngredients
                .Where(ri => ri.RecipeId == id)
                .ToListAsync();
            _context.RecipeIngredients.RemoveRange(existingIngredients);

            var existingSteps = await _context.RecipeSteps
                .Where(rs => rs.RecipeId == id)
                .ToListAsync();
            _context.RecipeSteps.RemoveRange(existingSteps);

            // Add new ingredients (skip invalid ones)
            foreach (var ingredient in model.Ingredients)
            {
                // Skip ingredients with invalid IDs
                if (ingredient.IngredientId <= 0 || ingredient.MeasurementId <= 0)
                {
                    continue;
                }

                var recipeIngredient = new RecipeIngredientEntity
                {
                    RecipeId = recipe.Id,
                    IngredientId = ingredient.IngredientId,
                    Quantity = ingredient.Quantity,
                    MeasurementId = ingredient.MeasurementId,
                    RawLine = string.Empty // Default empty since not in model
                };
                _context.RecipeIngredients.Add(recipeIngredient);
            }

            // Add new steps
            foreach (var step in model.Steps)
            {
                var recipeStep = new RecipeStepEntity
                {
                    RecipeId = recipe.Id,
                    Summary = step.Description, // Use Description as Summary
                    Description = step.Description,
                    StepNumber = step.Order // Use Order as StepNumber
                };
                _context.RecipeSteps.Add(recipeStep);
            }

            // Save all changes
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
                AuthorId = ingredient.CreatedByPersonId ?? 0L,
                CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<IngredientEditModel> CreateIngredientAsync(CreateIngredientRequest model)
        {
            Console.WriteLine($"CreateIngredientAsync called with {model.Nutrients.Count} nutrients");
            
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

            // Add nutrients for the new ingredient
            await UpdateIngredientNutrientsAsync(ingredient.Id, model.Nutrients);
            await _context.SaveChangesAsync();

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.CreatedByPersonId ?? 0L,
                CurationStatus = "NonCurated",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<IngredientEditModel> UpdateIngredientAsync(UpdateIngredientRequest model)
        {
            Console.WriteLine($"UpdateIngredientAsync called for ingredient {model.Id} with {model.Nutrients.Count} nutrients");
            
            var ingredient = await _context.Ingredients
                .Include(i => i.CurationStatus)
                .FirstOrDefaultAsync(i => i.Id == model.Id);
            if (ingredient == null)
                throw new ArgumentException("Ingredient not found");

            ingredient.Name = model.Name;
            ingredient.Description = model.Description;
            ingredient.LastModifiedDate = DateTime.UtcNow;

            // Update nutrients
            await UpdateIngredientNutrientsAsync(ingredient.Id, model.Nutrients);

            Console.WriteLine("About to save changes to database");
            await _context.SaveChangesAsync();
            Console.WriteLine("Database changes saved successfully");

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.CreatedByPersonId ?? 0L,
                CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<RecipeDashboardAnalyticsModel> GetDashboardAnalyticsAsync(long personId)
        {
            var recipes = await _context.Recipes
                .Include(r => r.CurationStatus)
                .Where(r => r.AuthorId == personId)
                .AsNoTracking()
                .ToListAsync();

            var recipesByStatus = recipes
                .GroupBy(r => r.CurationStatus?.Name ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            var topRated = recipes
                .Where(r => r.Rating.HasValue && r.Rating > 0)
                .OrderByDescending(r => r.Rating)
                .Take(5)
                .Select(r => new RecipeSummaryModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Rating = r.Rating ?? 0,
                    CreatedDate = r.CreatedDate,
                    ImageUrl = r.Image
                }).ToList();

            var recentlyCreated = recipes
                .OrderByDescending(r => r.CreatedDate)
                .Take(5)
                .Select(r => new RecipeSummaryModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Rating = r.Rating ?? 0,
                    CreatedDate = r.CreatedDate,
                    ImageUrl = r.Image
                }).ToList();

            var recipeIds = recipes.Select(r => r.Id).ToList();
            var mostUsedIngredients = await _context.RecipeIngredients
                .Where(ri => recipeIds.Contains(ri.RecipeId))
                .Include(ri => ri.Ingredient)
                .GroupBy(ri => new { ri.IngredientId, Name = ri.Ingredient!.Name })
                .Select(g => new IngredientUsageModel
                {
                    IngredientId = g.Key.IngredientId,
                    Name = g.Key.Name,
                    UsageCount = g.Count()
                })
                .OrderByDescending(i => i.UsageCount)
                .Take(10)
                .ToListAsync();

            return new RecipeDashboardAnalyticsModel
            {
                TotalRecipes = recipes.Count,
                RecipesByStatus = recipesByStatus,
                TopRatedRecipes = topRated,
                RecentlyCreated = recentlyCreated,
                MostUsedIngredients = mostUsedIngredients
            };
        }

        public async Task<List<IngredientEditModel>> GetMyIngredientsAsync(long personId)
        {
            var ingredients = await _context.Ingredients
                .Where(i => i.AuthorId == personId)
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
                    AuthorId = ingredient.AuthorId ?? 0L,
                    CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                    Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
                });
            }

            return result;
        }

        // Recipe Image/Assets Implementation
        public async Task<RecipeAssetResponseModel> UploadImageAsync(long recipeId, long personId, string fileName, string contentType, byte[] fileData)
        {
            var recipe = await _context.Recipes.FindAsync(recipeId);
            if (recipe == null)
                throw new ArgumentException("Recipe not found");
            if (recipe.AuthorId != personId)
                throw new UnauthorizedAccessException("Only the recipe author can upload images");

            // Resize image if needed using ImageSharp
            using var image = SixLabors.ImageSharp.Image.Load(fileData);
            if (image.Width > 1200)
            {
                var ratio = 1200.0 / image.Width;
                var newHeight = (int)(image.Height * ratio);
                image.Mutate(x => x.Resize(1200, newHeight));
            }

            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms);
            var processedData = ms.ToArray();

            var extension = Path.GetExtension(fileName);
            var asset = new RecipeAssetEntity
            {
                RecipeId = recipeId,
                Name = fileName,
                FileExtension = extension,
                Icon = "image",
                FileData = processedData,
                ContentType = "image/jpeg",
                FileSize = processedData.Length,
                Description = "Recipe image",
                CreatedDate = DateTime.UtcNow
            };

            _context.Set<RecipeAssetEntity>().Add(asset);

            // Update recipe image URL to point to serve endpoint
            recipe.Image = $"/api/recipe/{recipeId}/image";
            recipe.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new RecipeAssetResponseModel
            {
                Id = asset.Id,
                RecipeId = asset.RecipeId,
                Name = asset.Name,
                FileExtension = asset.FileExtension,
                ContentType = asset.ContentType,
                FileSize = asset.FileSize,
                Description = asset.Description,
                CreatedDate = asset.CreatedDate
            };
        }

        public async Task<(byte[] FileData, string ContentType)?> GetImageAsync(long recipeId)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            var asset = await _context.Set<RecipeAssetEntity>()
                .Where(a => a.RecipeId == recipeId && imageExtensions.Contains(a.FileExtension.ToLower()))
                .OrderByDescending(a => a.CreatedDate)
                .FirstOrDefaultAsync();

            if (asset == null)
                return null;

            return (asset.FileData, asset.ContentType ?? "image/jpeg");
        }

        public async Task<bool> DeleteImageAsync(long recipeId, long assetId, long personId)
        {
            var recipe = await _context.Recipes.FindAsync(recipeId);
            if (recipe == null)
                return false;
            if (recipe.AuthorId != personId)
                throw new UnauthorizedAccessException("Only the recipe author can delete images");

            var asset = await _context.Set<RecipeAssetEntity>()
                .FirstOrDefaultAsync(a => a.Id == assetId && a.RecipeId == recipeId);
            if (asset == null)
                return false;

            _context.Set<RecipeAssetEntity>().Remove(asset);

            // Check if there are remaining image assets
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            var remainingImages = await _context.Set<RecipeAssetEntity>()
                .AnyAsync(a => a.RecipeId == recipeId && a.Id != assetId && imageExtensions.Contains(a.FileExtension.ToLower()));

            if (!remainingImages)
            {
                recipe.Image = null;
                recipe.LastModifiedDate = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<RecipeAssetResponseModel>> GetAssetsAsync(long recipeId)
        {
            var assets = await _context.Set<RecipeAssetEntity>()
                .Where(a => a.RecipeId == recipeId)
                .OrderByDescending(a => a.CreatedDate)
                .Select(a => new RecipeAssetResponseModel
                {
                    Id = a.Id,
                    RecipeId = a.RecipeId,
                    Name = a.Name,
                    FileExtension = a.FileExtension,
                    ContentType = a.ContentType,
                    FileSize = a.FileSize,
                    Description = a.Description,
                    CreatedDate = a.CreatedDate
                })
                .ToListAsync();

            return assets;
        }
    }
}