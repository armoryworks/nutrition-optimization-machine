using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Recipe; // For IngredientEntity, RecipeIngredientEntity
using Nom.Data.Reference; // For ReferenceEntity, MeasurementTypeViewEntity, ReferenceDiscriminatorEnum
using Nom.Import.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Import.Data.Recipe.Importers
{
    /// <summary>
    /// Represents the parsed components of an ingredient line.
    /// </summary>
    public class ParsedIngredientDto
    {
        public decimal Quantity { get; set; }
        public string? UnitName { get; set; }
        public string CleanedName { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty; // Keep original raw line for traceability
    }

    /// <summary>
    /// Handles parsing, splitting, and fuzzy matching of raw ingredient lines.
    /// Translates PL/pgSQL parsing logic into C#.
    /// </summary>
    public class RecipeIngredientParser
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<RecipeIngredientParser> _logger;
        private readonly ImportConfig _importConfig;

        // Cached MeasurementType IDs for efficient lookups (Key: ReferenceName, Value: ReferenceId)
        private Dictionary<string, long> _measurementTypeLookup = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        // Cached Ingredient names and IDs for fuzzy matching
        private List<(string Name, long Id)> _cachedIngredientNames = new List<(string Name, long Id)>();

        // Define common measurement units and their abbreviations as a constant array of tuples.
        // Each tuple represents (RegexPattern, CanonicalName).
        // Order matters for overlapping terms (e.g., "tablespoon" before "tablespoons").
        // Using word boundaries \b to prevent partial matches (e.g., 'can' matching 'candy')
        // Note: \y is PostgreSQL specific for word boundaries. \b is C# Regex word boundary.
        private static readonly (string RegexPattern, string CanonicalName)[] _measurementsMapData = new (string, string)[]
        {
            (@"\b(?:tablespoons?|tbsp\.?|T)\b", "tablespoon"),
            (@"\b(?:teaspoons?|tsp\.?|t)\b", "teaspoon"),
            (@"\b(?:cups?|c\.?)\b", "cup"),
            (@"\b(?:ounces?|oz\.?)\b", "ounce"),
            (@"\b(?:pounds?|lbs?\.?|#)\b", "pound"),
            (@"\b(?:grams?|g\.?)\b", "gram"),
            (@"\b(?:kilograms?|kg\.?)\b", "kilogram"),
            (@"\b(?:milliliters?|ml\.?)\b", "milliliter"),
            (@"\b(?:liters?|L\.?)\b", "liter"),
            (@"\b(?:pints?|pt\.?)\b", "pint"),
            (@"\b(?:quarts?|qt\.?)\b", "quart"),
            (@"\b(?:gallons?|gal\.?)\b", "gallon"),
            (@"\b(?:dashes?|drops?|pinches?|sprinkles?)\b", "dash"),
            (@"\b(?:cloves?|stalks?|leaves?|sprigs?|slices?|strips?)\b", "piece"),
            (@"\b(?:cans?|box(?:es)?|packages?|bunches?|heads?)\b", "package"),
            (@"\b(?:large|medium|small|extra-large|extra-small)\b", "size"),
            (@"\b(?:sheets?|fillets?|loins?|breasts?|thighs?)\b", "piece"),
            (@"\b(?:bags?|bottles?|jars?|containers?)\b", "container"),
            (@"\b(?:to taste|as needed|optional)\b", "to taste"), // Special "measurements"
            (@"\b(?:each)\b", "each") // Explicitly add 'each'
        };

        public RecipeIngredientParser(
            ApplicationDbContext dbContext,
            ILogger<RecipeIngredientParser> logger,
            IOptions<ImportConfig> importConfig)
        {
            _dbContext = dbContext;
            _logger = logger;
            _importConfig = importConfig.Value;
        }

        /// <summary>
        /// Initializes necessary reference data (MeasurementType IDs and Ingredient names) from the database.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing RecipeIngredientParser reference data...");

            // Load Measurement Types from the MeasurementTypes DbSet (MeasurementTypeViewEntity)
            // This directly queries the view, which is more efficient and translatable.
            var measurementTypes = await _dbContext.MeasurementTypes
                .AsNoTracking()
                .Where(mtv => mtv.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                .ToListAsync(cancellationToken);

            if (!measurementTypes.Any())
            {
                _logger.LogError("No MeasurementType reference data found in MeasurementTypes view for GroupId {GroupId}. Please ensure initial reference data is seeded and the ReferenceGroupView is correctly defined and accessible.", (long)ReferenceDiscriminatorEnum.MeasurementType);
                throw new InvalidOperationException("MeasurementType reference data not found.");
            }

            foreach (var mtv in measurementTypes)
            {
                _measurementTypeLookup[mtv.ReferenceName] = mtv.ReferenceId;
            }

            // Ensure 'unknown' is mapped, crucial for fallback.
            // We'll try to find it by name if it wasn't already loaded via the view.
            if (!_measurementTypeLookup.ContainsKey("unknown"))
            {
                var unknownRef = await _dbContext.References.AsNoTracking().FirstOrDefaultAsync(r => r.Name.Equals("unknown", StringComparison.OrdinalIgnoreCase), cancellationToken);
                if (unknownRef != null)
                {
                    _measurementTypeLookup["unknown"] = unknownRef.Id;
                }
                else
                {
                    _logger.LogWarning("MeasurementType 'unknown' not found in reference data. This may lead to issues if unmappable units are encountered and 'unknown' is required.");
                }
            }
            _logger.LogInformation("Cached {Count} MeasurementType IDs.", _measurementTypeLookup.Count);

            // Load all existing Ingredient names and IDs for fuzzy matching
            _cachedIngredientNames = await _dbContext.Ingredients
                .AsNoTracking()
                .Select(i => new { i.Name, i.Id })
                .ToListAsync(cancellationToken)
                .ContinueWith(task => task.Result.Select(i => (i.Name, i.Id)).ToList(), cancellationToken);

            _logger.LogInformation("Cached {Count} Ingredient names for fuzzy matching.", _cachedIngredientNames.Count);
        }

        /// <summary>
        /// Parses a single raw ingredient line, extracts quantity and unit, and cleans the name.
        /// This is a C# translation of parse_ingredient_line_comprehensive_v2.
        /// </summary>
        /// <param name="rawLine">The raw ingredient text line.</param>
        /// <returns>A ParsedIngredientDto containing the extracted details.</returns>
        public ParsedIngredientDto ParseIngredientLine(string rawLine)
        {
            var trimmedRawText = rawLine.Trim();
            var tempWorkingText = trimmedRawText;
            decimal quantityOut = 1.0m; // Default
            string? unitNameOut = null;
            string cleanedNameOut = trimmedRawText; // Default fallback

            string? quantityMatchStr = null;
            string? measurementMatchStr = null;

            _logger.LogDebug("Parsing raw text: \"{RawText}\"", trimmedRawText);

            // Step 1: Attempt to extract Quantity
            // Regex for quantity: captures whole numbers, decimals, fractions, or mixed numbers.
            // (\d+(?:\.\d+)?(?:\s+\d+\/\d+)?|\d+\/\d+)
            var quantityMatch = Regex.Match(trimmedRawText, @"(\d+(?:\.\d+)?(?:\s+\d+\/\d+)?|\d+\/\d+)", RegexOptions.IgnoreCase);
            if (quantityMatch.Success)
            {
                quantityMatchStr = quantityMatch.Groups[1].Value;
                decimal decimalQuantity;

                if (quantityMatchStr.Contains(" ") && quantityMatchStr.Contains("/"))
                {
                    // Mixed number (e.g., "1 1/2")
                    var parts = quantityMatchStr.Split(' ');
                    decimalQuantity = decimal.Parse(parts[0], CultureInfo.InvariantCulture);
                    var fractionParts = parts[1].Split('/');
                    decimalQuantity += decimal.Parse(fractionParts[0], CultureInfo.InvariantCulture) / decimal.Parse(fractionParts[1], CultureInfo.InvariantCulture);
                }
                else if (quantityMatchStr.Contains("/"))
                {
                    // Fraction (e.g., "1/2")
                    var parts = quantityMatchStr.Split('/');
                    decimalQuantity = decimal.Parse(parts[0], CultureInfo.InvariantCulture) / decimal.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else
                {
                    // Whole number or decimal
                    decimalQuantity = decimal.Parse(quantityMatchStr, CultureInfo.InvariantCulture);
                }
                quantityOut = decimalQuantity;
                _logger.LogDebug("Found quantity: \"{QuantityStr}\" ({Quantity}).", quantityMatchStr, quantityOut);
            }
            else
            {
                _logger.LogDebug("No quantity found. Defaulting to 1.0. Raw text: \"{RawText}\"", trimmedRawText);
            }


            // Step 2: Attempt to extract Measurement Type
            foreach (var (pattern, canonicalName) in _measurementsMapData)
            {
                var measurementMatch = Regex.Match(trimmedRawText, pattern, RegexOptions.IgnoreCase);
                if (measurementMatch.Success)
                {
                    measurementMatchStr = measurementMatch.Groups[0].Value; // Group 0 is the entire match
                    unitNameOut = canonicalName;
                    _logger.LogDebug("Found unit: \"{UnitStr}\" (Canonical: \"{CanonicalName}\").", measurementMatchStr, unitNameOut);
                    break; // Exit loop once a measurement is found (order matters in _measurementsMapData)
                }
            }

            // Step 3: Derive cleaned_name_out by removing identified quantity and measurement
            tempWorkingText = trimmedRawText;

            // Remove quantity_match_str first, if found (case-insensitive removal)
            if (quantityMatchStr != null)
            {
                tempWorkingText = Regex.Replace(tempWorkingText, Regex.Escape(quantityMatchStr), "", RegexOptions.IgnoreCase).Trim();
                _logger.LogDebug("After removing quantity \"{QuantityStr}\": \"{TempText}\"", quantityMatchStr, tempWorkingText);
            }

            // Remove measurement_match_str next, if found (case-insensitive removal)
            if (measurementMatchStr != null)
            {
                tempWorkingText = Regex.Replace(tempWorkingText, Regex.Escape(measurementMatchStr), "", RegexOptions.IgnoreCase).Trim();
                _logger.LogDebug("After removing unit \"{UnitStr}\": \"{TempText}\"", measurementMatchStr, tempWorkingText);
            }

            // Final cleanup: remove leading/trailing commas/periods/extra spaces
            cleanedNameOut = Regex.Replace(tempWorkingText, @"^[\s,.]+|[\s,.]+$", "").Trim(); // Use tempWorkingText here
            cleanedNameOut = Regex.Replace(cleanedNameOut, @"\s+", " ").Trim(); // Replace multiple spaces with single space

            _logger.LogDebug("After basic cleanup: \"{CleanedName}\"", cleanedNameOut);

            // Fallback if cleaned name is empty
            if (string.IsNullOrWhiteSpace(cleanedNameOut))
            {
                // If after removing quantity and measurement, the string is empty,
                // fall back to the original raw text, but remove "to taste" if it was the only thing.
                if (Regex.IsMatch(trimmedRawText, @"^\s*to taste\s*$", RegexOptions.IgnoreCase))
                {
                    cleanedNameOut = "pepper"; // Specific fallback for "pepper to taste"
                }
                else
                {
                    cleanedNameOut = trimmedRawText; // Fallback to original if cleaning resulted in empty string
                }
                _logger.LogDebug("Cleaned name was empty, fell back to original or specific: \"{CleanedName}\"", cleanedNameOut);
            }

            _logger.LogDebug("Final parsed: Quantity: {Quantity}, Unit: \"{Unit}\", Cleaned Name: \"{CleanedName}\"", quantityOut, unitNameOut, cleanedNameOut);

            return new ParsedIngredientDto
            {
                Quantity = quantityOut,
                UnitName = unitNameOut,
                CleanedName = cleanedNameOut,
                RawLine = rawLine // Store original raw line
            };
        }

        /// <summary>
        /// Splits a cleaned ingredient name into multiple parts if it contains " and " or commas.
        /// This is a C# translation of split_cleaned_ingredient.
        /// </summary>
        /// <param name="parsedIngredient">The parsed ingredient DTO.</param>
        /// <returns>A list of ParsedIngredientDto, one for each split part.</returns>
        public List<ParsedIngredientDto> SplitCleanedIngredient(ParsedIngredientDto parsedIngredient)
        {
            var result = new List<ParsedIngredientDto>();
            var tempName = parsedIngredient.CleanedName.Trim();
            var originalQuantity = parsedIngredient.Quantity;
            var originalUnit = parsedIngredient.UnitName; // Use original unit, not default 'each' yet

            // First, split by " and " (case-insensitive, with optional surrounding whitespace)
            string[] splitParts;
            if (Regex.IsMatch(tempName, @"\s+and\s+", RegexOptions.IgnoreCase))
            {
                splitParts = Regex.Split(tempName, @"\s+and\s+", RegexOptions.IgnoreCase);
            }
            else
            {
                splitParts = new[] { tempName };
            }

            var finalSplitParts = new List<string>();
            foreach (var part in splitParts)
            {
                // Now, for each part, try to split by commas that seem to separate distinct items
                if (part.Contains(','))
                {
                    // Heuristic: Split by comma if it's not followed by a common descriptor.
                    // Given 04_06_3 is doing aggressive cleaning, a simple split by comma followed by whitespace is reasonable.
                    finalSplitParts.AddRange(part.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)));
                }
                else
                {
                    finalSplitParts.Add(part.Trim());
                }
            }

            // Ensure no empty strings in final parts
            finalSplitParts = finalSplitParts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

            if (!finalSplitParts.Any())
            {
                // Fallback: If no parts were generated, return original as a fallback
                // This ensures that even if splitting removes everything, we still get a record.
                _logger.LogWarning("Splitting of cleaned ingredient '{CleanedName}' resulted in no parts. Falling back to original cleaned name.", parsedIngredient.CleanedName);
                result.Add(new ParsedIngredientDto
                {
                    Quantity = originalQuantity,
                    UnitName = originalUnit,
                    CleanedName = parsedIngredient.CleanedName,
                    RawLine = parsedIngredient.RawLine
                });
            }
            else
            {
                foreach (var part in finalSplitParts)
                {
                    result.Add(new ParsedIngredientDto
                    {
                        Quantity = originalQuantity,
                        UnitName = originalUnit,
                        CleanedName = part,
                        RawLine = parsedIngredient.RawLine // Propagate original raw line
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Performs fuzzy matching to find the best matching IngredientEntity for a given cleaned name.
        /// This is a C# translation of the fuzzy matching logic in 06_recipe_com_process_ingredients.sql.
        /// </summary>
        /// <param name="cleanedIngredientName">The cleaned ingredient name to match.</param>
        /// <returns>The ID of the best matching IngredientEntity, or null if no good match is found.</returns>
        public long? FindMatchingIngredientId(string cleanedIngredientName)
        {
            if (string.IsNullOrWhiteSpace(cleanedIngredientName))
            {
                return null;
            }

            var lowerCleanedName = cleanedIngredientName.ToLowerInvariant();
            long? bestMatchId = null;
            int bestLevenshteinDistance = int.MaxValue;
            double bestJaroWinklerSimilarity = 0.0;

            // First, try exact match (case-insensitive)
            var exactMatch = _cachedIngredientNames.FirstOrDefault(i => i.Name.Equals(cleanedIngredientName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch.Id != 0) // Default tuple value is (null, 0) for value types, so check Id
            {
                _logger.LogDebug("Exact match found for '{CleanedName}': ID {Id}", cleanedIngredientName, exactMatch.Id);
                return exactMatch.Id;
            }

            // Fallback to fuzzy matching
            foreach (var (ingredientName, ingredientId) in _cachedIngredientNames)
            {
                var lowerIngredientName = ingredientName.ToLowerInvariant();

                // Levenshtein distance (lower is better)
                var levenshteinDist = LevenshteinDistance(lowerCleanedName, lowerIngredientName);
                // Jaro-Winkler similarity (higher is better)
                var jaroWinklerSim = JaroWinklerSimilarity(lowerCleanedName, lowerIngredientName);

                // Apply thresholds similar to SQL script (Levenshtein <= 3 OR Jaro-Winkler >= 0.8)
                bool isGoodMatch = (levenshteinDist <= 3 || jaroWinklerSim >= 0.8);

                if (isGoodMatch)
                {
                    // Prioritize lower Levenshtein distance, then higher Jaro-Winkler similarity
                    if (levenshteinDist < bestLevenshteinDistance ||
                        (levenshteinDist == bestLevenshteinDistance && jaroWinklerSim > bestJaroWinklerSimilarity))
                    {
                        bestLevenshteinDistance = levenshteinDist;
                        bestJaroWinklerSimilarity = jaroWinklerSim;
                        bestMatchId = ingredientId;
                    }
                }
            }

            if (bestMatchId.HasValue)
            {
                _logger.LogDebug("Fuzzy match found for '{CleanedName}': ID {Id} (Levenshtein: {Lev}, Jaro-Winkler: {Jaro})",
                    cleanedIngredientName, bestMatchId.Value, bestLevenshteinDistance, bestJaroWinklerSimilarity);
            }
            else
            {
                _logger.LogWarning("No good fuzzy match found for '{CleanedName}'.", cleanedIngredientName);
            }

            return bestMatchId;
        }

        /// <summary>
        /// Calculates the Levenshtein distance between two strings.
        /// (Simplified implementation, for production consider a dedicated library)
        /// </summary>
        private int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.IsNullOrEmpty(t) ? 0 : t.Length;
            }
            if (string.IsNullOrEmpty(t))
            {
                return s.Length;
            }

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Initialize
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            // Compute
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        /// <summary>
        /// Calculates the Jaro-Winkler similarity between two strings.
        /// (Simplified implementation, for production consider a dedicated library)
        /// </summary>
        private double JaroWinklerSimilarity(string s1, string s2)
        {
            // Jaro distance calculation
            int s1Len = s1.Length;
            int s2Len = s2.Length;

            if (s1Len == 0 && s2Len == 0) return 1.0;
            if (s1Len == 0 || s2Len == 0) return 0.0;

            int matchWindow = Math.Max(0, Math.Max(s1Len, s2Len) / 2 - 1);
            bool[] s1Matches = new bool[s1Len];
            bool[] s2Matches = new bool[s2Len];

            int matches = 0;
            List<char> s1MatchedChars = new List<char>();
            List<char> s2MatchedChars = new List<char>();

            for (int i = 0; i < s1Len; i++)
            {
                int start = Math.Max(0, i - matchWindow);
                int end = Math.Min(s2Len - 1, i + matchWindow);

                for (int j = start; j <= end; j++)
                {
                    if (!s2Matches[j] && s1[i] == s2[j])
                    {
                        s1Matches[i] = true;
                        s2Matches[j] = true;
                        matches++;
                        s1MatchedChars.Add(s1[i]);
                        s2MatchedChars.Add(s2[j]);
                        break;
                    }
                }
            }

            if (matches == 0) return 0.0;

            int transpositions = 0;
            for (int i = 0; i < s1MatchedChars.Count; i++)
            {
                if (s1MatchedChars[i] != s2MatchedChars[i])
                {
                    transpositions++;
                }
            }
            transpositions /= 2;

            double jaro = ((double)matches / s1Len + (double)matches / s2Len + (double)(matches - transpositions) / matches) / 3.0;

            // Winkler modification
            double p = 0.1; // Scale factor (usually 0.1)
            int l = 0;     // Length of common prefix
            int maxPrefixLength = Math.Min(4, Math.Min(s1Len, s2Len)); // Max prefix length is 4

            for (int i = 0; i < maxPrefixLength; i++)
            {
                if (s1[i] == s2[i])
                {
                    l++;
                }
                else
                {
                    break;
                }
            }

            return jaro + l * p * (1 - jaro);
        }

        /// <summary>
        /// Gets the MeasurementType ID for a given unit name, using a case-insensitive lookup.
        /// </summary>
        /// <param name="unitName">The name of the unit (e.g., "g", "mg", "cup").</param>
        /// <returns>The ID of the MeasurementType, or the 'unknown' ID if not found, or 0 as a final fallback.</returns>
        public long GetMeasurementTypeId(string? unitName)
        {
            if (string.IsNullOrWhiteSpace(unitName))
            {
                _logger.LogWarning("Attempted to get MeasurementType ID for null/empty unit name. Using 'unknown'.");
                return _measurementTypeLookup.TryGetValue("unknown", out long unknownId) ? unknownId : 0; // Fallback to 0 if 'unknown' not found
            }

            // Perform case-insensitive lookup
            if (_measurementTypeLookup.TryGetValue(unitName, out long id))
            {
                return id;
            }
            // Fallback to 'unknown' MeasurementType if specific unit not found
            if (_measurementTypeLookup.TryGetValue("unknown", out long fallbackUnknownId)) // Renamed this variable
            {
                _logger.LogWarning("Measurement unit '{UnitName}' not found. Using 'unknown' MeasurementType (ID: {FallbackUnknownId}).", unitName, fallbackUnknownId);
                return fallbackUnknownId;
            }
            // This should ideally not happen if 'unknown' is seeded correctly and loaded.
            _logger.LogError("MeasurementType 'unknown' not found in reference data. Returning 0.");
            return 0; // Final fallback
        }
    }
}
