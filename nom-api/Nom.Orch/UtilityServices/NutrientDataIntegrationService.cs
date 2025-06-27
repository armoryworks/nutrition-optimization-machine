// Nom.Orch/UtilityServices/NutientDataIntegrationService.cs
using Nom.Orch.UtilityInterfaces; // For INutrientDataIntegrationService, IExternalNutrientApiService
using Nom.Orch.Models.NutrientApi; // For FoodSearchResult, FoodDetailResult, NutrientValue, FdcNutrientInfo
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Nutrient; // For NutrientEntity, IngredientNutrientEntity
using Nom.Data.Recipe; // For IngredientEntity
using Nom.Data.Reference; // For ReferenceDiscriminatorEnum, MeasurementTypeViewEntity
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Globalization;
using System.Text.RegularExpressions; // For CleanNutrientNameFromApi

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Utility service responsible for integrating and associating nutrient data with ingredients.
    /// This implementation uses the USDA FoodData Central API (via IExternalNutrientApiService)
    /// to fetch comprehensive nutrient profiles for ingredients and persists them as IngredientNutrientEntity records.
    /// </summary>
    public class NutrientDataIntegrationService : INutrientDataIntegrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<NutrientDataIntegrationService> _logger;
        private readonly IExternalNutrientApiService _externalNutrientApiService; // Inject FDC API service

        // Cache for common NutrientEntities (e.g., Protein, Fat, Carbs, Calories)
        private static Dictionary<string, NutrientEntity>? _nutrientCache;
        // Cache for MeasurementTypeViewEntity (e.g., "g", "kcal", "mg")
        private static Dictionary<string, MeasurementTypeViewEntity>? _measurementTypeViewsCache;

        private static MeasurementTypeViewEntity? _gramMeasurementType; // Common unit for nutrient amounts
        private static MeasurementTypeViewEntity? _kcalMeasurementType; // Specific unit for Calories
        private static MeasurementTypeViewEntity? _mgMeasurementType; // Specific unit for Milligrams
        private static MeasurementTypeViewEntity? _unknownMeasurementTypeView; // Fallback for units not found

        private static readonly object _cacheLock = new object();

        public NutrientDataIntegrationService(ApplicationDbContext dbContext,
                                              ILogger<NutrientDataIntegrationService> logger,
                                              IExternalNutrientApiService externalNutrientApiService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _externalNutrientApiService = externalNutrientApiService;

            // Initialize caches on service creation if not already done
            if (_nutrientCache == null || _measurementTypeViewsCache == null ||
                _gramMeasurementType == null || _kcalMeasurementType == null || _mgMeasurementType == null || _unknownMeasurementTypeView == null)
            {
                lock (_cacheLock)
                {
                    if (_nutrientCache == null || _measurementTypeViewsCache == null ||
                        _gramMeasurementType == null || _kcalMeasurementType == null || _mgMeasurementType == null || _unknownMeasurementTypeView == null)
                    {
                        InitializeCachesAsync().Wait(); // Block to ensure caches are ready
                    }
                }
            }
        }

        /// <summary>
        /// Initializes the nutrient and measurement type caches.
        /// </summary>
        private async Task InitializeCachesAsync()
        {
            _logger.LogInformation("Initializing nutrient and measurement type caches for NutrientDataIntegrationService...");

            // Populate Nutrient Cache (primarily for core nutrients that map easily from API)
            _nutrientCache = await _dbContext.Nutrients.ToDictionaryAsync(n => n.Name.ToLowerInvariant(), n => n);

            // Populate MeasurementTypeViews Cache using GroupedReferenceViewEntity's properties
            var measurementTypeViews = await _dbContext.GroupedReferenceViews
                                                   .OfType<MeasurementTypeViewEntity>()
                                                   .Where(mt => mt.GroupId == (long)ReferenceDiscriminatorEnum.MeasurementType)
                                                   .ToListAsync();
            _measurementTypeViewsCache = measurementTypeViews.ToDictionary(r => r.ReferenceName.ToLowerInvariant(), r => r);

            // Set specific common nutrient measurement types from the cache
            _gramMeasurementType = _measurementTypeViewsCache.GetValueOrDefault("g") ?? _measurementTypeViewsCache.GetValueOrDefault("gram");
            _kcalMeasurementType = _measurementTypeViewsCache.GetValueOrDefault("kcal") ?? _measurementTypeViewsCache.GetValueOrDefault("calories"); // "calories" as fallback name for Energy/Calories
            _mgMeasurementType = _measurementTypeViewsCache.GetValueOrDefault("mg") ?? _measurementTypeViewsCache.GetValueOrDefault("milligram");

            // Ensure "Unknown" measurement type view is cached for fallbacks.
            _unknownMeasurementTypeView = _measurementTypeViewsCache.GetValueOrDefault("unknown");
            if (_unknownMeasurementTypeView == null)
            {
                _logger.LogCritical("CRITICAL: 'Unknown' measurement type (ReferenceId: {Id}) not found in MeasurementTypeView. Please pre-seed this reference (Name: 'unknown', GroupId: {GroupId}).", (long)ReferenceDiscriminatorEnum.Unknown, (long)ReferenceDiscriminatorEnum.MeasurementType);
                _unknownMeasurementTypeView = new MeasurementTypeViewEntity { ReferenceId = (long)ReferenceDiscriminatorEnum.Unknown, ReferenceName = "Unknown", GroupName = "MeasurementType", GroupId = (long)ReferenceDiscriminatorEnum.MeasurementType };
            }

            _logger.LogInformation("Nutrient cache initialized with {NutrientCount} entries. MeasurementTypeViews cache initialized with {MeasurementTypeCount} entries. Core units ready: Gram={GramExists}, Kcal={KcalExists}, Mg={MgExists}, Unknown={UnknownExists}",
                _nutrientCache.Count, _measurementTypeViewsCache.Count, _gramMeasurementType != null, _kcalMeasurementType != null, _mgMeasurementType != null, _unknownMeasurementTypeView != null);
        }


        /// <summary>
        /// Retrieves an existing NutrientEntity by name, or creates a new one if it doesn't exist.
        /// This ensures internal consistency of nutrient definitions.
        /// </summary>
        /// <param name="nutrientName">The standardized name of the nutrient (e.g., "Protein", "Energy").</param>
        /// <param name="defaultUnitName">The default unit of this nutrient from the external API (e.g., "g", "kcal").</param>
        private async Task<NutrientEntity> GetOrCreateNutrientAsync(string nutrientName, string defaultUnitName)
        {
            if (_nutrientCache != null && _nutrientCache.TryGetValue(nutrientName.ToLowerInvariant(), out var cachedNutrient))
            {
                return cachedNutrient;
            }

            var existingNutrient = await _dbContext.Nutrients
                                                   .FirstOrDefaultAsync(n => n.Name.ToLowerInvariant() == nutrientName.ToLowerInvariant());

            if (existingNutrient != null)
            {
                _nutrientCache?.TryAdd(existingNutrient.Name.ToLowerInvariant(), existingNutrient);
                return existingNutrient;
            }

            // Determine the default measurement type ID for the new nutrient based on its common unit from FDC
            long defaultMeasurementTypeId = _unknownMeasurementTypeView!.ReferenceId;
            if (_measurementTypeViewsCache != null && _measurementTypeViewsCache.TryGetValue(defaultUnitName.ToLowerInvariant(), out var unitType))
            {
                defaultMeasurementTypeId = unitType.ReferenceId;
            }
            else // Specific handling for common units not directly matching simple names
            {
                if (defaultUnitName.ToLowerInvariant() == "kcal")
                {
                    defaultMeasurementTypeId = _kcalMeasurementType?.ReferenceId ?? _unknownMeasurementTypeView.ReferenceId;
                }
                else if (defaultUnitName.ToLowerInvariant() == "mg")
                {
                    defaultMeasurementTypeId = _mgMeasurementType?.ReferenceId ?? _unknownMeasurementTypeView.ReferenceId;
                }
                else if (defaultUnitName.ToLowerInvariant() == "g" || defaultUnitName.ToLowerInvariant() == "gram")
                {
                    defaultMeasurementTypeId = _gramMeasurementType?.ReferenceId ?? _unknownMeasurementTypeView.ReferenceId;
                }
            }


            var newNutrient = new NutrientEntity
            {
                Name = nutrientName,
                Description = $"System-generated nutrient for '{nutrientName}' derived from USDA FoodData Central.",
                DefaultMeasurementTypeId = defaultMeasurementTypeId,
                // CreatedByPersonId will be set by ApplyAuditInformation.
            };
            _dbContext.Nutrients.Add(newNutrient);
            await _dbContext.SaveChangesAsync(); // Save immediately to get the ID
            _nutrientCache?.TryAdd(newNutrient.Name.ToLowerInvariant(), newNutrient);
            _logger.LogInformation("Created new NutrientEntity: {Name} (ID: {Id}) with Default Unit ID: {UnitId}", newNutrient.Name, newNutrient.Id, newNutrient.DefaultMeasurementTypeId);
            return newNutrient;
        }

        /// <summary>
        /// Associates real nutrient data (from USDA FoodData Central API) with an IngredientEntity.
        /// </summary>
        /// <param name="ingredient">The IngredientEntity for which to integrate nutrient data.</param>
        /// <returns>A list of IngredientNutrientEntity records associated with the provided ingredient.</returns>
        public async Task<List<IngredientNutrientEntity>> AssociateNutrientDataWithIngredientAsync(IngredientEntity ingredient)
        {
            var ingredientNutrients = new List<IngredientNutrientEntity>();

            // 1. Search for the ingredient in the FDC API
            // Use the standardized ingredient name for the search
            var searchResults = await _externalNutrientApiService.SearchFoodsAsync(ingredient.Name, limit: 1);

            if (searchResults == null || !searchResults.Any())
            {
                _logger.LogWarning("No FDC nutrient data found for ingredient: '{IngredientName}'. Associating with default core nutrients (value 0).", ingredient.Name);
                // Fallback: If no external data, ensure core nutrients are added with 0 values
                await EnsureCoreNutrientsAreAssociated(ingredient, ingredientNutrients);
                return ingredientNutrients;
            }

            // For simplicity, take the first search result.
            var bestMatch = searchResults.First();
            _logger.LogInformation("Found FDC match for '{IngredientName}': '{MatchDescription}' (FdcId: {FdcId})", ingredient.Name, bestMatch.Description, bestMatch.FdcId);

            // 2. Get detailed nutrient information for the matched food
            var foodDetails = await _externalNutrientApiService.GetFoodDetailsAsync(bestMatch.FdcId);

            if (foodDetails == null || !foodDetails.Nutrients.Any())
            {
                _logger.LogWarning("No detailed nutrient data found for FdcId: {FdcId} (Ingredient: '{IngredientName}'). Associating with default core nutrients (value 0).", bestMatch.FdcId, ingredient.Name);
                // Fallback: If no detailed data, ensure core nutrients are added with 0 values
                await EnsureCoreNutrientsAreAssociated(ingredient, ingredientNutrients);
                return ingredientNutrients;
            }

            // 3. Process each nutrient value from the FDC API response
            foreach (var apiNutrient in foodDetails.Nutrients)
            {
                try
                {
                    // Corrected: Access nutrient name via apiNutrient.NutrientInfo.Name
                    string standardizedNutrientName = CleanNutrientNameFromApi(apiNutrient.NutrientInfo.Name);

                    // Get or Create the internal NutrientEntity (e.g., "Protein", "Energy", "Vitamin C")
                    // Use the unit from the API as the default unit for new NutrientEntities
                    var nutrient = await GetOrCreateNutrientAsync(standardizedNutrientName, apiNutrient.Unit);
                    if (nutrient == null)
                    {
                        _logger.LogError("Failed to get or create internal NutrientEntity for FDC nutrient '{ApiNutrientName}'.", apiNutrient.NutrientInfo.Name);
                        continue;
                    }

                    // Get the internal MeasurementTypeViewEntity for the API's unit
                    long measurementTypeId = _unknownMeasurementTypeView!.ReferenceId;
                    if (_measurementTypeViewsCache != null && _measurementTypeViewsCache.TryGetValue(apiNutrient.Unit.ToLowerInvariant(), out var unitType))
                    {
                        measurementTypeId = unitType.ReferenceId;
                    }
                    else // More specific unit mapping if direct match fails
                    {
                        if (apiNutrient.Unit.ToLowerInvariant() == "kcal")
                        {
                            measurementTypeId = _kcalMeasurementType?.ReferenceId ?? _unknownMeasurementTypeView.ReferenceId;
                        }
                        else if (apiNutrient.Unit.ToLowerInvariant() == "mg")
                        {
                            measurementTypeId = _mgMeasurementType?.ReferenceId ?? _unknownMeasurementTypeView.ReferenceId;
                        }
                        else if (apiNutrient.Unit.ToLowerInvariant() == "g" || apiNutrient.Unit.ToLowerInvariant() == "gram")
                        {
                            measurementTypeId = _gramMeasurementType?.ReferenceId ?? _unknownMeasurementTypeView.ReferenceId;
                        }
                        else
                        {
                            _logger.LogWarning("Measurement unit '{ApiUnit}' from FDC API not found in pre-seeded types for Nutrient '{ApiNutrientName}'. Assigning 'Unknown'.", apiNutrient.Unit, apiNutrient.NutrientInfo.Name);
                        }
                    }


                    // Check if this IngredientNutrient relationship already exists to prevent duplicates
                    var existingLink = await _dbContext.IngredientNutrients
                                                       .AnyAsync(link => link.IngredientId == ingredient.Id && link.NutrientId == nutrient.Id);

                    if (!existingLink)
                    {
                        var newIngredientNutrient = new IngredientNutrientEntity
                        {
                            IngredientId = ingredient.Id,
                            NutrientId = nutrient.Id, // Corrected typo here
                            // Amount is usually per 100g. If your system requires per-serving, conversion needed here.
                            // Assuming API returns per 100g, and your system also uses 100g as default per-ingredient basis.
                            Measurement = apiNutrient.Amount, // Use the actual amount from FDC API
                            MeasurementTypeId = measurementTypeId, // Use the actual unit's ID from FDC
                            // CreatedByPersonId will be set by ApplyAuditInformation.
                        };
                        ingredientNutrients.Add(newIngredientNutrient);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing FDC API nutrient '{ApiNutrientName}' for ingredient '{IngredientName}'.", apiNutrient.NutrientInfo.Name, ingredient.Name);
                }
            }

            // Add any remaining core nutrients with 0 values if they weren't returned by the API
            await EnsureCoreNutrientsAreAssociated(ingredient, ingredientNutrients);


            // If any new IngredientNutrient entities were created, add them to the context
            // We check for default(long) which is 0 for Id, indicating it's a new entity not yet saved.
            if (ingredientNutrients.Any(inn => inn.Id == default(long))) // Assuming BaseEntity provides 'Id'
            {
                await _dbContext.IngredientNutrients.AddRangeAsync(ingredientNutrients.Where(inn => inn.Id == default(long)));
                // Note: We don't SaveChangesAsync here. The calling service (IngredientParsingService)
                // will save all pending changes as part of the overall recipe/ingredient creation transaction.
            }

            return ingredientNutrients;
        }

        /// <summary>
        /// Ensures that core nutrients (Calories, Protein, Fat, Carbohydrates) are associated
        /// with an ingredient, creating them with a value of 0 if they haven't been
        /// retrieved from the external API or already exist.
        /// </summary>
        /// <param name="ingredient">The ingredient to check.</param>
        /// <param name="currentIngredientNutrients">The list of already associated ingredient-nutrients.</param>
        private async Task EnsureCoreNutrientsAreAssociated(IngredientEntity ingredient, List<IngredientNutrientEntity> currentIngredientNutrients)
        {
            var coreNutrientNames = new[] { "Calories", "Protein", "Fat", "Carbohydrates" };
            foreach (var nutrientName in coreNutrientNames)
            {
                // Check if this core nutrient is already in our current list (from API or previous pass)
                if (currentIngredientNutrients.Any(inn => inn.Nutrient?.Name.Equals(nutrientName, StringComparison.OrdinalIgnoreCase) == true))
                {
                    continue;
                }

                // Get or create the core nutrient entity
                // Provide a reasonable default unit for the GetOrCreateNutrientAsync method
                var nutrient = await GetOrCreateNutrientAsync(nutrientName,
                    nutrientName.Equals("Calories", StringComparison.OrdinalIgnoreCase) ? "kcal" : "g");

                if (nutrient == null)
                {
                    _logger.LogError("Could not get or create core nutrient '{NutrientName}' for fallback.", nutrientName);
                    continue;
                }

                // Check if the association already exists in the database
                var existingLink = await _dbContext.IngredientNutrients
                                                   .AnyAsync(link => link.IngredientId == ingredient.Id && link.NutrientId == nutrient.Id);

                if (!existingLink)
                {
                    currentIngredientNutrients.Add(new IngredientNutrientEntity
                    {
                        IngredientId = ingredient.Id,
                        NutrientId = nutrient.Id, // Corrected typo here
                        Measurement = 0, // Default to 0 if not from API
                        MeasurementTypeId = nutrient.DefaultMeasurementTypeId, // Use default or unknown
                        // CreatedByPersonId will be set by ApplyAuditInformation.
                    });
                }
            }
        }

        /// <summary>
        /// Cleans and standardizes nutrient names returned by the USDA FoodData Central API.
        /// (e.g., "Total lipid (fat)" -> "Fat", "Energy" -> "Calories", "Carbohydrate, by difference" -> "Carbohydrates", "Sodium, Na" -> "Sodium")
        /// </summary>
        /// <param name="apiNutrientName">The raw nutrient name from the FDC API.</param>
        /// <returns>A standardized nutrient name.</returns>
        private string CleanNutrientNameFromApi(string apiNutrientName)
        {
            string cleanedName = apiNutrientName.Trim();

            // Remove parenthetical descriptions (e.g., "Sodium, Na" -> "Sodium")
            cleanedName = Regex.Replace(cleanedName, @"\s*,\s*[a-zA-Z\s]*", "").Trim(); // Catches ", Na" or ", by difference"
            cleanedName = Regex.Replace(cleanedName, @"\s*\([^)]*\)", "").Trim(); // Catches "(fat)"

            // Specific mappings for common FDC variations
            if (cleanedName.Equals("Total lipid", StringComparison.OrdinalIgnoreCase)) return "Fat";
            if (cleanedName.Equals("Carbohydrate", StringComparison.OrdinalIgnoreCase)) return "Carbohydrates";
            if (cleanedName.Equals("Energy", StringComparison.OrdinalIgnoreCase)) return "Calories"; // FDC often uses "Energy" for "Calories"
            if (cleanedName.Equals("Sugars", StringComparison.OrdinalIgnoreCase)) return "Sugar";
            if (cleanedName.Equals("Fatty acids, total saturated", StringComparison.OrdinalIgnoreCase)) return "Saturated Fat";
            if (cleanedName.Equals("Cholesterol", StringComparison.OrdinalIgnoreCase)) return "Cholesterol";
            if (cleanedName.Equals("Sodium", StringComparison.OrdinalIgnoreCase)) return "Sodium";
            if (cleanedName.Equals("Fiber, total dietary", StringComparison.OrdinalIgnoreCase)) return "Fiber";
            if (cleanedName.Equals("Protein", StringComparison.OrdinalIgnoreCase)) return "Protein";

            // If a numeric identifier is part of the name (e.g., "Vitamin D (D2 + D3)", or specific nutrient IDs)
            // Regex to remove anything that looks like a database-specific identifier or additional info after a number/short string
            cleanedName = Regex.Replace(cleanedName, @"\s+\[[A-Za-z0-9]+\]$|\s+NDB_No\.\s*\d+", "", RegexOptions.IgnoreCase).Trim();


            // Capitalize first letter of each word (simple common capitalization, not full title case)
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            cleanedName = textInfo.ToTitleCase(cleanedName.ToLower());


            return cleanedName;
        }
    }
}
