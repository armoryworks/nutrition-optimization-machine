namespace Nom.Data.Plan
{
    /// <summary>
    /// What one restriction set decided for one recipe when it is not free: it contains what the set restricts, or it
    /// cannot be decided. Derived by the build job from the recipe's lines, their foods' facts and the lines' own text.
    /// A recipe the build decided without a row is free.
    /// </summary>
    public class DietRecipeFlagEntity
    {
        public long SetId { get; set; }

        public long RecipeId { get; set; }

        public DietVerdict Verdict { get; set; }

        public DietFlagReason Reason { get; set; }

        /// <summary>The food of the line that decided it, when a food did.</summary>
        public long? IngredientId { get; set; }

        /// <summary>The criterion that decided it, when one did.</summary>
        public long? CriterionId { get; set; }
    }
}
