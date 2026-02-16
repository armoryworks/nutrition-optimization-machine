// File: Nom.Data/Plan/HouseholdEntity.cs

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Recipe;
namespace Nom.Data.Plan
{
    [Table("Household", Schema = "plan")]
    public class HouseholdEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Slug { get; set; }

        [MaxLength(2047)]
        public string? Description { get; set; }

        // Household group association
        [Required]
        public long HouseholdGroupId { get; set; }
        [ForeignKey(nameof(HouseholdGroupId))]
        public virtual HouseholdGroupEntity? HouseholdGroup { get; set; }

        // Navigation properties
        public virtual ICollection<PersonEntity> Members { get; set; } = new List<PersonEntity>();
        public virtual ICollection<PlanEntity> Plans { get; set; } = new List<PlanEntity>();
        public virtual ICollection<HouseholdPreferenceEntity> Preferences { get; set; } = new List<HouseholdPreferenceEntity>();
        public virtual ICollection<HouseholdInviteTokenEntity> InviteTokens { get; set; } = new List<HouseholdInviteTokenEntity>();
        public virtual ICollection<HouseholdWebhookEntity> Webhooks { get; set; } = new List<HouseholdWebhookEntity>();
        public virtual ICollection<HouseholdEventNotifierEntity> EventNotifiers { get; set; } = new List<HouseholdEventNotifierEntity>();
        public virtual ICollection<HouseholdRecipeActionEntity> RecipeActions { get; set; } = new List<HouseholdRecipeActionEntity>();
        public virtual ICollection<HouseholdCookbookEntity> Cookbooks { get; set; } = new List<HouseholdCookbookEntity>();
        public virtual ICollection<HouseholdIngredientEntity> IngredientsOnHand { get; set; } = new List<HouseholdIngredientEntity>();
        public virtual ICollection<HouseholdToolEntity> ToolsOnHand { get; set; } = new List<HouseholdToolEntity>();
        public virtual ICollection<RecipeEntity> MadeRecipes { get; set; } = new List<RecipeEntity>();
    }
}