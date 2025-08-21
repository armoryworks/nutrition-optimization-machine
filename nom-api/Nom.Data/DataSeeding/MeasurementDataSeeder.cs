using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Measurement;

namespace Nom.Data.DataSeeding
{
    /// <summary>
    /// Service for seeding initial measurement data.
    /// </summary>
    public class MeasurementDataSeeder
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<MeasurementDataSeeder> _logger;

        public MeasurementDataSeeder(ApplicationDbContext dbContext, ILogger<MeasurementDataSeeder> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Seeds initial measurement data if the database is empty.
        /// </summary>
        public async Task SeedAsync()
        {
            try
            {
                if (await _dbContext.MeasurementCategories.AnyAsync())
                {
                    _logger.LogInformation("Measurement data already exists, skipping seeding.");
                    return;
                }

                _logger.LogInformation("Seeding measurement data...");

                // Create measurement categories
                var massCategory = new MeasurementCategoryEntity
                {
                    Name = "Mass",
                    Description = "Units of mass/weight",
                    BaseUnitId = 0 // Will be set after creating the base unit
                };

                var volumeCategory = new MeasurementCategoryEntity
                {
                    Name = "Volume",
                    Description = "Units of volume/capacity",
                    BaseUnitId = 0 // Will be set after creating the base unit
                };

                var countCategory = new MeasurementCategoryEntity
                {
                    Name = "Count",
                    Description = "Units for counting items",
                    BaseUnitId = 0 // Will be set after creating the base unit
                };

                var temperatureCategory = new MeasurementCategoryEntity
                {
                    Name = "Temperature",
                    Description = "Units of temperature",
                    BaseUnitId = 0 // Will be set after creating the base unit
                };

                // Add categories to context
                _dbContext.MeasurementCategories.AddRange(massCategory, volumeCategory, countCategory, temperatureCategory);
                await _dbContext.SaveChangesAsync();

                // Create base measurement units
                var gram = new BaseMeasurementEntity
                {
                    Name = "Gram",
                    Description = "Metric unit of mass",
                    Symbol = "g",
                    MeasurementCategoryId = massCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m
                };

                var kilogram = new BaseMeasurementEntity
                {
                    Name = "Kilogram",
                    Description = "Metric unit of mass (1000 grams)",
                    Symbol = "kg",
                    MeasurementCategoryId = massCategory.Id,
                    IsBaseUnit = false,
                    BaseUnitConversionFactor = 1000.0m
                };

                var milliliter = new BaseMeasurementEntity
                {
                    Name = "Milliliter",
                    Description = "Metric unit of volume",
                    Symbol = "ml",
                    MeasurementCategoryId = volumeCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m
                };

                var liter = new BaseMeasurementEntity
                {
                    Name = "Liter",
                    Description = "Metric unit of volume (1000 milliliters)",
                    Symbol = "l",
                    MeasurementCategoryId = volumeCategory.Id,
                    IsBaseUnit = false,
                    BaseUnitConversionFactor = 1000.0m
                };

                var piece = new BaseMeasurementEntity
                {
                    Name = "Piece",
                    Description = "Unit for counting individual items",
                    Symbol = "pc",
                    MeasurementCategoryId = countCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m
                };

                var celsius = new BaseMeasurementEntity
                {
                    Name = "Celsius",
                    Description = "Metric unit of temperature",
                    Symbol = "°C",
                    MeasurementCategoryId = temperatureCategory.Id,
                    IsBaseUnit = true,
                    BaseUnitConversionFactor = 1.0m
                };

                // Add base units to context
                _dbContext.Measurements.AddRange(gram, kilogram, milliliter, liter, piece, celsius);
                await _dbContext.SaveChangesAsync();

                // Update categories with base unit IDs
                massCategory.BaseUnitId = gram.Id;
                volumeCategory.BaseUnitId = milliliter.Id;
                countCategory.BaseUnitId = piece.Id;
                temperatureCategory.BaseUnitId = celsius.Id;

                await _dbContext.SaveChangesAsync();

                // Create conversion rules
                var conversions = new[]
                {
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = kilogram.Id,
                        ToMeasurementId = gram.Id,
                        ConversionFactor = 1000.0m,
                        IsDirectConversion = true
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = gram.Id,
                        ToMeasurementId = kilogram.Id,
                        ConversionFactor = 0.001m,
                        IsDirectConversion = true
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = liter.Id,
                        ToMeasurementId = milliliter.Id,
                        ConversionFactor = 1000.0m,
                        IsDirectConversion = true
                    },
                    new MeasurementConversionEntity
                    {
                        FromMeasurementId = milliliter.Id,
                        ToMeasurementId = liter.Id,
                        ConversionFactor = 0.001m,
                        IsDirectConversion = true
                    }
                };

                _dbContext.MeasurementConversions.AddRange(conversions);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Measurement data seeded successfully. Created {CategoryCount} categories and {MeasurementCount} base units.",
                    await _dbContext.MeasurementCategories.CountAsync(),
                    await _dbContext.Measurements.CountAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding measurement data");
                throw;
            }
        }
    }
}
