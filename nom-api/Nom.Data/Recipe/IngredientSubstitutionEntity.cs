// File: nom-api/Nom.Data/Recipe/IngredientSubstitutionEntity.cs

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A curated substitution for an ingredient — e.g. butter → margarine.
    /// Surfaced on recipe pages as swap options next to each ingredient.
    /// </summary>
    public class IngredientSubstitutionEntity : BaseEntity
    {
        /// <summary>The ingredient being substituted.</summary>
        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>The replacement ingredient.</summary>
        public long SubstituteIngredientId { get; set; }
        public virtual IngredientEntity? SubstituteIngredient { get; set; }

        /// <summary>
        /// Quantity multiplier: substitute quantity = original quantity × Ratio,
        /// in the original's measurement (1 cup butter → 1 cup margarine at 1.0).
        /// </summary>
        public decimal Ratio { get; set; } = 1m;

        /// <summary>Optional guidance ("adjust salt", "expect denser crumb").</summary>
        public string? Notes { get; set; }
    }
}
