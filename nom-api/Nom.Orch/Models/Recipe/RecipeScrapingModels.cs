using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for scraping a recipe from a URL
    /// </summary>
    public class RecipeScrapingRequestModel
    {
        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;

        public bool ImportKeywordsAsTags { get; set; } = false;
        public bool StayInEditMode { get; set; } = false;
    }

    /// <summary>
    /// Model for scraping recipe from HTML or JSON data
    /// </summary>
    public class RecipeScrapingDataRequestModel
    {
        [Required]
        public string Data { get; set; } = string.Empty;

        public bool ImportKeywordsAsTags { get; set; } = false;
        public bool StayInEditMode { get; set; } = false;
    }

    /// <summary>
    /// Model for testing recipe scraping
    /// </summary>
    public class RecipeScrapingTestRequestModel
    {
        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;

        public bool UseOpenAI { get; set; } = false;
    }

    /// <summary>
    /// Model for bulk recipe scraping
    /// </summary>
    public class RecipeBulkScrapingRequestModel
    {
        [Required]
        public List<RecipeBulkScrapingItemModel> Imports { get; set; } = new();
    }

    /// <summary>
    /// Individual item for bulk recipe scraping
    /// </summary>
    public class RecipeBulkScrapingItemModel
    {
        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;

        public List<string>? Tags { get; set; }
        public List<string>? Categories { get; set; }
    }

    /// <summary>
    /// Response model for recipe scraping
    /// </summary>
    public class RecipeScrapingResponseModel
    {
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Response model for bulk recipe scraping
    /// </summary>
    public class RecipeBulkScrapingResponseModel
    {
        public long ReportId { get; set; }
        public List<RecipeScrapingResponseModel> Results { get; set; } = new();
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// Scraped recipe data model
    /// </summary>
    public class ScrapedRecipeModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Image { get; set; }
        public string? SourceUrl { get; set; }
        public string? SourceSite { get; set; }
        public string? PrepTime { get; set; }
        public string? CookTime { get; set; }
        public string? TotalTime { get; set; }
        public string? RecipeYield { get; set; }
        public decimal? RecipeYieldQuantity { get; set; }
        public decimal? RecipeServings { get; set; }
        public List<ScrapedIngredientModel> Ingredients { get; set; } = new();
        public List<ScrapedStepModel> Steps { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<string> Categories { get; set; } = new();
    }

    /// <summary>
    /// Scraped ingredient model
    /// </summary>
    public class ScrapedIngredientModel
    {
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Scraped step model
    /// </summary>
    public class ScrapedStepModel
    {
        public int Order { get; set; }
        public string Instruction { get; set; } = string.Empty;
        public string? Image { get; set; }
    }
} 