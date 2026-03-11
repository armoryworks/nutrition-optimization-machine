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