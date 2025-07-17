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
using EFCore.BulkExtensions; // Required for BulkInsertOrUpdateAsync
using Microsoft.Extensions.DependencyInjection; // Required for IServiceScopeFactory

namespace Nom.Import.Data.Fdc.Importers
{
    /// <summary>
    /// Imports FDC food data from food.csv into the IngredientEntity and IngredientAliasEntity tables.
    /// Handles mapping, deduplication, and upsert logic.
    /// </summary>
    public class FdcFoodImporter
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FdcFoodImporter> _logger;
        private readonly CsvDataLoader<FdcFoodCsv> _csvDataLoader;
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportConfig _importConfig;
        private readonly ImportReportGenerator _reportGenerator;

        public FdcFoodImporter(
            IServiceScopeFactory scopeFactory,
            ILogger<FdcFoodImporter> logger,
            CsvDataLoader<FdcFoodCsv> csvDataLoader,
            ImportProgressTracker progressTracker,
            IOptions<ImportConfig> importConfig,
            ImportReportGenerator reportGenerator)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _csvDataLoader = csvDataLoader;
            _progressTracker = progressTracker;
            _importConfig = importConfig.Value;
            _reportGenerator = reportGenerator;
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
            _progressTracker.SetTotalRecords(stageName, totalRecords); // Report total discovered
            long duplicateNameCount = 0; // This will now track in-batch duplicates, not total DB duplicates

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
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<FdcFoodImporter>>(); // Get logger for this scope

                    var ingredientsToUpsert = new List<IngredientEntity>();
                    // Use a HashSet to track unique ingredient names within the current batch
                    var uniqueBatchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var csvRecord in batch)
                    {
                        // Check for cancellation within the inner loop
                        innerCancellationToken.ThrowIfCancellationRequested();

                        // Basic validation: skip if FdcId or Description is empty
                        if (string.IsNullOrWhiteSpace(csvRecord.FdcId) || string.IsNullOrWhiteSpace(csvRecord.Description))
                        {
                            string reason = "Empty FdcId or Description";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            logger.LogWarning("Skipping FDC food record due to {Reason}. Record FdcId: {FdcId}, Description: {Description}",
                                reason, csvRecord.FdcId, csvRecord.Description);
                            continue;
                        }

                        var trimmedDescription = csvRecord.Description.Trim();
                        var trimmedFdcId = csvRecord.FdcId.Trim();

                        // Deduplicate within the current batch by Name (case-insensitive)
                        if (!uniqueBatchNames.Add(trimmedDescription))
                        {
                            string reason = $"Duplicate ingredient description '{trimmedDescription}' in batch";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            duplicateNameCount++; // Increment local duplicate counter
                            continue;
                        }

                        // Create new IngredientEntity (it will be updated if a match is found by BulkExtensions)
                        var newIngredient = new IngredientEntity
                        {
                            Name = trimmedDescription,
                            Description = trimmedDescription, // Use description as both name and description for FDC food
                            FdcId = trimmedFdcId,
                            CreatedDate = DateTime.UtcNow, // These will be overwritten by EF Core's audit if entity is new
                            CreatedByPersonId = _importConfig.SystemPersonId,
                            LastModifiedDate = DateTime.UtcNow,
                            LastModifiedByPersonId = _importConfig.SystemPersonId
                        };
                        ingredientsToUpsert.Add(newIngredient);
                    }

                    // Configure BulkConfig to use Name as the unique key for upsert.
                    var bulkConfig = new BulkConfig
                    {
                        UpdateByProperties = new List<string> { nameof(IngredientEntity.Name) },
                        PropertiesToExcludeOnUpdate = new List<string> {
                            nameof(IngredientEntity.Id), // Primary key, never update via upsert
                            nameof(IngredientEntity.CreatedDate),
                            nameof(IngredientEntity.CreatedByPersonId),
                            nameof(IngredientEntity.Name) // Exclude Name from being updated when conflict on Name occurs
                        }
                        // Removed DisableTemporaryTable = true
                    };

                    if (ingredientsToUpsert.Any())
                    {
                        try
                        {
                            // Perform bulk upsert for the batch
                            await dbContext.BulkInsertOrUpdateAsync(ingredientsToUpsert, bulkConfig, cancellationToken: innerCancellationToken);
                            _progressTracker.RecordImported(stageName, ingredientsToUpsert.Count); // Report imported count
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            string errorMessage = $"FDC Food (Ingredient) import failed unexpectedly in batch. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                            logger.LogError(ex, errorMessage);
                            _reportGenerator.RecordError(errorMessage); // Record the error
                            if (ex.InnerException != null)
                            {
                                logger.LogError(ex.InnerException, "Inner Exception Stack Trace:");
                            }
                            linkedCts.Cancel(); // Signal cancellation to other tasks
                            throw;
                        }
                    }
                    else
                    {
                        logger.LogInformation("No FDC food records to process for this batch.");
                        _progressTracker.RecordImported(stageName, 0); // No records imported, but batch processed
                    }

                    logger.LogInformation("Processed {Count} FDC food records in a parallel task. Total processed (approx): {ProcessedCount}/{TotalRecords}. Duplicates by Name in batch: {BatchDuplicates}",
                        batch.Count, _progressTracker.GetLastProcessedOffset(stageName), totalRecords, uniqueBatchNames.Count - ingredientsToUpsert.Count);
                });

                await _progressTracker.UpdateProgressAsync(stageName, totalRecords); // Final update to ensure total is correct
                _logger.LogInformation("FDC Food (Ingredient) import completed successfully. Total processed: {ProcessedCount}. Total duplicates by Name found (in batches): {DuplicateNameCount}", _progressTracker.GetLastProcessedOffset(stageName), duplicateNameCount);
            }
            catch (OperationCanceledException ex)
            {
                string errorMessage = $"FDC Food (Ingredient) import was cancelled. Exception: {ex.Message}";
                _logger.LogWarning(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage); // Record as fatal if cancelled due to an upstream error
            }
            catch (Exception ex)
            {
                string errorMessage = $"FDC Food (Ingredient) import failed unexpectedly during overall process. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                _logger.LogError(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage); // Record as fatal
                if (ex.InnerException != null)
                {
                    _logger.LogError(ex.InnerException, "Inner Exception Stack Trace:");
                }
                throw;
            }
        }
    }
}
