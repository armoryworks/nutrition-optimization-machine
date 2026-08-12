// File: Nom.Orch/Models/Recipe/RecipeResponseModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long AuthorId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public long? PrepTimeMinutes { get; set; }
        public long? CookTimeMinutes { get; set; }
        public long? Servings { get; set; }

        /// <summary>Per-serving amount for the nutrition label, e.g. 252 + "g". Null when unset.</summary>
        public decimal? ServingQuantity { get; set; }
        public string? ServingUnit { get; set; }

        public decimal Rating { get; set; }
        public int CommentCount { get; set; }
        public int RatingCount { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public string? CurationStatus { get; set; }
        public List<RecipeIngredientModel> Ingredients { get; set; } = new();

        /// <summary>The caller's saved default variation, when authenticated and one exists.</summary>
        public List<RecipeVariationItemModel>? Variation { get; set; }
        public List<RecipeStepModel> Steps { get; set; } = new();
        public List<RecipeNutritionSearchModel> Nutrition { get; set; } = new();
    }
} 