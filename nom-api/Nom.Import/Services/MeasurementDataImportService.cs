using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Measurement;

namespace Nom.Import.Services
{
    /// <summary>
    /// Service for importing and seeding measurement data.
    /// </summary>
    public class MeasurementDataImportService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<MeasurementDataImportService> _logger;

        public MeasurementDataImportService(
            ApplicationDbContext dbContext,
            ILogger<MeasurementDataImportService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Seeds initial measurement categories and base units.
        /// </summary>
        public async Task SeedInitialMeasurementDataAsync()
        {
            try
            {
                _logger.LogInformation("Starting measurement data seeding...");

                // Check if data already exists
                if (await _dbContext.MeasurementCategories.AnyAsync())
                {
                    _logger.LogInformation("Measurement data already exists, skipping seeding.");
                    return;
                }

                // Create measurement categories
                var massCategory = new MeasurementCategoryEntity
                {
                    Name = "Mass",
                    Description = "Units of mass/weight measurement",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                var volumeCategory = new MeasurementCategoryEntity
                {
                    Name = "Volume",
                    Description = "Units of volume/capacity measurement",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                var countCategory = new MeasurementCategoryEntity
                {
                    Name = "Count",
                    Description = "Units for counting items",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                var temperatureCategory = new MeasurementCategoryEntity
                {
                    Name = "Temperature",
                    Description = "Units of temperature measurement",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                // Add categories to context (without BaseUnitId for now)
                _dbContext.MeasurementCategories.AddRange(massCategory, volumeCategory, countCategory, temperatureCategory);
                await _dbContext.SaveChangesAsync();

                // Create base measurements for each category
                var gram = new BaseMeasurementEntity
                {
                    Name = "Gram",
                    Description = "Base unit of mass in metric system",
                    Symbol = "g",
                    MeasurementCategoryId = massCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                var milliliter = new BaseMeasurementEntity
                {
                    Name = "Milliliter",
                    Description = "Base unit of volume in metric system",
                    Symbol = "ml",
                    MeasurementCategoryId = volumeCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                var piece = new BaseMeasurementEntity
                {
                    Name = "Piece",
                    Description = "Base unit for counting items",
                    Symbol = "pc",
                    MeasurementCategoryId = countCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                var celsius = new BaseMeasurementEntity
                {
                    Name = "Celsius",
                    Description = "Base unit of temperature in metric system",
                    Symbol = "°C",
                    MeasurementCategoryId = temperatureCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                };

                // Add base measurements to context
                _dbContext.Measurements.AddRange(gram, milliliter, piece, celsius);
                await _dbContext.SaveChangesAsync();

                // Now update the categories with the base unit references
                massCategory.BaseUnitId = gram.Id;
                volumeCategory.BaseUnitId = milliliter.Id;
                countCategory.BaseUnitId = piece.Id;
                temperatureCategory.BaseUnitId = celsius.Id;

                // Update the categories in the database
                _dbContext.MeasurementCategories.UpdateRange(massCategory, volumeCategory, countCategory, temperatureCategory);
                await _dbContext.SaveChangesAsync();

                // Create common measurement units
                var commonMeasurements = new List<BaseMeasurementEntity>
                {
                    // Mass units
                    new BaseMeasurementEntity
                    {
                        Name = "Kilogram",
                        Description = "1000 grams",
                        Symbol = "kg",
                        MeasurementCategoryId = massCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 1000.0m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new BaseMeasurementEntity
                    {
                        Name = "Pound",
                        Description = "Imperial unit of mass",
                        Symbol = "lb",
                        MeasurementCategoryId = massCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 453.592m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new BaseMeasurementEntity
                    {
                        Name = "Ounce",
                        Description = "Imperial unit of mass",
                        Symbol = "oz",
                        MeasurementCategoryId = massCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 28.3495m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },

                    // Volume units
                    new BaseMeasurementEntity
                    {
                        Name = "Liter",
                        Description = "1000 milliliters",
                        Symbol = "L",
                        MeasurementCategoryId = volumeCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 1000.0m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new BaseMeasurementEntity
                    {
                        Name = "Cup",
                        Description = "US customary unit of volume",
                        Symbol = "cup",
                        MeasurementCategoryId = volumeCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 236.588m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new BaseMeasurementEntity
                    {
                        Name = "Tablespoon",
                        Description = "US customary unit of volume",
                        Symbol = "tbsp",
                        MeasurementCategoryId = volumeCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 14.7868m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new BaseMeasurementEntity
                    {
                        Name = "Teaspoon",
                        Description = "US customary unit of volume",
                        Symbol = "tsp",
                        MeasurementCategoryId = volumeCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 4.92892m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },

                    // Count units
                    new BaseMeasurementEntity
                    {
                        Name = "Dozen",
                        Description = "12 pieces",
                        Symbol = "doz",
                        MeasurementCategoryId = countCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 12.0m,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },

                    // Temperature units
                    new BaseMeasurementEntity
                    {
                        Name = "Fahrenheit",
                        Description = "Imperial unit of temperature",
                        Symbol = "°F",
                        MeasurementCategoryId = temperatureCategory.Id,
                        IsBaseUnit = false,
                        BaseUnitConversionFactor = 1.0m, // Will be handled by conversion rules
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    }
                };

                _dbContext.Measurements.AddRange(commonMeasurements);
                await _dbContext.SaveChangesAsync();

                // Create conversion rules
                var conversions = new List<MeasurementConversionEntity>
                {
                    // Mass conversions
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = gram.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "kg").Id,
                        ConversionFactor = 0.001m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = gram.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "lb").Id,
                        ConversionFactor = 0.00220462m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = gram.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "oz").Id,
                        ConversionFactor = 0.035274m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },

                    // Volume conversions
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = milliliter.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "L").Id,
                        ConversionFactor = 0.001m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = milliliter.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "cup").Id,
                        ConversionFactor = 0.00422675m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = milliliter.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "tbsp").Id,
                        ConversionFactor = 0.067628m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = milliliter.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "tsp").Id,
                        ConversionFactor = 0.202884m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },

                    // Count conversions
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = piece.Id,
                        ToMeasurementId = commonMeasurements.First(m => m.Symbol == "doz").Id,
                        ConversionFactor = 0.0833333m,
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    },

                    // Temperature conversions (Fahrenheit to Celsius)
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = commonMeasurements.First(m => m.Symbol == "°F").Id,
                        ToMeasurementId = celsius.Id,
                        ConversionFactor = 0.555556m,
                        Offset = -17.7778m,
                        Formula = "(°F - 32) × 5/9",
                        IsDirectConversion = true,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    }
                };

                _dbContext.MeasurementConversions.AddRange(conversions);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Measurement data seeding completed successfully. Created {CategoryCount} categories, {MeasurementCount} measurements, and {ConversionCount} conversion rules.",
                    await _dbContext.MeasurementCategories.CountAsync(),
                    await _dbContext.Measurements.CountAsync(),
                    await _dbContext.MeasurementConversions.CountAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding measurement data");
                throw;
            }
        }

        /// <summary>
        /// Imports measurement data from external sources.
        /// </summary>
        public async Task ImportMeasurementDataAsync(string sourcePath)
        {
            try
            {
                _logger.LogInformation("Starting measurement data import from {SourcePath}", sourcePath);

                // TODO: Implement import logic for external measurement data sources
                // This could include CSV files, API calls, or other data sources

                _logger.LogInformation("Measurement data import completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing measurement data from {SourcePath}", sourcePath);
                throw;
            }
        }

        /// <summary>
        /// Exports measurement data to external formats.
        /// </summary>
        public async Task ExportMeasurementDataAsync(string targetPath, string format = "csv")
        {
            try
            {
                _logger.LogInformation("Starting measurement data export to {TargetPath} in {Format} format", targetPath, format);

                // TODO: Implement export logic for measurement data
                // This could include CSV, JSON, or other export formats

                _logger.LogInformation("Measurement data export completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting measurement data to {TargetPath}", targetPath);
                throw;
            }
        }
    }
}
