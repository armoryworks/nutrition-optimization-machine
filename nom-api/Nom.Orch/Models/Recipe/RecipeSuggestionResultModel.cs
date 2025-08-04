namespace Nom.Orch.Models.Recipe{
    /// <summary>
    /// Model for recipe suggestion result
    /// </summary>
    public class RecipeSuggestionResultModel{
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public decimal? Rating { get; set; }
        public int RatingCount { get; set; }
        public List<string> Categories { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
        public List<RecipeIngredientSearchModel>? Ingredients { get; set; }
        public List<RecipeStepSearchModel>? Steps { get; set; }
    }
}