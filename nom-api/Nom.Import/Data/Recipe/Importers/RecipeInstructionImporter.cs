using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Recipe; // For RecipeStepEntity
using Nom.Import.Models;
using Nom.Import.Data.Shared; // For ImportProgressTracker, ImportReportGenerator
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json; // For JSON parsing
using System.Threading;
using System.Threading.Tasks;
using EFCore.BulkExtensions; // For BulkInsertOrUpdateAsync
using Microsoft.Extensions.DependencyInjection; // Required for IServiceScopeFactory

namespace Nom.Import.Data.Recipe.Importers
{
    /// <summary>
    /// Imports recipe instruction steps into the RecipeStepEntity table.
    /// </summary>
    public class RecipeInstructionImporter
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecipeInstructionImporter> _logger;
        private readonly ImportConfig _importConfig;
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportReportGenerator _reportGenerator;

        public RecipeInstructionImporter(
            IServiceScopeFactory scopeFactory,
            ILogger<RecipeInstructionImporter> logger,
            IOptions<ImportConfig> importConfig,
            ImportProgressTracker progressTracker,
            ImportReportGenerator reportGenerator)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _importConfig = importConfig.Value;
            _progressTracker = progressTracker;
            _reportGenerator = reportGenerator;
        }

        /// <summary>
        /// Processes a batch of recipes to extract and import their instruction steps.
        /// </summary>
        /// <param name="recipes">A list of RecipeEntity objects that have just been processed (and have their IDs).</param>
        /// <param name="stageName">The name of the import stage for instructions (e.g., "Recipe_Instructions_Import").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ImportInstructionsForRecipesAsync(List<RecipeEntity> recipes, string stageName, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting import of instructions for {Count} recipes.", recipes.Count);
            // Note: Total records for instructions are not easily known upfront without parsing all recipes.
            // We'll increment imported/skipped counts per step.

            // Create a new scope and DbContext for this operation, as it's called from a parallel context
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<RecipeInstructionImporter>>(); // Get logger for this scope

            var recipeStepsToProcess = new List<RecipeStepEntity>();

            foreach (var recipe in recipes)
            {
                cancellationToken.ThrowIfCancellationRequested(); // Check for cancellation

                if (string.IsNullOrWhiteSpace(recipe.Instructions))
                {
                    string reason = $"Recipe '{recipe.Name}' has no instructions";
                    _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                    logger.LogDebug(reason + ". Skipping.");
                    continue;
                }

                try
                {
                    // Parse the JSON array of instructions
                    var rawSteps = JsonSerializer.Deserialize<List<string>>(recipe.Instructions);

                    if (rawSteps == null || !rawSteps.Any())
                    {
                        string reason = $"Recipe '{recipe.Name}' instructions JSON is empty or invalid";
                        _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                        logger.LogDebug(reason + ". Skipping.");
                        continue;
                    }

                    byte stepNumber = 0;
                    // Use a HashSet to track unique (RecipeId, StepNumber) pairs within this recipe's steps
                    var uniqueRecipeStepKeys = new HashSet<(long RecipeId, byte StepNumber)>();

                    foreach (var rawStepText in rawSteps)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); // Check for cancellation

                        if (string.IsNullOrWhiteSpace(rawStepText))
                        {
                            string reason = $"Empty raw instruction line for recipe '{recipe.Name}' step {stepNumber}";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            continue;
                        }

                        // Check for duplicates within this recipe's steps
                        if (!uniqueRecipeStepKeys.Add((recipe.Id, stepNumber)))
                        {
                            string reason = $"Duplicate RecipeStep ({recipe.Id}, {stepNumber}) for recipe '{recipe.Name}'";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            logger.LogWarning(reason + ". Skipping.");
                            stepNumber++; // Still increment to avoid potential future conflicts if this was just a data issue
                            continue;
                        }

                        var step = new RecipeStepEntity
                        {
                            RecipeId = recipe.Id,
                            StepNumber = stepNumber,
                            Summary = rawStepText.Length > 255 ? rawStepText.Substring(0, 255) : rawStepText, // Truncate for summary
                            Description = rawStepText,
                            StepTypeId = null, // No specific step type from raw data
                            CreatedByPersonId = _importConfig.SystemPersonId,
                            CreatedDate = DateTime.UtcNow,
                            LastModifiedByPersonId = _importConfig.SystemPersonId,
                            LastModifiedDate = DateTime.UtcNow
                        };
                        recipeStepsToProcess.Add(step);
                        stepNumber++;
                    }
                }
                catch (JsonException ex)
                {
                    string errorMessage = $"Error deserializing instructions JSON for recipe '{recipe.Name}' (ID: {recipe.Id}). Raw JSON: {recipe.Instructions}. Exception: {ex.Message}";
                    logger.LogError(ex, errorMessage);
                    _reportGenerator.RecordError(errorMessage);
                    throw; // Re-throw to propagate
                }
                catch (Exception ex)
                {
                    string errorMessage = $"An unexpected error occurred while processing instructions for recipe '{recipe.Name}' (ID: {recipe.Id}). Exception: {ex.Message}";
                    logger.LogError(ex, errorMessage);
                    _reportGenerator.RecordError(errorMessage);
                    throw; // Re-throw to propagate
                }
            }

            if (recipeStepsToProcess.Any())
            {
                // Use BulkInsertOrUpdateAsync. The unique key for RecipeStep is (RecipeId, StepNumber).
                var bulkConfig = new BulkConfig
                {
                    UpdateByProperties = new List<string> { nameof(RecipeStepEntity.RecipeId), nameof(RecipeStepEntity.StepNumber) },
                    PropertiesToExcludeOnUpdate = new List<string> {
                        nameof(RecipeStepEntity.Id),
                        nameof(RecipeStepEntity.CreatedDate),
                        nameof(RecipeStepEntity.CreatedByPersonId)
                    }
                    // Removed DisableTemporaryTable = true
                };
                try
                {
                    await dbContext.BulkInsertOrUpdateAsync(recipeStepsToProcess, bulkConfig, cancellationToken: cancellationToken);
                    _progressTracker.RecordImported(stageName, recipeStepsToProcess.Count); // Report imported count for instructions
                    logger.LogInformation("Imported {Count} recipe steps.", recipeStepsToProcess.Count);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string errorMessage = $"Bulk insert/update of recipe steps failed unexpectedly. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                    logger.LogError(ex, errorMessage);
                    _reportGenerator.RecordError(errorMessage);
                    if (ex.InnerException != null)
                    {
                        logger.LogError(ex.InnerException, "Inner Exception Stack Trace:");
                    }
                    throw;
                }
            }
            else
            {
                logger.LogInformation("No recipe steps to import for this batch.");
                _progressTracker.RecordImported(stageName, 0); // No records imported, but batch processed
            }
        }
    }
}
