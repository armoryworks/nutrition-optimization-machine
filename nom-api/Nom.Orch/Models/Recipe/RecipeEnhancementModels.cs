using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>A recipe-scoped substitution offered on the recipe page, with its step effects.</summary>
    public class RecipeSubstitutionModel
    {
        public long Id { get; set; }

        /// <summary>The original ingredient (of this recipe) being substituted.</summary>
        public long IngredientId { get; set; }
        public long SubstituteIngredientId { get; set; }
        public string SubstituteName { get; set; } = string.Empty;
        public decimal Ratio { get; set; } = 1m;
        public decimal? SubstituteQuantity { get; set; }
        public long? SubstituteMeasurementId { get; set; }
        public string? SubstituteMeasurement { get; set; }
        public string? Notes { get; set; }
        public bool IsCurated { get; set; }
        public List<RecipeSubstitutionStepEffectModel> StepEffects { get; set; } = new();
    }

    public class RecipeSubstitutionStepEffectModel
    {
        public long Id { get; set; }
        public int StepNumber { get; set; }
        public string AlteredDescription { get; set; } = string.Empty;
        public int? NewTemperatureFahrenheit { get; set; }
        public int? DurationDeltaMinutes { get; set; }
    }

    public class RecipeSubstitutionUpsertModel
    {
        [Required]
        public long IngredientId { get; set; }

        [Required]
        public long SubstituteIngredientId { get; set; }

        public decimal Ratio { get; set; } = 1m;
        public decimal? SubstituteQuantity { get; set; }
        public long? SubstituteMeasurementId { get; set; }
        public string? Notes { get; set; }
        public List<RecipeSubstitutionStepEffectUpsertModel> StepEffects { get; set; } = new();
    }

    public class RecipeSubstitutionStepEffectUpsertModel
    {
        [Required]
        public int StepNumber { get; set; }

        [Required]
        public string AlteredDescription { get; set; } = string.Empty;

        public int? NewTemperatureFahrenheit { get; set; }
        public int? DurationDeltaMinutes { get; set; }
    }

    /// <summary>An optional add-in ingredient offered on the recipe page.</summary>
    public class RecipeAugmentationModel
    {
        public long Id { get; set; }
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public long? MeasurementId { get; set; }
        public string? Measurement { get; set; }
        public string FlavorEffect { get; set; } = string.Empty;
        public string? Instructions { get; set; }
        public int? InsertAfterStepNumber { get; set; }
        public int? NewTemperatureFahrenheit { get; set; }
        public int? DurationDeltaMinutes { get; set; }
        public bool IsCurated { get; set; }
    }

    public class RecipeAugmentationUpsertModel
    {
        [Required]
        public long IngredientId { get; set; }

        public decimal? Quantity { get; set; }
        public long? MeasurementId { get; set; }

        [Required]
        public string FlavorEffect { get; set; } = string.Empty;

        public string? Instructions { get; set; }
        public int? InsertAfterStepNumber { get; set; }
        public int? NewTemperatureFahrenheit { get; set; }
        public int? DurationDeltaMinutes { get; set; }
    }
}
