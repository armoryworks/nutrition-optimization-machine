// File: Nom.Orch/Models/Recipe/UpdateIngredientRequest.cs

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class UpdateIngredientRequest
    {
        [Required]
        public long Id { get; set; }

        [Required]
        [MaxLength(2047)]
        public required string Name { get; set; }

        [MaxLength(4095)]
        public string? Description { get; set; }

        public List<NutrientValueModel> Nutrients { get; set; } = new List<NutrientValueModel>();
    }
}