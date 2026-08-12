// File: nom-api/Nom.Data/Recipe/RecipeVariationItemEntity.cs

using Nom.Data.Measurement;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// One ingredient swap within a person's recipe variation: the recipe's
    /// original ingredient replaced by a substitute at a stored quantity, so
    /// the variation stays stable even if the curated ratio changes later.
    /// </summary>
    public class RecipeVariationItemEntity : BaseEntity
    {
        public long RecipeVariationId { get; set; }
        public virtual RecipeVariationEntity? RecipeVariation { get; set; }

        /// <summary>The recipe's original ingredient being replaced.</summary>
        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>The replacement ingredient.</summary>
        public long SubstituteIngredientId { get; set; }
        public virtual IngredientEntity? SubstituteIngredient { get; set; }

        public decimal Quantity { get; set; }

        public long? MeasurementId { get; set; }
        public virtual MeasurementEntity? Measurement { get; set; }
    }
}
