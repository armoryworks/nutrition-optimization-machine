// File: Nom.Data/Plan/MealPlanEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    [Table("MealPlan", Schema = "plan")]
    public class MealPlanEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        public long AuthorId { get; set; }
        [ForeignKey(nameof(AuthorId))]
        public virtual PersonEntity? Author { get; set; }

        [Required]
        [Column(TypeName = "date")]
        public DateOnly Date { get; set; }

        [Required]
        public long MealTypeId { get; set; }
        [ForeignKey(nameof(MealTypeId))]
        public virtual ReferenceEntity? MealType { get; set; }

        public long? RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [MaxLength(2047)]
        public string? Note { get; set; }

        [MaxLength(255)]
        public string? Title { get; set; }

        /// <summary>
        /// The date when this meal was actually prepared/cooked.
        /// Used to trigger pantry deductions when a meal is completed.
        /// Null means the meal has not been completed yet.
        /// </summary>
        [Column(TypeName = "date")]
        public DateOnly? CompletedDate { get; set; }
    }
}