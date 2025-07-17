using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Recipe; // For RecipeEntity, RecipeIngredientEntity, RecipeStepEntity
using Nom.Import.Data.Recipe.CsvModels;
using Nom.Import.Data.Shared;
using Nom.Import.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json; // For JSON parsing
using System.Threading;
using System.Threading.Tasks;
using EFCore.BulkExtensions; // For BulkInsertOrUpdateAsync
using Microsoft.Extensions.DependencyInjection; // Required for IServiceScopeFactory

namespace Nom.Import.Data.Recipe.Importers
{
    /// <summary>
    /// Imports raw recipe data into the RecipeEntity table.
    /// This importer handles the top-level recipe metadata and orchestrates
    /// the import of ingredients and instructions.
    /// </summary>
    public class RecipeImporter
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecipeImporter> _logger; // Declared here
        private readonly CsvDataLoader<RecipeComRawCsv> _csvDataLoader;
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportConfig _importConfig;
        private readonly ImportReportGenerator _reportGenerator;
        private readonly RecipeIngredientParser _ingredientParser;
        private readonly RecipeInstructionImporter _instructionImporter;

        public RecipeImporter(
            IServiceScopeFactory scopeFactory,
            ILogger<RecipeImporter> logger, // Injected here
            CsvDataLoader<RecipeComRawCsv> csvDataLoader,
            ImportProgressTracker progressTracker,
            IOptions<ImportConfig> importConfig,
            ImportReportGenerator reportGenerator,
            RecipeIngredientParser ingredientParser,
            RecipeInstructionImporter instructionImporter)
        {
            _scopeFactory = scopeFactory;
            _logger = logger; // Assigned here
            _csvDataLoader = csvDataLoader;
            _progressTracker = progressTracker;
            _importConfig = importConfig.Value;
            _reportGenerator = reportGenerator;
            _ingredientParser = ingredientParser;
            _instructionImporter = instructionImporter;
        }

        /// <summary>
        /// Imports raw recipe data from the specified CSV file into RecipeEntity.
        /// </summary>
        /// <param name="filePath">The full path to the raw recipe CSV file.</param>
        /// <param name="totalRecords">The total number of records expected in the CSV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ImportAsync(string filePath, long totalRecords, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting Recipe import from: {FilePath}", filePath);

            string stageName = "Recipe_Import_Recipes";
            _progressTracker.SetTotalRecords(stageName, totalRecords); // Report total discovered
            long skippedCount = 0;

            // Create a CancellationTokenSource linked to the main cancellationToken
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _importConfig.MaxParallelism, CancellationToken = linkedCts.Token };

            try
            {
                await Parallel.ForEachAsync(_csvDataLoader.LoadCsvInBatchesAsync(filePath, _importConfig.BatchSize, linkedCts.Token), parallelOptions, async (batch, innerCancellationToken) =>
                {
                    // Check for cancellation at the start of each batch processing task
                    innerCancellationToken.ThrowIfCancellationRequested();

                    // Create a new scope and DbContext for each parallel task
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<RecipeImporter>>(); // Use a scoped logger
                    // Resolve the importers within this scope to ensure they use this DbContext instance
                    var scopedIngredientParser = scope.ServiceProvider.GetRequiredService<RecipeIngredientParser>();
                    var scopedInstructionImporter = scope.ServiceProvider.GetRequiredService<RecipeInstructionImporter>();

                    // Initialize the scoped ingredient parser's reference data
                    // This will happen for each parallel task, which is less efficient for static data,
                    // but ensures thread safety and correct DbContext usage.
                    await scopedIngredientParser.InitializeAsync(innerCancellationToken);


                    // Pre-load existing recipes for upsert logic within this parallel task's scope.
                    // This ensures each task has its own view of existing data for its batch.
                    var existingRecipesBySourceUrl = await dbContext.Recipes
                        .AsNoTracking()
                        .Where(r => r.SourceUrl != null && r.SourceUrl != string.Empty)
                        .ToDictionaryAsync(r => r.SourceUrl!, r => r, StringComparer.OrdinalIgnoreCase, innerCancellationToken);

                    var recipesToProcess = new List<RecipeEntity>();
                    var currentBatchSkipped = new List<RecipeComRawCsv>();

                    foreach (var csvRecord in batch)
                    {
                        // Check for cancellation within the inner loop
                        innerCancellationToken.ThrowIfCancellationRequested();

                        if (string.IsNullOrWhiteSpace(csvRecord.Link) || string.IsNullOrWhiteSpace(csvRecord.Title))
                        {
                            string reason = "Empty Link or Title";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            scopedLogger.LogWarning("Skipping raw recipe record due to {Reason}. Record: {Record}", reason, csvRecord);
                            continue;
                        }

                        var trimmedLink = csvRecord.Link.Trim();
                        var trimmedTitle = csvRecord.Title.Trim();
                        var trimmedSource = csvRecord.Source?.Trim();

                        // Check for duplicates within the current batch based on SourceUrl
                        if (recipesToProcess.Any(r => r.SourceUrl!.Equals(trimmedLink, StringComparison.OrdinalIgnoreCase)))
                        {
                            string reason = $"Duplicate recipe link '{trimmedLink}' in batch";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            skippedCount++; // Count as skipped due to in-batch duplicate
                            continue;
                        }

                        // Determine if it's an update or insert
                        RecipeEntity? existingRecipe = null;
                        if (existingRecipesBySourceUrl.TryGetValue(trimmedLink, out var recipeMatch))
                        {
                            existingRecipe = recipeMatch;
                        }

                        if (existingRecipe != null)
                        {
                            // Update existing recipe (e.g., if title/source changed, or raw strings were empty)
                            bool needsUpdate = false;
                            if (!existingRecipe.Name.Equals(trimmedTitle, StringComparison.OrdinalIgnoreCase))
                            {
                                existingRecipe.Name = trimmedTitle;
                                needsUpdate = true;
                            }
                            if (!string.IsNullOrWhiteSpace(trimmedSource) && (string.IsNullOrWhiteSpace(existingRecipe.SourceSite) || !existingRecipe.SourceSite.Equals(trimmedSource, StringComparison.OrdinalIgnoreCase)))
                            {
                                existingRecipe.SourceSite = trimmedSource;
                                needsUpdate = true;
                            }
                            // Always update RawIngredientsString and Instructions if the CSV provides them,
                            // to ensure the latest raw data is available for re-parsing if needed.
                            if (!string.IsNullOrWhiteSpace(csvRecord.IngredientsJson))
                            {
                                existingRecipe.RawIngredientsString = csvRecord.IngredientsJson;
                                needsUpdate = true;
                            }
                            if (!string.IsNullOrWhiteSpace(csvRecord.DirectionsJson))
                            {
                                existingRecipe.Instructions = csvRecord.DirectionsJson;
                                needsUpdate = true;
                            }

                            if (needsUpdate)
                            {
                                existingRecipe.LastModifiedDate = DateTime.UtcNow;
                                existingRecipe.LastModifiedByPersonId = _importConfig.SystemPersonId;
                                recipesToProcess.Add(existingRecipe); // Add to list for bulk upsert
                                scopedLogger.LogDebug("Marking existing recipe for update: {Title} (Link: {Link})", trimmedTitle, trimmedLink);
                            }
                            else
                            {
                                scopedLogger.LogDebug("Recipe '{Title}' (Link: {Link}) already exists and is up-to-date. Skipping.", trimmedTitle, trimmedLink);
                                // This record is effectively skipped from being re-processed in this batch for update
                                _progressTracker.RecordSkipped(stageName, "Recipe already exists and is up-to-date");
                                continue; // Skip to next csvRecord
                            }
                        }
                        else
                        {
                            // Create new RecipeEntity
                            var newRecipe = new RecipeEntity
                            {
                                Name = trimmedTitle,
                                Description = trimmedTitle, // Default description to title
                                Instructions = csvRecord.DirectionsJson, // Store raw JSON for later processing
                                RawIngredientsString = csvRecord.IngredientsJson, // Store raw JSON for later processing
                                SourceUrl = trimmedLink,
                                SourceSite = trimmedSource,
                                IsCurated = false, // Default
                                CreatedDate = DateTime.UtcNow,
                                CreatedByPersonId = _importConfig.SystemPersonId,
                                LastModifiedDate = DateTime.UtcNow,
                                LastModifiedByPersonId = _importConfig.SystemPersonId
                            };
                            recipesToProcess.Add(newRecipe);
                            scopedLogger.LogDebug("Adding new recipe: {Title} (Link: {Link})", newRecipe.Name, newRecipe.SourceUrl);
                        }
                    }

                    // Perform bulk upsert for recipes
                    var bulkConfig = new BulkConfig
                    {
                        UpdateByProperties = new List<string> { nameof(RecipeEntity.SourceUrl) },
                        PropertiesToExcludeOnUpdate = new List<string> {
                            nameof(RecipeEntity.Id), // Primary key, never update via upsert
                            nameof(RecipeEntity.CreatedDate),
                            nameof(RecipeEntity.CreatedByPersonId)
                        }
                        // Removed DisableTemporaryTable = true
                    };

                    if (recipesToProcess.Any())
                    {
                        try
                        {
                            await dbContext.BulkInsertOrUpdateAsync(recipesToProcess, bulkConfig, cancellationToken: innerCancellationToken);
                            _progressTracker.RecordImported(stageName, recipesToProcess.Count); // Report imported count
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            string errorMessage = $"Recipe import (recipes) failed unexpectedly in batch. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                            scopedLogger.LogError(ex, errorMessage); // Use scopedLogger here
                            _reportGenerator.RecordError(errorMessage);
                            if (ex.InnerException != null)
                            {
                                scopedLogger.LogError(ex.InnerException, "Inner Exception Stack Trace:"); // Use scopedLogger here
                            }
                            linkedCts.Cancel(); // Signal cancellation to other tasks
                            throw;
                        }
                    }
                    else
                    {
                        scopedLogger.LogInformation("No recipe records to process for this batch.");
                        _progressTracker.RecordImported(stageName, 0); // No records imported, but batch processed
                    }


                    // --- Process Ingredients for this batch of Recipes ---
                    // This section now relies on the Recipe entities that were just upserted,
                    // so their IDs are available.
                    var allRecipeIngredientsToProcess = new List<RecipeIngredientEntity>();
                    string ingredientStageName = "Recipe_Ingredients_Import"; // Define a separate stage name for ingredients

                    foreach (var recipe in recipesToProcess) // Iterate over the *updated* recipesToProcess list
                    {
                        innerCancellationToken.ThrowIfCancellationRequested(); // Check for cancellation

                        if (string.IsNullOrWhiteSpace(recipe.RawIngredientsString))
                        {
                            _progressTracker.RecordSkipped(ingredientStageName, $"Recipe '{recipe.Name}' has no raw ingredient string");
                            continue;
                        }

                        try
                        {
                            var rawIngredientLines = JsonSerializer.Deserialize<List<string>>(recipe.RawIngredientsString);
                            if (rawIngredientLines == null)
                            {
                                _progressTracker.RecordSkipped(ingredientStageName, $"Recipe '{recipe.Name}' raw ingredient string deserialized to null");
                                continue;
                            }

                            // Use a HashSet to track unique (RecipeId, IngredientId) pairs within this recipe's ingredients
                            var uniqueRecipeIngredientKeys = new HashSet<(long RecipeId, long IngredientId)>();

                            foreach (var rawLine in rawIngredientLines)
                            {
                                innerCancellationToken.ThrowIfCancellationRequested(); // Check for cancellation

                                if (string.IsNullOrWhiteSpace(rawLine))
                                {
                                    _progressTracker.RecordSkipped(ingredientStageName, $"Empty raw ingredient line for recipe '{recipe.Name}'");
                                    continue;
                                }

                                // Parse and split the ingredient line
                                var parsedIngredient = scopedIngredientParser.ParseIngredientLine(rawLine);
                                var splitIngredients = scopedIngredientParser.SplitCleanedIngredient(parsedIngredient);

                                foreach (var splitIng in splitIngredients)
                                {
                                    innerCancellationToken.ThrowIfCancellationRequested(); // Check for cancellation

                                    // Find matching IngredientId using fuzzy matching
                                    long? matchedIngredientId = scopedIngredientParser.FindMatchingIngredientId(splitIng.CleanedName);

                                    if (matchedIngredientId.HasValue)
                                    {
                                        // Check for duplicates within this recipe's ingredients
                                        if (!uniqueRecipeIngredientKeys.Add((recipe.Id, matchedIngredientId.Value)))
                                        {
                                            string reason = $"Duplicate RecipeIngredient ({recipe.Id}, {matchedIngredientId.Value}) for recipe '{recipe.Name}'";
                                            _progressTracker.RecordSkipped(ingredientStageName, reason); // Report skipped
                                            scopedLogger.LogWarning(reason + ". Skipping."); // Use scopedLogger here
                                            continue;
                                        }

                                        allRecipeIngredientsToProcess.Add(new RecipeIngredientEntity
                                        {
                                            RecipeId = recipe.Id,
                                            IngredientId = matchedIngredientId.Value,
                                            Quantity = splitIng.Quantity,
                                            MeasurementTypeId = scopedIngredientParser.GetMeasurementTypeId(splitIng.UnitName),
                                            RawLine = splitIng.RawLine, // Original raw line
                                            CreatedByPersonId = _importConfig.SystemPersonId,
                                            CreatedDate = DateTime.UtcNow,
                                            LastModifiedByPersonId = _importConfig.SystemPersonId,
                                            LastModifiedDate = DateTime.UtcNow
                                        });
                                    }
                                    else
                                    {
                                        string reason = $"No ingredient match found for '{splitIng.CleanedName}' from recipe '{recipe.Name}' (ID: {recipe.Id})";
                                        _progressTracker.RecordSkipped(ingredientStageName, reason); // Report skipped
                                        scopedLogger.LogWarning(reason + ". Skipping ingredient."); // Use scopedLogger here
                                    }
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            string errorMessage = $"Error deserializing ingredients JSON for recipe '{recipe.Name}' (ID: {recipe.Id}). Raw JSON: {recipe.RawIngredientsString}. Exception: {ex.Message}";
                            scopedLogger.LogError(ex, errorMessage); // Use scopedLogger here
                            _reportGenerator.RecordError(errorMessage);
                            linkedCts.Cancel(); // Signal cancellation
                            throw; // Re-throw
                        }
                        catch (Exception ex)
                        {
                            string errorMessage = $"An unexpected error occurred while processing ingredients for recipe '{recipe.Name}' (ID: {recipe.Id}). Exception: {ex.Message}";
                            scopedLogger.LogError(ex, errorMessage); // Use scopedLogger here
                            _reportGenerator.RecordError(errorMessage);
                            linkedCts.Cancel(); // Signal cancellation
                            throw; // Re-throw
                        }
                    }

                    if (allRecipeIngredientsToProcess.Any())
                    {
                        // Bulk upsert for RecipeIngredients. Unique key is (RecipeId, IngredientId).
                        var ingredientBulkConfig = new BulkConfig
                        {
                            UpdateByProperties = new List<string> {
                                nameof(RecipeIngredientEntity.RecipeId),
                                nameof(RecipeIngredientEntity.IngredientId)
                            },
                            PropertiesToExcludeOnUpdate = new List<string> {
                                nameof(RecipeIngredientEntity.Id),
                                nameof(RecipeIngredientEntity.CreatedDate),
                                nameof(RecipeIngredientEntity.CreatedByPersonId)
                            }
                            // Removed DisableTemporaryTable = true
                        };
                        try
                        {
                            await dbContext.BulkInsertOrUpdateAsync(allRecipeIngredientsToProcess, ingredientBulkConfig, cancellationToken: innerCancellationToken);
                            _progressTracker.RecordImported(ingredientStageName, allRecipeIngredientsToProcess.Count); // Report imported count for ingredients
                            scopedLogger.LogInformation("Imported/Updated {Count} recipe ingredients for batch.", allRecipeIngredientsToProcess.Count); // Use scopedLogger here
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            string errorMessage = $"Bulk insert/update of recipe ingredients failed unexpectedly in batch. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                            scopedLogger.LogError(ex, errorMessage); // Use scopedLogger here
                            _reportGenerator.RecordError(errorMessage);
                            if (ex.InnerException != null)
                            {
                                scopedLogger.LogError(ex.InnerException, "Inner Exception Stack Trace:"); // Use scopedLogger here
                            }
                            linkedCts.Cancel(); // Signal cancellation
                            throw;
                        }
                    }
                    else
                    {
                        scopedLogger.LogInformation("No recipe ingredients to import for this batch."); // Use scopedLogger here
                        _progressTracker.RecordImported(ingredientStageName, 0); // No records imported, but batch processed
                    }

                    // --- Process Instructions for this batch of Recipes ---
                    // The instruction importer is called with the innerCancellationToken
                    string instructionStageName = "Recipe_Instructions_Import"; // Define a separate stage name for instructions
                    try
                    {
                        await scopedInstructionImporter.ImportInstructionsForRecipesAsync(recipesToProcess, instructionStageName, innerCancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string errorMessage = $"Import of recipe instructions failed unexpectedly in batch. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                        scopedLogger.LogError(ex, errorMessage); // Use scopedLogger here
                        _reportGenerator.RecordError(errorMessage);
                        if (ex.InnerException != null)
                        {
                            scopedLogger.LogError(ex.InnerException, "Inner Exception Stack Trace:"); // Use scopedLogger here
                        }
                        linkedCts.Cancel(); // Signal cancellation
                        throw;
                    }


                    // Update progress tracker (needs to be thread-safe)
                    // The overall batch count is still relevant for the top-level recipe stage
                    // _progressTracker.IncrementProcessedCountAsync(stageName, batch.Count); // This is now implicitly handled by RecordImported/RecordSkipped calls

                    scopedLogger.LogInformation("Processed {Count} raw recipe records in a parallel task. Total processed (approx): {ProcessedCount}/{TotalRecords}. Skipped in batch: {SkippedCount}",
                        batch.Count, _progressTracker.GetLastProcessedOffset(stageName), totalRecords, currentBatchSkipped.Count); // Use scopedLogger here
                });

                await _progressTracker.UpdateProgressAsync(stageName, totalRecords); // Final update to ensure total is correct
                _logger.LogInformation("Recipe import (Recipes stage) completed successfully. Total processed: {ProcessedCount}. Total skipped: {SkippedCount}", _progressTracker.GetLastProcessedOffset(stageName), skippedCount);
            }
            catch (OperationCanceledException ex)
            {
                string errorMessage = $"Recipe import (Recipes stage) was cancelled. Exception: {ex.Message}";
                _logger.LogWarning(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Recipe import (Recipes stage) failed unexpectedly during overall process. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                _logger.LogError(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage);
                if (ex.InnerException != null)
                {
                    _logger.LogError(ex.InnerException, "Inner Exception Stack Trace:");
                }
                throw;
            }
        }
    }
}
