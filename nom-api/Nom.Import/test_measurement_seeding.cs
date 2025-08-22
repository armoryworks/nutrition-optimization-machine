using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Measurement;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for List

namespace Nom.Import
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("Starting measurement data seeding...");

                // Create connection string
                var connectionString = "UserID=NomUser;Password=StercusAcciditShitHappens;Host=localhost;Port=5432;Database=nomdb;Pooling=true;";

                // Create DbContext options
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                optionsBuilder.UseNpgsql(connectionString);

                // Create DbContext
                using var dbContext = new ApplicationDbContext(optionsBuilder.Options);

                // Check if data already exists
                if (await dbContext.MeasurementCategories.AnyAsync())
                {
                    Console.WriteLine("Measurement data already exists, skipping seeding.");
                    return;
                }

                Console.WriteLine("Creating measurement categories...");

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

                // Add categories to context
                dbContext.MeasurementCategories.AddRange(massCategory, volumeCategory, countCategory, temperatureCategory);
                await dbContext.SaveChangesAsync();

                Console.WriteLine("Creating base measurements...");

                // Create base measurements for each category
                var gram = new MeasurementEntity
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

                var milliliter = new MeasurementEntity
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

                var piece = new MeasurementEntity
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

                var celsius = new MeasurementEntity
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
                dbContext.Measurements.AddRange(gram, milliliter, piece, celsius);
                await dbContext.SaveChangesAsync();

                // Update categories with base unit references
                massCategory.BaseUnitId = gram.Id;
                volumeCategory.BaseUnitId = milliliter.Id;
                countCategory.BaseUnitId = piece.Id;
                temperatureCategory.BaseUnitId = celsius.Id;

                await dbContext.SaveChangesAsync();

                Console.WriteLine("Creating common measurement units...");

                // Create common measurement units
                var commonMeasurements = new List<MeasurementEntity>
                {
                    // Mass units
                    new MeasurementEntity
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
                    new MeasurementEntity
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
                    new MeasurementEntity
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
                    new MeasurementEntity
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
                    new MeasurementEntity
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
                    new MeasurementEntity
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
                    new MeasurementEntity
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
                    new MeasurementEntity
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
                    new MeasurementEntity
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

                dbContext.Measurements.AddRange(commonMeasurements);
                await dbContext.SaveChangesAsync();

                Console.WriteLine("Creating conversion rules...");

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

                dbContext.MeasurementConversions.AddRange(conversions);
                await dbContext.SaveChangesAsync();

                Console.WriteLine($"Measurement data seeding completed successfully!");
                Console.WriteLine($"Created {await dbContext.MeasurementCategories.CountAsync()} categories");
                Console.WriteLine($"Created {await dbContext.Measurements.CountAsync()} measurements");
                Console.WriteLine($"Created {await dbContext.MeasurementConversions.CountAsync()} conversion rules");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error seeding measurement data: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}

