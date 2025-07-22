// File: nom-api/Nom.Orch/Models/Recipe/IngredientSearchResponseModel.cs
namespace Nom.Orch.Models.Recipe
{
    public class IngredientSearchResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? FdcId { get; set; }
    }
}