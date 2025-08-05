// File: Nom.Orch/Models/Recipe/CreateIngredientRequest.cs

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class CreateIngredientRequest
    {
        [Required]
        [MaxLength(2047)]
        public required string Name { get; set; }

        [MaxLength(4095)]
        public string? Description { get; set; }

        // This would be populated with the nutrient data from the dynamic form
        public List<NutrientValueModel> Nutrients { get; set; } = new List<NutrientValueModel>();
    }
}