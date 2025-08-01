using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeSearchModel
    {
        [StringLength(255, ErrorMessage = "Search query cannot exceed 255 characters.")]
        public string? Query { get; set; }
        
        public List<long>? IngredientIds { get; set; }
        public List<long>? CategoryIds { get; set; }
        public List<long>? TagIds { get; set; }
        public List<long>? ToolIds { get; set; }
        public List<long>? CuisineTypeIds { get; set; }
        
        public int? MinRating { get; set; }
        public int? MaxPrepTime { get; set; }
        public int? MaxCookTime { get; set; }
        public int? MaxTotalTime { get; set; }
        
        public bool? IsPublic { get; set; }
        public bool? IsApproved { get; set; }
        
        public string? SortBy { get; set; } // "name", "rating", "date", "prepTime", "cookTime"
        public string? SortDirection { get; set; } // "asc", "desc"
        
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        
        public bool IncludeIngredients { get; set; } = true;
        public bool IncludeSteps { get; set; } = false;
        public bool IncludeNutrition { get; set; } = false;
    }
} 