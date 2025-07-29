// File: Nom.Orch/Models/Recipe/RecipeIngredientModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeIngredientModel
    {
        [Required]
        public long IngredientId { get; set; }

        public string Name { get; set; } = string.Empty;

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public long MeasurementTypeId { get; set; }
    }
}