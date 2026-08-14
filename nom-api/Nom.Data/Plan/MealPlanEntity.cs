// File: Nom.Data/Plan/MealPlanEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    public class MealPlanEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long AuthorId { get; set; }
        public virtual PersonEntity? Author { get; set; }

        public DateOnly Date { get; set; }

        public long MealTypeId { get; set; }
        public virtual ReferenceEntity? MealType { get; set; }

        public long? RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        /// <summary>
        /// A whole/standalone food scheduled directly into this slot without a recipe —
        /// an apple, a protein bar, a frozen dinner. Mutually exclusive with <see cref="RecipeId"/>
        /// (enforced by a check constraint). NULL when the slot holds a recipe or is free-text.
        /// Nutrition for the slot is derived from the ingredient's nutrients scaled by
        /// <see cref="Quantity"/> in <see cref="MeasurementId"/>.
        /// </summary>
        public long? IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>Amount of the standalone <see cref="Ingredient"/> in this slot (e.g. 1, 2). Defaults to 1 serving.</summary>
        public decimal? Quantity { get; set; }

        public long? MeasurementId { get; set; }
        public virtual Nom.Data.Measurement.MeasurementEntity? Measurement { get; set; }

        public string? Note { get; set; }

        public string? Title { get; set; }

        /// <summary>
        /// The date when this meal was actually prepared/cooked.
        /// Used to trigger pantry deductions when a meal is completed.
        /// Null means the meal has not been completed yet.
        /// </summary>
        public DateOnly? CompletedDate { get; set; }

        /// <summary>
        /// When shopping was completed for this meal entry.
        /// Entries with this set are protected from being replaced during shuffles.
        /// </summary>
        public DateTime? ShoppingCompletedAt { get; set; }
    }
}