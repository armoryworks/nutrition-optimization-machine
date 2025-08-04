using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
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
} 