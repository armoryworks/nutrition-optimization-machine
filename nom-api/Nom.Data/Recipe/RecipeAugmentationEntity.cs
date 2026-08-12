// File: nom-api/Nom.Data/Recipe/RecipeAugmentationEntity.cs

using Nom.Data.Measurement;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// An optional add-in ingredient that improves or changes a recipe's flavor
    /// without substantially changing its cooking qualities — with structured
    /// deltas for the cases where it does shift temperature or duration.
    /// Offered in the UI as an opt-in alongside substitutions.
    /// </summary>
    public class RecipeAugmentationEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        public decimal? Quantity { get; set; }

        public long? MeasurementId { get; set; }
        public virtual MeasurementEntity? Measurement { get; set; }

        /// <summary>What it does to the dish ("adds smoky depth", "brightens the sauce").</summary>
        public string FlavorEffect { get; set; } = string.Empty;

        /// <summary>How and when to add it ("stir in with the tomatoes in step 3").</summary>
        public string? Instructions { get; set; }

        /// <summary>1-based step number after which the add-in happens, when known.</summary>
        public int? InsertAfterStepNumber { get; set; }

        /// <summary>New target temperature when the augmentation changes it.</summary>
        public int? NewTemperatureFahrenheit { get; set; }

        /// <summary>Total minutes added to (positive) or removed from (negative) the cook.</summary>
        public int? DurationDeltaMinutes { get; set; }

        /// <summary>
        /// Follows the standard curation flow: machine-proposed augmentations
        /// start NonCurated and are only offered in the UI once curated.
        /// </summary>
        public long CurationStatusId { get; set; }
        public virtual ReferenceEntity? CurationStatus { get; set; }
    }
}
