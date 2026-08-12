// File: nom-api/Nom.Data/Recipe/RecipeSubstitutionStepEffectEntity.cs

namespace Nom.Data.Recipe
{
    /// <summary>
    /// How choosing a substitution alters one instruction step. The UI overlays
    /// these on the recipe's steps when the user opts into the substitution.
    /// Steps are addressed by StepNumber (RecipeStep is keyed by
    /// RecipeId + StepNumber, and step lists get renumbered on edit, so a soft
    /// reference validated at write time beats a brittle FK).
    /// </summary>
    public class RecipeSubstitutionStepEffectEntity : BaseEntity
    {
        public long RecipeSubstitutionId { get; set; }
        public virtual RecipeSubstitutionEntity? RecipeSubstitution { get; set; }

        /// <summary>1-based step number within the substitution's recipe.</summary>
        public int StepNumber { get; set; }

        /// <summary>Replacement instruction text for this step.</summary>
        public string AlteredDescription { get; set; } = string.Empty;

        /// <summary>New target temperature when the swap changes it (e.g. 325).</summary>
        public int? NewTemperatureFahrenheit { get; set; }

        /// <summary>Minutes added to (positive) or removed from (negative) this step.</summary>
        public int? DurationDeltaMinutes { get; set; }
    }
}
