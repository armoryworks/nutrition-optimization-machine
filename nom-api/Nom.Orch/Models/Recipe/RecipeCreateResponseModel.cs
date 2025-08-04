// File: Nom.Orch/Models/Recipe/RecipeCreateResponseModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeCreateResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long AuthorId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Message { get; set; } = string.Empty;
    }
} 