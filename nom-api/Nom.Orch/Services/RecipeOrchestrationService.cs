// File: Nom.Orch/Services/RecipeOrchestrationService.cs

using Nom.Data;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using System;
using Nom.Orch.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Nom.Orch.Models.Recipe;
using System.Collections.Generic;
using System.Linq;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using System.Text.Json;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service responsible for orchestrating high-level recipe-related operations.
    /// </summary>
    public class RecipeOrchestrationService : IRecipeOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<RecipeOrchestrationService> _logger;

        public RecipeOrchestrationService(ApplicationDbContext dbContext, ILogger<RecipeOrchestrationService> logger)
        {
            _db = dbContext;
            _logger = logger;
        }

        public async Task<List<IngredientSearchResponseModel>> SearchIngredientsAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return new List<IngredientSearchResponseModel>();
            }
            var lowerSearchTerm = searchTerm.ToLower();
            return await _db.Ingredients
                .Where(i => EF.Functions.ILike(i.Name, $"%{searchTerm}%"))
                .OrderBy(i => i.FdcDataType == "foundation_food" || i.FdcDataType == "sr_legacy_food" ? 0 : i.FdcDataType == "branded_food" ? 2 : 1)
                .ThenBy(i => i.Name.ToLower() == lowerSearchTerm ? 0 : i.Name.ToLower().StartsWith(lowerSearchTerm) ? 1 : 2)
                .ThenBy(i => i.Name)
                .Select(i => new IngredientSearchResponseModel { Id = i.Id, Name = i.Name, FdcId = i.FdcId })
                .Take(25)
                .ToListAsync();
        }

        public async Task<IngredientModel> GetIngredientDetailsAsync(long ingredientId)
        {
            var ingredient = await _db.Ingredients
                .AsNoTracking()
                .Where(i => i.Id == ingredientId)
                .Select(i => new IngredientModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    FdcId = i.FdcId,
                    Description = i.Description,
                    Nutrients = _db.IngredientNutrients
                        .Where(inu => inu.IngredientId == i.Id)
                        .Select(inu => new NutrientValueModel
                        {
                            NutrientId = inu.NutrientId,
                            NutrientName = inu.Nutrient.Name,
                            Amount = inu.Amount,
                            UnitName = inu.MeasurementType.Name
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();
            if (ingredient == null)
            {
                throw new KeyNotFoundException($"Ingredient with ID {ingredientId} not found.");
            }
            return ingredient;
        }

        public async Task<long> CreateRecipeAsync(CreateRecipeRequest request, long authorPersonId)
        {
            _logger.LogInformation("Creating new recipe '{RecipeName}' for author {AuthorPersonId}", request.Name, authorPersonId);

            var recipe = new RecipeEntity
            {
                Name = request.Name,
                Description = request.Description,
                AuthorId = authorPersonId,
                CurationStatusId = 9000L, // NonCurated
                Version = 1,
                RecipeIngredients = request.Ingredients.Select(i => new RecipeIngredientEntity
                {
                    IngredientId = i.IngredientId,
                    Quantity = i.Quantity,
                    MeasurementTypeId = i.MeasurementTypeId
                }).ToList(),
                RecipeSteps = request.Steps.Select((s, index) => new RecipeStepEntity
                {
                    StepNumber = (byte)(index + 1),
                    Description = s.Description,
                    Summary = s.Description.Substring(0, Math.Min(s.Description.Length, 255)) // Auto-generate summary
                }).ToList()
            };

            _db.Recipes.Add(recipe);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully created recipe {RecipeId}", recipe.Id);
            return recipe.Id;
        }

        public async Task UpdateRecipeAsync(UpdateRecipeRequest request, long authorPersonId)
        {
            _logger.LogInformation("Updating recipe {RecipeId} by author {AuthorPersonId}", request.Id, authorPersonId);

            var recipe = await _db.Recipes
                .Include(r => r.RecipeIngredients)
                .Include(r => r.RecipeSteps)
                .FirstOrDefaultAsync(r => r.Id == request.Id);

            if (recipe == null)
            {
                throw new KeyNotFoundException($"Recipe with ID {request.Id} not found.");
            }
            if (recipe.AuthorId != authorPersonId)
            {
                throw new UnauthorizedAccessException("User is not authorized to edit this recipe.");
            }

            // Update simple properties
            recipe.Name = request.Name;
            recipe.Description = request.Description;

            // Update collections using "clear and replace" strategy
            // Ingredients
            recipe.RecipeIngredients.Clear();
            recipe.RecipeIngredients = request.Ingredients.Select(i => new RecipeIngredientEntity
            {
                IngredientId = i.IngredientId,
                Quantity = i.Quantity,
                MeasurementTypeId = i.MeasurementTypeId
            }).ToList();

            // Steps
            recipe.RecipeSteps.Clear();
            recipe.RecipeSteps = request.Steps.Select((s, index) => new RecipeStepEntity
            {
                StepNumber = (byte)(index + 1),
                Description = s.Description,
                Summary = s.Description.Substring(0, Math.Min(s.Description.Length, 255))
            }).ToList();

            await _db.SaveChangesAsync();
            _logger.LogInformation("Successfully updated recipe {RecipeId}", recipe.Id);
        }

        public async Task<RecipeEditModel> GetRecipeForEditAsync(long recipeId, long authorPersonId)
        {
            var recipe = await _db.Recipes
                .AsNoTracking()
                .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Ingredient)
                .Include(r => r.RecipeSteps)
                .Where(r => r.Id == recipeId)
                .Select(r => new RecipeEditModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    AuthorId = r.AuthorId,
                    Ingredients = r.RecipeIngredients.Select(ri => new RecipeIngredientModel
                    {
                        IngredientId = ri.IngredientId,
                        Name = ri.Ingredient.Name,
                        Quantity = ri.Quantity,
                        MeasurementTypeId = ri.MeasurementTypeId
                    }).ToList(),
                    Steps = r.RecipeSteps.Select(rs => new RecipeStepModel
                    {
                        Id = rs.Id,
                        Description = rs.Description,
                        Order = rs.StepNumber
                    }).OrderBy(s => s.Order).ToList()
                })
                .FirstOrDefaultAsync();

            if (recipe == null)
            {
                throw new KeyNotFoundException($"Recipe with ID {recipeId} not found.");
            }

            if (recipe.AuthorId != authorPersonId)
            {
                throw new UnauthorizedAccessException("User is not authorized to edit this recipe.");
            }

            return recipe;
        }


        public async Task<long> CreateNewRecipeVersionAsync(long parentRecipeId, long authorPersonId)
        {
            _logger.LogInformation("Creating new version for recipe {ParentRecipeId} by author {AuthorPersonId}", parentRecipeId, authorPersonId);

            var parentRecipe = await _db.Recipes
                .AsNoTracking()
                .Include(r => r.RecipeIngredients)
                .Include(r => r.RecipeSteps) // Ensure steps are loaded
                .FirstOrDefaultAsync(r => r.Id == parentRecipeId);

            if (parentRecipe == null)
            {
                _logger.LogWarning("Parent recipe with ID {ParentRecipeId} not found for versioning.", parentRecipeId);
                throw new KeyNotFoundException($"Parent recipe with ID {parentRecipeId} not found.");
            }

            if (parentRecipe.CurationStatusId != 9003L) // 9003L is Curated
            {
                _logger.LogWarning("Attempted to create a new version of a non-curated recipe {ParentRecipeId}", parentRecipeId);
                throw new InvalidOperationException("Cannot create a new version of a recipe that is not curated.");
            }

            // Create a copy for the new version
            var newVersion = new RecipeEntity
            {
                Name = parentRecipe.Name,
                Description = parentRecipe.Description,
                PrepTimeMinutes = parentRecipe.PrepTimeMinutes,
                CookTimeMinutes = parentRecipe.CookTimeMinutes,
                Servings = parentRecipe.Servings,
                ServingQuantity = parentRecipe.ServingQuantity,
                ServingQuantityMeasurementTypeId = parentRecipe.ServingQuantityMeasurementTypeId,
                SourceUrl = parentRecipe.SourceUrl,
                SourceSite = parentRecipe.SourceSite,
                AuthorId = authorPersonId,
                CurationStatusId = 9000L, // New versions start as NonCurated
                ParentRecipeId = parentRecipe.Id,
                Version = parentRecipe.Version + 1,
                // Deep copy collection properties
                RecipeIngredients = parentRecipe.RecipeIngredients?.Select(ri => new RecipeIngredientEntity
                {
                    IngredientId = ri.IngredientId,
                    MeasurementTypeId = ri.MeasurementTypeId,
                    Quantity = ri.Quantity,
                    RawLine = ri.RawLine
                }).ToList(),
                RecipeSteps = parentRecipe.RecipeSteps?.Select(rs => new RecipeStepEntity
                {
                    StepNumber = rs.StepNumber,
                    Summary = rs.Summary,
                    Description = rs.Description,
                    StepTypeId = rs.StepTypeId
                }).ToList()
            };

            _db.Recipes.Add(newVersion);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully created new version {NewRecipeId} for parent recipe {ParentRecipeId}", newVersion.Id, parentRecipeId);
            return newVersion.Id;
        }

        public async Task<IngredientEditModel> GetIngredientForEditAsync(long ingredientId, long authorPersonId)
        {
            var ingredient = await _db.Ingredients
                .AsNoTracking()
                .Where(i => i.Id == ingredientId)
                .Select(i => new IngredientEditModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    Description = i.Description,
                    AuthorId = i.AuthorId,
                    Nutrients = _db.IngredientNutrients
                        .Where(inu => inu.IngredientId == i.Id)
                        .Select(inu => new NutrientValueModel
                        {
                            NutrientId = inu.NutrientId,
                            NutrientName = inu.Nutrient.Name,
                            Amount = inu.Amount,
                            UnitName = inu.MeasurementType.Name
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (ingredient == null)
            {
                throw new KeyNotFoundException($"Ingredient with ID {ingredientId} not found.");
            }

            if (ingredient.AuthorId != authorPersonId)
            {
                throw new UnauthorizedAccessException("User is not authorized to edit this ingredient.");
            }

            return ingredient;
        }

        public async Task<IngredientModel> CreateIngredientAsync(CreateIngredientRequest request, long authorPersonId)
        {
            var newIngredient = new IngredientEntity
            {
                Name = request.Name,
                Description = request.Description,
                AuthorId = authorPersonId,
                CurationStatusId = 9000L // NonCurated
            };

            // Logic to add nutrient records from the request would go here

            _db.Ingredients.Add(newIngredient);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Created new ingredient {IngredientId} by author {AuthorPersonId}", newIngredient.Id, authorPersonId);
            
            // Return the full ingredient model
            return new IngredientModel
            {
                Id = newIngredient.Id,
                Name = newIngredient.Name,
                Description = newIngredient.Description,
                Nutrients = new List<NutrientValueModel>() // Empty for now, can be populated if needed
            };
        }

        public async Task UpdateIngredientAsync(UpdateIngredientRequest request, long authorPersonId)
        {
            var ingredient = await _db.Ingredients
                .Include(i => i.IngredientNutrients)
                .FirstOrDefaultAsync(i => i.Id == request.Id);

            if (ingredient == null)
            {
                throw new KeyNotFoundException($"Ingredient with ID {request.Id} not found.");
            }
            if (ingredient.AuthorId != authorPersonId)
            {
                throw new UnauthorizedAccessException("User is not authorized to edit this ingredient.");
            }

            ingredient.Name = request.Name;
            ingredient.Description = request.Description;
            // In a real implementation, you'd have more robust logic to update, add, and remove nutrient entries.

            await _db.SaveChangesAsync();
            _logger.LogInformation("Updated ingredient {IngredientId} by author {AuthorPersonId}", request.Id, authorPersonId);
        }

        public async Task<List<RecipeDashboardItemModel>> GetAuthorRecipesAsync(long authorPersonId)
        {
            _logger.LogInformation("Fetching recipes for author {AuthorPersonId}", authorPersonId);

            var recipes = await _db.Recipes
                .Where(r => r.AuthorId == authorPersonId)
                .OrderByDescending(r => r.LastModifiedDate)
                .Select(r => new RecipeDashboardItemModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    CurationStatus = r.CurationStatus.Name // Assumes CurationStatus is a loaded navigation property
                })
                .ToListAsync();

            return recipes;
        }

        public async Task<List<RecipeDashboardItemModel>> GetAuthorIngredientsAsync(long authorPersonId)
        {
            _logger.LogInformation("Fetching ingredients for author {AuthorPersonId}", authorPersonId);

            var ingredients = await _db.Ingredients
                .Where(i => i.AuthorId == authorPersonId)
                .OrderByDescending(i => i.LastModifiedDate)
                .Select(i => new RecipeDashboardItemModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    CurationStatus = i.CurationStatus.Name // Assumes CurationStatus is a loaded navigation property
                })
                .ToListAsync();

            return ingredients;
        }
    }
}