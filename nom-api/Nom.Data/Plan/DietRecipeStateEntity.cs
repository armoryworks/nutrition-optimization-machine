using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// That a diet gate build has decided a recipe under every one of its sets, with the fingerprint of what the
    /// decision read (its live lines' foods, order, text and amounts, and its servings). A recipe without one is not
    /// decided yet and counts as unknown.
    /// </summary>
    public class DietRecipeStateEntity
    {
        public long BuildId { get; set; }

        public long RecipeId { get; set; }

        public long Fingerprint { get; set; }

        public DateTime ComputedAt { get; set; }
    }
}
