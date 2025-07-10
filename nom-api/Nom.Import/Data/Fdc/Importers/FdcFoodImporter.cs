using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Recipe; // For IngredientEntity, IngredientAliasEntity
using Nom.Import.Data.Fdc.CsvModels;
using Nom.Import.Data.Shared;
using Nom.Import.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Import.Data.Fdc.Importers
{
    /// <summary>
    /// Imports FDC food data from food.csv into the IngredientEntity and IngredientAliasEntity tables.
    /// Handles mapping, deduplication, and upsert logic.
    /// </summary>
    public class FdcFoodImporter
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<FdcFoodImporter> _logger;
        private readonly CsvDataLoader<FdcFoodCsv> _csvDataLoader;
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportConfig _importConfig;

        public FdcFoodImporter(
            ApplicationDbContext dbContext,
            ILogger<FdcFoodImporter> logger,
            CsvDataLoader<FdcFoodCsv> csvDataLoader,
            ImportProgressTracker progressTracker,
            IOptions<ImportConfig> importConfig)
        {
            _dbContext = dbContext;
            _logger = logger;
            _csvDataLoader = csvDataLoader;
            _progressTracker = progressTracker;
            _importConfig = importConfig.Value;
        }

        /// <summary>
        /// Imports FDC food data from the specified CSV file.
        /// </summary>
        /// <param name="filePath">The full path to the FDC food.csv file.</param>
        /// <param name="totalRecords">The total number of records expected in the CSV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ImportAsync(string filePath, long totalRecords, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting FDC Food (Ingredient) import from: {FilePath}", filePath);

            string stageName = "FDC_Foods_Import";
            long processedCount = _progressTracker.GetLastProcessedOffset(stageName); // Resume from last processed count
            long duplicateCount = 0;

            try
            {
                // Retrieve all existing ingredients for efficient upsert logic
                var existingIngredients = await _dbContext.Ingredients
                    .AsNoTracking() // Use AsNoTracking for initial load to avoid tracking overhead
                    .ToListAsync(cancellationToken);

                // Create dictionaries for quick lookups.
                // Handle potential duplicate names by taking the first encountered.
                var existingIngredientsByFdcId = existingIngredients
                    .Where(i => i.FdcId != null)
                    .ToDictionary(i => i.FdcId!, i => i, StringComparer.OrdinalIgnoreCase);

                // FIX: Handle duplicate keys in existingIngredientsByName
                var existingIngredientsByName = existingIngredients
                    .GroupBy(i => i.Name.ToLowerInvariant()) // Group by lowercased name
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase); // Take the first for each unique name

                long recordsInCsv = 0;
                await foreach (var batch in _csvDataLoader.LoadCsvInBatchesAsync(filePath, _importConfig.BatchSize, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    recordsInCsv += batch.Count;
                    // Skip batches already processed if resuming
                    if (recordsInCsv <= processedCount)
                    {
                        _logger.LogInformation("Skipping batch as it was already processed. Current records in CSV: {RecordsInCsv}, Processed Count: {ProcessedCount}", recordsInCsv, processedCount);
                        continue;
                    }

                    var ingredientsToAdd = new List<IngredientEntity>();
                    var ingredientsToUpdate = new List<IngredientEntity>();
                    var currentBatchDuplicates = new List<FdcFoodCsv>();

                    foreach (var csvRecord in batch)
                    {
                        // Basic validation: skip if FdcId or Description is empty
                        if (string.IsNullOrWhiteSpace(csvRecord.FdcId) || string.IsNullOrWhiteSpace(csvRecord.Description))
                        {
                            //_logger.LogWarning("Skipping FDC food record due to empty FdcId or Description: {Record}", csvRecord);
                            continue;
                        }

                        var trimmedDescription = csvRecord.Description.Trim();
                        var trimmedFdcId = csvRecord.FdcId.Trim();

                        // Check for duplicates within the current batch (based on description, similar to SQL script's DISTINCT ON)
                        if (ingredientsToAdd.Any(i => i.Name.Equals(trimmedDescription, StringComparison.OrdinalIgnoreCase)))
                        {
                            //_logger.LogWarning("Duplicate ingredient description '{Description}' found in current batch. Skipping: {FdcId}", trimmedDescription, trimmedFdcId);
                            currentBatchDuplicates.Add(csvRecord);
                            duplicateCount++;
                            continue;
                        }

                        IngredientEntity? existingIngredient = null;
                        if (existingIngredientsByFdcId.TryGetValue(trimmedFdcId, out var fdcIdMatch))
                        {
                            existingIngredient = fdcIdMatch;
                        }
                        else if (existingIngredientsByName.TryGetValue(trimmedDescription, out var nameMatch))
                        {
                            existingIngredient = nameMatch;
                        }

                        if (existingIngredient != null)
                        {
                            // Update existing ingredient if FdcId is null or Description is null (as per SQL logic)
                            bool needsUpdate = false;
                            if (string.IsNullOrWhiteSpace(existingIngredient.FdcId))
                            {
                                existingIngredient.FdcId = trimmedFdcId;
                                needsUpdate = true;
                            }
                            // The SQL script's MERGE for Ingredient doesn't update description if it's already there
                            // and the source description is NULL. Here, we assume the CSV description is the primary.
                            // If existing description is null, or if the CSV description is richer, update it.
                            if (string.IsNullOrWhiteSpace(existingIngredient.Description)) // Assuming CSV description is the primary source
                            {
                                existingIngredient.Description = trimmedDescription;
                                needsUpdate = true;
                            }

                            if (needsUpdate)
                            {
                                existingIngredient.LastModifiedDate = DateTime.UtcNow;
                                existingIngredient.LastModifiedByPersonId = _importConfig.SystemPersonId;
                                _dbContext.Ingredients.Attach(existingIngredient); // Attach if not already tracked
                                _dbContext.Entry(existingIngredient).State = EntityState.Modified; // Mark as modified
                                _logger.LogDebug("Updating existing ingredient: {Name} (ID: {Id})", existingIngredient.Name, existingIngredient.Id);
                            }
                            else
                            {
                                _logger.LogDebug("Ingredient '{Name}' (FdcId: {FdcId}) already exists and is up-to-date. Skipping.", trimmedDescription, trimmedFdcId);
                            }
                        }
                        else
                        {
                            // Create new IngredientEntity
                            var newIngredient = new IngredientEntity
                            {
                                Name = trimmedDescription,
                                Description = trimmedDescription, // Use description as both name and description for FDC food
                                FdcId = trimmedFdcId,
                                CreatedDate = DateTime.UtcNow,
                                CreatedByPersonId = _importConfig.SystemPersonId,
                                LastModifiedDate = DateTime.UtcNow,
                                LastModifiedByPersonId = _importConfig.SystemPersonId
                            };
                            ingredientsToAdd.Add(newIngredient);
                            _logger.LogDebug("Adding new ingredient: {Name} (FdcId: {FdcId})", newIngredient.Name, newIngredient.FdcId);
                        }
                    }

                    // Add new entities to context
                    _dbContext.Ingredients.AddRange(ingredientsToAdd);
                    // For entities that were already tracked (e.g., if retrieved earlier in a larger context),
                    // EF Core will automatically detect changes. For those that were loaded AsNoTracking,
                    // and then modified, we need to explicitly attach and mark as modified if not already done.
                    // The .UpdateRange call is generally safer if you're not sure about tracking state.
                    // However, if you load all existing with AsNoTracking, then modify them, and then AddRange new ones,
                    // you typically need to Attach and then mark as Modified, or use a library like EFCore.BulkExtensions.
                    // For now, relying on EF's change tracking for entities already in context.
                    // If existingIngredient was found via AsNoTracking, it's not tracked. So we need to attach it.
                    // The 'ingredientsToUpdate' list is no longer strictly needed if we attach and modify directly.
                    // However, for clarity, if you had a separate list, you'd iterate it here.
                    // The current logic where `_dbContext.Entry(existingIngredient).State = EntityState.Modified;` is applied
                    // to `existingIngredient` directly after it's found (if it needs update) is correct,
                    // provided `existingIngredient` was attached or is already tracked.
                    // If existingIngredient was obtained via AsNoTracking, it's not tracked. So we need to attach it first.
                    // The logic above: `_dbContext.Ingredients.Attach(existingIngredient);` handles this.

                    // Save changes for the batch
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    // After successful save, update the in-memory dictionaries for subsequent batches
                    // This is crucial for correctness in subsequent batches of the same import run.
                    foreach (var newIngredient in ingredientsToAdd)
                    {
                        existingIngredientsByFdcId[newIngredient.FdcId!] = newIngredient;
                        existingIngredientsByName[newIngredient.Name] = newIngredient;
                    }
                    // For updated items, ensure they are also in the cache if they weren't already (unlikely if retrieved from DB, but good for consistency)
                    // The existingIngredient variable already points to the object that was potentially attached and modified.
                    // No need to iterate a separate list here.

                    processedCount += batch.Count; // Count all records in the batch as "processed" for progress tracking
                    await _progressTracker.UpdateProgressAsync(stageName, processedCount);
                    _logger.LogInformation("Processed {Count} FDC food records. Total processed: {ProcessedCount}/{TotalRecords}. Duplicates in batch: {DuplicateCount}",
                        batch.Count, processedCount, totalRecords, currentBatchDuplicates.Count);
                }

                await _progressTracker.UpdateProgressAsync(stageName, totalRecords); // Mark stage as fully processed
                _logger.LogInformation("FDC Food (Ingredient) import completed successfully. Total processed: {ProcessedCount}. Total duplicates found: {DuplicateCount}", processedCount, duplicateCount);
            }
            catch (OperationCanceledException)
            {
                //_logger.LogWarning("FDC Food (Ingredient) import was cancelled.");
                // Progress is already saved by batch
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FDC Food (Ingredient) import failed unexpectedly.");
                throw; // Re-throw to indicate failure to the calling process
            }
        }
    }
}
