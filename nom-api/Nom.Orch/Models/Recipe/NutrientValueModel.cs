// File: nom-api/Nom.Orch/Models/Recipe/NutrientValueModel.cs
namespace Nom.Orch.Models.Recipe
{
    public class NutrientValueModel
    {
        public long NutrientId { get; set; }
        public string NutrientName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string UnitName { get; set; } = string.Empty;
    }
}