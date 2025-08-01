// File: nom-api/Nom.Orch/Models/Recipe/RecipeStepSearchModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeStepSearchModel
    {
        public long Id { get; set; }
        public int StepNumber { get; set; }
        public string Instructions { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int? DurationMinutes { get; set; }
    }
}