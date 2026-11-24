using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// When a recipe's derived nutrition label was last checked against its inputs, and the fingerprint of its lines'
    /// inputs then (each live line's food, amount, unit, kind and alcohol retention, the unit's factor, the food's weights,
    /// a container line's text, and the servings). A recipe whose lines
    /// were written since but whose fingerprint is the same (an order-only pass, a text edit) is not derived again, and a
    /// label that comes out the same is not rewritten.
    /// </summary>
    public class RecipeNutritionCheckEntity
    {
        public long RecipeId { get; set; }

        public long InputPrint { get; set; }

        public DateTime CheckedAt { get; set; }
    }
}
