// File: Nom.Data/Plan/HouseholdRecipeActionEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    [Table("HouseholdRecipeAction", Schema = "plan")]
    public class HouseholdRecipeActionEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long ActorId { get; set; }
        [ForeignKey(nameof(ActorId))]
        public virtual PersonEntity? Actor { get; set; }

        [Required]
        [MaxLength(100)]
        public string ActionType { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [Column(TypeName = "text")]
        public string? Details { get; set; }

        public DateTime? ActionDate { get; set; }
    }
} 