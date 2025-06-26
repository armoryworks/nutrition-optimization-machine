// Nom.Orch/UtilityServices/RecipeParsingService.cs
using Nom.Orch.UtilityInterfaces; // Reference the new utility interface
using Nom.Orch.Interfaces; // Reference core orchestration interfaces (e.g., IIngredientParsingService)
using Nom.Orch.Models.Recipe; // Reference models like KaggleRawRecipeDataModel
using Nom.Data.Recipe; // Reference RecipeEntity, IngredientEntity, RecipeStepEntity, etc.
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Orch.UtilityServices // Corrected namespace for Utility Services
{
    /// <summary>
    /// Utility service responsible for taking raw recipe data (e.g., from Kaggle)
    /// and transforming it into structured Nom.Data.Recipe entities,
    /// leveraging specialized parsing services for ingredients and steps.
    /// </summary>
    public class RecipeParsingService : IRecipeParsingService
    {
        private readonly IIngredientParsingService _ingredientParsingService;
        private readonly IRecipeStepParsingService _recipeStepParsingService;
        private readonly ILogger<RecipeParsingService> _logger;

        public RecipeParsingService(IIngredientParsingService ingredientParsingService,
                                      IRecipeStepParsingService recipeStepParsingService,
                                      ILogger<RecipeParsingService> logger)
        {
            _ingredientParsingService = ingredientParsingService;
            _recipeStepParsingService = recipeStepParsingService;
            _logger = logger;
        }

        /// <summary>
        /// Parses a raw recipe data model into a fully structured RecipeEntity object.
        /// This involves parsing ingredients and instructions into their respective entity collections.
        /// </summary>
        /// <param name="rawRecipeData">The raw recipe data model.</param>
        /// <returns>A structured RecipeEntity, or null if critical parsing fails.</returns>
        public async Task<RecipeEntity?> ParseRawRecipeDataAsync(KaggleRawRecipeDataModel rawRecipeData)
        {
            if (string.IsNullOrWhiteSpace(rawRecipeData.Title) ||
                string.IsNullOrWhiteSpace(rawRecipeData.Instructions) ||
                string.IsNullOrWhiteSpace(rawRecipeData.Ingredients))
            {
                _logger.LogWarning("Skipping recipe parsing due to missing Title, Instructions, or Ingredients in raw data.");
                return null;
            }

            // 1. Parse and Standardize Ingredients
            var parsedIngredientsData = await _ingredientParsingService.ParseAndStandardizeIngredientsAsync(rawRecipeData.Ingredients);
            if (!parsedIngredientsData.Any())
            {
                _logger.LogWarning("No ingredients could be parsed for recipe '{Title}'. Skipping recipe.", rawRecipeData.Title);
                return null;
            }

            // 2. Parse Instructions into Steps
            var parsedSteps = await _recipeStepParsingService.ParseInstructionsIntoStepsAsync(rawRecipeData.Instructions);
            if (!parsedSteps.Any())
            {
                _logger.LogWarning("No steps could be parsed from instructions for recipe '{Title}'. Skipping recipe.", rawRecipeData.Title);
                return null;
            }

            // 3. Create the RecipeEntity
            var newRecipe = new RecipeEntity
            {
                Name = rawRecipeData.Title,
                Instructions = rawRecipeData.Instructions, // Store raw instructions string for historical/debug
                RawIngredientsString = rawRecipeData.Ingredients, // Store raw ingredients string for historical/debug
                IsCurated = false, // Imported recipes are not curated by default

                // Map time and servings from raw data, converting seconds to minutes
                PrepTimeMinutes = rawRecipeData.PrepTimeSeconds.HasValue ? (int?)(rawRecipeData.PrepTimeSeconds.Value / 60) : null,
                CookTimeMinutes = rawRecipeData.CookTimeSeconds.HasValue ? (int?)(rawRecipeData.CookTimeSeconds.Value / 60) : null,
                Servings = rawRecipeData.ServingsCount, // Directly map Servings if present

                // Initialize collections
                Ingredients = new List<RecipeIngredientEntity>(),
                Steps = new List<RecipeStepEntity>(),
                // RecipeTypes and Meals might need further logic if extracted from Kaggle data
                RecipeTypes = new List<Data.Reference.ReferenceEntity>(), // Assuming default for now
                Meals = new List<Data.Plan.MealEntity>() // Assuming default for now
            };

            // 4. Link parsed ingredients and steps to the recipe
            foreach (var (recipeIngredient, standardizedIngredient) in parsedIngredientsData)
            {
                // Attach the standardized ingredient to the RecipeIngredientEntity's navigation property
                recipeIngredient.Ingredient = standardizedIngredient;
                newRecipe.Ingredients.Add(recipeIngredient);
            }

            foreach (var recipeStep in parsedSteps)
            {
                newRecipe.Steps.Add(recipeStep);
            }

            return newRecipe;
        }
    }
}
