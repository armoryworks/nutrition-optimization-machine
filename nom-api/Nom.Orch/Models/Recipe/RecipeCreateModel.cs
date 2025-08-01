// File: Nom.Orch/Models/Recipe/RecipeCreateModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeCreateModel
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long AuthorId { get; set; }
    }
} 