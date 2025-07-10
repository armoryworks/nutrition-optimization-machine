using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Nutrient; // For NutrientEntity
using Nom.Data.Reference; // For ReferenceEntity, ReferenceDiscriminatorEnum, MeasurementTypeViewEntity
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
    /// Imports nutrient data from FDC nutrient.csv into the NutrientEntity table.
    /// Handles mapping, deduplication, and upsert logic.
    /// </summary>
    public class FdcNutrientImporter
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<FdcNutrientImporter> _logger;
        private readonly CsvDataLoader<FdcNutrientCsv> _csvDataLoader;
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportConfig _importConfig;

        // Cached MeasurementType IDs for efficient lookups
        private Dictionary<string, long> _measurementTypeIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public FdcNutrientImporter(
            ApplicationDbContext dbContext,
            ILogger<FdcNutrientImporter> logger,
            CsvDataLoader<FdcNutrientCsv> csvDataLoader,
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
        /// Initializes necessary reference data (MeasurementType IDs) from the database.
        /// </summary>
        private async Task InitializeReferenceDataAsync()
        {
            _logger.LogInformation("Initializing MeasurementType reference data...");

            // Query the MeasurementTypeViewEntity directly, filtering by the MeasurementType GroupId.
            // This is the correct and translatable way to get references belonging to a specific group.
            var measurementTypes = await _dbContext.MeasurementTypes
                .AsNoTracking()
                .ToListAsync();

            if (!measurementTypes.Any())
            {
                _logger.LogError("No MeasurementType reference data found in the database for GroupId {GroupId}. Please ensure initial reference data is seeded and the ReferenceGroupView is correctly defined and accessible.", (long)ReferenceDiscriminatorEnum.MeasurementType);
                throw new InvalidOperationException("MeasurementType reference data not found.");
            }

            foreach (var mtv in measurementTypes)
            {
                _measurementTypeIds[mtv.ReferenceName] = mtv.ReferenceId;
            }

            // Add common variations/aliases if not already present
            // Ensure 'unknown' is handled as it's a critical fallback
            if (!_measurementTypeIds.ContainsKey("unknown"))
            {
                // Attempt to find 'unknown' by name if not mapped by ID 0
                var unknownRef = await _dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("unknown", StringComparison.OrdinalIgnoreCase));
                if (unknownRef != null)
                {
                    _measurementTypeIds["unknown"] = unknownRef.Id;
                }
                else
                {
                    //_logger.LogWarning("MeasurementType 'unknown' not found in reference data. This may lead to errors if CSV contains unmappable units and 'unknown' is required.");
                    // As a last resort, if 'unknown' is not seeded, we might need a hardcoded fallback or throw.
                    // For now, if it's not found, the GetMeasurementTypeId will throw.
                }
            }


            if (!_measurementTypeIds.ContainsKey("µg") && _measurementTypeIds.ContainsKey("mcg")) _measurementTypeIds["µg"] = _measurementTypeIds["mcg"];
            if (!_measurementTypeIds.ContainsKey("l") && _measurementTypeIds.ContainsKey("liter")) _measurementTypeIds["l"] = _measurementTypeIds["liter"];
            if (!_measurementTypeIds.ContainsKey("g") && _measurementTypeIds.ContainsKey("gram")) _measurementTypeIds["g"] = _measurementTypeIds["gram"];
            if (!_measurementTypeIds.ContainsKey("mg") && _measurementTypeIds.ContainsKey("milligram")) _measurementTypeIds["mg"] = _measurementTypeIds["milligram"];
            if (!_measurementTypeIds.ContainsKey("kg") && _measurementTypeIds.ContainsKey("kilogram")) _measurementTypeIds["kg"] = _measurementTypeIds["kilogram"];
            if (!_measurementTypeIds.ContainsKey("ml") && _measurementTypeIds.ContainsKey("milliliter")) _measurementTypeIds["ml"] = _measurementTypeIds["milliliter"];
            if (!_measurementTypeIds.ContainsKey("tsp") && _measurementTypeIds.ContainsKey("teaspoon")) _measurementTypeIds["tsp"] = _measurementTypeIds["teaspoon"];
            if (!_measurementTypeIds.ContainsKey("tbsp") && _measurementTypeIds.ContainsKey("tablespoon")) _measurementTypeIds["tbsp"] = _measurementTypeIds["tablespoon"];
            if (!_measurementTypeIds.ContainsKey("oz") && _measurementTypeIds.ContainsKey("ounce")) _measurementTypeIds["oz"] = _measurementTypeIds["ounce"];
            if (!_measurementTypeIds.ContainsKey("lb") && _measurementTypeIds.ContainsKey("pound")) _measurementTypeIds["lb"] = _measurementTypeIds["pound"];


            _logger.LogInformation("MeasurementType reference data initialized. Found {Count} types.", _measurementTypeIds.Count);
        }

        /// <summary>
        /// Gets the MeasurementType ID for a given unit name.
        /// </summary>
        /// <param name="unitName">The name of the unit (e.g., "g", "mg", "mcg").</param>
        /// <returns>The ID of the MeasurementType, or the 'unknown' ID if not found.</returns>
        private long GetMeasurementTypeId(string unitName)
        {
            if (_measurementTypeIds.TryGetValue(unitName, out long id))
            {
                return id;
            }
            // Fallback to 'unknown' MeasurementType
            if (_measurementTypeIds.TryGetValue("unknown", out long unknownId))
            {
                //_logger.LogWarning("Measurement unit '{UnitName}' not found. Using 'unknown' MeasurementType (ID: {UnknownId}).", unitName, unknownId);
                return unknownId;
            }
            // This should ideally not happen if 'unknown' is seeded correctly
            throw new InvalidOperationException("MeasurementType 'unknown' not found in reference data. Please seed it with ID 0 or ensure it's loaded correctly.");
        }

        /// <summary>
        /// Imports FDC nutrient data from the specified CSV file.
        /// </summary>
        /// <param name="filePath">The full path to the FDC nutrient.csv file.</param>
        /// <param name="totalRecords">The total number of records expected in the CSV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ImportAsync(string filePath, long totalRecords, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting FDC Nutrient import from: {FilePath}", filePath);

            await InitializeReferenceDataAsync();

            string stageName = "FDC_Nutrients_Import";
            long processedCount = _progressTracker.GetLastProcessedOffset(stageName); // Resume from last processed count
            long duplicateCount = 0;

            try
            {
                // Retrieve existing nutrients for efficient upsert logic
                // Using a dictionary for quick lookups by FdcId and Name
                // Load all existing nutrients once to reduce DB calls
                var existingNutrients = await _dbContext.Nutrients
                    .AsNoTracking() // Use AsNoTracking for initial load to avoid tracking overhead
                    .ToListAsync(cancellationToken);

                var existingNutrientsByFdcId = existingNutrients
                    .Where(n => n.FdcId != null)
                    .ToDictionary(n => n.FdcId!, n => n, StringComparer.OrdinalIgnoreCase);

                var existingNutrientsByName = existingNutrients
                    .ToDictionary(n => n.Name, n => n, StringComparer.OrdinalIgnoreCase);


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

                    var nutrientsToUpsert = new List<NutrientEntity>();
                    var currentBatchDuplicates = new List<FdcNutrientCsv>();

                    foreach (var csvRecord in batch)
                    {
                        // Basic validation: skip if ID or Name is empty
                        if (string.IsNullOrWhiteSpace(csvRecord.Id) || string.IsNullOrWhiteSpace(csvRecord.Name))
                        {
                            //_logger.LogWarning("Skipping FDC nutrient record due to empty ID or Name: {Record}", csvRecord);
                            continue;
                        }

                        // Check for duplicates within the current batch (based on name for now, as per SQL script's DISTINCT ON)
                        if (nutrientsToUpsert.Any(n => n.Name.Equals(csvRecord.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            //_logger.LogWarning("Duplicate nutrient name '{NutrientName}' found in current batch. Skipping: {FdcId}", csvRecord.Name, csvRecord.Id);
                            currentBatchDuplicates.Add(csvRecord);
                            duplicateCount++;
                            continue;
                        }

                        // Try to find existing nutrient by FdcId or Name
                        NutrientEntity? existingNutrient = null;
                        if (existingNutrientsByFdcId.TryGetValue(csvRecord.Id.Trim(), out var fdcIdMatch))
                        {
                            existingNutrient = fdcIdMatch;
                        }
                        else if (existingNutrientsByName.TryGetValue(csvRecord.Name.Trim(), out var nameMatch))
                        {
                            existingNutrient = nameMatch;
                        }

                        if (existingNutrient != null)
                        {
                            // Update existing nutrient if FdcId is null or Description is null (as per SQL logic)
                            // Note: Your SQL script sets Description to NULL, so this update condition might always be true for Description.
                            // If FdcId is null in existing record, update it.
                            if (string.IsNullOrWhiteSpace(existingNutrient.FdcId))
                            {
                                existingNutrient.FdcId = csvRecord.Id.Trim();
                                existingNutrient.LastModifiedDate = DateTime.UtcNow;
                                existingNutrient.LastModifiedByPersonId = _importConfig.SystemPersonId;
                                _dbContext.Entry(existingNutrient).State = EntityState.Modified; // Mark as modified
                                _logger.LogDebug("Updating existing nutrient FdcId: {Name} (ID: {Id})", existingNutrient.Name, existingNutrient.Id);
                            }
                            // No description update from this CSV, as it's NULL in SQL.
                            // If you later get a source with descriptions, this logic would need to be revisited.
                        }
                        else
                        {
                            // Create new NutrientEntity
                            var newNutrient = new NutrientEntity
                            {
                                Name = csvRecord.Name.Trim(),
                                Description = null, // No description in nutrient.csv
                                DefaultMeasurementTypeId = GetMeasurementTypeId(csvRecord.UnitName.Trim()),
                                FdcId = csvRecord.Id.Trim(),
                                CreatedDate = DateTime.UtcNow,
                                CreatedByPersonId = _importConfig.SystemPersonId,
                                LastModifiedDate = DateTime.UtcNow,
                                LastModifiedByPersonId = _importConfig.SystemPersonId
                            };
                            nutrientsToUpsert.Add(newNutrient);
                            _logger.LogDebug("Adding new nutrient: {Name} (FdcId: {FdcId})", newNutrient.Name, newNutrient.FdcId);
                        }
                    }

                    // Only add new entities. EF Core will track changes for existing ones if they were attached/modified.
                    _dbContext.Nutrients.AddRange(nutrientsToUpsert);

                    // Save changes for the batch
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    // After successful save, update the in-memory dictionaries for subsequent batches
                    foreach (var newNutrient in nutrientsToUpsert)
                    {
                        existingNutrientsByFdcId[newNutrient.FdcId!] = newNutrient;
                        existingNutrientsByName[newNutrient.Name] = newNutrient;
                    }
                    // For updated items, ensure they are also in the cache if they weren't already (unlikely if retrieved from DB, but good for consistency)
                    // The current approach of loading all and then using Entry(entity).State = Modified is fine.

                    processedCount += batch.Count; // Count all records in the batch as "processed" for progress tracking
                    await _progressTracker.UpdateProgressAsync(stageName, processedCount);
                    _logger.LogInformation("Processed {Count} FDC nutrient records. Total processed: {ProcessedCount}/{TotalRecords}. Duplicates in batch: {DuplicateCount}",
                        batch.Count, processedCount, totalRecords, currentBatchDuplicates.Count);
                }

                await _progressTracker.UpdateProgressAsync(stageName, totalRecords); // Mark stage as fully processed
                _logger.LogInformation("FDC Nutrient import completed successfully. Total processed: {ProcessedCount}. Total duplicates found: {DuplicateCount}", processedCount, duplicateCount);
            }
            catch (OperationCanceledException)
            {
                //_logger.LogWarning("FDC Nutrient import was cancelled.");
                // Progress is already saved by batch
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FDC Nutrient import failed unexpectedly.");
                throw; // Re-throw to indicate failure to the calling process
            }
        }
    }
}
