// File: nom-api/Nom.Orch/Models/Recipe/RecipeIngredientSearchModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeIngredientSearchModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string MeasurementUnit { get; set; } = string.Empty;
        public string Measurement { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Notes { get; set; }
    }
}