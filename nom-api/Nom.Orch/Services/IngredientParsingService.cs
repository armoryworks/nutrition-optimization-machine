// Nom.Orch/Services/IngredientParsingService.cs
using Nom.Orch.Interfaces;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Recipe; // For IngredientEntity, RecipeIngredientEntity
using Nom.Data.Reference; // For MeasurementTypeViewEntity, GroupedReferenceViewEntity, and ReferenceDiscriminatorEnum
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Globalization;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service responsible for parsing raw ingredient strings and standardizing them
    /// into structured RecipeIngredientEntity objects and managing IngredientEntities.
    /// </summary>
    public class IngredientParsingService : IIngredientParsingService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<IngredientParsingService> _logger;

        // Cache for MeasurementTypeViewEntity. Key: Lowercased Name (e.g., "cup", "gram")
        private static Dictionary<string, MeasurementTypeViewEntity>? _measurementTypeCache;
        private static MeasurementTypeViewEntity? _unknownMeasurementType; // Special fallback for unparsable units
        private static MeasurementTypeViewEntity? _toTasteMeasurementType; // Special type for "to taste"
        private static MeasurementTypeViewEntity? _eachMeasurementType; // Special type for "each"
        private static readonly object _cacheLock = new object();

        // System user ID for newly created entities/references if not from an authenticated user.
        // This ID is used for `CreatedById` when HttpContext (and thus current user) is not available,
        // for example, in background tasks or initial seeding. The DbContext's ApplyAuditInformation
        // will prefer the HttpContext user's PersonId if available.
        private readonly long _systemCreatedById;

        public IngredientParsingService(ApplicationDbContext dbContext, ILogger<IngredientParsingService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
            // Using a default system user ID, typically '1' or a specific ID for automation.
            // This PersonId should exist in your 'person.Person' table.
            _systemCreatedById = 1;

            // Initialize cache on service creation if not already done.
            // This is done synchronously on first access to ensure dependencies are ready.
            if (_measurementTypeCache == null || _unknownMeasurementType == null || _toTasteMeasurementType == null || _eachMeasurementType == null)
            {
                lock (_cacheLock)
                {
                    if (_measurementTypeCache == null || _unknownMeasurementType == null || _toTasteMeasurementType == null || _eachMeasurementType == null)
                    {
                        InitializeMeasurementTypeCacheAsync().Wait();
                    }
                }
            }
        }

        /// <summary>
        /// Asynchronously initializes the measurement type cache by loading MeasurementTypeViewEntity entries.
        /// Also ensures key measurement types ("Unknown", "to taste", "each") are cached for fallbacks.
        /// </summary>
        private async Task InitializeMeasurementTypeCacheAsync()
        {
            _logger.LogInformation("Initializing measurement type cache...");

            // Fetch all MeasurementTypeViewEntities via the TPH discriminator, specifically for MeasurementType GroupId.
            var measurementTypes = await _dbContext.GroupedReferenceViews
                                                   .OfType<MeasurementTypeViewEntity>()
                                                   .Where(mt => mt.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                                                   .ToListAsync();

            _measurementTypeCache = measurementTypes.ToDictionary(r => r.ReferenceName.ToLowerInvariant(), r => r);

            // Fetch or log if critical fallback types are missing.
            // These should ideally be pre-seeded in your database's Reference table.
            _unknownMeasurementType = _measurementTypeCache.GetValueOrDefault("unknown");
            if (_unknownMeasurementType == null)
            {
                _logger.LogCritical("CRITICAL: 'Unknown' measurement type (ID: {Id}) not found in MeasurementTypeView. Please pre-seed this reference type in your database. This will cause parsing issues.", (long)ReferenceDiscriminatorEnum.Unknown);
                // Fallback for runtime if not pre-seeded (will not persist, indicates severe setup issue).
                _unknownMeasurementType = new MeasurementTypeViewEntity { ReferenceName = "Unknown" };
            }

            _toTasteMeasurementType = _measurementTypeCache.GetValueOrDefault("to taste");
            if (_toTasteMeasurementType == null)
            {
                _logger.LogWarning("'To taste' measurement type not found in pre-seeded data. Ensure it's added (Name: 'to taste', GroupId: {GroupId}). Using 'Unknown' as fallback.", (long)ReferenceDiscriminatorEnum.MeasurementType);
                _toTasteMeasurementType = _unknownMeasurementType;
            }

            _eachMeasurementType = _measurementTypeCache.GetValueOrDefault("each");
            if (_eachMeasurementType == null)
            {
                _logger.LogWarning("'Each' measurement type not found in pre-seeded data. Ensure it's added (Name: 'each', GroupId: {GroupId}). Using 'Unknown' as fallback.", (long)ReferenceDiscriminatorEnum.MeasurementType);
                _eachMeasurementType = _unknownMeasurementType;
            }

            _logger.LogInformation("Measurement type cache initialized with {Count} entries. Fallback types ready: Unknown={UnknownExists}, ToTaste={ToTasteExists}, Each={EachExists}",
                _measurementTypeCache.Count, _unknownMeasurementType.ReferenceName != "Unknown" ? true : false, _toTasteMeasurementType.ReferenceName != "Unknown" ? true : false, _eachMeasurementType.ReferenceName != "Unknown" ? true : false);
        }

        /// <summary>
        /// Parses a single raw ingredient line and standardizes it.
        /// </summary>
        /// <param name="rawIngredientLine">The raw string (e.g., "1/2 cup all-purpose flour").</param>
        /// <param name="createdById">The ID of the user/system creating new IngredientEntities (used if HttpContext is not available).</param>
        /// <returns>Parsed entities or null if parsing fails.</returns>
        public async Task<(RecipeIngredientEntity? RecipeIngredient, IngredientEntity? StandardizedIngredient)> ParseAndStandardizeIngredientAsync(string rawIngredientLine, long createdById)
        {
            if (string.IsNullOrWhiteSpace(rawIngredientLine))
            {
                _logger.LogWarning("Attempted to parse an empty or null ingredient line.");
                return (null, null);
            }

            decimal? quantity = null;
            string? unitText = null;
            string ingredientNameRaw = rawIngredientLine;

            // Pattern for "to taste" or "as needed" first, as it changes the quantity/unit logic
            if (rawIngredientLine.ToLowerInvariant().Contains("to taste") || rawIngredientLine.ToLowerInvariant().Contains("as needed"))
            {
                quantity = null; // No precise quantity
                unitText = "to taste";
                ingredientNameRaw = Regex.Replace(rawIngredientLine, @"\b(to taste|as needed)\b", "", RegexOptions.IgnoreCase).Trim();
            }
            else
            {
                // Regex to capture quantity, optional unit, and remaining ingredient name
                // Handles "1", "1/2", "1 1/2", "0.5", "50g", "2 large"
                // Group 1: Quantity (e.g., "1", "1/2", "1 1/2", "0.5")
                // Group 2: Unit (optional, e.g., "cup", "g", "large")
                // Group 3: Remaining ingredient name
                var quantityUnitMatch = Regex.Match(rawIngredientLine, @"^(\d+\s*\d*\/\d+|\d*\.?\d+)\s*([a-zA-Z\.]*)\s*(.*)", RegexOptions.IgnoreCase);

                if (quantityUnitMatch.Success)
                {
                    string quantityString = quantityUnitMatch.Groups[1].Value.Trim();
                    unitText = quantityUnitMatch.Groups[2].Value.Trim();
                    ingredientNameRaw = quantityUnitMatch.Groups[3].Value.Trim();

                    if (!string.IsNullOrWhiteSpace(quantityString))
                    {
                        if (quantityString.Contains("/"))
                        {
                            if (quantityString.Contains(" ")) // Mixed number "1 1/2"
                            {
                                var parts = quantityString.Split(' ');
                                if (decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal whole) && TryParseFraction(parts[1], out decimal fraction))
                                {
                                    quantity = whole + fraction;
                                }
                            }
                            else // Pure fraction "1/2"
                            {
                                if (TryParseFraction(quantityString, out decimal fraction))
                                {
                                    quantity = fraction;
                                }
                            }
                        }
                        else // Simple decimal/integer
                        {
                            if (decimal.TryParse(quantityString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedQuantity))
                            {
                                quantity = parsedQuantity;
                            }
                        }
                    }
                }
            }


            // Clean up ingredient name: remove trailing commas, common descriptive words
            ingredientNameRaw = ingredientNameRaw.Replace(",", "").Trim();
            // Broader removal of common cooking descriptors (can be expanded)
            ingredientNameRaw = Regex.Replace(ingredientNameRaw, @"\b(chopped|sliced|diced|minced|finely|fresh|dried|ground|canned|crushed|peeled|drained|toasted|roasted|cooked|raw|unsalted|salted)\b", "", RegexOptions.IgnoreCase).Trim();
            ingredientNameRaw = ingredientNameRaw.Replace("  ", " ").Trim(); // Remove double spaces
            ingredientNameRaw = ingredientNameRaw.TrimEnd('.'); // Remove trailing period if any

            // Fallback for cases where quantity/unit regex might have consumed part of the name incorrectly
            if (string.IsNullOrWhiteSpace(ingredientNameRaw))
            {
                ingredientNameRaw = rawIngredientLine; // Revert to full line if name extraction failed, then try cleaning.
                // Re-apply common cleaning here if revert happens, as regex might have failed.
                ingredientNameRaw = Regex.Replace(ingredientNameRaw, @"^(\d+\s*\d*\/\d+|\d*\.?\d+)\s*([a-zA-Z\.]*)\s*", "", RegexOptions.IgnoreCase).Trim();
                ingredientNameRaw = Regex.Replace(ingredientNameRaw, @"\b(to taste|as needed|chopped|sliced|diced|minced|finely|fresh|dried|ground|canned|crushed|peeled|drained|toasted|roasted|cooked|raw|unsalted|salted)\b", "", RegexOptions.IgnoreCase).Trim();
                ingredientNameRaw = ingredientNameRaw.Replace(",", "").Replace("  ", " ").Trim();
            }


            string standardizedName = ingredientNameRaw; // Future: implement synonym lookup/mapping here

            // Step 2: Get Standardized Unit from MeasurementTypeViewEntity
            MeasurementTypeViewEntity measurementType = GetMeasurementType(unitText, quantity); // Pass quantity for 'each' heuristic

            // Step 3: Get or Create Standardized IngredientEntity
            var standardizedIngredient = await GetOrCreateIngredientAsync(standardizedName, createdById); // createdById passed as fallback

            if (standardizedIngredient == null)
            {
                _logger.LogError("Failed to get or create IngredientEntity for '{StandardizedName}'. Raw: '{RawLine}'", standardizedName, rawIngredientLine);
                return (null, null);
            }

            // Create RecipeIngredientEntity
            var recipeIngredient = new RecipeIngredientEntity
            {
                IngredientId = standardizedIngredient.Id,
                Measurement = quantity ?? 0, // Use 0 if quantity is null (e.g., "to taste"). Db will handle null if column is nullable.
                MeasurementTypeId = measurementType.ReferenceId,
                OriginalText = rawIngredientLine // Store the original line for traceability
            };

            return (recipeIngredient, standardizedIngredient);
        }

        /// <summary>
        /// Helper to parse fractions including mixed numbers.
        /// </summary>
        private bool TryParseFraction(string fractionString, out decimal result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(fractionString)) return false;

            string[] parts = fractionString.Split('/');
            if (parts.Length == 2 && decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numerator) && decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal denominator))
            {
                if (denominator != 0)
                {
                    result = numerator / denominator;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Retrieves an existing IngredientEntity by name, or creates a new one if it doesn't exist.
        /// </summary>
        /// <param name="name">The standardized name of the ingredient.</param>
        /// <param name="createdById">The ID of the user/system creating new IngredientEntities.</param>
        /// <returns>The IngredientEntity.</returns>
        private async Task<IngredientEntity> GetOrCreateIngredientAsync(string name, long createdById)
        {
            var existingIngredient = await _dbContext.Ingredients
                                                     .FirstOrDefaultAsync(i => i.Name.ToLowerInvariant() == name.ToLowerInvariant());

            if (existingIngredient != null)
            {
                return existingIngredient;
            }

            var newIngredient = new IngredientEntity
            {
                Name = name,
                // CreatedById and CreatedDate will be set by ApplicationDbContext.ApplyAuditInformation if IAuditableEntity.
                // The createdById parameter here acts as a fallback or initial value if HttpContextAccessor is not active (e.g., in background tasks).
                CreatedByPersonId = 1, // Default to system user ID, can be overridden by ApplyAuditInformation
            };
            _dbContext.Ingredients.Add(newIngredient);
            await _dbContext.SaveChangesAsync(); // Save immediately to get the ID for RecipeIngredientEntity
            _logger.LogInformation("Created new IngredientEntity: {Name} (ID: {Id})", newIngredient.Name, newIngredient.Id);
            return newIngredient;
        }

        /// <summary>
        /// Retrieves a MeasurementTypeViewEntity from the cache.
        /// Falls back to a predefined "Unknown" type if not found.
        /// </summary>
        /// <param name="unitName">The raw unit string (e.g., "cup", "g", "large").</param>
        /// <param name="quantity">The parsed quantity, used as a heuristic for 'each' if unit is ambiguous.</param>
        /// <returns>The found MeasurementTypeViewEntity or the "Unknown" fallback.</returns>
        private MeasurementTypeViewEntity GetMeasurementType(string? unitName, decimal? quantity)
        {
            string cleanedUnit = (unitName ?? "").ToLowerInvariant().Trim();

            // Heuristics for common unit ambiguities or missing units
            if (string.IsNullOrWhiteSpace(cleanedUnit))
            {
                if (quantity.HasValue && quantity.Value > 0)
                {
                    // If there's a quantity but no unit (e.g., "2 apples"), it's often "each"
                    return _eachMeasurementType ?? _unknownMeasurementType!;
                }
                // If no quantity and no unit (e.g., just "salt"), default to "Unknown"
                return _unknownMeasurementType!;
            }

            // Direct match or common plural/singular matches
            if (_measurementTypeCache != null)
            {
                MeasurementTypeViewEntity? foundType;
                if (_measurementTypeCache.TryGetValue(cleanedUnit, out foundType)) return foundType;
                if (cleanedUnit.EndsWith("s") && _measurementTypeCache.TryGetValue(cleanedUnit.TrimEnd('s'), out foundType)) return foundType;
                if (!cleanedUnit.EndsWith("s") && _measurementTypeCache.TryGetValue(cleanedUnit + "s", out foundType)) return foundType;

                // Handle specific common descriptive words as units, mapping them to 'each'
                if (cleanedUnit == "large" || cleanedUnit == "medium" || cleanedUnit == "small")
                {
                    return _eachMeasurementType ?? _unknownMeasurementType!;
                }
                if (cleanedUnit == "to taste")
                {
                    return _toTasteMeasurementType ?? _unknownMeasurementType!;
                }
            }

            _logger.LogWarning("Measurement unit '{UnitName}' not found in pre-seeded types. Assigning 'Unknown'.", unitName);
            return _unknownMeasurementType!; // Always return the fallback. It should have been initialized.
        }


        /// <summary>
        /// Parses a comma-separated string of raw ingredient lines into structured representations.
        /// This method assumes the input from Kaggle is a single string like "[ing1, ing2, ing3]".
        /// </summary>
        /// <param name="rawIngredientLinesString">A single string containing comma-separated raw ingredient lines.</param>
        /// <param name="createdById">The ID of the user/system creating new entities.</param>
        /// <returns>A list of structured RecipeIngredientEntities and their associated StandardizedIngredientEntities.</returns>
        public async Task<List<(RecipeIngredientEntity RecipeIngredient, IngredientEntity StandardizedIngredient)>> ParseAndStandardizeIngredientsAsync(string rawIngredientLinesString, long createdById)
        {
            var parsedIngredients = new List<(RecipeIngredientEntity RecipeIngredient, IngredientEntity StandardizedIngredient)>();

            if (string.IsNullOrWhiteSpace(rawIngredientLinesString))
            {
                return parsedIngredients;
            }

            // Kaggle's "Ingredients" field is often a single string like "[ingredient 1, ingredient 2, ingredient 3]"
            // We need to split it properly.
            // First, remove brackets if they exist
            string cleanedString = rawIngredientLinesString.Trim();
            if (cleanedString.StartsWith("[") && cleanedString.EndsWith("]"))
            {
                cleanedString = cleanedString.Substring(1, cleanedString.Length - 2);
            }

            // Split by comma. This might need refinement based on exact dataset variations,
            // or even more advanced NLP if commas are sometimes part of an ingredient name.
            // For now, simple comma split.
            var individualLines = cleanedString.Split(',')
                                              .Select(s => s.Trim())
                                              .Where(s => !string.IsNullOrWhiteSpace(s))
                                              .ToList();

            foreach (var line in individualLines)
            {
                var (recipeIngredient, standardizedIngredient) = await ParseAndStandardizeIngredientAsync(line, createdById);
                if (recipeIngredient != null && standardizedIngredient != null)
                {
                    parsedIngredients.Add((recipeIngredient, standardizedIngredient));
                }
            }

            return parsedIngredients;
        }
    }
}
