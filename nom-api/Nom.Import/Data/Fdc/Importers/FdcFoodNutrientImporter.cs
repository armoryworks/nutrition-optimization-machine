using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Nutrient; // For IngredientNutrientEntity, NutrientEntity
using Nom.Data.Recipe; // For IngredientEntity
using Nom.Import.Data.Fdc.CsvModels;
using Nom.Import.Data.Shared;
using Nom.Import.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EFCore.BulkExtensions; // Required for BulkInsertOrUpdateAsync

namespace Nom.Import.Data.Fdc.Importers
{
    /// <summary>
    /// Imports FDC food_nutrient data into the IngredientNutrientEntity table.
    /// Handles mapping, lookups for Ingredient and Nutrient IDs, and upsert logic.
    /// Uses EFCore.BulkExtensions for set-based operations.
    /// </summary>
    public class FdcFoodNutrientImporter
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<FdcFoodNutrientImporter> _logger;
        private readonly CsvDataLoader<FdcFoodNutrientCsv> _csvDataLoader;
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportConfig _importConfig;

        // Cached Ingredient and Nutrient IDs for efficient lookups
        private Dictionary<string, long> _ingredientFdcIdToIdMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (long Id, long DefaultMeasurementTypeId)> _nutrientFdcIdToInfoMap = new Dictionary<string, (long Id, long DefaultMeasurementTypeId)>(StringComparer.OrdinalIgnoreCase);

        public FdcFoodNutrientImporter(
            ApplicationDbContext dbContext,
            ILogger<FdcFoodNutrientImporter> logger,
            CsvDataLoader<FdcFoodNutrientCsv> csvDataLoader,
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
        /// Initializes necessary reference data (Ingredient and Nutrient IDs) from the database.
        /// </summary>
        private async Task InitializeReferenceDataAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing Ingredient and Nutrient reference data for food nutrients...");

            // Fetch all Ingredients with FdcId
            var ingredients = await _dbContext.Ingredients
                .AsNoTracking()
                .Where(i => i.FdcId != null && i.FdcId != string.Empty)
                .Select(i => new { i.Id, i.FdcId })
                .ToListAsync(cancellationToken);

            foreach (var ing in ingredients)
            {
                _ingredientFdcIdToIdMap[ing.FdcId!] = ing.Id;
            }
            _logger.LogInformation("Cached {Count} Ingredient FdcIds.", _ingredientFdcIdToIdMap.Count);


            // Fetch all Nutrients with FdcId and their DefaultMeasurementTypeId
            var nutrients = await _dbContext.Nutrients
                .AsNoTracking()
                .Where(n => n.FdcId != null && n.FdcId != string.Empty)
                .Select(n => new { n.Id, n.FdcId, n.DefaultMeasurementTypeId })
                .ToListAsync(cancellationToken);

            foreach (var nut in nutrients)
            {
                _nutrientFdcIdToInfoMap[nut.FdcId!] = (nut.Id, nut.DefaultMeasurementTypeId);
            }
            _logger.LogInformation("Cached {Count} Nutrient FdcIds with default measurement types.", _nutrientFdcIdToInfoMap.Count);
        }

        /// <summary>
        /// Imports FDC food_nutrient data from the specified CSV file.
        /// </summary>
        /// <param name="filePath">The full path to the FDC food_nutrient.csv file.</param>
        /// <param name="totalRecords">The total number of records expected in the CSV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ImportAsync(string filePath, long totalRecords, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting FDC Food Nutrient import from: {FilePath}", filePath);

            string stageName = "FDC_Food_Nutrients_Import";
            long processedCount = _progressTracker.GetLastProcessedOffset(stageName); // Resume from last processed count
            long duplicateCount = 0;
            long skippedCount = 0;

            try
            {
                // Initialize reference data (ingredients and nutrients)
                await InitializeReferenceDataAsync(cancellationToken);

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

                    var ingredientNutrientsToProcess = new List<IngredientNutrientEntity>();
                    var currentBatchSkipped = new List<FdcFoodNutrientCsv>(); // For records that are invalid or missing lookups

                    // Use a HashSet to track unique (IngredientId, NutrientId) pairs within the current batch
                    var uniqueBatchKeys = new HashSet<(long IngredientId, long NutrientId)>();

                    foreach (var csvRecord in batch)
                    {
                        // Basic validation: skip if FdcId, NutrientId, or Amount is empty
                        if (string.IsNullOrWhiteSpace(csvRecord.FdcId) ||
                            string.IsNullOrWhiteSpace(csvRecord.NutrientId) ||
                            string.IsNullOrWhiteSpace(csvRecord.Amount))
                        {
                            //_logger.LogWarning("Skipping FDC food nutrient record due to empty FdcId, NutrientId, or Amount: {Record}", csvRecord);
                            skippedCount++;
                            currentBatchSkipped.Add(csvRecord);
                            continue;
                        }

                        // Parse Amount
                        if (!decimal.TryParse(csvRecord.Amount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) || amount < 0)
                        {
                            //_logger.LogWarning("Skipping FDC food nutrient record due to invalid or negative amount '{Amount}': {Record}", csvRecord.Amount, csvRecord);
                            skippedCount++;
                            currentBatchSkipped.Add(csvRecord);
                            continue;
                        }

                        // Lookup IngredientId and NutrientId from cached maps
                        if (!_ingredientFdcIdToIdMap.TryGetValue(csvRecord.FdcId.Trim(), out long ingredientId))
                        {
                            //_logger.LogWarning("Skipping FDC food nutrient record: Ingredient with FdcId '{FdcId}' not found in database. Record: {Record}", csvRecord.FdcId, csvRecord);
                            skippedCount++;
                            currentBatchSkipped.Add(csvRecord);
                            continue;
                        }

                        if (!_nutrientFdcIdToInfoMap.TryGetValue(csvRecord.NutrientId.Trim(), out var nutrientInfo))
                        {
                            //_logger.LogWarning("Skipping FDC food nutrient record: Nutrient with FdcId '{NutrientId}' not found in database. Record: {Record}", csvRecord.NutrientId, csvRecord);
                            skippedCount++;
                            currentBatchSkipped.Add(csvRecord);
                            continue;
                        }

                        long nutrientId = nutrientInfo.Id;
                        long defaultMeasurementTypeId = nutrientInfo.DefaultMeasurementTypeId;

                        // Check for duplicates within the current batch before adding to the list for bulk operation
                        if (!uniqueBatchKeys.Add((ingredientId, nutrientId)))
                        {
                            //_logger.LogWarning("Duplicate IngredientNutrient ({IngredientId}, {NutrientId}) found in current batch. Skipping: {FdcId}", ingredientId, nutrientId, csvRecord.Id);
                            duplicateCount++;
                            currentBatchSkipped.Add(csvRecord); // Consider as skipped/duplicate for batch stats
                            continue;
                        }

                        // Create the entity. BulkInsertOrUpdate will handle if it's an insert or update.
                        var ingredientNutrient = new IngredientNutrientEntity
                        {
                            // IMPORTANT: For EFCore.BulkExtensions to correctly upsert,
                            // if `Id` is the primary key, it must be 0 for new records
                            // and the actual ID for existing records.
                            // Since we are not pre-loading IngredientNutrients, new entities will have Id=0.
                            // Existing entities will be matched by the `UpdateByProperties` below.
                            IngredientId = ingredientId,
                            NutrientId = nutrientId,
                            Amount = amount,
                            MeasurementTypeId = defaultMeasurementTypeId,
                            FdcId = csvRecord.Id.Trim(), // FdcId for this specific ingredient_nutrient record
                            CreatedDate = DateTime.UtcNow,
                            CreatedByPersonId = _importConfig.SystemPersonId,
                            LastModifiedDate = DateTime.UtcNow,
                            LastModifiedByPersonId = _importConfig.SystemPersonId
                        };
                        ingredientNutrientsToProcess.Add(ingredientNutrient);
                    }

                    // Configure BulkConfig to use (IngredientId, NutrientId) as the unique key for upsert.
                    var bulkConfig = new BulkConfig
                    {
                        UpdateByProperties = new List<string> { nameof(IngredientNutrientEntity.IngredientId), nameof(IngredientNutrientEntity.NutrientId) },
                        // PropertiesToUpdateOnInsert and PropertiesToUpdateOnUpdate are not available in this version.
                        // By default, BulkInsertOrUpdateAsync will update all non-primary-key properties if a match is found.
                    };

                    // Perform bulk upsert for the batch
                    // EFCore.BulkExtensions requires a primary key or unique index for upsert.
                    // The "IX_IngredientNutrient_IngredientId_NutrientId" constraint indicates
                    // that your database already has a unique index on these two columns,
                    // which is exactly what BulkInsertOrUpdate needs.
                    await _dbContext.BulkInsertOrUpdateAsync(ingredientNutrientsToProcess, bulkConfig, cancellationToken: cancellationToken);

                    // Note: The processedCount should reflect the total records from the CSV that were *attempted* to be processed,
                    // not just those that resulted in an insert/update.
                    // The `currentBatchSkipped.Count` includes both invalid records and in-batch duplicates.
                    processedCount += batch.Count; // Increment by total records read in the batch
                    await _progressTracker.UpdateProgressAsync(stageName, processedCount);
                    _logger.LogInformation("Processed {Count} FDC food nutrient records. Total processed: {ProcessedCount}/{TotalRecords}. Skipped/Duplicates in batch: {SkippedCount}",
                        batch.Count, processedCount, totalRecords, currentBatchSkipped.Count);
                }

                await _progressTracker.UpdateProgressAsync(stageName, totalRecords); // Mark stage as fully processed
                _logger.LogInformation("FDC Food Nutrient import completed successfully. Total processed: {ProcessedCount}. Total duplicates found: {DuplicateCount}. Total skipped: {SkippedCount}", processedCount, duplicateCount, skippedCount);
            }
            catch (OperationCanceledException)
            {
                //_logger.LogWarning("FDC Food Nutrient import was cancelled.");
                // Progress is already saved by batch
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FDC Food Nutrient import failed unexpectedly.");
                throw; // Re-throw to indicate failure to the calling process
            }
        }
    }
}
