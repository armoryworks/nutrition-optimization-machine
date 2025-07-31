using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Import.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using System.Globalization;
using Nom.Data;
using Nom.Data.Recipe;
using Microsoft.EntityFrameworkCore;

namespace Nom.Import.Services
{
    /// <summary>
    /// Service for AI-powered enhancement of ingredient data.
    /// Extracts ingredient data to CSV, processes with AI, and updates the database.
    /// </summary>
    public class AiIngredientEnhancementService
    {
        private readonly ILogger<AiIngredientEnhancementService> _logger;
        private readonly ImportSettings _importSettings;
        private readonly IAiService _aiService;
        private readonly ApplicationDbContext _dbContext;
        private readonly string _tempDirectory;

        public AiIngredientEnhancementService(
            ILogger<AiIngredientEnhancementService> logger,
            IOptions<ImportSettings> importSettings,
            IAiService aiService,
            ApplicationDbContext dbContext)
        {
            _logger = logger;
            _importSettings = importSettings.Value;
            _aiService = aiService;
            _dbContext = dbContext;
            _tempDirectory = Path.Combine(_importSettings.SourceDirectory, "temp");
        }

        /// <summary>
        /// Enhances ingredient data using AI processing.
        /// </summary>
        public async Task EnhanceIngredientsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting AI-powered ingredient enhancement process...");

            try
            {
                // Ensure temp directory exists
                Directory.CreateDirectory(_tempDirectory);

                // Step 1: Extract ingredients to CSV
                var csvFilePath = await ExtractIngredientsToCsvAsync(cancellationToken);

                // Step 2: Process with AI
                var enhancedData = await ProcessWithAiAsync(csvFilePath, cancellationToken);

                // Step 3: Update database with enhanced data
                await UpdateDatabaseWithEnhancedDataAsync(enhancedData, cancellationToken);

                // Step 4: Cleanup
                CleanupTempFiles(csvFilePath);

                _logger.LogInformation("AI-powered ingredient enhancement completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during AI-powered ingredient enhancement");
                throw;
            }
        }

        /// <summary>
        /// Extracts ingredient data from database to CSV format.
        /// </summary>
        private async Task<string> ExtractIngredientsToCsvAsync(CancellationToken cancellationToken)
        {
            var csvFilePath = Path.Combine(_tempDirectory, $"ingredients_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            
            _logger.LogInformation("Extracting ingredients to CSV: {FilePath}", csvFilePath);

            // Query ingredients from database
            var ingredients = await _dbContext.Ingredients
                .AsNoTracking()
                .Select(i => new IngredientExportModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    Description = i.Description ?? "",
                    FdcId = i.FdcId ?? "",
                    FdcDataType = i.FdcDataType
                })
                .ToListAsync(cancellationToken);

            // Write to CSV
            using var writer = new StreamWriter(csvFilePath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            
            await csv.WriteRecordsAsync(ingredients, cancellationToken);

            _logger.LogInformation("Extracted {Count} ingredients to CSV", ingredients.Count);
            return csvFilePath;
        }

        /// <summary>
        /// Processes the CSV data with AI to enhance ingredient names and descriptions.
        /// </summary>
        private async Task<List<EnhancedIngredientModel>> ProcessWithAiAsync(string csvFilePath, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing ingredients with AI...");

            var enhancedData = new List<EnhancedIngredientModel>();

            // Read the CSV file
            using var reader = new StreamReader(csvFilePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            
            var ingredients = csv.GetRecords<IngredientExportModel>().ToList();

            // Process in batches to avoid overwhelming the AI service
            var batchSize = _importSettings.AiEnhancement.BatchSize;
            for (int i = 0; i < ingredients.Count; i += batchSize)
            {
                var batch = ingredients.Skip(i).Take(batchSize).ToList();
                var enhancedBatch = await ProcessBatchWithAiAsync(batch, cancellationToken);
                enhancedData.AddRange(enhancedBatch);

                _logger.LogInformation("Processed batch {BatchNumber}/{TotalBatches}", 
                    (i / batchSize) + 1, (ingredients.Count + batchSize - 1) / batchSize);

                // Add delay between batches to respect rate limits
                if (i + batchSize < ingredients.Count && _importSettings.AiEnhancement.BatchDelayMs > 0)
                {
                    await Task.Delay(_importSettings.AiEnhancement.BatchDelayMs, cancellationToken);
                }
            }

            return enhancedData;
        }

        /// <summary>
        /// Processes a batch of ingredients with AI.
        /// </summary>
        private async Task<List<EnhancedIngredientModel>> ProcessBatchWithAiAsync(
            List<IngredientExportModel> batch, 
            CancellationToken cancellationToken)
        {
            var enhancedBatch = new List<EnhancedIngredientModel>();

            foreach (var ingredient in batch)
            {
                try
                {
                    var enhanced = await EnhanceSingleIngredientAsync(ingredient, cancellationToken);
                    enhancedBatch.Add(enhanced);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enhance ingredient {IngredientId}, using original data", ingredient.Id);
                    // Fallback to original data
                    enhancedBatch.Add(new EnhancedIngredientModel
                    {
                        Id = ingredient.Id,
                        OriginalName = ingredient.Name,
                        EnhancedName = ingredient.Name,
                        EnhancedDescription = ingredient.Description,
                        Aliases = new List<string> { ingredient.Name }
                    });
                }
            }

            return enhancedBatch;
        }

        /// <summary>
        /// Enhances a single ingredient using AI.
        /// </summary>
        private async Task<EnhancedIngredientModel> EnhanceSingleIngredientAsync(
            IngredientExportModel ingredient, 
            CancellationToken cancellationToken)
        {
            var prompt = CreateEnhancementPrompt(ingredient);
            
            var response = await _aiService.ProcessPromptAsync(prompt, cancellationToken);
            
            return ParseAiResponse(ingredient.Id, ingredient.Name, response);
        }

        /// <summary>
        /// Enhances a single ingredient with AI processing.
        /// </summary>
        public async Task<EnhancedIngredientModel> EnhanceIngredientAsync(IngredientEntity ingredient, string? customPrompt = null)
        {
            try
            {
                var prompt = customPrompt ?? CreateEnhancementPrompt(ingredient);
                var aiResponse = await _aiService.ProcessPromptAsync(prompt, CancellationToken.None);
                
                return ParseAiResponse(ingredient.Id, ingredient.Name, aiResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enhancing ingredient {IngredientId}: {IngredientName}", 
                    ingredient.Id, ingredient.Name);
                return new EnhancedIngredientModel
                {
                    Id = ingredient.Id,
                    OriginalName = ingredient.Name,
                    EnhancedName = ingredient.Name,
                    EnhancedDescription = ingredient.Description ?? "",
                    Aliases = new List<string>()
                };
            }
        }

        /// <summary>
        /// Creates a prompt for AI enhancement.
        /// </summary>
        /// <summary>
        /// Creates the enhancement prompt for an ingredient.
        /// </summary>
        public string CreateEnhancementPrompt(IngredientEntity ingredient)
        {
            return $@"
You are a nutrition data specialist adjusting ingredient details for recipes that will be shared across multiple recipes. The enhancedName should be as simple as possible without inferring additional non-supplied details. The description should be comprehensive, but not add any non-inferrable details. If any brand names are in the original, they can be placed in the aliases, but should not be included in the produced Name or Description.

CRITICAL: If the original name contains nutritional modifiers like 'lean', 'low fat', 'low sodium', 'fat-free', 'reduced fat', 'light', 'nonfat', 'low-calorie', 'diet', 'lite', 'no salt added', 'fortified', '95% lean', '2%', 'whole fat', 'whole milk', 'skim', '1%', 'reduced sodium', 'sugar-free', 'unsweetened', 'enriched', 'organic', 'grass-fed', 'free-range', 'cage-free', 'hormone-free', 'antibiotic-free', etc., these MUST be retained in the enhanced name as they significantly affect nutritional values. IMPORTANT: If a specific percentage is given (like '95% lean', '90% lean', '85% lean', '80% lean'), the exact percentage MUST be retained in the enhanced name as it affects cooking characteristics and nutritional content. Examples: 'Beef, ground, 95% lean meat / 5% fat' should become '95% Lean Ground Beef', 'Beef, ground, 90% lean meat / 10% fat' should become '90% Lean Ground Beef', 'Milk, fluid, 2% milkfat' should become '2% Milk'.

Original: {ingredient.Name} - {ingredient.Description}

Respond with ONLY a JSON object containing enhancedName, enhancedDescription, and aliases array. No other text.";
        }

        /// <summary>
        /// Creates the enhancement prompt for an ingredient export model.
        /// </summary>
        private string CreateEnhancementPrompt(IngredientExportModel ingredient)
        {
            return $@"
You are a nutrition data specialist adjusting ingredient details for recipes that will be shared across multiple recipes. The enhancedName should be as simple as possible without inferring additional non-supplied details. The description should be comprehensive, but not add any non-inferrable details. If any brand names are in the original, they can be placed in the aliases, but should not be included in the produced Name or Description.

CRITICAL: If the original name contains nutritional modifiers like 'lean', 'low fat', 'low sodium', 'fat-free', 'reduced fat', 'light', 'nonfat', 'low-calorie', 'diet', 'lite', 'no salt added', 'fortified', '95% lean', '2%', 'whole fat', 'whole milk', 'skim', '1%', 'reduced sodium', 'sugar-free', 'unsweetened', 'enriched', 'organic', 'grass-fed', 'free-range', 'cage-free', 'hormone-free', 'antibiotic-free', etc., these MUST be retained in the enhanced name as they significantly affect nutritional values. IMPORTANT: If a specific percentage is given (like '95% lean', '90% lean', '85% lean', '80% lean'), the exact percentage MUST be retained in the enhanced name as it affects cooking characteristics and nutritional content. Examples: 'Beef, ground, 95% lean meat / 5% fat' should become '95% Lean Ground Beef', 'Beef, ground, 90% lean meat / 10% fat' should become '90% Lean Ground Beef', 'Milk, fluid, 2% milkfat' should become '2% Milk'.

Original: {ingredient.Name} - {ingredient.Description}

Respond with ONLY a JSON object containing enhancedName, enhancedDescription, and aliases array. No other text.";
        }

        /// <summary>
        /// Parses the AI response into structured data.
        /// </summary>
        private EnhancedIngredientModel ParseAiResponse(long ingredientId, string originalName, string aiResponse)
        {
            try
            {
                // Clean up the response (remove markdown formatting if present)
                var cleanResponse = aiResponse.Trim();
                if (cleanResponse.StartsWith("```json"))
                {
                    cleanResponse = cleanResponse.Substring(7);
                }
                if (cleanResponse.EndsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                }
                cleanResponse = cleanResponse.Trim();

                var response = JsonSerializer.Deserialize<AiEnhancementResponse>(cleanResponse);
                
                return new EnhancedIngredientModel
                {
                    Id = ingredientId,
                    OriginalName = originalName,
                    EnhancedName = response?.enhancedName ?? originalName,
                    EnhancedDescription = response?.enhancedDescription ?? "",
                    Aliases = response?.aliases ?? new List<string> { originalName }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse AI response for ingredient {IngredientId}", ingredientId);
                throw;
            }
        }

        /// <summary>
        /// Updates the database with enhanced ingredient data.
        /// </summary>
        private async Task UpdateDatabaseWithEnhancedDataAsync(
            List<EnhancedIngredientModel> enhancedData, 
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Updating database with enhanced ingredient data...");

            var updatedCount = 0;
            var aliasCount = 0;

            foreach (var enhanced in enhancedData)
            {
                try
                {
                    // Get the ingredient from database
                    var ingredient = await _dbContext.Ingredients
                        .Include(i => i.Aliases)
                        .FirstOrDefaultAsync(i => i.Id == enhanced.Id, cancellationToken);

                    if (ingredient == null)
                    {
                        _logger.LogWarning("Ingredient {Id} not found in database", enhanced.Id);
                        continue;
                    }

                    // Update name if enabled and different
                    if (_importSettings.AiEnhancement.UpdateNames && 
                        !string.Equals(ingredient.Name, enhanced.EnhancedName, StringComparison.OrdinalIgnoreCase))
                    {
                        ingredient.Name = enhanced.EnhancedName;
                        updatedCount++;
                        _logger.LogDebug("Updated name for ingredient {Id}: {Original} -> {Enhanced}", 
                            ingredient.Id, enhanced.OriginalName, enhanced.EnhancedName);
                    }

                    // Update description if enabled and different
                    if (_importSettings.AiEnhancement.UpdateDescriptions && 
                        !string.Equals(ingredient.Description ?? "", enhanced.EnhancedDescription, StringComparison.OrdinalIgnoreCase))
                    {
                        ingredient.Description = enhanced.EnhancedDescription;
                        updatedCount++;
                        _logger.LogDebug("Updated description for ingredient {Id}", ingredient.Id);
                    }

                    // Add aliases
                    foreach (var aliasName in enhanced.Aliases)
                    {
                        if (string.IsNullOrWhiteSpace(aliasName))
                            continue;

                        // Check if alias already exists
                        var existingAlias = ingredient.Aliases.FirstOrDefault(a => 
                            string.Equals(a.AliasName, aliasName, StringComparison.OrdinalIgnoreCase));

                        if (existingAlias == null)
                        {
                            var newAlias = new Nom.Data.Recipe.IngredientAliasEntity
                            {
                                IngredientId = ingredient.Id,
                                AliasName = aliasName.Trim(),
                                SourceContext = "AI Enhancement"
                            };

                            ingredient.Aliases.Add(newAlias);
                            aliasCount++;
                            _logger.LogDebug("Added alias '{AliasName}' for ingredient {Id}", aliasName, ingredient.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update ingredient {Id} with enhanced data", enhanced.Id);
                }
            }

            // Save changes to database
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Database update completed: {UpdatedCount} ingredients updated, {AliasCount} aliases added", 
                updatedCount, aliasCount);
        }

        /// <summary>
        /// Cleans up temporary files.
        /// </summary>
        private void CleanupTempFiles(string csvFilePath)
        {
            try
            {
                if (File.Exists(csvFilePath))
                {
                    File.Delete(csvFilePath);
                    _logger.LogInformation("Cleaned up temporary CSV file: {FilePath}", csvFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temporary file: {FilePath}", csvFilePath);
            }
        }
    }

    #region Data Models

    public class IngredientExportModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FdcId { get; set; } = string.Empty;
        public string FdcDataType { get; set; } = string.Empty;
    }

    public class EnhancedIngredientModel
    {
        public long Id { get; set; }
        public string OriginalName { get; set; } = string.Empty;
        public string EnhancedName { get; set; } = string.Empty;
        public string EnhancedDescription { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = new List<string>();
    }

    public class AiEnhancementResponse
    {
        public string enhancedName { get; set; } = string.Empty;
        public string enhancedDescription { get; set; } = string.Empty;
        public List<string> aliases { get; set; } = new List<string>();
    }

    #endregion
} 