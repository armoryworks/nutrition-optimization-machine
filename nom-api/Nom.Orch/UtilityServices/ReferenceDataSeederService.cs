// Nom.Orch/UtilityServices/ReferenceDataSeederService.cs
using Nom.Orch.UtilityInterfaces;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Reference; // For MeasurementTypeViewEntity, ReferenceDiscriminatorEnum, GroupedReferenceViewEntity
using Nom.Data.Nutrient; // For NutrientEntity
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Service responsible for seeding initial reference data into the database.
    /// This includes essential MeasurementTypeViewEntities and core NutrientEntities
    /// that are required for the application's functionality.
    /// </summary>
    public class ReferenceDataSeederService : IReferenceDataSeederService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<ReferenceDataSeederService> _logger;

        public ReferenceDataSeederService(ApplicationDbContext dbContext, ILogger<ReferenceDataSeederService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Ensures that essential reference data (like common measurement types and core nutrients)
        /// are present in the database. This method is idempotent, meaning it can be run multiple times
        /// without duplicating existing data.
        /// </summary>
        public async Task SeedReferenceDataAsync()
        {
            _logger.LogInformation("Starting reference data seeding process...");

            // --- Seed Measurement Types ---
            await SeedMeasurementTypesAsync();

            // --- Seed Core Nutrients ---
            await SeedCoreNutrientsAsync();

            _logger.LogInformation("Reference data seeding process completed.");
        }

        private async Task SeedMeasurementTypesAsync()
        {
            _logger.LogInformation("Seeding MeasurementTypeViewEntities...");

            var existingMeasurementTypes = await _dbContext.GroupedReferenceViews
                                                         .OfType<MeasurementTypeViewEntity>()
                                                         .Where(mt => mt.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                                                         .ToDictionaryAsync(mt => mt.ReferenceName.ToLowerInvariant());

            var measurementTypesToSeed = new List<(string Name, string Description)>();
            // Essential for parsing service
            measurementTypesToSeed.Add(("unknown", "Used when the measurement unit cannot be determined."));
            measurementTypesToSeed.Add(("to taste", "Indicates an ingredient quantity determined by preference."));
            measurementTypesToSeed.Add(("each", "Used when quantity refers to individual items."));

            // Common units for nutrients and ingredients
            measurementTypesToSeed.Add(("g", "Gram"));
            measurementTypesToSeed.Add(("mg", "Milligram"));
            measurementTypesToSeed.Add(("µg", "Microgram")); // For vitamins
            measurementTypesToSeed.Add(("kg", "Kilogram"));
            measurementTypesToSeed.Add(("l", "Liter"));
            measurementTypesToSeed.Add(("ml", "Milliliter"));
            measurementTypesToSeed.Add(("tsp", "Teaspoon"));
            measurementTypesToSeed.Add(("tbsp", "Tablespoon"));
            measurementTypesToSeed.Add(("cup", "Cup"));
            measurementTypesToSeed.Add(("oz", "Ounce"));
            measurementTypesToSeed.Add(("lb", "Pound"));
            measurementTypesToSeed.Add(("pint", "Pint"));
            measurementTypesToSeed.Add(("quart", "Quart"));
            measurementTypesToSeed.Add(("gallon", "Gallon"));
            measurementTypesToSeed.Add(("slice", "Slice"));
            measurementTypesToSeed.Add(("piece", "Piece"));
            measurementTypesToSeed.Add(("can", "Can"));
            measurementTypesToSeed.Add(("bottle", "Bottle"));
            measurementTypesToSeed.Add(("package", "Package"));
            measurementTypesToSeed.Add(("clove", "Clove (e.g., garlic)"));
            measurementTypesToSeed.Add(("sprig", "Sprig (e.g., herb)"));
            measurementTypesToSeed.Add(("leaf", "Leaf (e.g., lettuce)"));
            measurementTypesToSeed.Add(("stalk", "Stalk (e.g., celery)"));
            measurementTypesToSeed.Add(("pinch", "Pinch"));
            measurementTypesToSeed.Add(("dash", "Dash"));
            measurementTypesToSeed.Add(("splash", "Splash"));
            measurementTypesToSeed.Add(("kcal", "Kilocalorie (for Energy/Calories)")); // Explicitly for calories

            int addedCount = 0;
            foreach (var (name, description) in measurementTypesToSeed)
            {
                if (!existingMeasurementTypes.ContainsKey(name.ToLowerInvariant()))
                {
                    var newEntity = new MeasurementTypeViewEntity
                    {
                        ReferenceName = name,
                        ReferenceDescription = description,
                        GroupName = "MeasurementType",
                        GroupId = (long)ReferenceDiscriminatorEnum.MeasurementType,
                        // ReferenceId will be generated by the database if it's identity.
                        // CreatedByPersonId and CreatedAt will be handled by audit info.
                    };
                    _dbContext.GroupedReferenceViews.Add(newEntity); // Add to base DbSet
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Added {AddedCount} new MeasurementTypeViewEntities.", addedCount);
            }
            else
            {
                _logger.LogInformation("All essential MeasurementTypeViewEntities already exist.");
            }
        }

        private async Task SeedCoreNutrientsAsync()
        {
            _logger.LogInformation("Seeding core NutrientEntities...");

            var existingNutrients = await _dbContext.Nutrients.ToDictionaryAsync(n => n.Name.ToLowerInvariant());

            // Ensure gram and kcal measurement types are available for default units
            var gramTypeId = await _dbContext.GroupedReferenceViews
                                            .OfType<MeasurementTypeViewEntity>()
                                            .Where(mt => mt.ReferenceName.ToLowerInvariant() == "g" && mt.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                                            .Select(mt => mt.ReferenceId)
                                            .FirstOrDefaultAsync();
            var kcalTypeId = await _dbContext.GroupedReferenceViews
                                            .OfType<MeasurementTypeViewEntity>()
                                            .Where(mt => mt.ReferenceName.ToLowerInvariant() == "kcal" && mt.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                                            .Select(mt => mt.ReferenceId)
                                            .FirstOrDefaultAsync();
            var unknownTypeId = await _dbContext.GroupedReferenceViews
                                            .OfType<MeasurementTypeViewEntity>()
                                            .Where(mt => mt.ReferenceName.ToLowerInvariant() == "unknown" && mt.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                                            .Select(mt => mt.ReferenceId)
                                            .FirstOrDefaultAsync();


            // Define core nutrients and their default units
            var coreNutrientsToSeed = new List<(string Name, string Description, long? DefaultUnitId)>();
            coreNutrientsToSeed.Add(("Calories", "Energy content of food.", kcalTypeId));
            coreNutrientsToSeed.Add(("Protein", "Macronutrient essential for building and repairing tissues.", gramTypeId));
            coreNutrientsToSeed.Add(("Fat", "Macronutrient providing energy and supporting cell function.", gramTypeId));
            coreNutrientsToSeed.Add(("Carbohydrates", "Primary energy source for the body.", gramTypeId));
            coreNutrientsToSeed.Add(("Sodium", "Electrolyte important for fluid balance and nerve function.", gramTypeId)); // Common from FDC
            coreNutrientsToSeed.Add(("Fiber", "Dietary fiber.", gramTypeId)); // Common from FDC
            coreNutrientsToSeed.Add(("Saturated Fat", "Saturated fatty acids.", gramTypeId)); // Common from FDC
            coreNutrientsToSeed.Add(("Sugar", "Total sugars content.", gramTypeId)); // Common from FDC

            int addedCount = 0;
            foreach (var (name, description, defaultUnitId) in coreNutrientsToSeed)
            {
                if (!existingNutrients.ContainsKey(name.ToLowerInvariant()))
                {
                    var newEntity = new NutrientEntity
                    {
                        Name = name,
                        Description = description,
                        DefaultMeasurementTypeId = defaultUnitId ?? unknownTypeId, // Use discovered ID or fallback
                        // CreatedByPersonId and CreatedAt will be handled by audit info.
                    };
                    _dbContext.Nutrients.Add(newEntity);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Added {AddedCount} new core NutrientEntities.", addedCount);
            }
            else
            {
                _logger.LogInformation("All essential core NutrientEntities already exist.");
            }
        }
    }
}
