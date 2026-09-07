using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.UtilityInterfaces;

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
        private readonly IRecipeScrapingService _recipeScraping;
        private readonly ITesseractOcrService _ocrService;

        public RecipeImportOrchestrationService(
            ApplicationDbContext context,
            ILogger<RecipeImportOrchestrationService> logger,
            IRecipeScrapingService recipeScraping,
            ITesseractOcrService ocrService)
        {
            _context = context;
            _logger = logger;
            _recipeScraping = recipeScraping;
            _ocrService = ocrService;
        }

        public async Task<RecipeCreateResponseModel> ImportFromUrlAsync(string url, long authorId)
        {
            _logger.LogInformation("Importing recipe from URL: {Url}", url);

            // Delegate to the scraping pipeline so URL imports get the same
            // whitelist gate, vetting, provenance, and copyright handling.
            var result = await _recipeScraping.ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel
            {
                Url = url,
            });

            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error ?? "Import failed.");
            }

            return new RecipeCreateResponseModel
            {
                Id = (int)result.RecipeId,
                Name = result.RecipeName,
                Message = result.Message,
            };
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
                    IsOcrRecipe = true,
                    PrepTime = Clamp(ocrData.PrepTime, 100),
                    CookTime = Clamp(ocrData.CookTime, 100),
                    TotalTime = Clamp(ocrData.TotalTime, 100),
                    RecipeYield = Clamp(ocrData.Yield, 100),
                    // The steps below are transcribed verbatim from someone's
                    // cookbook page, so the same public-listing gate the scraper
                    // applies to source prose applies here.
                    ContainsSourceProse = ocrData.Instructions.Count > 0,
                };

                _context.Recipes.Add(recipe);
                await _context.SaveChangesAsync();

                var stepCount = AddSteps(recipe.Id, ocrData.Instructions);
                var (matched, unmatched) = await AddIngredientLinesAsync(recipe.Id, ocrData.Ingredients);
                await _context.SaveChangesAsync();

                return new RecipeCreateResponseModel
                {
                    Id = (int)recipe.Id,
                    Name = recipe.Name,
                    Message = BuildImportSummary(stepCount, matched, unmatched),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import recipe from image");
                throw;
            }
        }

        private const long DefaultMeasurementId = 1L;

        private static string? Clamp(string? value, int maxLength) =>
            string.IsNullOrWhiteSpace(value) ? null
                : value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

        private int AddSteps(long recipeId, IEnumerable<string> instructions)
        {
            var stepNumber = 1;
            foreach (var instruction in instructions.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                _context.RecipeSteps.Add(new RecipeStepEntity
                {
                    RecipeId = recipeId,
                    Summary = Clamp(instruction, 255)!,
                    Description = Clamp(instruction, 2047)!,
                    StepNumber = stepNumber++,
                });
            }

            return stepNumber - 1;
        }

        /// <summary>
        /// Links OCR ingredient lines to ingredients that already exist in the
        /// catalog. OCR yields whole lines ("2 cups all-purpose flour") and there
        /// is no line parser, so a new catalog entry is never created from one —
        /// that would fill the catalog with quantities. Unmatched lines are
        /// reported to the caller instead of being dropped.
        /// </summary>
        private async Task<(int Matched, List<string> Unmatched)> AddIngredientLinesAsync(
            long recipeId, IReadOnlyCollection<string> lines)
        {
            var unmatched = new List<string>();
            var cleaned = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
            if (cleaned.Count == 0)
            {
                return (0, unmatched);
            }

            // One pass over the catalog rather than a query per line. Longest
            // name first so "all-purpose flour" wins over "flour".
            var catalog = await _context.Ingredients
                .Where(i => !i.IsDeleted && i.Name.Length >= 3)
                .Select(i => new { i.Id, i.Name })
                .ToListAsync();

            var aliases = await _context.IngredientAliases
                .Where(a => !a.IsDeleted && a.AliasName.Length >= 3)
                .Select(a => new { Id = a.IngredientId, Name = a.AliasName })
                .ToListAsync();

            var candidates = catalog.Concat(aliases)
                .Select(c => new { c.Id, Lowered = c.Name.ToLowerInvariant() })
                .OrderByDescending(c => c.Lowered.Length)
                .ToList();

            var rows = new Dictionary<long, RecipeIngredientEntity>();
            foreach (var line in cleaned)
            {
                var lowered = line.ToLowerInvariant();
                var hit = candidates.FirstOrDefault(c => ContainsWord(lowered, c.Lowered));
                if (hit == null)
                {
                    unmatched.Add(line);
                    continue;
                }

                // Same convention as the scraper: 0 means "not parsed", the raw
                // line holds the truth, and vetting flags these for review.
                if (rows.TryGetValue(hit.Id, out var existing))
                {
                    existing.RawLine = Clamp($"{existing.RawLine} + {line}", 2047)!;
                    continue;
                }

                var row = new RecipeIngredientEntity
                {
                    RecipeId = recipeId,
                    IngredientId = hit.Id,
                    Quantity = 0m,
                    MeasurementId = DefaultMeasurementId,
                    RawLine = Clamp(line, 2047)!,
                };
                rows[hit.Id] = row;
                _context.RecipeIngredients.Add(row);
            }

            return (rows.Count, unmatched);
        }

        private static bool ContainsWord(string haystack, string needle)
        {
            var index = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                var startsClean = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
                var endIndex = index + needle.Length;
                var endsClean = endIndex == haystack.Length || !char.IsLetterOrDigit(haystack[endIndex]);
                if (startsClean && endsClean)
                {
                    return true;
                }

                index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal);
            }

            return false;
        }

        private static string BuildImportSummary(int steps, int matched, List<string> unmatched)
        {
            var parts = new List<string>
            {
                $"{steps} step{(steps == 1 ? "" : "s")}",
                $"{matched} ingredient{(matched == 1 ? "" : "s")}",
            };

            var summary = $"Recipe imported from image: {string.Join(", ", parts)}.";
            if (unmatched.Count > 0)
            {
                summary += $" {unmatched.Count} ingredient line{(unmatched.Count == 1 ? "" : "s")} " +
                    $"could not be matched to the catalog and need{(unmatched.Count == 1 ? "s" : "")} to be added by hand: " +
                    string.Join("; ", unmatched.Take(10));
            }

            return summary;
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
                var scraped = await _recipeScraping.TestScrapeRecipeAsync(new RecipeScrapingTestRequestModel
                {
                    Url = url,
                });

                return new RecipeScrapeTestModel
                {
                    Url = url,
                    Title = scraped.Name,
                    Description = scraped.Description ?? string.Empty,
                    Image = scraped.Image ?? string.Empty,
                    Ingredients = scraped.Ingredients.Select(i => i.Name).ToList(),
                    Instructions = scraped.Steps.Select(st => st.Instruction).ToList(),
                    PrepTime = scraped.PrepTime ?? string.Empty,
                    CookTime = scraped.CookTime ?? string.Empty,
                    TotalTime = scraped.TotalTime ?? string.Empty,
                    Yield = scraped.RecipeYield ?? string.Empty,
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
        private Task<ParsedRecipeData> ParseHtmlOrJsonAsync(string htmlOrJson)
        {
            var trimmed = htmlOrJson.TrimStart();

            // Try JSON first
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                return Task.FromResult(ParseJsonRecipe(trimmed));
            }

            // Otherwise treat as HTML
            return Task.FromResult(ParseHtmlRecipe(trimmed));
        }

        private ParsedRecipeData ParseJsonRecipe(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new ParsedRecipeData
                {
                    Title = root.TryGetProperty("name", out var name) ? name.GetString() ?? ""
                          : root.TryGetProperty("title", out var title) ? title.GetString() ?? ""
                          : "Imported Recipe",
                    Description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : ""
                };
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON recipe data");
                return new ParsedRecipeData { Title = "Imported Recipe" };
            }
        }

        private ParsedRecipeData ParseHtmlRecipe(string html)
        {
            var result = new ParsedRecipeData();

            // Extract title from <title>, <h1>, or schema.org name
            var titleMatch = System.Text.RegularExpressions.Regex.Match(html, @"<title[^>]*>([^<]+)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!titleMatch.Success)
                titleMatch = System.Text.RegularExpressions.Regex.Match(html, @"<h1[^>]*>([^<]+)</h1>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            result.Title = titleMatch.Success ? System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim()) : "Imported Recipe";

            // Extract description from meta tag
            var descMatch = System.Text.RegularExpressions.Regex.Match(html, @"<meta\s+name=""description""\s+content=""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (descMatch.Success)
                result.Description = System.Net.WebUtility.HtmlDecode(descMatch.Groups[1].Value.Trim());

            return result;
        }

        private Task<List<ScrapedRecipeData>> ExtractRecipesFromZipAsync(byte[] zipData)
        {
            var recipes = new List<ScrapedRecipeData>();

            using var stream = new MemoryStream(zipData);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0) continue; // skip directories

                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext is not ".json" and not ".html" and not ".htm" and not ".txt")
                    continue;

                try
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream);
                    var content = reader.ReadToEnd();

                    var parsed = (ext == ".json") ? ParseJsonRecipe(content) : ParseHtmlRecipe(content);

                    recipes.Add(new ScrapedRecipeData
                    {
                        Title = string.IsNullOrWhiteSpace(parsed.Title) ? Path.GetFileNameWithoutExtension(entry.Name) : parsed.Title,
                        Description = parsed.Description
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse recipe from ZIP entry: {EntryName}", entry.FullName);
                }
            }

            if (!recipes.Any())
                _logger.LogWarning("No valid recipe files found in ZIP archive");

            return Task.FromResult(recipes);
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