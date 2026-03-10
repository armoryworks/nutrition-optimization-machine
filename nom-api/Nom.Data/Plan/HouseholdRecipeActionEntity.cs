// File: Nom.Data/Plan/HouseholdRecipeActionEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    public class HouseholdRecipeActionEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long ActorId { get; set; }
        public virtual PersonEntity? Actor { get; set; }

        public string ActionType { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Details { get; set; }

        public DateTime? ActionDate { get; set; }
    }
}