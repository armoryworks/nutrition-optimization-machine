using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Import.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using System.Globalization;
using Nom.Data;
using Microsoft.EntityFrameworkCore;

namespace Nom.Import.Services
{
    /// <summary>
    /// Service for exporting ingredient data to CSV format for manual AI processing.
    /// </summary>
    public class CsvIngredientExportService
    {
        private readonly ILogger<CsvIngredientExportService> _logger;
        private readonly ImportSettings _importSettings;
        private readonly ApplicationDbContext _dbContext;

        public CsvIngredientExportService(
            ILogger<CsvIngredientExportService> logger,
            IOptions<ImportSettings> importSettings,
            ApplicationDbContext dbContext)
        {
            _logger = logger;
            _importSettings = importSettings.Value;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Exports all ingredients to CSV format for manual AI processing.
        /// </summary>
        public async Task<string> ExportIngredientsToCsvAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting ingredient export to CSV...");

            try
            {
                // Ensure export directory exists
                var exportDirectory = Path.Combine(_importSettings.SourceDirectory, "exports");
                Directory.CreateDirectory(exportDirectory);

                var csvFilePath = Path.Combine(exportDirectory, $"ingredients_for_ai_enhancement_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                
                _logger.LogInformation("Exporting ingredients to CSV: {FilePath}", csvFilePath);

                // Query ingredients from database
                var ingredients = await _dbContext.Ingredients
                    .AsNoTracking()
                    .Select(i => new IngredientExportModel
                    {
                        Id = i.Id,
                        Name = i.Name,
                        Description = i.Description ?? "",
                        FdcId = i.FdcId ?? "",
                        FdcDataType = i.FdcDataType
                    })
                    .ToListAsync(cancellationToken);

                // Write to CSV
                using var writer = new StreamWriter(csvFilePath);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                
                await csv.WriteRecordsAsync(ingredients, cancellationToken);

                _logger.LogInformation("Successfully exported {Count} ingredients to CSV", ingredients.Count);
                
                return csvFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ingredient export to CSV");
                throw;
            }
        }

        /// <summary>
        /// Exports a subset of ingredients for testing.
        /// </summary>
        public async Task<string> ExportSampleIngredientsToCsvAsync(int sampleSize = 50, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting sample ingredient export to CSV (sample size: {SampleSize})...", sampleSize);

            try
            {
                // Ensure export directory exists
                var exportDirectory = Path.Combine(_importSettings.SourceDirectory, "exports");
                Directory.CreateDirectory(exportDirectory);

                var csvFilePath = Path.Combine(exportDirectory, $"sample_ingredients_for_ai_enhancement_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                
                _logger.LogInformation("Exporting sample ingredients to CSV: {FilePath}", csvFilePath);

                // Query a sample of ingredients from database
                var ingredients = await _dbContext.Ingredients
                    .AsNoTracking()
                    .Take(sampleSize)
                    .Select(i => new IngredientExportModel
                    {
                        Id = i.Id,
                        Name = i.Name,
                        Description = i.Description ?? "",
                        FdcId = i.FdcId ?? "",
                        FdcDataType = i.FdcDataType
                    })
                    .ToListAsync(cancellationToken);

                // Write to CSV
                using var writer = new StreamWriter(csvFilePath);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                
                await csv.WriteRecordsAsync(ingredients, cancellationToken);

                _logger.LogInformation("Successfully exported {Count} sample ingredients to CSV", ingredients.Count);
                
                return csvFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sample ingredient export to CSV");
                throw;
            }
        }
    }
} 