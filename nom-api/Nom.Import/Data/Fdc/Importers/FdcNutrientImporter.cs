using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Nutrient; // For NutrientEntity
using Nom.Data.Reference; // For ReferenceEntity, ReferenceDiscriminatorEnum, MeasurementTypeViewEntity
using Nom.Import.Data.Fdc.CsvModels; // For FdcNutrientCsv
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
using System.Globalization; // For CultureInfo.InvariantCulture

namespace Nom.Import.Data.Fdc.Importers
{
    /// <summary>
    /// Imports nutrient data from FDC nutrient.csv into the NutrientEntity table.
    /// Handles mapping, deduplication, and upsert logic.
    /// </summary>
    public class FdcNutrientImporter
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FdcNutrientImporter> _logger;
        private readonly CsvDataLoader<FdcNutrientCsv> _csvDataLoader; // Correctly uses FdcNutrientCsv
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportConfig _importConfig;
        private readonly ImportReportGenerator _reportGenerator;

        // Cached MeasurementType IDs for efficient lookups (this is correct for this importer)
        private Dictionary<string, long> _measurementTypeIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public FdcNutrientImporter(
            IServiceScopeFactory scopeFactory,
            ILogger<FdcNutrientImporter> logger,
            CsvDataLoader<FdcNutrientCsv> csvDataLoader,
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
        /// Initializes necessary reference data (MeasurementType IDs) from the database.
        /// This is needed to map FDC unit names to internal MeasurementType IDs.
        /// </summary>
        private async Task InitializeReferenceDataAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing MeasurementType reference data for nutrients...");

            var measurementTypes = await dbContext.MeasurementTypes
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!measurementTypes.Any())
            {
                string errorMessage = "No MeasurementType reference data found in the database. Please ensure initial reference data is seeded and the ReferenceGroupView is correctly defined and accessible for MeasurementTypeViewEntity.";
                _logger.LogError(errorMessage);
                _reportGenerator.RecordFatalError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            foreach (var mtv in measurementTypes)
            {
                _measurementTypeIds[mtv.ReferenceName] = mtv.ReferenceId;
            }

            // Ensure 'unknown' is mapped, crucial for fallback.
            if (!_measurementTypeIds.ContainsKey("unknown"))
            {
                var unknownRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("unknown", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (unknownRef != null)
                {
                    _measurementTypeIds["unknown"] = unknownRef.Id;
                }
                else
                {
                    string warningMessage = "MeasurementType 'unknown' not found in reference data. This may lead to errors if CSV contains unmappable units and 'unknown' is required.";
                    _logger.LogWarning(warningMessage);
                    _reportGenerator.RecordWarning(warningMessage);
                }
            }

            // Add common variations/aliases if not already present
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

            // NEW INFERENCES/MAPPINGS (consistent with FdcFoodNutrientImporter)
            // Map 'UG' to 'mcg' (microgram)
            if (_measurementTypeIds.ContainsKey("mcg") && !_measurementTypeIds.ContainsKey("UG")) _measurementTypeIds["UG"] = _measurementTypeIds["mcg"];

            // Add 'IU' if it exists in your reference data, or map to 'unknown' if not.
            if (!_measurementTypeIds.ContainsKey("IU"))
            {
                var iuRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("IU", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (iuRef != null)
                {
                    _measurementTypeIds["IU"] = iuRef.Id;
                }
                else
                {
                    string warningMessage = "MeasurementType 'IU' not found in reference data. Mapping 'IU' to 'unknown'. Please consider adding 'IU' as a specific measurement type.";
                    _logger.LogWarning(warningMessage);
                    _reportGenerator.RecordWarning(warningMessage);
                    if (_measurementTypeIds.ContainsKey("unknown")) _measurementTypeIds["IU"] = _measurementTypeIds["unknown"];
                }
            }

            // Add 'kJ' if it exists, otherwise map to 'kcal' and convert amount.
            if (!_measurementTypeIds.ContainsKey("kJ"))
            {
                var kJRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("kJ", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (kJRef != null)
                {
                    _measurementTypeIds["kJ"] = kJRef.Id;
                }
                else
                {
                    string warningMessage = "MeasurementType 'kJ' not found in reference data. Will attempt to map to 'kcal' or 'unknown'. Please consider adding 'kJ' as a specific measurement type.";
                    _logger.LogWarning(warningMessage);
                    _reportGenerator.RecordWarning(warningMessage);
                }
            }

            // Add specific derived units if they exist in your reference data
            if (!_measurementTypeIds.ContainsKey("mcg_RE"))
            {
                var mcgReRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("mcg_RE", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (mcgReRef != null) _measurementTypeIds["mcg_RE"] = mcgReRef.Id;
            }
            if (!_measurementTypeIds.ContainsKey("MG_ATE"))
            {
                var mgAteRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("mg_ATE", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (mgAteRef != null) _measurementTypeIds["MG_ATE"] = mgAteRef.Id;
            }
            if (!_measurementTypeIds.ContainsKey("UMOL_TE"))
            {
                var umolTeRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("umol_TE", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (umolTeRef != null) _measurementTypeIds["UMOL_TE"] = umolTeRef.Id;
            }

            // Add pH and SP_GR if they exist in your reference data
            if (!_measurementTypeIds.ContainsKey("pH"))
            {
                var pHRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("pH", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (pHRef != null) _measurementTypeIds["pH"] = pHRef.Id;
            }
            if (!_measurementTypeIds.ContainsKey("SP_GR"))
            {
                var spGrRef = await dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("SP_GR", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (spGrRef != null) _measurementTypeIds["SP_GR"] = spGrRef.Id;
            }

            _logger.LogInformation("MeasurementType reference data initialized. Found {Count} types.", _measurementTypeIds.Count);
        }

        /// <summary>
        /// Gets the MeasurementType ID for a given unit name.
        /// Handles specific unit conversions (e.g., kJ to kcal) and mappings.
        /// For NutrientEntity, the originalAmount is not directly used for the nutrient itself,
        /// but this method is kept consistent with FdcFoodNutrientImporter for unit mapping logic.
        /// </summary>
        /// <param name="unitName">The name of the unit (e.g., "g", "mg", "mcg").</param>
        /// <param name="originalAmount">The original amount (not directly used for the Nutrient entity itself).</param>
        /// <returns>A tuple containing the MeasurementType ID and the potentially converted amount (original amount passed through).</returns>
        private (long MeasurementTypeId, decimal ConvertedAmount) GetMeasurementTypeIdAndAmount(string unitName, decimal originalAmount)
        {
            // Handle kJ to kcal conversion
            if (unitName.Equals("kJ", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("kcal", out long kcalId))
                {
                    // For NutrientEntity, we just map the unit, not convert the amount.
                    // The conversion logic is more relevant for IngredientNutrientEntity.
                    return (kcalId, originalAmount); // Pass originalAmount through for consistency in signature
                }
            }

            // Handle UG to mcg mapping
            if (unitName.Equals("UG", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("mcg", out long mcgId))
                {
                    return (mcgId, originalAmount);
                }
            }

            // Handle IU mapping.
            if (unitName.Equals("IU", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("IU", out long iuId))
                {
                    return (iuId, originalAmount);
                }
            }

            // Handle derived units like MCG_RE, MG_ATE, UMOL_TE
            if (unitName.Equals("MCG_RE", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("mcg_RE", out long mcgReId))
                {
                    return (mcgReId, originalAmount);
                }
            }
            if (unitName.Equals("MG_ATE", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("MG_ATE", out long mgAteId))
                {
                    return (mgAteId, originalAmount);
                }
            }
            if (unitName.Equals("UMOL_TE", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("UMOL_TE", out long umolTeId))
                {
                    return (umolTeId, originalAmount);
                }
            }
            // Handle pH
            if (unitName.Equals("PH", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("pH", out long phId))
                {
                    return (phId, originalAmount);
                }
            }
            // Handle SP_GR
            if (unitName.Equals("SP_GR", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("SP_GR", out long spGrId))
                {
                    return (spGrId, originalAmount);
                }
            }

            // For all other units, perform a direct case-insensitive lookup
            if (_measurementTypeIds.TryGetValue(unitName, out long id))
            {
                return (id, originalAmount);
            }

            // Fallback to 'unknown' MeasurementType if specific unit not found
            string unitWarningMessage = $"Measurement unit '{unitName}' not found. Using 'unknown' MeasurementType (ID: {_measurementTypeIds["unknown"]}).";
            _logger.LogWarning(unitWarningMessage);
            _reportGenerator.RecordWarning(unitWarningMessage);
            if (_measurementTypeIds.TryGetValue("unknown", out long fallbackUnknownId))
            {
                return (fallbackUnknownId, originalAmount);
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

            string stageName = "FDC_Nutrients_Import";
            _progressTracker.SetTotalRecords(stageName, totalRecords); // Report total discovered
            long duplicateNameCount = 0; // Track duplicates by Name within batches

            // Create a CancellationTokenSource linked to the main cancellationToken
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _importConfig.MaxParallelism, CancellationToken = linkedCts.Token };

            try
            {
                // Initialize reference data once per import run (not per batch)
                // Use a temporary scope for initialization if it needs DbContext
                using var initScope = _scopeFactory.CreateScope();
                var initDbContext = initScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await InitializeReferenceDataAsync(initDbContext, linkedCts.Token);


                await Parallel.ForEachAsync(_csvDataLoader.LoadCsvInBatchesAsync(filePath, _importConfig.BatchSize, linkedCts.Token), parallelOptions, async (batch, innerCancellationToken) =>
                {
                    // Check for cancellation at the start of each batch processing task
                    innerCancellationToken.ThrowIfCancellationRequested();

                    // Create a new scope and DbContext for each parallel task
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<FdcNutrientImporter>>(); // Get logger for this scope

                    var nutrientsToUpsert = new List<NutrientEntity>();
                    // Use a HashSet to track unique nutrient names within the current batch
                    var uniqueBatchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var csvRecord in batch)
                    {
                        // Check for cancellation within the inner loop
                        innerCancellationToken.ThrowIfCancellationRequested();

                        // Basic validation: skip if Id or Name is empty (using Id and Name from FdcNutrientCsv)
                        if (string.IsNullOrWhiteSpace(csvRecord.Id) || string.IsNullOrWhiteSpace(csvRecord.Name))
                        {
                            string reason = "Empty Id or Name";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            logger.LogWarning("Skipping FDC nutrient record due to {Reason}. Record: {RecordId}, {RecordName}", reason, csvRecord.Id, csvRecord.Name);
                            continue;
                        }

                        var trimmedName = csvRecord.Name.Trim();
                        var trimmedId = csvRecord.Id.Trim(); // This is the FDC ID for the nutrient

                        // Deduplicate within the current batch by Name (case-insensitive)
                        if (!uniqueBatchNames.Add(trimmedName))
                        {
                            string reason = $"Duplicate nutrient name '{trimmedName}' in batch";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            duplicateNameCount++; // Increment local duplicate counter
                            continue;
                        }

                        // Get MeasurementType ID for the nutrient's default unit
                        // Pass 0 for originalAmount as it's not relevant for the nutrient definition itself
                        var (measurementTypeId, _) = GetMeasurementTypeIdAndAmount(csvRecord.UnitName.Trim(), 0);

                        // Create new NutrientEntity (it will be updated if a match is found by BulkExtensions)
                        var newNutrient = new NutrientEntity
                        {
                            Name = trimmedName,
                            Description = null, // No description in nutrient.csv for the nutrient itself
                            DefaultMeasurementTypeId = measurementTypeId, // Use the resolved ID
                            FdcId = trimmedId, // This is the FDC ID for the nutrient
                            CreatedDate = DateTime.UtcNow, // These will be overwritten by EF Core's audit if entity is new
                            CreatedByPersonId = _importConfig.SystemPersonId,
                            LastModifiedDate = DateTime.UtcNow,
                            LastModifiedByPersonId = _importConfig.SystemPersonId
                        };
                        nutrientsToUpsert.Add(newNutrient);
                    }

                    // Configure BulkConfig to use Name as the unique key for upsert.
                    // This aligns with the "IX_Nutrient_Name" constraint.
                    var bulkConfig = new BulkConfig
                    {
                        UpdateByProperties = new List<string> { nameof(NutrientEntity.Name) },
                        PropertiesToExcludeOnUpdate = new List<string> {
                            nameof(NutrientEntity.Id), // Primary key, never update via upsert
                            nameof(NutrientEntity.CreatedDate),
                            nameof(NutrientEntity.CreatedByPersonId)
                        }
                        // Removed DisableTemporaryTable = true
                    };

                    if (nutrientsToUpsert.Any())
                    {
                        try
                        {
                            // Perform bulk upsert for the batch
                            await dbContext.BulkInsertOrUpdateAsync(nutrientsToUpsert, bulkConfig, cancellationToken: innerCancellationToken);
                            _progressTracker.RecordImported(stageName, nutrientsToUpsert.Count); // Report imported count
                        }
                        catch (OperationCanceledException)
                        {
                            // Re-throw if cancellation was requested
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // Log the error and signal cancellation for other tasks
                            string errorMessage = $"FDC Nutrient import failed unexpectedly in batch. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                            logger.LogError(ex, errorMessage);
                            _reportGenerator.RecordError(errorMessage); // Record the error
                            if (ex.InnerException != null)
                            {
                                logger.LogError(ex.InnerException, "Inner Exception Stack Trace:");
                            }
                            linkedCts.Cancel(); // Signal cancellation to other tasks
                            throw; // Re-throw to propagate the exception out of Parallel.ForEachAsync
                        }
                    }
                    else
                    {
                        logger.LogInformation("No FDC nutrient records to process for this batch.");
                        _progressTracker.RecordImported(stageName, 0); // No records imported, but batch processed
                    }

                    logger.LogInformation("Processed {Count} FDC nutrient records in a parallel task. Total processed (approx): {ProcessedCount}/{TotalRecords}. Duplicates by Name in batch: {BatchDuplicates}",
                        batch.Count, _progressTracker.GetLastProcessedOffset(stageName), totalRecords, uniqueBatchNames.Count - nutrientsToUpsert.Count);
                });

                await _progressTracker.UpdateProgressAsync(stageName, totalRecords); // Final update to ensure total is correct
                _logger.LogInformation("FDC Nutrient import completed successfully. Total processed: {ProcessedCount}. Total duplicates by Name found (in batches): {DuplicateNameCount}", _progressTracker.GetLastProcessedOffset(stageName), duplicateNameCount);
            }
            catch (OperationCanceledException ex)
            {
                string errorMessage = $"FDC Nutrient import was cancelled. Exception: {ex.Message}";
                _logger.LogWarning(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage); // Record as fatal if cancelled due to an upstream error
            }
            catch (Exception ex)
            {
                string errorMessage = $"FDC Nutrient import failed unexpectedly during overall process. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                _logger.LogError(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage); // Record as fatal
                if (ex.InnerException != null)
                {
                    _logger.LogError(ex.InnerException, "Inner Exception Stack Trace:");
                }
                throw; // Re-throw to indicate failure to the calling process
            }
        }
    }
}
