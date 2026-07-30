using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Data.Recipe;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service for scraping recipes from URLs and HTML data
    /// </summary>
    public class RecipeScrapingService : IRecipeScrapingService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<RecipeScrapingService> _logger;
        private readonly HttpClient _httpClient;

        public RecipeScrapingService(
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            ICurrentUserService currentUser,
            ILogger<RecipeScrapingService> logger,
            HttpClient httpClient)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Scrape a recipe from a URL
        /// </summary>
        public async Task<RecipeScrapingResponseModel> ScrapeRecipeFromUrlAsync(RecipeScrapingRequestModel request)
        {
            try
            {
                _logger.LogInformation("Starting recipe scraping from URL: {Url}", request.Url);

                // Extract URL using regex (similar to Mealie)
                var extractedUrl = ExtractUrl(request.Url);
                if (string.IsNullOrEmpty(extractedUrl))
                {
                    return new RecipeScrapingResponseModel
                    {
                        Success = false,
                        Error = "Invalid URL format"
                    };
                }

                // Fetch HTML content
                var html = await FetchHtmlAsync(extractedUrl);
                if (string.IsNullOrEmpty(html))
                {
                    return new RecipeScrapingResponseModel
                    {
                        Success = false,
                        Error = "Failed to fetch HTML content"
                    };
                }

                // Parse recipe data
                var scrapedRecipe = await ParseRecipeFromHtmlAsync(html, extractedUrl);
                if (scrapedRecipe == null)
                {
                    return new RecipeScrapingResponseModel
                    {
                        Success = false,
                        Error = "Failed to parse recipe data"
                    };
                }

                // Create recipe in database
                var recipeEntity = await CreateRecipeFromScrapedDataAsync(scrapedRecipe, request);

                return new RecipeScrapingResponseModel
                {
                    RecipeId = recipeEntity.Id,
                    RecipeName = recipeEntity.Name,
                    Message = "Recipe successfully scraped and created",
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping recipe from URL: {Url}", request.Url);
                return new RecipeScrapingResponseModel
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Scrape a recipe from HTML or JSON data
        /// </summary>
        public async Task<RecipeScrapingResponseModel> ScrapeRecipeFromDataAsync(RecipeScrapingDataRequestModel request)
        {
            try
            {
                _logger.LogInformation("Starting recipe scraping from data");

                ScrapedRecipeModel? scrapedRecipe;

                // Check if data is JSON
                if (request.Data.TrimStart().StartsWith("{"))
                {
                    scrapedRecipe = ParseRecipeFromJsonAsync(request.Data);
                }
                else
                {
                    // Parse as HTML
                    scrapedRecipe = await ParseRecipeFromHtmlAsync(request.Data, null);
                }

                if (scrapedRecipe == null)
                {
                    return new RecipeScrapingResponseModel
                    {
                        Success = false,
                        Error = "Failed to parse recipe data"
                    };
                }

                // Create recipe in database
                var recipeEntity = await CreateRecipeFromScrapedDataAsync(scrapedRecipe, new RecipeScrapingRequestModel
                {
                    ImportKeywordsAsTags = request.ImportKeywordsAsTags,
                    StayInEditMode = request.StayInEditMode
                });

                return new RecipeScrapingResponseModel
                {
                    RecipeId = recipeEntity.Id,
                    RecipeName = recipeEntity.Name,
                    Message = "Recipe successfully scraped and created",
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping recipe from data");
                return new RecipeScrapingResponseModel
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Test recipe scraping from a URL
        /// </summary>
        public async Task<ScrapedRecipeModel> TestScrapeRecipeAsync(RecipeScrapingTestRequestModel request)
        {
            try
            {
                _logger.LogInformation("Testing recipe scraping from URL: {Url}", request.Url);

                var extractedUrl = ExtractUrl(request.Url);
                if (string.IsNullOrEmpty(extractedUrl))
                {
                    throw new ArgumentException("Invalid URL format");
                }

                var html = await FetchHtmlAsync(extractedUrl);
                if (string.IsNullOrEmpty(html))
                {
                    throw new InvalidOperationException("Failed to fetch HTML content");
                }

                var scrapedRecipe = await ParseRecipeFromHtmlAsync(html, extractedUrl);
                if (scrapedRecipe == null)
                {
                    throw new InvalidOperationException("Failed to parse recipe data");
                }

                return scrapedRecipe;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing recipe scraping from URL: {Url}", request.Url);
                throw;
            }
        }

        /// <summary>
        /// Bulk scrape recipes from multiple URLs
        /// </summary>
        public async Task<RecipeBulkScrapingResponseModel> BulkScrapeRecipesAsync(RecipeBulkScrapingRequestModel request)
        {
            var report = new RecipeBulkScrapingResponseModel
            {
                ReportId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TotalProcessed = request.Imports.Count,
                Results = new List<RecipeScrapingResponseModel>()
            };

            var semaphore = new SemaphoreSlim(3, 3); // Limit concurrent requests

            var tasks = request.Imports.Select(async import =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel
                    {
                        Url = import.Url,
                        ImportKeywordsAsTags = false,
                        StayInEditMode = false
                    });

                    // Add tags and categories if provided
                    if (result.Success && (import.Tags?.Any() == true || import.Categories?.Any() == true))
                    {
                        await AddTagsAndCategoriesAsync(result.RecipeId, import.Tags, import.Categories);
                    }

                    return result;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            report.Results.AddRange(results);
            report.SuccessCount = results.Count(r => r.Success);
            report.ErrorCount = results.Count(r => !r.Success);

            return report;
        }

        /// <summary>
        /// Get scraping report by ID
        /// </summary>
        public async Task<RecipeBulkScrapingResponseModel?> GetScrapingReportAsync(long reportId)
        {
            try
            {
                var report = await _dbContext.ScrapingReports
                    .FirstOrDefaultAsync(r => r.Id == reportId);

                if (report == null)
                {
                    _logger.LogWarning("Scraping report {ReportId} not found", reportId);
                    return null;
                }

                return new RecipeBulkScrapingResponseModel
                {
                    Id = report.Id,
                    Status = report.Status,
                    TotalUrls = report.TotalUrls,
                    SuccessfulScrapes = report.SuccessfulScrapes,
                    FailedScrapes = report.FailedScrapes,
                    CreatedDate = report.CreatedDate,
                    CompletedDate = report.CompletedDate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scraping report {ReportId}", reportId);
                return null;
            }
        }

        /// <summary>
        /// Get all scraping reports for the current user
        /// </summary>
        public async Task<List<RecipeBulkScrapingResponseModel>> GetScrapingReportsAsync()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var reports = await _dbContext.ScrapingReports
                    .Where(r => r.UserId == currentUserId)
                    .OrderByDescending(r => r.CreatedDate)
                    .ToListAsync();

                var result = new List<RecipeBulkScrapingResponseModel>();

                foreach (var report in reports)
                {
                    result.Add(new RecipeBulkScrapingResponseModel
                    {
                        Id = report.Id,
                        Status = report.Status,
                        TotalUrls = report.TotalUrls,
                        SuccessfulScrapes = report.SuccessfulScrapes,
                        FailedScrapes = report.FailedScrapes,
                        CreatedDate = report.CreatedDate,
                        CompletedDate = report.CompletedDate
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scraping reports for user");
                return new List<RecipeBulkScrapingResponseModel>();
            }
        }

        #region Private Methods

        private string? ExtractUrl(string input)
        {
            var match = Regex.Match(input, @"(https?://|www\.)[^\s]+");
            return match.Success ? match.Value : null;
        }

        private async Task<string?> FetchHtmlAsync(string url)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching HTML from URL: {Url}", url);
                return null;
            }
        }

        private async Task<ScrapedRecipeModel?> ParseRecipeFromHtmlAsync(string html, string? sourceUrl)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var recipe = new ScrapedRecipeModel
                {
                    SourceUrl = sourceUrl,
                    SourceSite = ExtractDomain(sourceUrl)
                };

                // Try to find JSON-LD structured data first
                var jsonLdNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
                if (jsonLdNodes != null)
                {
                    foreach (var node in jsonLdNodes)
                    {
                        try
                        {
                            var jsonData = JsonSerializer.Deserialize<JsonElement>(node.InnerText);
                            if (ParseJsonLdRecipe(jsonData, recipe))
                            {
                                return recipe;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error parsing JSON-LD data");
                        }
                    }
                }

                // Fallback to HTML parsing
                ParseHtmlRecipe(doc, recipe);

                return recipe;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing recipe from HTML");
                return null;
            }
        }

        private ScrapedRecipeModel? ParseRecipeFromJsonAsync(string jsonData)
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
                var recipe = new ScrapedRecipeModel();
                return ParseJsonLdRecipe(jsonElement, recipe) ? recipe : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing recipe from JSON");
                return null;
            }
        }

        private bool ParseJsonLdRecipe(JsonElement json, ScrapedRecipeModel recipe)
        {
            try
            {
                if (json.TryGetProperty("@type", out var type) && type.GetString() == "Recipe")
                {
                    if (json.TryGetProperty("name", out var name))
                        recipe.Name = name.GetString() ?? string.Empty;

                    if (json.TryGetProperty("description", out var description))
                        recipe.Description = description.GetString();

                    if (json.TryGetProperty("image", out var image))
                    {
                        if (image.ValueKind == JsonValueKind.Array)
                        {
                            recipe.Image = image.EnumerateArray().FirstOrDefault().GetString();
                        }
                        else
                        {
                            recipe.Image = image.GetString();
                        }
                    }

                    if (json.TryGetProperty("prepTime", out var prepTime))
                        recipe.PrepTime = prepTime.GetString();

                    if (json.TryGetProperty("cookTime", out var cookTime))
                        recipe.CookTime = cookTime.GetString();

                    if (json.TryGetProperty("totalTime", out var totalTime))
                        recipe.TotalTime = totalTime.GetString();

                    if (json.TryGetProperty("recipeYield", out var recipeYield))
                        recipe.RecipeYield = recipeYield.GetString();

                    if (json.TryGetProperty("recipeServings", out var recipeServings))
                    {
                        if (decimal.TryParse(recipeServings.GetString(), out var servings))
                            recipe.RecipeServings = servings;
                    }

                    // Parse ingredients
                    if (json.TryGetProperty("recipeIngredient", out var ingredients))
                    {
                        foreach (var ingredient in ingredients.EnumerateArray())
                        {
                            recipe.Ingredients.Add(new ScrapedIngredientModel
                            {
                                Name = ingredient.GetString() ?? string.Empty
                            });
                        }
                    }

                    // Parse instructions
                    if (json.TryGetProperty("recipeInstructions", out var instructions))
                    {
                        var order = 1;
                        foreach (var instruction in instructions.EnumerateArray())
                        {
                            if (instruction.TryGetProperty("text", out var text))
                            {
                                recipe.Steps.Add(new ScrapedStepModel
                                {
                                    Order = order++,
                                    Instruction = text.GetString() ?? string.Empty
                                });
                            }
                            else
                            {
                                recipe.Steps.Add(new ScrapedStepModel
                                {
                                    Order = order++,
                                    Instruction = instruction.GetString() ?? string.Empty
                                });
                            }
                        }
                    }

                    return !string.IsNullOrEmpty(recipe.Name);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing JSON-LD recipe");
                return false;
            }
        }

        private void ParseHtmlRecipe(HtmlDocument doc, ScrapedRecipeModel recipe)
        {
            // Basic HTML parsing fallback
            var titleNode = doc.DocumentNode.SelectSingleNode("//h1") ??
                           doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
                recipe.Name = titleNode.InnerText.Trim();

            var descriptionNode = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
            if (descriptionNode != null)
                recipe.Description = descriptionNode.GetAttributeValue("content", "");

            var imageNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
            if (imageNode != null)
                recipe.Image = imageNode.GetAttributeValue("content", "");

            // Basic ingredient parsing (look for common patterns)
            var ingredientNodes = doc.DocumentNode.SelectNodes("//li[contains(text(), 'cup') or contains(text(), 'tbsp') or contains(text(), 'tsp')]");
            if (ingredientNodes != null)
            {
                foreach (var node in ingredientNodes.Take(20)) // Limit to first 20
                {
                    recipe.Ingredients.Add(new ScrapedIngredientModel
                    {
                        Name = node.InnerText.Trim()
                    });
                }
            }
        }

        private string? ExtractDomain(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return null;
            }
        }

        private async Task<RecipeEntity> CreateRecipeFromScrapedDataAsync(ScrapedRecipeModel scrapedRecipe, RecipeScrapingRequestModel request)
        {
            var recipe = new RecipeEntity
            {
                Name = string.IsNullOrEmpty(scrapedRecipe.Name) ? "Untitled Recipe" : scrapedRecipe.Name,
                Description = scrapedRecipe.Description,
                SourceUrl = scrapedRecipe.SourceUrl,
                SourceSite = scrapedRecipe.SourceSite,
                PrepTime = scrapedRecipe.PrepTime,
                CookTime = scrapedRecipe.CookTime,
                TotalTime = scrapedRecipe.TotalTime,
                RecipeYield = scrapedRecipe.RecipeYield,
                RecipeYieldQuantity = scrapedRecipe.RecipeYieldQuantity,
                RecipeServings = scrapedRecipe.RecipeServings,
                Image = scrapedRecipe.Image ?? string.Empty,
                AuthorId = GetCurrentPersonId() ?? 1,
                CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                Version = 1,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = GetCurrentPersonId()
            };

            _dbContext.Recipes.Add(recipe);
            await _dbContext.SaveChangesAsync();

            // Add ingredients
            foreach (var ingredient in scrapedRecipe.Ingredients)
            {
                // First, find or create the ingredient
                var ingredientEntity = await _dbContext.Ingredients
                    .FirstOrDefaultAsync(i => i.Name.Equals(ingredient.Name, StringComparison.OrdinalIgnoreCase));

                if (ingredientEntity == null)
                {
                    ingredientEntity = new IngredientEntity
                    {
                        Name = ingredient.Name,
                        CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = GetCurrentPersonId()
                    };
                    _dbContext.Ingredients.Add(ingredientEntity);
                    await _dbContext.SaveChangesAsync();
                }

                // Find measurement
                var measurement = await _dbContext.Measurements
                    .Where(m => ingredient.Unit != null && m.Name.ToLower() == ingredient.Unit.ToLower())
                    .FirstOrDefaultAsync();

                var recipeIngredient = new RecipeIngredientEntity
                {
                    RecipeId = recipe.Id,
                    IngredientId = ingredientEntity.Id,
                    Quantity = ingredient.Quantity ?? 1,
                    MeasurementId = measurement?.Id ?? 1, // Default measurement
                    RawLine = ingredient.Notes ?? ingredient.Name
                };
                _dbContext.RecipeIngredients.Add(recipeIngredient);
            }

            // Add steps
            foreach (var step in scrapedRecipe.Steps)
            {
                var recipeStep = new RecipeStepEntity
                {
                    RecipeId = recipe.Id,
                    Summary = step.Instruction,
                    Description = step.Instruction,
                    StepNumber = step.Order ?? 1
                };
                _dbContext.RecipeSteps.Add(recipeStep);
            }

            await _dbContext.SaveChangesAsync();

            return recipe;
        }

        private async Task AddTagsAndCategoriesAsync(long recipeId, List<string>? tags, List<string>? categories)
        {
            try
            {
                // Add tags
                if (tags != null && tags.Any())
                {
                    foreach (var tagName in tags)
                    {
                        // Find or create tag
                        var tag = await _dbContext.Tags
                            .FirstOrDefaultAsync(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));

                        if (tag == null)
                        {
                            tag = new TagEntity
                            {
                                Name = tagName,
                                CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                                CreatedDate = DateTime.UtcNow,
                                CreatedByPersonId = GetCurrentPersonId()
                            };
                            _dbContext.Tags.Add(tag);
                            await _dbContext.SaveChangesAsync();
                        }

                        // Create recipe-tag relationship
                        var recipeTag = new RecipeTagEntity
                        {
                            RecipeId = recipeId,
                            TagId = tag.Id
                        };
                        _dbContext.RecipeTags.Add(recipeTag);
                    }
                }

                // Add categories
                if (categories != null && categories.Any())
                {
                    foreach (var categoryName in categories)
                    {
                        // Find or create category
                        var category = await _dbContext.Categories
                            .FirstOrDefaultAsync(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

                        if (category == null)
                        {
                            category = new CategoryEntity
                            {
                                Name = categoryName,
                                CurationStatusId = (long)CurationStatusEnum.NonCurated, // Default to NonCurated
                                CreatedDate = DateTime.UtcNow,
                                CreatedByPersonId = GetCurrentPersonId()
                            };
                            _dbContext.Categories.Add(category);
                            await _dbContext.SaveChangesAsync();
                        }

                        // Create recipe-category relationship
                        var recipeCategory = new RecipeCategoryEntity
                        {
                            RecipeId = recipeId,
                            CategoryId = category.Id
                        };
                        _dbContext.RecipeCategories.Add(recipeCategory);
                    }
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Added tags and categories to recipe {RecipeId}: Tags={Tags}, Categories={Categories}",
                    recipeId, string.Join(",", tags ?? new List<string>()), string.Join(",", categories ?? new List<string>()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding tags and categories to recipe {RecipeId}", recipeId);
            }
        }

        // Note: the previous implementation long.TryParse'd the GUID user id and
        // therefore threw for every authenticated user.
        private string GetCurrentUserId() => _currentUser.RequiredUserId;

        private long? GetCurrentPersonId() => _currentUser.PersonId;

        #endregion
    }
}