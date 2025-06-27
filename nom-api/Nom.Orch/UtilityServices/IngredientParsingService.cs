// Nom.Orch/Services/IngredientParsingService.cs
using Nom.Orch.Interfaces; // For core interfaces
using Nom.Orch.UtilityInterfaces; // For INutrientDataIntegrationService
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Recipe; // For IngredientEntity, RecipeIngredientEntity
using Nom.Data.Reference; // For MeasurementTypeViewEntity, GroupedReferenceViewEntity, ReferenceDiscriminatorEnum
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions; // Required for Regex
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Globalization; // Required for CultureInfo and TextInfo
using CsvHelper.Configuration.Attributes; // For Ignore (though not used directly in this file, helpful for context)

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service responsible for parsing raw ingredient strings and standardizing them
    /// into structured RecipeIngredientEntity objects and managing IngredientEntities.
    /// This version includes enhanced parsing logic using more robust regex and heuristics
    /// to better extract quantities, units, and standardized ingredient names.
    /// It also triggers nutrient data association for new ingredients.
    /// </summary>
    public class IngredientParsingService : IIngredientParsingService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<IngredientParsingService> _logger;
        private readonly INutrientDataIntegrationService _nutrientDataIntegrationService;

        // Caches for common reference data to reduce database lookups
        private Dictionary<string, MeasurementTypeViewEntity>? _measurementTypesByName;
        private MeasurementTypeViewEntity? _defaultMeasurementType;
        private readonly object _cacheLock = new object(); // For thread-safe cache initialization

        public IngredientParsingService(ApplicationDbContext dbContext,
                                        ILogger<IngredientParsingService> logger,
                                        INutrientDataIntegrationService nutrientDataIntegrationService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _nutrientDataIntegrationService = nutrientDataIntegrationService;

            // Initialize caches in constructor if they can be synchronously loaded,
            // or ensure they are loaded asynchronously before first use.
            // Kick off cache loading in background, but ensure it's awaited by methods that rely on it.
            Task.Run(() => InitializeCachesAsync());
        }

        private async Task InitializeCachesAsync()
        {
            // Use lock to ensure only one thread initializes the cache
            if (_measurementTypesByName == null || _defaultMeasurementType == null)
            {
                lock (_cacheLock)
                {
                    // Double-check inside lock
                    if (_measurementTypesByName == null || _defaultMeasurementType == null)
                    {
                        try
                        {
                            // Load all measurement types from the view
                            var allMeasurementTypes = _dbContext.GroupedReferenceViews
                                .OfType<MeasurementTypeViewEntity>()
                                .AsNoTracking()
                                .ToList(); // Materialize to list to avoid multiple enumerations

                            _measurementTypesByName = allMeasurementTypes
                                .ToDictionary(mt => mt.ReferenceName.ToLower(CultureInfo.InvariantCulture), mt => mt); // Store by lowercase name for easy lookup

                            // Attempt to find the "unknown" measurement type
                            _defaultMeasurementType = allMeasurementTypes.FirstOrDefault(mt => mt.ReferenceName.ToLower(CultureInfo.InvariantCulture) == "unknown");

                            if (_defaultMeasurementType == null)
                            {
                                _logger.LogWarning("Measurement type 'unknown' not found in database. Please ensure it is seeded.");
                                // Fallback: if 'unknown' is not seeded, proceed, but parsing might fail to assign a valid type.
                                // For now, we'll proceed with _defaultMeasurementType being null, and use 0L as fallback if its ID is accessed.
                            }

                            _logger.LogInformation("Measurement type caches initialized with {Count} entries. Default 'unknown' found: {Found}",
                                _measurementTypesByName.Count, _defaultMeasurementType != null);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to initialize measurement type caches.");
                            _measurementTypesByName = new Dictionary<string, MeasurementTypeViewEntity>(); // Ensure it's not null to prevent NRE
                            _defaultMeasurementType = null; // Explicitly set to null on error
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Parses a single raw ingredient line (e.g., "1/2 cup all-purpose flour")
        /// and returns a structured representation, associating with existing or new IngredientEntities.
        /// </summary>
        /// <param name="rawIngredientLine">The raw string representing one ingredient line from a recipe.</param>
        /// <returns>
        /// A tuple containing:
        /// - RecipeIngredientEntity: The structured ingredient with quantity and unit.
        /// - IngredientEntity: The associated standardized IngredientEntity (could be new or existing).
        /// Returns null if parsing fails or input is invalid.
        /// </returns>
        public async Task<(RecipeIngredientEntity? RecipeIngredient, IngredientEntity? StandardizedIngredient)> ParseAndStandardizeIngredientAsync(string rawIngredientLine)
        {
            if (string.IsNullOrWhiteSpace(rawIngredientLine))
            {
                _logger.LogWarning("Attempted to parse an empty or null ingredient line.");
                return (null, null);
            }

            // Ensure caches are initialized before proceeding
            await InitializeCachesAsync();

            string normalizedLine = rawIngredientLine.Trim().ToLower(CultureInfo.InvariantCulture); // Normalize for consistent parsing

            // Regex to extract quantity, unit, and the remaining ingredient name
            // FIX: Removed the extra closing parenthesis ')' after 'gal'
            string pattern = @"^\s*(?<quantity>(\d+(\s+\d+\/\d+)?|\d*\.\d+|\d+\/\d+)?)\s*(?<unit>[a-z]{1,4}(s|es)?|\b(?:cup|cups|teaspoon|teaspoons|tablespoon|tablespoons|ounce|ounces|pound|pounds|gram|grams|liter|liters|ml|milliliter|milliliters|kg|kilogram|kilograms|each|pinch|dash|slice|piece|can|bottle|package|clove|sprig|leaf|stalk|g|mg|mcg|µg|kcal|l|tsp|tbsp|oz|lb|pt|qt|gal)\b)?\s*(?<name>.*)$";

            Match match = Regex.Match(normalizedLine, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            string quantityString = match.Groups["quantity"].Value.Trim();
            string unitString = match.Groups["unit"].Value.Trim();
            string ingredientNameRaw = match.Groups["name"].Value.Trim();

            decimal quantity = 0;
            // Correctly access ReferenceId for the measurement type
            // Fallback to 0L if _defaultMeasurementType is null or its ReferenceId is not available
            long measurementTypeId = _defaultMeasurementType?.ReferenceId ?? 0L; // Use 0L as a fallback for 'unknown' if not found

            // 1. Parse Quantity
            if (!string.IsNullOrEmpty(quantityString))
            {
                try
                {
                    // Handle mixed fractions (e.g., "1 1/2")
                    if (quantityString.Contains(" "))
                    {
                        var parts = quantityString.Split(' ');
                        if (decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal whole) &&
                            parts.Length > 1 && parts[1].Contains("/"))
                        {
                            var fractionParts = parts[1].Split('/');
                            if (fractionParts.Length == 2 &&
                                decimal.TryParse(fractionParts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numerator) &&
                                decimal.TryParse(fractionParts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal denominator) &&
                                denominator != 0)
                            {
                                quantity = whole + (numerator / denominator);
                            }
                        }
                    }
                    // Handle simple fractions (e.g., "1/2")
                    else if (quantityString.Contains("/"))
                    {
                        var parts = quantityString.Split('/');
                        if (parts.Length == 2 &&
                            decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numerator) &&
                            decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal denominator) &&
                            denominator != 0)
                        {
                            quantity = numerator / denominator;
                        }
                    }
                    // Handle decimals or whole numbers
                    else if (decimal.TryParse(quantityString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedQuantity))
                    {
                        quantity = parsedQuantity;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse quantity '{QuantityString}' for ingredient line: '{RawLine}'", quantityString, rawIngredientLine);
                    quantity = 0; // Default to 0 on parse failure
                }
            }

            // 2. Determine Measurement Type (Unit)
            if (!string.IsNullOrEmpty(unitString))
            {
                var foundMeasurementType = _measurementTypesByName?.GetValueOrDefault(unitString.ToLower(CultureInfo.InvariantCulture));
                if (foundMeasurementType != null)
                {
                    measurementTypeId = foundMeasurementType.ReferenceId; // Correctly use ReferenceId
                }
                else
                {
                    _logger.LogWarning("Unknown measurement unit '{Unit}' encountered in ingredient line: '{RawLine}'. Defaulting to 'unknown' ({MeasurementTypeId}).", unitString, rawIngredientLine, measurementTypeId);
                }
            }

            // 3. Standardize Ingredient Name
            string standardizedIngredientName = Regex.Replace(ingredientNameRaw, @"\b(fresh|dried|organic|canned|frozen|chopped|diced|sliced|minced|crushed|powdered|ground|whole|boneless|skinless|cooked|raw|unsalted|salted|finely|large|medium|small|extra-virgin|light|dark|pure|sweet|hot|mild|green|red|yellow|white|brown|black|creamy|chunky)\b", "", RegexOptions.IgnoreCase).Trim();
            standardizedIngredientName = Regex.Replace(standardizedIngredientName, @"^[,\.;\s]+|[,\.;\s]+$", "").Trim();
            standardizedIngredientName = Regex.Replace(standardizedIngredientName, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(standardizedIngredientName))
            {
                _logger.LogWarning("Could not extract a meaningful ingredient name from line: '{RawLine}'. Skipping.", rawIngredientLine);
                return (null, null);
            }

            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            standardizedIngredientName = textInfo.ToTitleCase(standardizedIngredientName.ToLower(CultureInfo.InvariantCulture));


            // 4. Get or Create IngredientEntity
            var standardizedIngredient = await GetOrCreateIngredientAsync(standardizedIngredientName);
            if (standardizedIngredient == null)
            {
                _logger.LogError("Failed to get or create standardized ingredient for '{Name}'.", standardizedIngredientName);
                return (null, null);
            }

            // 5. Create RecipeIngredientEntity
            var recipeIngredient = new RecipeIngredientEntity
            {
                Quantity = quantity, // Quantity is now a property
                MeasurementTypeId = measurementTypeId,
                RawLine = rawIngredientLine, // RawLine is now a property
                IngredientId = standardizedIngredient.Id, // Link to the standardized ingredient
            };

            return (recipeIngredient, standardizedIngredient);
        }

        /// <summary>
        /// Parses a collection of raw ingredient lines and returns structured representations.
        /// </summary>
        /// <param name="rawIngredientLinesString">A single string containing multiple raw ingredient lines, typically comma-separated or bracketed.</param>
        /// <returns>
        /// A list of tuples, each containing a structured RecipeIngredientEntity and its associated StandardizedIngredient.
        /// Only successfully parsed ingredients are returned.
        /// </returns>
        public async Task<List<(RecipeIngredientEntity RecipeIngredient, IngredientEntity StandardizedIngredient)>> ParseAndStandardizeIngredientsAsync(string rawIngredientLinesString)
        {
            var parsedIngredients = new List<(RecipeIngredientEntity RecipeIngredient, IngredientEntity StandardizedIngredient)>();

            if (string.IsNullOrWhiteSpace(rawIngredientLinesString))
            {
                return parsedIngredients;
            }

            string cleanedString = rawIngredientLinesString.Trim();
            if (cleanedString.StartsWith("[") && cleanedString.EndsWith("]"))
            {
                cleanedString = cleanedString.Substring(1, cleanedString.Length - 2);
            }

            // Split by comma outside of quotes, handling cases like "1/2 cup, chopped tomatoes" vs "1 cup 'fresh, ripe' tomatoes"
            var individualLines = Regex.Split(cleanedString, ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)")
                                              .Select(s => s.Trim().Replace("\"", "")) // Remove quotes if any
                                              .Where(s => !string.IsNullOrWhiteSpace(s))
                                              .ToList();


            foreach (var line in individualLines)
            {
                var (recipeIngredient, standardizedIngredient) = await ParseAndStandardizeIngredientAsync(line);
                if (recipeIngredient != null && standardizedIngredient != null)
                {
                    parsedIngredients.Add((recipeIngredient, standardizedIngredient));
                }
            }

            return parsedIngredients;
        }


        /// <summary>
        /// Retrieves an existing IngredientEntity by name (case-insensitive) or creates a new one if it doesn't exist.
        /// </summary>
        /// <param name="name">The standardized name of the ingredient.</param>
        /// <returns>The existing or newly created IngredientEntity.</returns>
        private async Task<IngredientEntity> GetOrCreateIngredientAsync(string name)
        {
            // Use ToLower() for EF Core translation compatibility with PostgreSQL LOWER() function.
            var existingIngredient = await _dbContext.Ingredients
                                                .AsNoTracking()
                                                .FirstOrDefaultAsync(i => i.Name.ToLower() == name.ToLower());

            if (existingIngredient != null)
            {
                return existingIngredient;
            }

            // Ingredient does not exist, create a new one
            var newIngredient = new IngredientEntity
            {
                Name = name,
                Description = $"Standardized ingredient for '{name}'.",
                // CreatedByPersonId will be set by the SaveChanges interceptor
                // CreatedDate will be set by the SaveChanges interceptor
            };

            await _dbContext.Ingredients.AddAsync(newIngredient);
            await _dbContext.SaveChangesAsync(); // Save immediately to get the ID for association
            _logger.LogInformation("Created new IngredientEntity: {IngredientName} (ID: {IngredientId})", newIngredient.Name, newIngredient.Id);

            // After saving, associate basic nutrient data.
            // This happens in the background, so we don't await it here to avoid blocking.
            // The ingestion service should ideally handle this in a more robust background processing fashion.
            _ = _nutrientDataIntegrationService.AssociateNutrientDataWithIngredientAsync(newIngredient);

            return newIngredient;
        }

        /// <summary>
        /// Helper to truncate strings to a specified maximum length.
        /// </summary>
        private static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
