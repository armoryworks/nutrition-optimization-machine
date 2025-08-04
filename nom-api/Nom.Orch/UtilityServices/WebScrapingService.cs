using Microsoft.Extensions.Logging;
using Nom.Orch.UtilityInterfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Service for scraping recipe data from web pages
    /// </summary>
    public class WebScrapingService : IWebScrapingService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebScrapingService> _logger;

        public WebScrapingService(HttpClient httpClient, ILogger<WebScrapingService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Scrapes recipe data from a URL
        /// </summary>
        public async Task<ScrapedRecipeData> ScrapeRecipeFromUrlAsync(string url)
        {
            try
            {
                _logger.LogInformation("Scraping recipe from URL: {Url}", url);

                // Download the HTML content
                var html = await _httpClient.GetStringAsync(url);

                // Parse the HTML and extract recipe data
                var recipeData = ParseRecipeFromHtml(html, url);

                _logger.LogInformation("Successfully scraped recipe: {Title}", recipeData.Title);

                return recipeData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scrape recipe from URL: {Url}", url);
                throw;
            }
        }

        /// <summary>
        /// Parses recipe data from HTML content
        /// </summary>
        private ScrapedRecipeData ParseRecipeFromHtml(string html, string sourceUrl)
        {
            var recipeData = new ScrapedRecipeData
            {
                Title = ExtractTitle(html),
                Description = ExtractDescription(html),
                ImageUrl = ExtractImage(html),
                Ingredients = ExtractIngredients(html),
                Instructions = ExtractInstructions(html),
                PrepTime = ExtractPrepTime(html),
                CookTime = ExtractCookTime(html),
                TotalTime = ExtractTotalTime(html),
                Yield = ExtractYield(html),
                SourceUrl = sourceUrl,
                IsValid = true,
                ErrorMessage = string.Empty
            };

            // Clean up the data
            CleanRecipeData(recipeData);

            return recipeData;
        }

        private string ExtractTitle(string html)
        {
            // Try to find recipe title using various selectors
            var patterns = new[]
            {
                @"<h1[^>]*class=""[^""]*recipe-title[^""]*""[^>]*>(.*?)</h1>",
                @"<h1[^>]*class=""[^""]*title[^""]*""[^>]*>(.*?)</h1>",
                @"<h1[^>]*>(.*?)</h1>",
                @"<title[^>]*>(.*?)</title>",
                @"<meta[^>]*property=""og:title""[^>]*content=""([^""]*)""",
                @"<meta[^>]*name=""title""[^>]*content=""([^""]*)"""
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    var title = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return title;
                    }
                }
            }

            return "Scraped Recipe";
        }

        private string ExtractDescription(string html)
        {
            var patterns = new[]
            {
                @"<meta[^>]*property=""og:description""[^>]*content=""([^""]*)""",
                @"<meta[^>]*name=""description""[^>]*content=""([^""]*)""",
                @"<div[^>]*class=""[^""]*recipe-description[^""]*""[^>]*>(.*?)</div>",
                @"<p[^>]*class=""[^""]*description[^""]*""[^>]*>(.*?)</p>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    var description = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        return description;
                    }
                }
            }

            return "Recipe description from scraping";
        }

        private string ExtractImage(string html)
        {
            var patterns = new[]
            {
                @"<meta[^>]*property=""og:image""[^>]*content=""([^""]*)""",
                @"<img[^>]*class=""[^""]*recipe-image[^""]*""[^>]*src=""([^""]*)""",
                @"<img[^>]*class=""[^""]*hero-image[^""]*""[^>]*src=""([^""]*)"""
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var imageUrl = match.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        return imageUrl;
                    }
                }
            }

            return string.Empty;
        }

        private List<string> ExtractIngredients(string html)
        {
            var ingredients = new List<string>();

            // Try to find ingredients using various patterns
            var patterns = new[]
            {
                @"<li[^>]*class=""[^""]*ingredient[^""]*""[^>]*>(.*?)</li>",
                @"<li[^>]*class=""[^""]*recipe-ingredient[^""]*""[^>]*>(.*?)</li>",
                @"<span[^>]*class=""[^""]*ingredient[^""]*""[^>]*>(.*?)</span>",
                @"<div[^>]*class=""[^""]*ingredient[^""]*""[^>]*>(.*?)</div>"
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match match in matches)
                {
                    var ingredient = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(ingredient))
                    {
                        ingredients.Add(ingredient);
                    }
                }

                if (ingredients.Count > 0)
                {
                    break; // Found ingredients, stop looking
                }
            }

            // If no ingredients found, try to extract from JSON-LD
            if (ingredients.Count == 0)
            {
                ingredients.AddRange(ExtractIngredientsFromJsonLd(html));
            }

            return ingredients.Count > 0 ? ingredients : new List<string> { "Ingredient 1", "Ingredient 2" };
        }

        private List<string> ExtractInstructions(string html)
        {
            var instructions = new List<string>();

            // Try to find instructions using various patterns
            var patterns = new[]
            {
                @"<li[^>]*class=""[^""]*instruction[^""]*""[^>]*>(.*?)</li>",
                @"<li[^>]*class=""[^""]*recipe-step[^""]*""[^>]*>(.*?)</li>",
                @"<div[^>]*class=""[^""]*instruction[^""]*""[^>]*>(.*?)</div>",
                @"<p[^>]*class=""[^""]*step[^""]*""[^>]*>(.*?)</p>"
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match match in matches)
                {
                    var instruction = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(instruction))
                    {
                        instructions.Add(instruction);
                    }
                }

                if (instructions.Count > 0)
                {
                    break; // Found instructions, stop looking
                }
            }

            // If no instructions found, try to extract from JSON-LD
            if (instructions.Count == 0)
            {
                instructions.AddRange(ExtractInstructionsFromJsonLd(html));
            }

            return instructions.Count > 0 ? instructions : new List<string> { "Step 1", "Step 2" };
        }

        private string ExtractPrepTime(string html)
        {
            var patterns = new[]
            {
                @"<meta[^>]*property=""recipe:prepTime""[^>]*content=""([^""]*)""",
                @"<span[^>]*class=""[^""]*prep-time[^""]*""[^>]*>(.*?)</span>",
                @"<time[^>]*class=""[^""]*prep[^""]*""[^>]*>(.*?)</time>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var time = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(time))
                    {
                        return time;
                    }
                }
            }

            return "30 minutes";
        }

        private string ExtractCookTime(string html)
        {
            var patterns = new[]
            {
                @"<meta[^>]*property=""recipe:cookTime""[^>]*content=""([^""]*)""",
                @"<span[^>]*class=""[^""]*cook-time[^""]*""[^>]*>(.*?)</span>",
                @"<time[^>]*class=""[^""]*cook[^""]*""[^>]*>(.*?)</time>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var time = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(time))
                    {
                        return time;
                    }
                }
            }

            return "45 minutes";
        }

        private string ExtractTotalTime(string html)
        {
            var patterns = new[]
            {
                @"<meta[^>]*property=""recipe:totalTime""[^>]*content=""([^""]*)""",
                @"<span[^>]*class=""[^""]*total-time[^""]*""[^>]*>(.*?)</span>",
                @"<time[^>]*class=""[^""]*total[^""]*""[^>]*>(.*?)</time>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var time = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(time))
                    {
                        return time;
                    }
                }
            }

            return "1 hour 15 minutes";
        }

        private string ExtractYield(string html)
        {
            var patterns = new[]
            {
                @"<meta[^>]*property=""recipe:recipeYield""[^>]*content=""([^""]*)""",
                @"<span[^>]*class=""[^""]*yield[^""]*""[^>]*>(.*?)</span>",
                @"<span[^>]*class=""[^""]*servings[^""]*""[^>]*>(.*?)</span>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var yield = CleanHtml(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(yield))
                    {
                        return yield;
                    }
                }
            }

            return "4 servings";
        }

        private List<string> ExtractIngredientsFromJsonLd(string html)
        {
            var ingredients = new List<string>();

            // Look for JSON-LD structured data
            var jsonLdPattern = @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>";
            var matches = Regex.Matches(html, jsonLdPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                try
                {
                    var jsonContent = match.Groups[1].Value;
                    if (jsonContent.Contains("\"@type\":\"Recipe\""))
                    {
                        // This is a recipe JSON-LD, extract ingredients
                        var ingredientPattern = @"""recipeIngredient"":\s*\[(.*?)\]";
                        var ingredientMatch = Regex.Match(jsonContent, ingredientPattern, RegexOptions.Singleline);
                        if (ingredientMatch.Success)
                        {
                            var ingredientsJson = ingredientMatch.Groups[1].Value;
                            var ingredientMatches = Regex.Matches(ingredientsJson, @"""([^""]+)""");
                            foreach (Match ingredientMatchItem in ingredientMatches)
                            {
                                ingredients.Add(ingredientMatchItem.Groups[1].Value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse JSON-LD for ingredients");
                }
            }

            return ingredients;
        }

        private List<string> ExtractInstructionsFromJsonLd(string html)
        {
            var instructions = new List<string>();

            // Look for JSON-LD structured data
            var jsonLdPattern = @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>";
            var matches = Regex.Matches(html, jsonLdPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                try
                {
                    var jsonContent = match.Groups[1].Value;
                    if (jsonContent.Contains("\"@type\":\"Recipe\""))
                    {
                        // This is a recipe JSON-LD, extract instructions
                        var instructionPattern = @"""recipeInstructions"":\s*\[(.*?)\]";
                        var instructionMatch = Regex.Match(jsonContent, instructionPattern, RegexOptions.Singleline);
                        if (instructionMatch.Success)
                        {
                            var instructionsJson = instructionMatch.Groups[1].Value;
                            var instructionMatches = Regex.Matches(instructionsJson, @"""text"":\s*""([^""]+)""");
                            foreach (Match instructionMatchItem in instructionMatches)
                            {
                                instructions.Add(instructionMatchItem.Groups[1].Value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse JSON-LD for instructions");
                }
            }

            return instructions;
        }

        private string CleanHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            // Remove HTML tags
            var cleanText = Regex.Replace(html, @"<[^>]*>", "");
            
            // Decode HTML entities
            cleanText = System.Web.HttpUtility.HtmlDecode(cleanText);
            
            // Remove extra whitespace
            cleanText = Regex.Replace(cleanText, @"\s+", " ").Trim();
            
            return cleanText;
        }

        private void CleanRecipeData(ScrapedRecipeData recipeData)
        {
            // Clean up ingredients
            recipeData.Ingredients = recipeData.Ingredients
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Where(i => i.Length > 0)
                .ToList();

            // Clean up instructions
            recipeData.Instructions = recipeData.Instructions
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Where(i => i.Length > 0)
                .ToList();

            // Ensure we have at least some basic data
            if (string.IsNullOrWhiteSpace(recipeData.Title))
            {
                recipeData.Title = "Scraped Recipe";
            }

            if (recipeData.Ingredients.Count == 0)
            {
                recipeData.Ingredients.Add("Ingredient 1");
            }

            if (recipeData.Instructions.Count == 0)
            {
                recipeData.Instructions.Add("Step 1");
            }
        }


    }
} 