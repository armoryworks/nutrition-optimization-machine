using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Advanced search model for recipes with comprehensive filtering options
    /// </summary>
    public class RecipeAdvancedSearchModel
    {
        public string? Query { get; set; }
        public List<long>? CategoryIds { get; set; }
        public List<long>? TagIds { get; set; }
        public List<long>? ToolIds { get; set; }
        public List<long>? IngredientIds { get; set; }
        public List<long>? CuisineTypeIds { get; set; }
        public List<long>? HouseholdIds { get; set; }
        public int? MinRating { get; set; }
        public int? MaxPrepTime { get; set; }
        public int? MaxCookTime { get; set; }
        public int? MaxTotalTime { get; set; }
        public bool? IsPublic { get; set; }
        public bool? IsApproved { get; set; }
        public string? SortBy { get; set; }
        public string? SortDirection { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public bool IncludeIngredients { get; set; } = false;
        public bool IncludeSteps { get; set; } = false;
        public bool IncludeNutrition { get; set; } = false;
    }
} 