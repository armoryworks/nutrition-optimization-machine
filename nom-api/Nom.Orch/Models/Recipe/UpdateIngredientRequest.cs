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

        /// <summary>
        /// Per-100 g nutrition. Null (property omitted) leaves the stored values untouched;
        /// an empty list clears them. Callers editing only name/description must omit it —
        /// otherwise an imported ingredient's FDC facts would be wiped by a rename.
        /// </summary>
        public List<NutrientValueModel>? Nutrients { get; set; }
    }
}