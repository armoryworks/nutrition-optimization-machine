// File: nom-api/Nom.Data/Recipe/IngredientComponentEntity.cs

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A sub-ingredient of a composite ingredient — e.g. "bread" breaks down
    /// into flour, water, yeast, salt. One level deep: components are plain
    /// ingredients. Surfaces on the nutrition label's Ingredients view.
    /// </summary>
    public class IngredientComponentEntity : BaseEntity
    {
        /// <summary>The composite (parent) ingredient.</summary>
        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>The component (sub-) ingredient.</summary>
        public long ComponentIngredientId { get; set; }
        public virtual IngredientEntity? ComponentIngredient { get; set; }

        /// <summary>Label ordering (packaged-food convention: by weight, descending).</summary>
        public int SortOrder { get; set; }
    }
}
