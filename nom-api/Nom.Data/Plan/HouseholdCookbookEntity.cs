// File: Nom.Data/Plan/HouseholdCookbookEntity.cs

using System;
using System.Collections.Generic;
using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    public class HouseholdCookbookEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Slug { get; set; }

        public bool IsPublic { get; set; } = false;
        // Navigation properties
        public virtual ICollection<HouseholdCookbookRecipeEntity> Recipes { get; set; } = new List<HouseholdCookbookRecipeEntity>();
    }
}