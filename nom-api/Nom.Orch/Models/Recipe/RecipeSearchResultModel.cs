// File: nom-api/Nom.Orch/Models/Recipe/RecipeSearchResultModel.cs

using System;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeSearchResultModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int PrepTimeMinutes { get; set; }
        public int CookTimeMinutes { get; set; }
        public int TotalTimeMinutes { get; set; }
        public int Servings { get; set; }
        public decimal? Rating { get; set; }
        public int RatingCount { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public int PrepTime { get; set; }
        public int CookTime { get; set; }
        public int TotalTime { get; set; }
        public decimal AverageRating { get; set; }
        public bool IsPublic { get; set; } = true;
        public bool IsApproved { get; set; } = true;
        public int AuthorId { get; set; }
        public List<RecipeIngredientSearchModel> Ingredients { get; set; } = new();
        public List<RecipeStepSearchModel> Steps { get; set; } = new();
        public List<RecipeNutritionSearchModel> Nutrition { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public List<string> CuisineTypes { get; set; } = new();
    }
}