// File: nom-api/Nom.Data/Recipe/RecipeSubstitutionEntity.cs

using System.Collections.Generic;
using Nom.Data.Measurement;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A recipe-scoped substitution: replace one ingredient line of this recipe
    /// with another ingredient, including how the swap alters the instructions
    /// (see <see cref="StepEffects"/>). Complements the global
    /// <see cref="IngredientSubstitutionEntity"/>, which is context-free.
    /// </summary>
    public class RecipeSubstitutionEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        /// <summary>
        /// The original ingredient this substitution replaces. Together with
        /// RecipeId this identifies the recipe's ingredient line
        /// (RecipeIngredient has a composite key of RecipeId + IngredientId).
        /// </summary>
        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        public long SubstituteIngredientId { get; set; }
        public virtual IngredientEntity? SubstituteIngredient { get; set; }

        /// <summary>
        /// Substitute quantity = original quantity × Ratio, in the original's
        /// measurement — unless an explicit quantity/measurement is given below.
        /// </summary>
        public decimal Ratio { get; set; } = 1m;

        public decimal? SubstituteQuantity { get; set; }

        public long? SubstituteMeasurementId { get; set; }
        public virtual MeasurementEntity? SubstituteMeasurement { get; set; }

        /// <summary>Guidance shown with the option ("expect a denser crumb").</summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Follows the standard curation flow: machine-proposed substitutions
        /// start NonCurated and are only offered in the UI once curated.
        /// </summary>
        public long CurationStatusId { get; set; }
        public virtual ReferenceEntity? CurationStatus { get; set; }

        public virtual ICollection<RecipeSubstitutionStepEffectEntity> StepEffects { get; set; }
            = new List<RecipeSubstitutionStepEffectEntity>();
    }
}
