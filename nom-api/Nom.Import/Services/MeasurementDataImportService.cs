using System.Globalization;
using System.Text.Json;
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
        /// Imports measurement data from a CSV file.
        /// Expected CSV format: Name,Symbol,Category,Description,IsBaseUnit,BaseUnitConversionFactor
        /// </summary>
        public async Task ImportMeasurementDataAsync(string sourcePath)
        {
            try
            {
                _logger.LogInformation("Starting measurement data import from {SourcePath}", sourcePath);

                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException("Import source file not found", sourcePath);

                var lines = await File.ReadAllLinesAsync(sourcePath);
                if (lines.Length < 2)
                {
                    _logger.LogWarning("Import file is empty or has only a header row");
                    return;
                }

                var categories = await _dbContext.MeasurementCategories.ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase);
                var imported = 0;

                // Skip header row
                foreach (var line in lines.Skip(1))
                {
                    var fields = line.Split(',');
                    if (fields.Length < 4)
                    {
                        _logger.LogWarning("Skipping malformed CSV line: {Line}", line);
                        continue;
                    }

                    var name = fields[0].Trim().Trim('"');
                    var symbol = fields[1].Trim().Trim('"');
                    var categoryName = fields[2].Trim().Trim('"');
                    var description = fields[3].Trim().Trim('"');
                    var isBaseUnit = fields.Length > 4 && bool.TryParse(fields[4].Trim(), out var b) && b;
                    var conversionFactor = fields.Length > 5 && decimal.TryParse(fields[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cf) ? cf : 1.0m;

                    // Find or create category
                    if (!categories.TryGetValue(categoryName, out var category))
                    {
                        category = new MeasurementCategoryEntity
                        {
                            Name = categoryName,
                            Description = $"{categoryName} measurements",
                            CreatedDate = DateTime.UtcNow,
                            CreatedByPersonId = 1
                        };
                        _dbContext.MeasurementCategories.Add(category);
                        await _dbContext.SaveChangesAsync();
                        categories[categoryName] = category;
                    }

                    // Skip if measurement already exists in this category
                    var exists = await _dbContext.Measurements.AnyAsync(m => m.Symbol == symbol && m.MeasurementCategoryId == category.Id);
                    if (exists)
                    {
                        _logger.LogDebug("Measurement {Symbol} already exists in {Category}, skipping", symbol, categoryName);
                        continue;
                    }

                    var measurement = new BaseMeasurementEntity
                    {
                        Name = name,
                        Symbol = symbol,
                        Description = description,
                        MeasurementCategoryId = category.Id,
                        IsBaseUnit = isBaseUnit,
                        BaseUnitConversionFactor = conversionFactor,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = 1
                    };

                    _dbContext.Measurements.Add(measurement);
                    imported++;
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Measurement data import completed. Imported {Count} measurements.", imported);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing measurement data from {SourcePath}", sourcePath);
                throw;
            }
        }

        /// <summary>
        /// Exports measurement data to CSV or JSON format.
        /// </summary>
        public async Task ExportMeasurementDataAsync(string targetPath, string format = "csv")
        {
            try
            {
                _logger.LogInformation("Starting measurement data export to {TargetPath} in {Format} format", targetPath, format);

                var measurements = await _dbContext.Measurements
                    .Include(m => m.Category)
                    .OrderBy(m => m.Category.Name)
                    .ThenBy(m => m.Name)
                    .ToListAsync();

                var conversions = await _dbContext.MeasurementConversions
                    .Include(c => c.FromMeasurement)
                    .Include(c => c.ToMeasurement)
                    .ToListAsync();

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                switch (format.ToLowerInvariant())
                {
                    case "json":
                        await ExportAsJsonAsync(targetPath, measurements, conversions);
                        break;
                    case "csv":
                    default:
                        await ExportAsCsvAsync(targetPath, measurements, conversions);
                        break;
                }

                _logger.LogInformation("Measurement data export completed. Exported {MeasurementCount} measurements and {ConversionCount} conversions.",
                    measurements.Count, conversions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting measurement data to {TargetPath}", targetPath);
                throw;
            }
        }

        private static async Task ExportAsCsvAsync(string targetPath, List<BaseMeasurementEntity> measurements, List<MeasurementConversionEntity> conversions)
        {
            var lines = new List<string>
            {
                "Name,Symbol,Category,Description,IsBaseUnit,BaseUnitConversionFactor"
            };

            foreach (var m in measurements)
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "\"{0}\",\"{1}\",\"{2}\",\"{3}\",{4},{5}",
                    m.Name, m.Symbol, m.Category?.Name ?? "", m.Description ?? "",
                    m.IsBaseUnit, m.BaseUnitConversionFactor ?? 1.0m));
            }

            await File.WriteAllLinesAsync(targetPath, lines);

            // Write conversions as a separate file alongside the measurements
            var conversionPath = Path.Combine(
                Path.GetDirectoryName(targetPath) ?? ".",
                Path.GetFileNameWithoutExtension(targetPath) + "_conversions.csv");

            var conversionLines = new List<string>
            {
                "FromSymbol,ToSymbol,ConversionFactor,Offset,Formula,IsDirectConversion"
            };

            foreach (var c in conversions)
            {
                conversionLines.Add(string.Format(CultureInfo.InvariantCulture,
                    "\"{0}\",\"{1}\",{2},{3},\"{4}\",{5}",
                    c.FromMeasurement?.Symbol ?? "", c.ToMeasurement?.Symbol ?? "",
                    c.ConversionFactor, c.Offset ?? 0m, c.Formula ?? "",
                    c.IsDirectConversion));
            }

            await File.WriteAllLinesAsync(conversionPath, conversionLines);
        }

        private static async Task ExportAsJsonAsync(string targetPath, List<BaseMeasurementEntity> measurements, List<MeasurementConversionEntity> conversions)
        {
            var exportData = new
            {
                measurements = measurements.Select(m => new
                {
                    m.Name,
                    m.Symbol,
                    Category = m.Category?.Name ?? "",
                    Description = m.Description ?? "",
                    m.IsBaseUnit,
                    BaseUnitConversionFactor = m.BaseUnitConversionFactor ?? 1.0m
                }),
                conversions = conversions.Select(c => new
                {
                    FromSymbol = c.FromMeasurement?.Symbol ?? "",
                    ToSymbol = c.ToMeasurement?.Symbol ?? "",
                    c.ConversionFactor,
                    Offset = c.Offset ?? 0m,
                    Formula = c.Formula ?? "",
                    c.IsDirectConversion
                })
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(targetPath, json);
        }
    }
}
