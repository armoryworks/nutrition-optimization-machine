// File: nom-api/Nom.Orch/Models/Recipe/RecipeVariationModels.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>A curated swap option for one of a recipe's ingredients.</summary>
    public class IngredientSubstitutionModel
    {
        /// <summary>The substitute ingredient's id.</summary>
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Computed for this recipe: original quantity × ratio.</summary>
        public decimal Quantity { get; set; }
        /// <summary>The original line's measurement (ratio preserves the unit).</summary>
        public string Measurement { get; set; } = string.Empty;
        public long? MeasurementId { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>One saved swap in the caller's default variation of a recipe.</summary>
    public class RecipeVariationItemModel
    {
        /// <summary>The recipe's original ingredient id being replaced.</summary>
        public long IngredientId { get; set; }
        public long SubstituteIngredientId { get; set; }
        public string SubstituteName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Measurement { get; set; } = string.Empty;
        public long? MeasurementId { get; set; }
    }

    public class SaveVariationItemRequest
    {
        [Required]
        public long IngredientId { get; set; }
        [Required]
        public long SubstituteIngredientId { get; set; }
    }

    /// <summary>A hit between the caller's dietary restrictions and a recipe's ingredients.</summary>
    public class RecipeDietMatchModel
    {
        public string RestrictionName { get; set; } = string.Empty;
        public string? RestrictionType { get; set; }
        public int? Severity { get; set; }
        public long? IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        /// <summary>Why the hit fired, from the category criterion ("high oxalate").</summary>
        public string? Notes { get; set; }
    }
}
