// File: Nom.Orch/Models/Recipe/RecipeIngredientModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeIngredientModel
    {
        [Required]
        public long IngredientId { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Component names of a composite ingredient (label order); empty for plain ingredients.</summary>
        public List<string> SubIngredients { get; set; } = new();

        /// <summary>Curated swap options for this line, quantities pre-computed for this recipe.</summary>
        public List<IngredientSubstitutionModel> Substitutions { get; set; } = new();

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public long MeasurementId { get; set; }

        public string? Measurement { get; set; }

        public string? Notes { get; set; }
    }
}