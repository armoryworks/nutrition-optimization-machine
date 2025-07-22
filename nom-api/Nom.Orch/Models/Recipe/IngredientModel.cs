// File: nom-api/Nom.Orch/Models/Recipe/IngredientModel.cs
namespace Nom.Orch.Models.Recipe
{
    public class IngredientModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? FdcId { get; set; }
        public string? Description { get; set; }
        public List<NutrientValueModel> Nutrients { get; set; } = new();
    }
}