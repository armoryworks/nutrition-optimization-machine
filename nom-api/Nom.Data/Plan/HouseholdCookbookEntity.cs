// File: Nom.Data/Plan/HouseholdCookbookEntity.cs

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    [Table("HouseholdCookbook", Schema = "plan")]
    public class HouseholdCookbookEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [MaxLength(255)]
        public string? Slug { get; set; }

        public bool IsPublic { get; set; } = false;
        // Navigation properties
        public virtual ICollection<HouseholdCookbookRecipeEntity> Recipes { get; set; } = new List<HouseholdCookbookRecipeEntity>();
    }
}