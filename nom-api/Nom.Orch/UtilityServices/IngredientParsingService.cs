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

        // Cache for MeasurementTypeViewEntity. Key: Lowercased ReferenceName
        private static Dictionary<string, MeasurementTypeViewEntity>? _measurementTypeCache;
        private static MeasurementTypeViewEntity? _unknownMeasurementType; // Special fallback for unparsable units
        private static MeasurementTypeViewEntity? _toTasteMeasurementType; // Special type for "to taste"
        private static MeasurementTypeViewEntity? _eachMeasurementType; // Special type for "each"
        private static readonly object _cacheLock = new object();

        // Common descriptive words to remove from ingredient names for standardization
        private static readonly string[] CommonDescriptors = new[]
        {
            "chopped", "sliced", "diced", "minced", "finely", "fresh", "dried", "ground", "canned",
            "crushed", "peeled", "drained", "toasted", "roasted", "cooked", "raw", "unsalted", "salted",
            "organic", "free-range", "extra virgin", "light", "dark", "sweet", "hot", "cold", "warm",
            "softened", "melted", "cubed", "shredded", "grated", "whole", "halved", "quartered",
            "pitted", "seeded", "boneless", "skinless", "trimmed", "roughly", "loosely", "packed",
            "squeezed", "zest of", "juice of", "divided", "and", "or", "to", "plus", "more"
        };

        // Common words indicating a quantity/unit relationship in ingredient phrases
        private static readonly string[] QuantityUnitIndicators = new[]
        {
            "large", "medium", "small", "cloves", "sprigs", "leaves", "stalks", "bottles", "cans", "packages", "pinches", "dashes", "splashes"
        };


        public IngredientParsingService(ApplicationDbContext dbContext, ILogger<IngredientParsingService> logger,
                                          INutrientDataIntegrationService nutrientDataIntegrationService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _nutrientDataIntegrationService = nutrientDataIntegrationService;

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

        private async Task InitializeMeasurementTypeCacheAsync()
        {
            _logger.LogInformation("Initializing measurement type cache...");

            var measurementTypes = await _dbContext.GroupedReferenceViews
                                                   .OfType<MeasurementTypeViewEntity>()
                                                   .Where(mt => mt.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                                                   .ToListAsync();

            _measurementTypeCache = measurementTypes.ToDictionary(r => r.ReferenceName.ToLowerInvariant(), r => r);

            _unknownMeasurementType = _measurementTypeCache.GetValueOrDefault("unknown");
            if (_unknownMeasurementType == null)
            {
                _logger.LogCritical("CRITICAL: 'Unknown' measurement type (ReferenceId: {Id}) not found in MeasurementTypeView. Please pre-seed this reference type in your database. This will cause parsing issues.", (long)ReferenceDiscriminatorEnum.Unknown);
                _unknownMeasurementType = new MeasurementTypeViewEntity { ReferenceId = (long)ReferenceDiscriminatorEnum.Unknown, ReferenceName = "Unknown", GroupName = "MeasurementType", GroupId = (long)ReferenceDiscriminatorEnum.MeasurementType };
            }

            _toTasteMeasurementType = _measurementTypeCache.GetValueOrDefault("to taste");
            if (_toTasteMeasurementType == null)
            {
                _logger.LogWarning("'To taste' measurement type not found in pre-seeded data. Ensure it's added (ReferenceName: 'to taste', GroupId: {GroupId}). Using 'Unknown' as fallback.", (long)ReferenceDiscriminatorEnum.MeasurementType);
                _toTasteMeasurementType = _unknownMeasurementType;
            }

            _eachMeasurementType = _measurementTypeCache.GetValueOrDefault("each");
            if (_eachMeasurementType == null)
            {
                _logger.LogWarning("'Each' measurement type not found in pre-seeded data. Ensure it's added (ReferenceName: 'each', GroupId: {GroupId}). Using 'Unknown' as fallback.", (long)ReferenceDiscriminatorEnum.MeasurementType);
                _eachMeasurementType = _unknownMeasurementType;
            }

            _logger.LogInformation("Measurement type cache initialized with {Count} entries. Fallback types ready: Unknown={UnknownExists}, ToTaste={ToTasteExists}, Each={EachExists}",
                _measurementTypeCache.Count, _unknownMeasurementType.ReferenceName != "Unknown" ? true : false, _toTasteMeasurementType.ReferenceName != "Unknown" ? true : false, _eachMeasurementType.ReferenceName != "Unknown" ? true : false);
        }

        /// <summary>
        /// Parses a single raw ingredient line and standardizes it, leveraging NLP-like heuristics.
        /// </summary>
        /// <param name="rawIngredientLine">The raw string (e.g., "1/2 cup all-purpose flour").</param>
        /// <returns>Parsed entities or null if parsing fails.</returns>
        public async Task<(RecipeIngredientEntity? RecipeIngredient, IngredientEntity? StandardizedIngredient)> ParseAndStandardizeIngredientAsync(string rawIngredientLine)
        {
            if (string.IsNullOrWhiteSpace(rawIngredientLine))
            {
                _logger.LogWarning("Attempted to parse an empty or null ingredient line.");
                return (null, null);
            }

            decimal? quantity = null;
            string? unitText = null;
            string ingredientNameRaw = rawIngredientLine;
            string originalLineLower = rawIngredientLine.ToLowerInvariant();

            // 1. Handle "to taste" or "as needed" first
            if (originalLineLower.Contains("to taste") || originalLineLower.Contains("as needed"))
            {
                quantity = null;
                unitText = "to taste";
                ingredientNameRaw = Regex.Replace(rawIngredientLine, @"\b(to taste|as needed)\b", "", RegexOptions.IgnoreCase).Trim();
            }
            else
            {
                // 2. Advanced Quantity and Unit Extraction Regex
                // This regex tries to capture:
                // - Group 1: Whole number part (e.g., "1 ")
                // - Group 2: Fractional part (e.g., "1/2")
                // - Group 3: Decimal part (e.g., ".5")
                // - Group 4: Unit (e.g., "cup", "g", "tsp", "large") - more flexible to capture common single words
                // - Group 5: Remaining ingredient name (everything after quantity and unit)
                var quantityUnitPattern = new Regex(
                    @"^\s*(?:(\d+)\s*)?" +             // Optional whole number (Group 1), followed by optional space
                    @"(?:(\d+\/\d+)" +                 // Optional fraction (Group 2)
                     @"|(\d*\.\d+))?" +                // OR Optional decimal (Group 3)
                    @"\s*" +                           // Optional space
                    @"(?:([a-zA-Z\.]+))?" +            // Optional unit (Group 4) - allows for "oz.", "lb."
                    @"\s*(.*)$",                       // Rest of the string as ingredient name (Group 5)
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace
                );


                var match = quantityUnitPattern.Match(rawIngredientLine);

                if (match.Success)
                {
                    // Parse quantity
                    string wholeNumStr = match.Groups[1].Value;
                    string fractionStr = match.Groups[2].Value;
                    string decimalStr = match.Groups[3].Value;

                    if (!string.IsNullOrWhiteSpace(wholeNumStr) && string.IsNullOrWhiteSpace(fractionStr) && string.IsNullOrWhiteSpace(decimalStr))
                    {
                        // Only whole number, e.g., "2 cups"
                        quantity = decimal.Parse(wholeNumStr, CultureInfo.InvariantCulture);
                    }
                    else if (string.IsNullOrWhiteSpace(wholeNumStr) && !string.IsNullOrWhiteSpace(fractionStr) && string.IsNullOrWhiteSpace(decimalStr))
                    {
                        // Only fraction, e.g., "1/2 cup"
                        if (TryParseFraction(fractionStr, out decimal fracVal))
                        {
                            quantity = fracVal;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(wholeNumStr) && !string.IsNullOrWhiteSpace(fractionStr) && string.IsNullOrWhiteSpace(decimalStr))
                    {
                        // Mixed number, e.g., "1 1/2 cups"
                        if (TryParseFraction(fractionStr, out decimal fracVal))
                        {
                            quantity = decimal.Parse(wholeNumStr, CultureInfo.InvariantCulture) + fracVal;
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(wholeNumStr) && string.IsNullOrWhiteSpace(fractionStr) && !string.IsNullOrWhiteSpace(decimalStr))
                    {
                        // Only decimal, e.g., ".5 cups" or "0.5 cups"
                        if (decimal.TryParse(decimalStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decVal))
                        {
                            quantity = decVal;
                        }
                    }
                    // Handle range, e.g., "1-2 cups" (simplistic: take first number)
                    else if (Regex.IsMatch(rawIngredientLine, @"\d+\s*-\s*\d+", RegexOptions.IgnoreCase))
                    {
                        var rangeMatch = Regex.Match(rawIngredientLine, @"^(\d+)", RegexOptions.IgnoreCase);
                        if (rangeMatch.Success && decimal.TryParse(rangeMatch.Groups[1].Value, CultureInfo.InvariantCulture, out decimal rangeVal))
                        {
                            quantity = rangeVal;
                            _logger.LogDebug("Parsed range '{0}' to quantity {1} for '{2}'", rangeMatch.Value, quantity, rawIngredientLine);
                        }
                    }


                    unitText = match.Groups[4].Value.Trim();
                    ingredientNameRaw = match.Groups[5].Value.Trim();
                }
            }


            // 3. Clean up ingredient name: more aggressive removal of descriptors and prepositions.
            string cleanedIngredientName = ingredientNameRaw;

            // Remove content in parentheses, e.g., "chicken (boneless, skinless)" -> "chicken"
            cleanedIngredientName = Regex.Replace(cleanedIngredientName, @"\s*\([^)]*\)", "").Trim();

            // Remove common descriptors (word boundary sensitive)
            foreach (var descriptor in CommonDescriptors)
            {
                cleanedIngredientName = Regex.Replace(cleanedIngredientName, $@"\b{Regex.Escape(descriptor)}\b", "", RegexOptions.IgnoreCase).Trim();
            }

            // Remove common trailing non-ingredient words (e.g., "for garnish", "or more")
            cleanedIngredientName = Regex.Replace(cleanedIngredientName, @"\s+for\s+garnish\b|\s+or\s+more\b|\s+as\s+needed\b|\s+to\s+taste\b", "", RegexOptions.IgnoreCase).Trim();


            // Replace multiple spaces with a single space
            cleanedIngredientName = Regex.Replace(cleanedIngredientName, @"\s+", " ").Trim();
            cleanedIngredientName = cleanedIngredientName.TrimEnd('.', ','); // Remove trailing punctuation

            // Final check: if cleaning made the name empty, revert to original raw or a portion
            if (string.IsNullOrWhiteSpace(cleanedIngredientName))
            {
                // Fallback to a simpler cleaning of the original raw line if aggressive cleaning fails.
                cleanedIngredientName = Regex.Replace(rawIngredientLine, @"^(\d+\s*\d*\/\d+|\d*\.?\d+)\s*([a-zA-Z\.]*)\s*", "", RegexOptions.IgnoreCase).Trim();
                cleanedIngredientName = Regex.Replace(cleanedIngredientName, @"\s*\([^)]*\)", "").Trim();
                cleanedIngredientName = Regex.Replace(cleanedIngredientName, @"\s+", " ").Trim();
                cleanedIngredientName = cleanedIngredientName.TrimEnd('.', ',');
            }


            string standardizedName = cleanedIngredientName; // Future: implement more advanced synonym lookup/mapping here if data-driven


            // 4. Get Standardized Unit from MeasurementTypeViewEntity
            MeasurementTypeViewEntity measurementType = GetMeasurementType(unitText, quantity, standardizedName); // Pass standardizedName for 'each' heuristic

            // 5. Get or Create Standardized IngredientEntity.
            var standardizedIngredient = await GetOrCreateIngredientAsync(standardizedName);

            if (standardizedIngredient == null)
            {
                _logger.LogError("Failed to get or create IngredientEntity for '{StandardizedName}'. Raw: '{RawLine}'", standardizedName, rawIngredientLine);
                return (null, null);
            }

            // 6. Associate nutrient data with the newly created/retrieved ingredient.
            await _nutrientDataIntegrationService.AssociateNutrientDataWithIngredientAsync(standardizedIngredient);


            // 7. Create RecipeIngredientEntity
            var recipeIngredient = new RecipeIngredientEntity
            {
                IngredientId = standardizedIngredient.Id,
                Measurement = quantity ?? 0,
                MeasurementTypeId = measurementType.ReferenceId, // Correctly use ReferenceId
                OriginalText = rawIngredientLine // Keep original for audit/debug
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
        /// Audit fields (CreatedByPersonId, CreatedDate) are handled by DbContext's ApplyAuditInformation.
        /// </summary>
        private async Task<IngredientEntity> GetOrCreateIngredientAsync(string name)
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
                // CreatedByPersonId and CreatedDate will be set by ApplicationDbContext.ApplyAuditInformation.
            };
            _dbContext.Ingredients.Add(newIngredient);
            await _dbContext.SaveChangesAsync(); // Save immediately to get the ID for RecipeIngredientEntity and its IngredientNutrientEntities
            _logger.LogInformation("Created new IngredientEntity: {Name} (ID: {Id})", newIngredient.Name, newIngredient.Id);
            return newIngredient;
        }

        /// <summary>
        /// Retrieves a MeasurementTypeViewEntity from the cache using its ReferenceName.
        /// Falls back to a predefined "Unknown" type if not found.
        /// Adds heuristics for "each" when unit is missing but quantity/name suggests it.
        /// </summary>
        /// <param name="unitName">The raw unit string (e.g., "cup", "g", "large").</param>
        /// <param name="quantity">The parsed quantity, used as a heuristic for 'each' if unit is ambiguous.</param>
        /// <param name="ingredientName">The parsed ingredient name, used as a heuristic for 'each' (e.g., "2 apples").</param>
        /// <returns>The found MeasurementTypeViewEntity or the "Unknown" fallback.</returns>
        private MeasurementTypeViewEntity GetMeasurementType(string? unitName, decimal? quantity, string? ingredientName)
        {
            string cleanedUnit = (unitName ?? "").ToLowerInvariant().Trim();

            if (string.IsNullOrWhiteSpace(cleanedUnit))
            {
                if (quantity.HasValue && quantity.Value > 0)
                {
                    return _eachMeasurementType ?? _unknownMeasurementType!;
                }
                return _unknownMeasurementType!;
            }

            if (_measurementTypeCache != null)
            {
                MeasurementTypeViewEntity? foundType;
                if (_measurementTypeCache.TryGetValue(cleanedUnit, out foundType)) return foundType;
                if (cleanedUnit.EndsWith("s", StringComparison.OrdinalIgnoreCase) && _measurementTypeCache.TryGetValue(cleanedUnit.TrimEnd('s'), out foundType)) return foundType;
                if (!cleanedUnit.EndsWith("s", StringComparison.OrdinalIgnoreCase) && _measurementTypeCache.TryGetValue(cleanedUnit + "s", out foundType)) return foundType;

                if (QuantityUnitIndicators.Contains(cleanedUnit, StringComparer.OrdinalIgnoreCase))
                {
                    return _eachMeasurementType ?? _unknownMeasurementType!;
                }
                if (cleanedUnit == "to taste")
                {
                    return _toTasteMeasurementType ?? _unknownMeasurementType!;
                }
            }

            _logger.LogWarning("Measurement unit '{UnitName}' not found in pre-seeded types. Assigning 'Unknown'.", unitName);
            return _unknownMeasurementType!;
        }

        /// <summary>
        /// Parses a comma-separated string of raw ingredient lines into structured representations.
        /// This method assumes the input from Kaggle is a single string like "[ing1, ing2, ing3]".
        /// </summary>
        /// <param name="rawIngredientLinesString">A single string containing comma-separated raw ingredient lines.</param>
        /// <returns>A list of structured RecipeIngredientEntities and their associated StandardizedIngredientEntities.</returns>
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

            var individualLines = cleanedString.Split(',')
                                              .Select(s => s.Trim())
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
    }
}
