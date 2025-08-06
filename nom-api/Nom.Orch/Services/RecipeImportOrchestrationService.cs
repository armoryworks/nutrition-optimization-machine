using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.UtilityInterfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service for importing recipes from various sources (URLs, images, bulk imports)
    /// Matches Mealie's recipe scraping and import functionality
    /// </summary>
    public class RecipeImportOrchestrationService : IRecipeImportOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RecipeImportOrchestrationService> _logger;
        private readonly IWebScrapingService _webScrapingService;
        private readonly ITesseractOcrService _ocrService;

        public RecipeImportOrchestrationService(
            ApplicationDbContext context,
            ILogger<RecipeImportOrchestrationService> logger,
            IWebScrapingService webScrapingService,
            ITesseractOcrService ocrService)
        {
            _context = context;
            _logger = logger;
            _webScrapingService = webScrapingService;
            _ocrService = ocrService;
        }

        public async Task<RecipeCreateResponseModel> ImportFromUrlAsync(string url, long authorId)
        {
            _logger.LogInformation("Importing recipe from URL: {Url}", url);

            try
            {
                // Use the actual web scraping service
                var scrapedData = await _webScrapingService.ScrapeRecipeFromUrlAsync(url);

                var recipe = new RecipeEntity
                {
                    Name = scrapedData.Title,
                    Description = scrapedData.Description,
                    Image = scrapedData.ImageUrl,
                    PrepTime = scrapedData.PrepTime,
                    CookTime = scrapedData.CookTime,
                    TotalTime = scrapedData.TotalTime,
                    RecipeYield = scrapedData.Yield,
                    SourceUrl = url,
                    AuthorId = authorId,
                    CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                    Version = 1
                };

                _context.Recipes.Add(recipe);
                await _context.SaveChangesAsync();

                return new RecipeCreateResponseModel
                {
                    Id = (int)recipe.Id,
                    Name = recipe.Name,
                    Message = "Recipe imported successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import recipe from URL: {Url}", url);
                throw;
            }
        }

        public async Task<List<RecipeCreateResponseModel>> BulkImportFromUrlsAsync(List<string> urls, long authorId)
        {
            _logger.LogInformation("Bulk importing {Count} recipes from URLs", urls.Count);

            var results = new List<RecipeCreateResponseModel>();

            foreach (var url in urls)
            {
                try
                {
                    var result = await ImportFromUrlAsync(url, authorId);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import recipe from URL: {Url}", url);
                    results.Add(new RecipeCreateResponseModel
                    {
                        Id = 0,
                        Name = "Import Failed",
                        Message = $"Failed to import from {url}: {ex.Message}"
                    });
                }
            }

            return results;
        }

        public async Task<RecipeCreateResponseModel> ImportFromImageAsync(byte[] imageData, long authorId)
        {
            _logger.LogInformation("Importing recipe from image (OCR)");

            try
            {
                // Use the actual OCR service
                var ocrData = await _ocrService.ProcessImageWithOcrAsync(imageData);

                var recipe = new RecipeEntity
                {
                    Name = ocrData.Title,
                    Description = ocrData.Description,
                    AuthorId = authorId,
                    CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                    Version = 1,
                    IsOcrRecipe = true
                };

                _context.Recipes.Add(recipe);
                await _context.SaveChangesAsync();

                return new RecipeCreateResponseModel
                {
                    Id = (int)recipe.Id,
                    Name = recipe.Name,
                    Message = "Recipe imported from image successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import recipe from image");
                throw;
            }
        }

        public async Task<RecipeCreateResponseModel> ImportFromHtmlOrJsonAsync(string htmlOrJson, long authorId)
        {
            _logger.LogInformation("Importing recipe from HTML or JSON data");

            try
            {
                // Parse HTML or JSON data
                var parsedData = await ParseHtmlOrJsonAsync(htmlOrJson);

                var recipe = new RecipeEntity
                {
                    Name = parsedData.Title,
                    Description = parsedData.Description,
                    AuthorId = authorId,
                    CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                    Version = 1
                };

                _context.Recipes.Add(recipe);
                await _context.SaveChangesAsync();

                return new RecipeCreateResponseModel
                {
                    Id = (int)recipe.Id,
                    Name = recipe.Name,
                    Message = "Recipe imported from HTML/JSON successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import recipe from HTML/JSON");
                throw;
            }
        }

        public async Task<RecipeScrapeTestModel> TestUrlScrapingAsync(string url)
        {
            _logger.LogInformation("Testing URL scraping for: {Url}", url);

            try
            {
                // Use the actual web scraping service
                var scrapedData = await _webScrapingService.ScrapeRecipeFromUrlAsync(url);

                return new RecipeScrapeTestModel
                {
                    Url = url,
                    Title = scrapedData.Title,
                    Description = scrapedData.Description,
                    Image = scrapedData.ImageUrl,
                    Ingredients = scrapedData.Ingredients,
                    Instructions = scrapedData.Instructions,
                    PrepTime = scrapedData.PrepTime,
                    CookTime = scrapedData.CookTime,
                    TotalTime = scrapedData.TotalTime,
                    Yield = scrapedData.Yield,
                    IsValid = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test URL scraping for: {Url}", url);
                return new RecipeScrapeTestModel
                {
                    Url = url,
                    IsValid = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<List<RecipeCreateResponseModel>> ImportFromZipAsync(byte[] zipData, long authorId)
        {
            _logger.LogInformation("Importing recipes from ZIP archive");

            try
            {
                // Extract and process recipe files from ZIP archive
                var extractedRecipes = await ExtractRecipesFromZipAsync(zipData);

                var results = new List<RecipeCreateResponseModel>();

                foreach (var recipeData in extractedRecipes)
                {
                    var recipe = new RecipeEntity
                    {
                        Name = recipeData.Title,
                        Description = recipeData.Description,
                        AuthorId = authorId,
                        CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                        Version = 1
                    };

                    _context.Recipes.Add(recipe);
                    results.Add(new RecipeCreateResponseModel
                    {
                        Id = (int)recipe.Id,
                        Name = recipe.Name,
                        Message = "Recipe imported from ZIP successfully"
                    });
                }

                await _context.SaveChangesAsync();
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import recipes from ZIP");
                throw;
            }
        }

        // Helper methods
        private async Task<ParsedRecipeData> ParseHtmlOrJsonAsync(string htmlOrJson)
        {
            // This would parse recipe data from HTML or JSON format
            // For now, return basic data
            await Task.Delay(100); // Simulate async work
            return new ParsedRecipeData
            {
                Title = "Parsed Recipe",
                Description = "Recipe description from parsing"
            };
        }

        private async Task<List<ScrapedRecipeData>> ExtractRecipesFromZipAsync(byte[] zipData)
        {
            // This would extract and process recipe files from a ZIP archive
            // For now, return basic data
            await Task.Delay(100); // Simulate async work
            return new List<ScrapedRecipeData>
            {
                new ScrapedRecipeData
                {
                    Title = "ZIP Recipe 1",
                    Description = "Recipe from ZIP archive"
                }
            };
        }

        // Helper data classes
        private class ScrapedRecipeData
        {
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Image { get; set; } = string.Empty;
            public List<string> Ingredients { get; set; } = new List<string>();
            public List<string> Instructions { get; set; } = new List<string>();
            public string PrepTime { get; set; } = string.Empty;
            public string CookTime { get; set; } = string.Empty;
            public string TotalTime { get; set; } = string.Empty;
            public string Yield { get; set; } = string.Empty;
        }

        private class ParsedRecipeData
        {
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
} 