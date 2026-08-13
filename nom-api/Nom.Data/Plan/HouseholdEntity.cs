// File: Nom.Data/Plan/HouseholdEntity.cs

using System;
using System.Collections.Generic;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Recipe;
namespace Nom.Data.Plan
{
    public class HouseholdEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Slug { get; set; }

        public string? Description { get; set; }

        /// <summary>
        /// Opaque marker of an external management tool this household is
        /// enrolled with (e.g. "brigade:456"); NULL = self-managed. Set only
        /// via consented enrollment (see household-policies design doc).
        /// </summary>
        public string? ManagedBy { get; set; }

        // Household group association
        public long HouseholdGroupId { get; set; }
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