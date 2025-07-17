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
using Microsoft.Extensions.DependencyInjection; // Required for IServiceScopeFactory

namespace Nom.Import.Data.Fdc.Importers
{
    /// <summary>
    /// Imports FDC food_nutrient data into the IngredientNutrientEntity table.
    /// Handles mapping, lookups for Ingredient and Nutrient IDs, and upsert logic.
    /// Uses EFCore.BulkExtensions for set-based operations.
    /// </summary>
    public class FdcFoodNutrientImporter
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FdcFoodNutrientImporter> _logger;
        private readonly CsvDataLoader<FdcFoodNutrientCsv> _csvDataLoader;
        private readonly ImportProgressTracker _progressTracker;
        private readonly ImportConfig _importConfig;
        private readonly ImportReportGenerator _reportGenerator; // NEW: Inject report generator


        // Cached Ingredient and Nutrient IDs for efficient lookups
        private Dictionary<string, long> _ingredientFdcIdToIdMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (long Id, long DefaultMeasurementTypeId, string UnitName)> _nutrientFdcIdToInfoMap = new Dictionary<string, (long Id, long DefaultMeasurementTypeId, string UnitName)>(StringComparer.OrdinalIgnoreCase);
        // Cached MeasurementType IDs for efficient lookups, similar to FdcNutrientImporter
        private Dictionary<string, long> _measurementTypeIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);


        public FdcFoodNutrientImporter(
            IServiceScopeFactory scopeFactory,
            ILogger<FdcFoodNutrientImporter> logger,
            CsvDataLoader<FdcFoodNutrientCsv> csvDataLoader,
            ImportProgressTracker progressTracker,
            IOptions<ImportConfig> importConfig,
            ImportReportGenerator reportGenerator) // NEW: Add to constructor
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _csvDataLoader = csvDataLoader;
            _progressTracker = progressTracker;
            _importConfig = importConfig.Value;
            _reportGenerator = reportGenerator; // Assign
        }

        /// <summary>
        /// Initializes necessary reference data (Ingredient and Nutrient IDs) from the database.
        /// </summary>
        private async Task InitializeReferenceDataAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing Ingredient and Nutrient reference data for food nutrients...");

            // Fetch all Ingredients with FdcId
            var ingredients = await dbContext.Ingredients
                .AsNoTracking()
                .Where(i => i.FdcId != null && i.FdcId != string.Empty)
                .Select(i => new { i.Id, i.FdcId })
                .ToListAsync(cancellationToken);

            foreach (var ing in ingredients)
            {
                _ingredientFdcIdToIdMap[ing.FdcId!] = ing.Id;
            }
            _logger.LogInformation("Cached {Count} Ingredient FdcIds.", _ingredientFdcIdToIdMap.Count);


            // Fetch all Nutrients with FdcId and their DefaultMeasurementTypeId and Name (for unit lookup)
            // IMPORTANT: Select n.DefaultMeasurementType.Name to get the actual unit name, not the nutrient's name.
            var nutrients = await dbContext.Nutrients
                .AsNoTracking()
                .Include(n => n.DefaultMeasurementType) // Eager load DefaultMeasurementType
                .Where(n => n.FdcId != null && n.FdcId != string.Empty)
                .Select(n => new { n.Id, n.FdcId, n.DefaultMeasurementTypeId, DefaultUnitName = n.DefaultMeasurementType!.Name }) // Select the unit's name
                .ToListAsync(cancellationToken);

            foreach (var nut in nutrients)
            {
                // Store the DefaultUnitName (e.g., "g", "mg", "kcal") in the tuple
                _nutrientFdcIdToInfoMap[nut.FdcId!] = (nut.Id, nut.DefaultMeasurementTypeId, nut.DefaultUnitName);
            }
            _logger.LogInformation("Cached {Count} Nutrient FdcIds with default measurement types and names.", _nutrientFdcIdToInfoMap.Count);

            // Also initialize MeasurementType IDs, similar to FdcNutrientImporter
            // MeasurementTypeViewEntity already filters by GroupId, so no explicit .Where() is needed here.
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

            // NEW INFERENCES/MAPPINGS (consistent with FdcNutrientImporter)
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
        /// </summary>
        /// <param name="unitName">The name of the unit (e.g., "g", "mg", "mcg"). This parameter should be the canonical unit name from the Nutrient entity.</param>
        /// <param name="originalAmount">The original amount associated with the unit.</param>
        /// <returns>A tuple containing the MeasurementType ID and the potentially converted amount.</returns>
        private (long MeasurementTypeId, decimal ConvertedAmount) GetMeasurementTypeIdAndAmount(string unitName, decimal originalAmount)
        {
            // Handle kJ to kcal conversion
            if (unitName.Equals("kJ", StringComparison.OrdinalIgnoreCase))
            {
                // 1 kcal = 4.184 kJ
                // Amount in kcal = Amount in kJ / 4.184
                if (_measurementTypeIds.TryGetValue("kcal", out long kcalId))
                {
                    _logger.LogDebug("Converting {OriginalAmount} kJ to kcal.", originalAmount);
                    return (kcalId, originalAmount / 4.184m);
                }
                else
                {
                    string warningMessage = $"kcal MeasurementType not found for kJ conversion. Using 'unknown' for {originalAmount} kJ.";
                    _logger.LogWarning(warningMessage);
                    _reportGenerator.RecordWarning(warningMessage);
                    return (_measurementTypeIds.TryGetValue("unknown", out long unknownId) ? unknownId : 0, originalAmount);
                }
            }

            // Handle UG to mcg mapping
            if (unitName.Equals("UG", StringComparison.OrdinalIgnoreCase))
            {
                if (_measurementTypeIds.TryGetValue("mcg", out long mcgId))
                {
                    return (mcgId, originalAmount); // No amount conversion needed, just unit mapping
                }
            }

            // Handle IU mapping. No conversion of amount here, just unit ID.
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
        /// Imports FDC food_nutrient data from the specified CSV file.
        /// </summary>
        /// <param name="filePath">The full path to the FDC food_nutrient.csv file.</param>
        /// <param name="totalRecords">The total number of records expected in the CSV.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ImportAsync(string filePath, long totalRecords, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting FDC Food Nutrient import from: {FilePath}", filePath);

            string stageName = "FDC_Food_Nutrients_Import";
            _progressTracker.SetTotalRecords(stageName, totalRecords); // Report total discovered
            long duplicateCount = 0;
            long skippedCount = 0;

            // Create a CancellationTokenSource linked to the main cancellationToken
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _importConfig.MaxParallelism, CancellationToken = linkedCts.Token };

            try
            {
                // Initialize reference data once per import run (not per batch)
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
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<FdcFoodNutrientImporter>>(); // Get logger for this scope

                    var ingredientNutrientsToProcess = new List<IngredientNutrientEntity>();
                    var currentBatchSkipped = new List<FdcFoodNutrientCsv>(); // For records that are invalid or missing lookups

                    // Use a HashSet to track unique (IngredientId, NutrientId) pairs within the current batch
                    var uniqueBatchKeys = new HashSet<(long IngredientId, long NutrientId)>();

                    foreach (var csvRecord in batch)
                    {
                        // Check for cancellation within the inner loop
                        innerCancellationToken.ThrowIfCancellationRequested();

                        // Basic validation: skip if FdcId, NutrientId, or Amount is empty
                        if (string.IsNullOrWhiteSpace(csvRecord.FdcId) ||
                            string.IsNullOrWhiteSpace(csvRecord.NutrientId) ||
                            string.IsNullOrWhiteSpace(csvRecord.Amount))
                        {
                            string reason = "Empty FdcId, NutrientId, or Amount";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            logger.LogWarning("Skipping FDC food nutrient record due to {Reason}. Record FdcId: {FdcId}, NutrientId: {NutrientId}, Amount: {Amount}",
                                reason, csvRecord.FdcId, csvRecord.NutrientId, csvRecord.Amount);
                            continue;
                        }

                        // Parse original Amount
                        if (!decimal.TryParse(csvRecord.Amount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal originalAmount) || originalAmount < 0)
                        {
                            string reason = $"Invalid or negative amount '{csvRecord.Amount}'";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            logger.LogWarning("Skipping FDC food nutrient record due to {Reason}. Record FdcId: {FdcId}, NutrientId: {NutrientId}",
                                reason, csvRecord.FdcId, csvRecord.NutrientId);
                            skippedCount++;
                            currentBatchSkipped.Add(csvRecord);
                            continue;
                        }

                        // Lookup IngredientId and NutrientId from cached maps
                        if (!_ingredientFdcIdToIdMap.TryGetValue(csvRecord.FdcId.Trim(), out long ingredientId))
                        {
                            string reason = $"Ingredient with FdcId '{csvRecord.FdcId}' not found in database";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            skippedCount++;
                            currentBatchSkipped.Add(csvRecord);
                            continue;
                        }

                        if (!_nutrientFdcIdToInfoMap.TryGetValue(csvRecord.NutrientId.Trim(), out var nutrientInfo))
                        {
                            string reason = $"Nutrient with FdcId '{csvRecord.NutrientId}' not found in database";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            skippedCount++;
                            currentBatchSkipped.Add(csvRecord);
                            continue;
                        }

                        long nutrientId = nutrientInfo.Id;
                        string nutrientUnitName = nutrientInfo.UnitName; // This will now correctly be the unit name (e.g., "g", "mg")

                        // Get MeasurementType ID and potentially converted amount
                        var (measurementTypeId, convertedAmount) = GetMeasurementTypeIdAndAmount(nutrientUnitName, originalAmount);

                        // Check for duplicates within the current batch before adding to the list for bulk operation
                        if (!uniqueBatchKeys.Add((ingredientId, nutrientId)))
                        {
                            string reason = $"Duplicate IngredientNutrient ({ingredientId}, {nutrientId}) in batch";
                            _progressTracker.RecordSkipped(stageName, reason); // Report skipped
                            duplicateCount++;
                            currentBatchSkipped.Add(csvRecord); // Consider as skipped/duplicate for batch stats
                            continue;
                        }

                        // Create the entity. BulkInsertOrUpdate will handle if it's an insert or update.
                        var ingredientNutrient = new IngredientNutrientEntity
                        {
                            IngredientId = ingredientId,
                            NutrientId = nutrientId,
                            Amount = convertedAmount, // Use the converted amount
                            MeasurementTypeId = measurementTypeId, // Use the resolved ID
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
                        PropertiesToExcludeOnUpdate = new List<string> {
                            nameof(IngredientNutrientEntity.Id),
                            nameof(IngredientNutrientEntity.CreatedDate),
                            nameof(IngredientNutrientEntity.CreatedByPersonId)
                        }
                        // Removed DisableTemporaryTable = true as it's not supported by your EFCore.BulkExtensions version
                    };

                    // Only perform bulk operation if there are entities to process
                    if (ingredientNutrientsToProcess.Any())
                    {
                        try
                        {
                            // Perform bulk upsert for the batch
                            await dbContext.BulkInsertOrUpdateAsync(ingredientNutrientsToProcess, bulkConfig, cancellationToken: innerCancellationToken);
                            _progressTracker.RecordImported(stageName, ingredientNutrientsToProcess.Count); // Report imported count
                        }
                        catch (OperationCanceledException)
                        {
                            // Re-throw if cancellation was requested
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // Log the error and signal cancellation for other tasks
                            string errorMessage = $"FDC Food Nutrient import failed unexpectedly in batch. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                            logger.LogError(ex, errorMessage);
                            _reportGenerator.RecordError(errorMessage);
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
                        logger.LogInformation("No ingredient nutrients to process for this batch.");
                        _progressTracker.RecordImported(stageName, 0); // No records imported, but batch processed
                    }

                    logger.LogInformation("Processed {Count} FDC food nutrient records in a parallel task. Total processed (approx): {ProcessedCount}/{TotalRecords}. Skipped/Duplicates in batch: {SkippedCount}",
                        batch.Count, _progressTracker.GetLastProcessedOffset(stageName), totalRecords, currentBatchSkipped.Count);
                });

                await _progressTracker.UpdateProgressAsync(stageName, totalRecords); // Mark stage as fully processed
                _logger.LogInformation("FDC Food Nutrient import completed successfully. Total processed: {ProcessedCount}. Total duplicates found: {DuplicateCount}. Total skipped: {SkippedCount}", _progressTracker.GetLastProcessedOffset(stageName), duplicateCount, skippedCount);
            }
            catch (OperationCanceledException ex)
            {
                string errorMessage = $"FDC Food Nutrient import was cancelled. Exception: {ex.Message}";
                _logger.LogWarning(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage);
            }
            catch (Exception ex)
            {
                string errorMessage = $"FDC Food Nutrient import failed unexpectedly during overall process. Exception details: {ex.Message}. Inner Exception: {ex.InnerException?.Message}";
                _logger.LogError(ex, errorMessage);
                _reportGenerator.RecordFatalError(errorMessage);
                if (ex.InnerException != null)
                {
                    _logger.LogError(ex.InnerException, "Inner Exception Stack Trace:");
                }
                throw; // Re-throw to indicate failure to the calling process
            }
        }
    }
}
