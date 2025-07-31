using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Import.Settings;
using System.Globalization;

namespace Nom.Import.Services
{
    public class FlavorProfileEnhancedImportService
    {
        private readonly ILogger<FlavorProfileEnhancedImportService> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ImportSettings _settings;
        private readonly AiIngredientEnhancementService _aiService;

        public FlavorProfileEnhancedImportService(
            ILogger<FlavorProfileEnhancedImportService> logger,
            ApplicationDbContext context,
            ImportSettings settings,
            AiIngredientEnhancementService aiService)
        {
            _logger = logger;
            _context = context;
            _settings = settings;
            _aiService = aiService;
        }

        public async Task ProcessFlavorProfileEnhancementAsync(string flavorProfileCsvPath)
        {
            _logger.LogInformation("Starting flavor profile enhanced import process");

            try
            {
                // Load flavor profile data
                var flavorProfiles = LoadFlavorProfileData(flavorProfileCsvPath);
                _logger.LogInformation("Loaded {Count} flavor profile entries", flavorProfiles.Count);

                // Get ingredients that need enhancement
                var ingredientsToEnhance = await GetIngredientsForEnhancementAsync();
                _logger.LogInformation("Found {Count} ingredients for enhancement", ingredientsToEnhance.Count);

                // Process in batches
                var batchSize = _settings.AiEnhancement.BatchSize;
                var processedCount = 0;

                for (int i = 0; i < ingredientsToEnhance.Count; i += batchSize)
                {
                    var batch = ingredientsToEnhance.Skip(i).Take(batchSize).ToList();
                    _logger.LogInformation("Processing batch {BatchNumber} of {TotalBatches}", 
                        (i / batchSize) + 1, (ingredientsToEnhance.Count + batchSize - 1) / batchSize);

                    await ProcessBatchWithFlavorProfilesAsync(batch, flavorProfiles);
                    processedCount += batch.Count;

                    // Delay between batches
                    if (i + batchSize < ingredientsToEnhance.Count)
                    {
                        await Task.Delay(_settings.AiEnhancement.BatchDelayMs);
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Flavor profile enhancement completed. Processed {Count} ingredients", processedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during flavor profile enhancement process");
                throw;
            }
        }

        private Dictionary<long, FlavorProfileData> LoadFlavorProfileData(string csvPath)
        {
            var flavorProfiles = new Dictionary<long, FlavorProfileData>();

            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null
            });

            foreach (var record in csv.GetRecords<dynamic>())
            {
                var id = long.Parse(record.Id.ToString());
                var group = record.Group?.ToString() ?? "";
                var rank = float.Parse(record.Rank?.ToString() ?? "1.0");

                flavorProfiles[id] = new FlavorProfileData
                {
                    Group = group,
                    Rank = rank
                };
            }

            return flavorProfiles;
        }

        private async Task<List<IngredientEntity>> GetIngredientsForEnhancementAsync()
        {
            return await _context.Ingredients
                .Where(i => i.CurationStatusId == 9000) // Imported ingredients
                .OrderBy(i => i.Id)
                .ToListAsync();
        }

        private async Task ProcessBatchWithFlavorProfilesAsync(
            List<IngredientEntity> ingredients, 
            Dictionary<long, FlavorProfileData> flavorProfiles)
        {
            foreach (var ingredient in ingredients)
            {
                try
                {
                    // Get flavor profile data if available
                    var flavorProfile = flavorProfiles.TryGetValue(ingredient.Id, out var profile) ? profile : null;
                    
                    // Create enhanced prompt with flavor profile context
                    var enhancedPrompt = CreateFlavorProfileEnhancedPrompt(ingredient, flavorProfile);
                    
                    // Process with AI
                    var aiResult = await _aiService.EnhanceIngredientAsync(ingredient, enhancedPrompt);
                    
                    if (aiResult != null)
                    {
                        // Update ingredient with AI enhancements
                        ingredient.Name = aiResult.EnhancedName;
                        ingredient.Description = aiResult.EnhancedDescription;
                        ingredient.LastModifiedDate = DateTime.UtcNow;

                        // Add flavor profile data if available
                        if (flavorProfile != null)
                        {
                            // Add flavor group as alias
                            if (!string.IsNullOrEmpty(flavorProfile.Group))
                            {
                                var groupAlias = new IngredientAliasEntity
                                {
                                    IngredientId = ingredient.Id,
                                    AliasName = flavorProfile.Group,
                                    SourceContext = "FlavorProfile",
                                    CreatedDate = DateTime.UtcNow
                                };
                                _context.IngredientAliases.Add(groupAlias);
                            }

                            // Add rank-based alias
                            var rankAlias = new IngredientAliasEntity
                            {
                                IngredientId = ingredient.Id,
                                AliasName = $"Rank_{flavorProfile.Rank}",
                                SourceContext = "FlavorProfileRank",
                                CreatedDate = DateTime.UtcNow
                            };
                            _context.IngredientAliases.Add(rankAlias);
                        }

                        // Add AI-generated aliases
                        foreach (var alias in aiResult.Aliases)
                        {
                            var aliasEntity = new IngredientAliasEntity
                            {
                                IngredientId = ingredient.Id,
                                AliasName = alias,
                                SourceContext = "AIEnhanced",
                                CreatedDate = DateTime.UtcNow
                            };
                            _context.IngredientAliases.Add(aliasEntity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing ingredient {IngredientId}: {IngredientName}", 
                        ingredient.Id, ingredient.Name);
                }
            }
        }

        private string CreateFlavorProfileEnhancedPrompt(IngredientEntity ingredient, FlavorProfileData? flavorProfile)
        {
            var basePrompt = _aiService.CreateEnhancementPrompt(ingredient);
            
            if (flavorProfile != null)
            {
                return $@"{basePrompt}

FLAVOR PROFILE CONTEXT:
- This ingredient belongs to the flavor group: {flavorProfile.Group}
- Recipe likelihood rank: {flavorProfile.Rank} (1.0 = highest likelihood in recipes)
- Consider this ingredient's role in recipes and its flavor profile when enhancing the name and description.
- Names should be optimized for recipe contexts and ingredient searches.
- If this is a high-ranking ingredient (rank 1.0-2.0), prioritize common, recognizable names.
- If this is a lower-ranking ingredient (rank 3.0+), consider more descriptive names that help users understand its use.";
            }
            
            return basePrompt;
        }
    }

    public class FlavorProfileData
    {
        public string Group { get; set; } = "";
        public float Rank { get; set; } = 1.0f;
    }
} 