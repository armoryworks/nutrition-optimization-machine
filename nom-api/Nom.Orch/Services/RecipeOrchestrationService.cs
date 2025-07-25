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

            var ingredients = await _db.Ingredients
                .Where(i => EF.Functions.ILike(i.Name, $"%{searchTerm}%"))
                .OrderBy(i =>
                    i.FdcDataType == "foundation_food" || i.FdcDataType == "sr_legacy_food" ? 0 :
                    i.FdcDataType == "branded_food" ? 2 :
                    1)
                .ThenBy(i =>
                    i.Name.ToLower() == lowerSearchTerm ? 0 :
                    i.Name.ToLower().StartsWith(lowerSearchTerm) ? 1 :
                    2)
                .ThenBy(i => i.Name)
                .Select(i => new IngredientSearchResponseModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    FdcId = i.FdcId
                })
                .Take(25)
                .ToListAsync();

            return ingredients;
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
                CurationStatusId = 9000L, // Corresponds to NonCurated from _CustomMigration
                Version = 1
                // Other fields will have their default values
            };

            _db.Recipes.Add(recipe);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully created recipe {RecipeId}", recipe.Id);
            return recipe.Id;
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
                Instructions = parentRecipe.Instructions,
                PrepTimeMinutes = parentRecipe.PrepTimeMinutes,
                CookTimeMinutes = parentRecipe.CookTimeMinutes,
                Servings = parentRecipe.Servings,
                ServingQuantity = parentRecipe.ServingQuantity,
                ServingQuantityMeasurementTypeId = parentRecipe.ServingQuantityMeasurementTypeId,
                RawIngredientsString = parentRecipe.RawIngredientsString,
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
    }
}